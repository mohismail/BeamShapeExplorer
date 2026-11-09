#!/usr/bin/env python3
"""
BSE Optimization Sampler - Python driver
========================================

Runs a batch of constrained optimizations of a Grasshopper definition, following the
data-collection method of the Beam Shape Explorer (BSE) surrogate-modelling workflow.
This is step 2; step 1, the sampling plan, is separate (sampling_plan.py):

    1. Sampling plan: N samples of the *design inputs* (the "Sample Parameters" sliders)
       inside the slider Min/Max limits, plus a start point for each run.
    2. For every plan row, hold the design inputs fixed and run a constrained optimization
       (COBYLA by default) over the *design variables* (the "Variables" sliders), whose
       bounds are the slider Min/Max limits.
    3. Save one CSV row per run with the sample inputs, start point, final variables,
       final objective(s), constraint values, success/status code and run time.

The Grasshopper side is the "Optimization Sampler" component (Beam Shape Explorer >
Optimization). Set its Listen input to True, then either run a plan made beforehand:

    python sampling_plan.py --samples 100 --out plan.csv
    python bse_optimization_sampler.py --plan plan.csv --out runs.csv

or let the driver make the plan itself (it is saved next to the results as runs.plan.csv):

    python bse_optimization_sampler.py --samples 100 --out runs.csv

Communication is newline-delimited JSON over a localhost TCP socket (see README.md).
Requires numpy and scipy; nlopt is optional (``--algorithm nlopt:LN_COBYLA``).
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import os
import socket
import sys
import time
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional, Sequence, Tuple

import numpy as np

from sampling_plan import (Plan, Slider, add_plan_arguments, log_plan, make_plan, normalize_sampling,
                           plan_from_info, print_sampling_methods, read_plan, resolve_sampling, write_plan)

try:
    import scipy
    from scipy.optimize import minimize
except ImportError:  # pragma: no cover - reported at runtime
    scipy = None
    minimize = None


# ============================================================================ client


class GrasshopperError(RuntimeError):
    """Raised when the Grasshopper component reports an error."""


class GrasshopperClient:
    """Thin client for the Optimization Sampler component's JSON-lines protocol."""

    def __init__(self, host: str = "127.0.0.1", port: int = 8765, timeout: float = 600.0,
                 connect_retries: int = 10):
        self.host, self.port, self.timeout = host, port, timeout
        last_err: Optional[Exception] = None
        for attempt in range(max(1, connect_retries)):
            try:
                self.sock = socket.create_connection((host, port), timeout=timeout)
                break
            except OSError as err:
                last_err = err
                time.sleep(min(2.0, 0.25 * (attempt + 1)))
        else:
            raise ConnectionError(
                f"Could not connect to Grasshopper at {host}:{port} ({last_err}). "
                "Is the Optimization Sampler component's Listen input set to True?")
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self._file = self.sock.makefile("rwb")

    def request(self, payload: dict) -> dict:
        payload = dict(payload)
        payload.setdefault("timeout", self.timeout)
        self._file.write((json.dumps(payload, allow_nan=False) + "\n").encode("utf-8"))
        self._file.flush()
        line = self._file.readline()
        if not line:
            raise ConnectionError("Grasshopper closed the connection.")
        response = json.loads(line.decode("utf-8"))
        if not response.get("ok", False):
            raise GrasshopperError(response.get("error", "unknown error"))
        return response

    def ping(self) -> dict:
        return self.request({"cmd": "ping"})

    def info(self) -> dict:
        return self.request({"cmd": "info"})

    def evaluate(self, variables: Optional[Sequence[float]] = None,
                 parameters: Optional[Sequence[float]] = None, count: bool = True) -> dict:
        payload: dict = {"cmd": "evaluate" if count else "set"}
        if variables is not None:
            payload["variables"] = [float(v) for v in variables]
        if parameters is not None:
            payload["parameters"] = [float(v) for v in parameters]
        return self.request(payload)

    def close(self) -> None:
        try:
            self.request({"cmd": "close"})
        except Exception:
            pass
        try:
            self._file.close()
            self.sock.close()
        except Exception:
            pass

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


# ============================================================================ problem


def _floats(values, n: Optional[int] = None) -> np.ndarray:
    """Convert a JSON list (None -> NaN) to a float array, optionally padded/truncated to n."""
    arr = np.array([np.nan if v is None else float(v) for v in (values or [])], dtype=float)
    if n is not None and arr.size != n:
        out = np.full(n, np.nan)
        out[: min(n, arr.size)] = arr[: min(n, arr.size)]
        arr = out
    return arr


@dataclass
class Evaluation:
    x: np.ndarray                 # variable values actually applied (after slider rounding)
    p: np.ndarray                 # parameter values actually applied
    objectives: np.ndarray
    objective: float              # weighted sum
    constraints: np.ndarray
    violations: np.ndarray
    gh_errors: List[str] = field(default_factory=list)
    solve_time: float = 0.0

    @property
    def max_violation(self) -> float:
        if self.violations.size == 0:
            return 0.0
        v = np.where(np.isnan(self.violations), np.inf, self.violations)
        return float(np.max(v))


class GrasshopperProblem:
    """Wraps the Grasshopper definition as an optimization problem in normalized [0, 1] space."""

    def __init__(self, client: GrasshopperClient, info: dict, nan_penalty: float = 1e12,
                 fail_on_gh_error: bool = False):
        self.client = client
        self.variables = [Slider.from_info(d) for d in info["variables"]]
        self.parameters = [Slider.from_info(d) for d in info.get("parameters", [])]
        self.objective_names: List[str] = list(info.get("objective_names", []))
        self.constraint_names: List[str] = list(info.get("constraint_names", []))
        self.limits = _floats(info.get("limits", []), len(self.constraint_names))
        self.types: List[str] = list(info.get("types", [])) or ["<="] * len(self.constraint_names)
        self.weights = _floats(info.get("weights", []), len(self.objective_names))
        self.nan_penalty = nan_penalty
        self.fail_on_gh_error = fail_on_gh_error

        if not self.variables:
            raise ValueError("The Optimization Sampler component reports no variable sliders.")
        if not self.objective_names:
            raise ValueError("The Optimization Sampler component reports no objective values.")

        self.lb = np.array([s.lo for s in self.variables])
        self.ub = np.array([s.hi for s in self.variables])
        self.span = np.where(self.ub > self.lb, self.ub - self.lb, 1.0)
        self.current_p: Optional[np.ndarray] = None
        self._cache: Dict[Tuple, Evaluation] = {}
        self.n_evals = 0
        self.solve_time = 0.0
        self.history: List[Evaluation] = []

    # -- dimensions
    @property
    def n(self) -> int:
        return len(self.variables)

    @property
    def m(self) -> int:
        return len(self.constraint_names)

    # -- scaling
    def to_x(self, u: np.ndarray) -> np.ndarray:
        return self.lb + np.clip(np.asarray(u, dtype=float), 0.0, 1.0) * self.span

    def to_u(self, x: np.ndarray) -> np.ndarray:
        return np.clip((np.asarray(x, dtype=float) - self.lb) / self.span, 0.0, 1.0)

    # -- evaluation
    def reset_run(self, p: Optional[np.ndarray]) -> None:
        self.current_p = None if p is None else np.asarray(p, dtype=float)
        self._cache.clear()
        self.n_evals = 0
        self.solve_time = 0.0
        self.history = []
        self._params_pending = self.current_p is not None and len(self.parameters) > 0

    def evaluate_x(self, x: np.ndarray) -> Evaluation:
        x = np.clip(np.asarray(x, dtype=float), self.lb, self.ub)
        key = tuple(np.round(x, 12))
        hit = self._cache.get(key)
        if hit is not None:
            return hit
        params = None
        if self._params_pending:
            params = self.current_p.tolist()
            self._params_pending = False
        r = self.client.evaluate(x.tolist(), params)
        ev = self._parse(r)
        self.n_evals += 1
        self.solve_time += ev.solve_time
        self._cache[key] = ev
        self.history.append(ev)
        return ev

    def _parse(self, r: dict) -> Evaluation:
        objectives = _floats(r.get("objectives"), len(self.objective_names))
        weights = _floats(r.get("weights"), len(self.objective_names))
        weights = np.where(np.isnan(weights), self.weights, weights)
        weights = np.where(np.isnan(weights), 1.0, weights)
        constraints = _floats(r.get("constraints"), self.m)
        violations = self.violation(constraints)
        objective = float(np.sum(weights * objectives)) if objectives.size else float("nan")
        return Evaluation(
            x=_floats(r.get("variables"), self.n),
            p=_floats(r.get("parameters"), len(self.parameters)),
            objectives=objectives,
            objective=objective,
            constraints=constraints,
            violations=violations,
            gh_errors=list(r.get("gh_errors") or []),
            solve_time=float(r.get("solve_time") or 0.0),
        )

    def violation(self, c: np.ndarray) -> np.ndarray:
        out = np.empty(self.m)
        for i in range(self.m):
            v, lim, t = c[i], self.limits[i], self.types[i]
            if np.isnan(v):
                out[i] = np.inf
            elif t == ">=":
                out[i] = max(0.0, lim - v)
            elif t == "=":
                out[i] = abs(v - lim)
            else:
                out[i] = max(0.0, v - lim)
        return out

    def _failed(self, ev: Evaluation) -> bool:
        return (not math.isfinite(ev.objective)) or (self.fail_on_gh_error and bool(ev.gh_errors))

    # -- functions in normalized space for the optimizers
    def f(self, u: np.ndarray) -> float:
        ev = self.evaluate_x(self.to_x(u))
        return self.nan_penalty if self._failed(ev) else ev.objective

    def g_signed(self, u: np.ndarray) -> np.ndarray:
        """Constraint margins in 'value >= 0 is feasible' form, one per constraint (equalities give v - limit)."""
        ev = self.evaluate_x(self.to_x(u))
        g = np.empty(self.m)
        for i in range(self.m):
            v, lim, t = ev.constraints[i], self.limits[i], self.types[i]
            if np.isnan(v) or self._failed(ev):
                g[i] = self.nan_penalty if t == "=" else -self.nan_penalty
            elif t == "<=":
                g[i] = lim - v
            else:  # ">=" (margin) and "=" (residual)
                g[i] = v - lim
        return g

    def ineq_index(self) -> np.ndarray:
        return np.array([i for i, t in enumerate(self.types) if t != "="], dtype=int)

    def eq_index(self) -> np.ndarray:
        return np.array([i for i, t in enumerate(self.types) if t == "="], dtype=int)


# ============================================================================ optimizers


@dataclass
class RunResult:
    u: np.ndarray
    success: bool
    status: int
    message: str
    nit: Optional[int] = None


def run_scipy(problem: GrasshopperProblem, u0: np.ndarray, method: str, args) -> RunResult:
    if minimize is None:
        raise RuntimeError("scipy is required for scipy algorithms: pip install scipy")
    n = problem.n
    bounds = [(0.0, 1.0)] * n
    ineq, eq = problem.ineq_index(), problem.eq_index()
    cons: List[dict] = []
    method_u = method.upper()

    if method_u in ("COBYLA", "COBYQA"):
        # inequality form only for COBYLA; equalities as two-sided band of width eq_tol
        if ineq.size:
            cons.append({"type": "ineq", "fun": lambda u: problem.g_signed(u)[ineq]})
        if eq.size:
            if method_u == "COBYQA":
                cons.append({"type": "eq", "fun": lambda u: problem.g_signed(u)[eq]})
            else:
                cons.append({"type": "ineq", "fun": lambda u: args.eq_tol - problem.g_signed(u)[eq]})
                cons.append({"type": "ineq", "fun": lambda u: args.eq_tol + problem.g_signed(u)[eq]})
        if method_u == "COBYLA" and _scipy_older_than(1, 11):
            # old scipy COBYLA ignores bounds -> add them as constraints
            cons.append({"type": "ineq", "fun": lambda u: np.asarray(u)})
            cons.append({"type": "ineq", "fun": lambda u: 1.0 - np.asarray(u)})
    else:
        if ineq.size:
            cons.append({"type": "ineq", "fun": lambda u: problem.g_signed(u)[ineq]})
        if eq.size:
            cons.append({"type": "eq", "fun": lambda u: problem.g_signed(u)[eq]})

    options: dict = {"maxiter": args.maxiter}
    if method_u == "COBYLA":
        options.update({"rhobeg": args.rhobeg, "catol": args.feas_tol})
    elif method_u == "COBYQA":
        options.update({"initial_tr_radius": args.rhobeg, "feasibility_tol": args.feas_tol})
    elif method_u == "SLSQP":
        options.update({"eps": args.fd_step})
    elif method_u == "TRUST-CONSTR":
        options.update({"finite_diff_rel_step": args.fd_step})

    res = minimize(problem.f, np.asarray(u0, dtype=float), method=method, bounds=bounds,
                   constraints=cons, tol=args.tol, options=options)
    status = int(res.status) if getattr(res, "status", None) is not None else (0 if res.success else -1)
    return RunResult(np.clip(np.asarray(res.x, dtype=float), 0.0, 1.0), bool(res.success), status,
                     str(res.message), getattr(res, "nit", None))


def run_nlopt(problem: GrasshopperProblem, u0: np.ndarray, algorithm: str, args) -> RunResult:
    try:
        import nlopt  # type: ignore
    except ImportError as err:
        raise RuntimeError("nlopt is not installed: pip install nlopt") from err
    algo = getattr(nlopt, algorithm.upper())
    n = problem.n
    opt = nlopt.opt(algo, n)
    opt.set_lower_bounds([0.0] * n)
    opt.set_upper_bounds([1.0] * n)
    opt.set_min_objective(lambda u, grad: float(problem.f(u)))
    ineq, eq = problem.ineq_index(), problem.eq_index()
    # nlopt uses c(u) <= 0 for feasibility
    if ineq.size:
        def c_ineq(result, u, grad):
            result[:] = -problem.g_signed(u)[ineq]
        opt.add_inequality_mconstraint(c_ineq, [args.feas_tol] * int(ineq.size))
    if eq.size:
        def c_eq(result, u, grad):
            result[:] = problem.g_signed(u)[eq]
        opt.add_equality_mconstraint(c_eq, [args.eq_tol] * int(eq.size))
    opt.set_maxeval(args.maxiter)
    opt.set_xtol_rel(args.tol)
    opt.set_ftol_rel(args.tol)
    try:
        opt.set_initial_step(args.rhobeg)
    except Exception:
        pass
    try:
        u = np.asarray(opt.optimize(np.asarray(u0, dtype=float)), dtype=float)
        code = int(opt.last_optimize_result())
        message = _NLOPT_CODES.get(code, str(code))
    except nlopt.RoundoffLimited:
        u, code, message = problem.to_u(problem.history[-1].x) if problem.history else u0, -4, "ROUNDOFF_LIMITED"
    success = code > 0 and code != 5 and code != 6  # MAXEVAL_REACHED / MAXTIME_REACHED are not convergence
    return RunResult(np.clip(u, 0.0, 1.0), success, code, message)


_NLOPT_CODES = {1: "SUCCESS", 2: "STOPVAL_REACHED", 3: "FTOL_REACHED", 4: "XTOL_REACHED",
                5: "MAXEVAL_REACHED", 6: "MAXTIME_REACHED", -1: "FAILURE", -2: "INVALID_ARGS",
                -3: "OUT_OF_MEMORY", -4: "ROUNDOFF_LIMITED", -5: "FORCED_STOP"}


def _scipy_older_than(major: int, minor: int) -> bool:
    try:
        parts = [int(p) for p in scipy.__version__.split(".")[:2]]
        return tuple(parts) < (major, minor)
    except Exception:
        return False


SCIPY_METHODS = ("COBYLA", "COBYQA", "SLSQP", "TRUST-CONSTR")


def make_optimizer(name: str) -> Callable[[GrasshopperProblem, np.ndarray, argparse.Namespace], RunResult]:
    if name.lower().startswith("nlopt:"):
        algo = name.split(":", 1)[1]
        try:
            import nlopt  # type: ignore
        except ImportError as err:
            raise SystemExit("nlopt is not installed: pip install nlopt") from err
        if not hasattr(nlopt, algo.upper()):
            raise SystemExit(f"Unknown nlopt algorithm '{algo}' (e.g. LN_COBYLA, LN_BOBYQA, GN_ISRES).")
        return lambda pr, u0, a: run_nlopt(pr, u0, algo, a)
    if name.upper() not in SCIPY_METHODS:
        raise SystemExit(f"Unsupported algorithm '{name}'. Use one of {SCIPY_METHODS} or nlopt:<ALGO>.")
    if minimize is None:
        raise SystemExit("scipy is required: pip install scipy")
    return lambda pr, u0, a: run_scipy(pr, u0, name, a)


# ============================================================================ CSV


def csv_header(problem: GrasshopperProblem) -> List[str]:
    cols = ["run"]
    cols += [f"param:{s.name}" for s in problem.parameters]
    cols += [f"x0:{s.name}" for s in problem.variables]
    cols += [f"x:{s.name}" for s in problem.variables]
    cols += ["objective"]
    cols += [f"obj:{n}" for n in problem.objective_names]
    cols += [f"con:{n}" for n in problem.constraint_names]
    cols += ["max_violation", "feasible", "success", "status", "message",
             "n_evals", "nit", "gh_solve_time_s", "time_s", "gh_errors"]
    return cols


def csv_row(run: int, problem: GrasshopperProblem, p: np.ndarray, x0: np.ndarray,
            ev: Optional[Evaluation], res: Optional[RunResult], feasible: bool,
            elapsed: float, message_override: Optional[str] = None) -> List:
    def fmt(v):
        if v is None:
            return ""
        if isinstance(v, (float, np.floating)):
            return "" if not np.isfinite(v) else repr(float(v))
        return v

    nan_n = [float("nan")] * problem.n
    row: List = [run]
    row += [fmt(v) for v in (p if p is not None and len(p) else [])]
    row += [fmt(v) for v in x0]
    row += [fmt(v) for v in (ev.x if ev is not None else nan_n)]
    row += [fmt(ev.objective if ev is not None else float("nan"))]
    row += [fmt(v) for v in (ev.objectives if ev is not None else [float("nan")] * len(problem.objective_names))]
    row += [fmt(v) for v in (ev.constraints if ev is not None else [float("nan")] * problem.m)]
    max_v = ev.max_violation if ev is not None else float("inf")
    row += [fmt(max_v) if np.isfinite(max_v) else "inf",
            int(bool(feasible)),
            int(bool(res.success)) if res is not None else 0,
            res.status if res is not None else -100,
            message_override if message_override is not None else (res.message if res is not None else ""),
            problem.n_evals,
            res.nit if res is not None and res.nit is not None else "",
            fmt(problem.solve_time),
            fmt(elapsed),
            " | ".join(ev.gh_errors) if ev is not None else ""]
    return row


# ============================================================================ main


DEFAULT_CSV_NAME = "optimization_samples.csv"


def resolve_out_path(cli_out: Optional[str], component_out: Optional[str]) -> Tuple[str, str]:
    """Pick the CSV path: --out first, then the component's Output Path, then the default.

    A folder (existing directory, trailing slash, or a path without a file extension) gets the
    default file name. Returns (absolute path, source description)."""
    if cli_out:
        path, source = cli_out, "--out"
    elif component_out:
        path, source = component_out, "component Output Path"
    else:
        path, source = DEFAULT_CSV_NAME, "default"
    path = os.path.expanduser(os.path.expandvars(path.strip().strip('"')))
    if os.path.isdir(path) or path.endswith(("/", "\\")) or not os.path.splitext(path)[1]:
        path = os.path.join(path, DEFAULT_CSV_NAME)
    return os.path.abspath(path), source


def parse_args(argv=None) -> argparse.Namespace:
    ap = argparse.ArgumentParser(
        description="Sample many constrained optimization runs of a Grasshopper definition "
                    "through the BSE Optimization Sampler component and save the results to CSV.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8765, help="must match the component's Port input")
    ap.add_argument("--out", default=None,
                    help="output CSV file or folder. Overrides the component's Output Path input; if neither is set, "
                         f"{DEFAULT_CSV_NAME} is written to the current folder")
    ap.add_argument("--append", action="store_true", help="append to an existing CSV instead of overwriting")
    ap.add_argument("--plan", default=None, metavar="FILE",
                    help="run this sampling plan CSV (from the Sampling Plan component, sampling_plan.py, edited, or "
                         "hand-made) instead of the plan connected in Grasshopper. Without either, a plan is generated "
                         "from the sampling-plan options below. The plan that runs is saved as <out>.plan.csv")
    ap.add_argument("--ignore-component-plan", action="store_true",
                    help="generate the plan from the options below even if a Sampling Plan is connected in Grasshopper")
    ap.add_argument("--samples-csv", dest="plan", help=argparse.SUPPRESS)  # older name for --plan
    ap.add_argument("--resume", action="store_true",
                    help="continue an interrupted study: skip plan runs already in the output CSV and append the "
                         "rest. Uses --plan, the connected Sampling Plan, or else the <out>.plan.csv saved by the "
                         "interrupted study")
    add_plan_arguments(ap, "sampling plan (used only when neither --plan nor a connected Sampling Plan is used)")
    ap.add_argument("--algorithm", default="COBYLA",
                    help="scipy method (COBYLA, COBYQA, SLSQP, trust-constr) or nlopt:<ALGO> (e.g. nlopt:LN_COBYLA)")
    ap.add_argument("--maxiter", type=int, default=500, help="max iterations / evaluations per run")
    ap.add_argument("--tol", type=float, default=1e-4, help="optimizer convergence tolerance")
    ap.add_argument("--rhobeg", type=float, default=0.1,
                    help="initial step for COBYLA/COBYQA/nlopt, as a fraction of each variable's slider range")
    ap.add_argument("--fd-step", type=float, default=1e-3,
                    help="finite-difference step for gradient methods (normalized units); keep larger than slider rounding")
    ap.add_argument("--feas-tol", type=float, default=1e-4, help="absolute constraint violation tolerated as feasible")
    ap.add_argument("--eq-tol", type=float, default=1e-3, help="tolerance band for '=' constraints")
    ap.add_argument("--nan-penalty", type=float, default=1e12,
                    help="objective used when the definition returns no/invalid objective")
    ap.add_argument("--fail-on-gh-error", action="store_true",
                    help="treat evaluations where any Grasshopper component has an error as failed")
    ap.add_argument("--timeout", type=float, default=600.0, help="seconds to wait for one Grasshopper solution")
    ap.add_argument("--no-restore", action="store_true", help="do not restore the original slider values at the end")
    ap.add_argument("--quiet", action="store_true")
    return ap.parse_args(argv)


def main(argv=None) -> int:
    args = parse_args(argv)
    if args.list_sampling:
        print_sampling_methods()
        return 0
    log = (lambda *a: None) if args.quiet else (lambda *a: print(*a, flush=True))
    cli_sampling = normalize_sampling(args.sampling)  # fail fast on a bad name
    optimizer = make_optimizer(args.algorithm)  # fail fast on a bad algorithm name

    with GrasshopperClient(args.host, args.port, timeout=args.timeout) as client:
        info = client.info()
        problem = GrasshopperProblem(client, info, args.nan_penalty, args.fail_on_gh_error)
        original_x = np.array([s.value for s in problem.variables])
        original_p = np.array([s.value for s in problem.parameters])

        log(f"Connected to '{info.get('document', '')}' on {args.host}:{args.port}")
        log("  variables : " + ", ".join(f"{s.name}[{s.lo:g}, {s.hi:g}]" for s in problem.variables))
        log("  parameters: " + (", ".join(f"{s.name}[{s.lo:g}, {s.hi:g}]" for s in problem.parameters) or "(none)"))
        log(f"  objectives: {problem.objective_names} weights={problem.weights.tolist()}")
        log(f"  {problem.m} constraint(s)")
        for s in problem.variables + problem.parameters:
            resolution = 10.0 ** (-s.decimals) / max(s.hi - s.lo, 1e-300)
            if not s.integer and resolution > 1e-4:
                log(f"  WARNING: slider '{s.name}' rounds to {s.decimals} decimal place(s) "
                    f"({resolution:.1e} of its range). Rounding makes the objective stepped and can stop "
                    "optimizers early; increase the slider's decimal places (5+ recommended).")

        # ---- output files
        out_path, out_source = resolve_out_path(args.out, info.get("output_path"))
        log(f"  output    : {out_path} (from {out_source})")
        out_dir = os.path.dirname(out_path)
        os.makedirs(out_dir, exist_ok=True)
        out_stem = os.path.splitext(out_path)[0]
        header = csv_header(problem)
        exists = os.path.exists(out_path) and os.path.getsize(out_path) > 0
        append = (args.append or args.resume) and exists
        existing_runs: set = set()
        run_offset = 0
        if append:
            with open(out_path, newline="", encoding="utf-8") as fh:
                existing = list(csv.reader(fh))
            if existing[0] != header:
                raise ValueError(f"Cannot append: {out_path} has different columns than the current definition.")
            existing_runs = {int(r[0]) for r in existing[1:] if r and r[0].strip().lstrip("-").isdigit()}
            run_offset = (max(existing_runs) + 1) if existing_runs else 0

        # ---- sampling plan (step 1): read it, or generate and save it
        # priority: --plan file > Sampling Plan connected in Grasshopper > saved plan (resume) > generate
        plan_file = args.plan
        component_plan = None if (plan_file or args.ignore_component_plan) else info.get("plan")
        if args.resume and not plan_file and not component_plan:
            plan_file = out_stem + ".plan.csv"
            if not os.path.exists(plan_file):
                raise SystemExit(f"--resume needs the study's plan: pass --plan, connect the Sampling Plan, "
                                 f"or keep {plan_file} next to the results.")
        if plan_file:
            plan: Plan = read_plan(plan_file, problem.variables, problem.parameters)
            plan_path = os.path.abspath(plan_file)
        elif component_plan:
            plan = plan_from_info(component_plan, problem.variables, problem.parameters)
            if append and not args.resume and run_offset:
                plan.runs = [r + run_offset for r in plan.runs]
                plan.notes.append(f"Run numbers shifted by {run_offset} to follow the existing results (--append).")
            plan_path = out_stem + (f".plan.{run_offset}.csv" if append and not args.resume else ".plan.csv")
            write_plan(plan, plan_path, info.get("document", ""))
            if args.sampling or args.grid_levels:
                log("  note      : using the Sampling Plan from Grasshopper; --sampling/--grid-levels are ignored "
                    "(use --ignore-component-plan to generate one here instead).")
        else:
            sampling, sampling_source = resolve_sampling(cli_sampling, info.get("sampling_method"))
            log(f"  sampling  : {sampling} (from {sampling_source})")
            # appended batches get their own seed stream and run numbers, so samples are not repeated
            plan = make_plan(problem.variables, problem.parameters, args.samples, sampling, args.start, args.seed,
                             args.grid_levels, seed_offset=run_offset, run_offset=run_offset)
            plan_path = out_stem + (f".plan.{run_offset}.csv" if append else ".plan.csv")
            write_plan(plan, plan_path, info.get("document", ""))
        log_plan(plan, log)
        if not plan_file:
            log(f"  plan saved: {plan_path}")
        todo = [k for k, run in enumerate(plan.runs) if not (args.resume and run in existing_runs)]
        if args.resume:
            log(f"  resume    : {len(plan) - len(todo)} run(s) already done, {len(todo)} to go")
        elif append and plan_file and set(plan.runs) & existing_runs:
            log("  WARNING: some plan run numbers already exist in the output CSV (use --resume to skip them).")
        n_runs = len(todo)
        n_par = len(problem.parameters)

        meta = {
            "document": info.get("document", ""),
            "created": time.strftime("%Y-%m-%d %H:%M:%S"),
            "variables": [vars(s) for s in problem.variables],
            "parameters": [vars(s) for s in problem.parameters],
            "objective_names": problem.objective_names,
            "weights": problem.weights.tolist(),
            "constraint_names": problem.constraint_names,
            "limits": problem.limits.tolist(),
            "types": problem.types,
            "settings": {k: v for k, v in vars(args).items()},
            "plan": {"path": plan_path, **plan.describe()},
        }
        meta_path = os.path.splitext(out_path)[0] + (f".meta.{run_offset}.json" if append else ".meta.json")
        with open(meta_path, "w", encoding="utf-8") as fh:
            json.dump(meta, fh, indent=2, default=str)

        n_feasible = 0
        t_all = time.perf_counter()
        try:
            with open(out_path, "a" if append else "w", newline="", encoding="utf-8") as fh:
                writer = csv.writer(fh)
                if not append:
                    writer.writerow(header)
                    fh.flush()
                for i, k in enumerate(todo):
                    run_id = plan.runs[k]
                    p = plan.P[k] if n_par else np.zeros(0)
                    x0 = plan.X0[k]  # already inside the slider bounds
                    u0 = problem.to_u(x0)
                    problem.reset_run(p if n_par else None)
                    t0 = time.perf_counter()
                    res: Optional[RunResult] = None
                    ev: Optional[Evaluation] = None
                    msg_override = None
                    try:
                        res = optimizer(problem, u0, args)
                        ev = problem.evaluate_x(problem.to_x(res.u))  # final state (cached if already evaluated)
                    except (ConnectionError, KeyboardInterrupt):
                        raise
                    except Exception as err:  # keep sampling even if one run fails
                        msg_override = f"ERROR: {type(err).__name__}: {err}"
                        if problem.history:
                            ev = problem.history[-1]
                    elapsed = time.perf_counter() - t0
                    # report the parameter values actually applied (after slider rounding)
                    p_rep = ev.p if (ev is not None and n_par) else p
                    feasible = ev is not None and not problem._failed(ev) and ev.max_violation <= args.feas_tol
                    n_feasible += int(feasible)
                    writer.writerow(csv_row(run_id, problem, p_rep, x0, ev, res, feasible, elapsed, msg_override))
                    fh.flush()
                    obj_txt = f"{ev.objective:.6g}" if ev is not None and math.isfinite(ev.objective) else "nan"
                    log(f"[{i + 1}/{n_runs}] run {run_id}: obj={obj_txt} feasible={feasible} "
                        f"success={res.success if res else False} status={res.status if res else -100} "
                        f"evals={problem.n_evals} time={elapsed:.2f}s"
                        + (f"  {msg_override}" if msg_override else ""))
        except KeyboardInterrupt:
            log("Interrupted - rows completed so far are saved.")
        finally:
            if not args.no_restore:
                try:
                    client.evaluate(original_x.tolist(), original_p.tolist() if n_par else None, count=False)
                except Exception as err:
                    log(f"Could not restore slider values: {err}")

        log(f"Done: {n_feasible} feasible run(s), total {time.perf_counter() - t_all:.1f}s -> {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
