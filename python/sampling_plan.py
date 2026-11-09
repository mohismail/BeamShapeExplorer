#!/usr/bin/env python3
"""
BSE Sampling Plan - step 1 of the optimization sampling workflow
================================================================

Creates the *sampling plan* (design of experiments) for a batch of optimization runs, separately
from running the optimizations. A plan is a CSV with one row per optimization run:

    run, param:<name> ..., x0:<name> ...

* ``param:<name>`` - value of each Sample Parameter slider, held fixed during that run
* ``x0:<name>``    - start value of each Variable slider for that run

The plan can be reviewed, plotted, edited, written by hand or by another tool, then run with:

    python bse_optimization_sampler.py --plan sampling_plan.csv

Create a plan from the open Grasshopper definition (the Optimization Sampler component's
Listen input must be True; nothing in the definition is changed):

    python sampling_plan.py --samples 200 --sampling sobol --out sampling_plan.csv

or offline, from the bounds saved in an earlier plan's or results' .meta.json file:

    python sampling_plan.py --from-meta runs.meta.json --samples 200 --out plan2.csv
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import os
import sys
import time
from dataclasses import dataclass, field
from typing import Callable, List, Optional, Sequence

import numpy as np


# ============================================================================ sliders


@dataclass
class Slider:
    name: str
    lo: float
    hi: float
    value: float
    decimals: int = 3
    integer: bool = False

    @classmethod
    def from_info(cls, d: dict) -> "Slider":
        """Build from the component's info reply (min/max) or from a saved .meta.json (lo/hi)."""
        lo = float(d["min"] if "min" in d else d["lo"])
        hi = float(d["max"] if "max" in d else d["hi"])
        if hi < lo:
            lo, hi = hi, lo
        value = d.get("value")
        value = 0.5 * (lo + hi) if value is None else float(value)
        return cls(d["name"], lo, hi, value, int(d.get("decimals", 3)), bool(d.get("integer", False)))


# ============================================================================ sampling methods



def latin_hypercube(n: int, d: int, rng: np.random.Generator) -> np.ndarray:
    """Random Latin Hypercube sample in [0, 1)^d with n points (one point per stratum per dimension)."""
    if d == 0:
        return np.zeros((n, 0))
    u = (rng.random((n, d)) + np.arange(n)[:, None]) / n
    for j in range(d):
        u[:, j] = u[rng.permutation(n), j]
    return u


SAMPLING_METHODS = {
    "lhs": "Latin Hypercube, random point inside each stratum (the method used in the BSE paper)",
    "lhs-optimized": "Latin Hypercube optimized for space filling (lower centered discrepancy)",
    "lhs-centered": "Latin Hypercube with every point at the center of its stratum",
    "sobol": "Scrambled Sobol low-discrepancy sequence (best with a power-of-2 sample count)",
    "halton": "Scrambled Halton low-discrepancy sequence",
    "random": "Uniform random (Monte Carlo)",
    "grid": "Full-factorial grid including the slider bounds (levels^dimensions runs)",
}
_SAMPLING_ALIASES = {"latin": "lhs", "latin-hypercube": "lhs", "lhs-maximin": "lhs-optimized",
                     "optimized-lhs": "lhs-optimized", "centered-lhs": "lhs-centered", "monte-carlo": "random",
                     "montecarlo": "random", "uniform": "random", "mc": "random", "factorial": "grid",
                     "full-factorial": "grid"}


def normalize_sampling(name: Optional[str]) -> Optional[str]:
    if not name or not str(name).strip():
        return None
    key = str(name).strip().lower().replace("_", "-").replace(" ", "-")
    key = _SAMPLING_ALIASES.get(key, key)
    if key not in SAMPLING_METHODS:
        raise SystemExit(f"Unknown sampling method '{name}'. Choose one of: {', '.join(SAMPLING_METHODS)}.")
    return key


def _qmc_engine(cls, d: int, rng: np.random.Generator, **kwargs):
    seed = int(rng.integers(2 ** 32))
    try:
        return cls(d, rng=seed, **kwargs)   # scipy >= 1.15
    except TypeError:
        return cls(d, seed=seed, **kwargs)  # older scipy


def grid_levels(levels_arg: Optional[str], n_target: int, d: int) -> List[int]:
    """Levels per dimension for a full-factorial grid: from --grid-levels, or the largest even split of --samples."""
    if levels_arg:
        levels = [int(v) for v in str(levels_arg).replace(";", ",").split(",") if v.strip()]
        if len(levels) == 1:
            levels = levels * d
        if len(levels) != d or any(v < 1 for v in levels):
            raise SystemExit(f"--grid-levels needs one value or {d} comma-separated values >= 1.")
        return levels
    k = max(2, int(math.floor(n_target ** (1.0 / d) + 1e-9))) if d else 1
    return [k] * d


def sample_unit(method: str, n: int, d: int, rng: np.random.Generator,
                levels: Optional[List[int]] = None) -> np.ndarray:
    """Sample points in the unit hypercube [0, 1]^d. For 'grid' the number of rows is prod(levels), not n."""
    if method == "grid":
        if d == 0:
            return np.zeros((n, 0))
        axes = [np.linspace(0.0, 1.0, k) if k > 1 else np.array([0.5]) for k in levels]
        mesh = np.meshgrid(*axes, indexing="ij")
        return np.stack([m.ravel() for m in mesh], axis=1)
    if d == 0:
        return np.zeros((n, 0))
    if method == "lhs":
        return latin_hypercube(n, d, rng)
    if method == "random":
        return rng.random((n, d))
    try:
        from scipy.stats import qmc
    except ImportError as err:  # scipy too old
        raise SystemExit(f"Sampling method '{method}' needs scipy >= 1.8 (scipy.stats.qmc).") from err
    if method == "lhs-optimized":
        engine = _qmc_engine(qmc.LatinHypercube, d, rng, optimization="random-cd")
    elif method == "lhs-centered":
        engine = _qmc_engine(qmc.LatinHypercube, d, rng, scramble=False)
    elif method == "sobol":
        engine = _qmc_engine(qmc.Sobol, d, rng, scramble=True)
    elif method == "halton":
        engine = _qmc_engine(qmc.Halton, d, rng, scramble=True)
    else:
        raise SystemExit(f"Unknown sampling method '{method}'.")
    import warnings
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")  # Sobol warns when n is not a power of 2
        return np.asarray(engine.random(n), dtype=float)


def scale(u: np.ndarray, sliders: Sequence[Slider]) -> np.ndarray:
    if not sliders:
        return np.zeros((u.shape[0], 0))
    lo = np.array([s.lo for s in sliders])
    hi = np.array([s.hi for s in sliders])
    return lo + u * (hi - lo)


# ============================================================================ plan


START_MODES = ("auto", "sampled", "lhs", "random", "center", "current")


@dataclass
class Plan:
    """One row per optimization run: the fixed parameter values and the variable start point."""
    variables: List[Slider]
    parameters: List[Slider]
    runs: List[int]
    P: np.ndarray                 # (runs, parameters)
    X0: np.ndarray                # (runs, variables)
    method: str = "file"
    start: str = "file"
    seed: Optional[int] = None
    grid_levels: Optional[List[int]] = None
    source: str = "generated"
    notes: List[str] = field(default_factory=list)

    def __len__(self) -> int:
        return len(self.runs)

    def describe(self) -> dict:
        return {"source": self.source, "runs": len(self.runs), "method": self.method, "start": self.start,
                "seed": self.seed, "grid_levels": self.grid_levels}


def make_plan(variables: Sequence[Slider], parameters: Sequence[Slider], samples: int, method: str = "lhs",
              start: str = "auto", seed: int = 0, grid_levels_arg: Optional[str] = None,
              seed_offset: int = 0, run_offset: int = 0) -> Plan:
    """Generate a sampling plan inside the slider bounds.

    Parameters and 'sampled' start points are drawn jointly with the chosen method, so the combined
    design is space-filling. ``seed_offset`` gives appended batches their own random stream."""
    variables, parameters = list(variables), list(parameters)
    method = normalize_sampling(method) or "lhs"
    if start not in START_MODES:
        raise SystemExit(f"Unknown start mode '{start}'. Use one of: {', '.join(START_MODES)}.")
    n_par, n_var = len(parameters), len(variables)
    start = "sampled" if start == "lhs" else start
    if start == "auto":
        start = "current" if n_par > 0 else "sampled"
    if samples < 1 and method != "grid":
        raise SystemExit("--samples must be at least 1.")

    rng = np.random.default_rng([seed, seed_offset])
    notes: List[str] = []
    d_joint = n_par + (n_var if start == "sampled" else 0)
    n_runs, levels = samples, None
    if method == "grid":
        if d_joint == 0:
            raise SystemExit("Grid sampling needs Sample Parameters or --start sampled.")
        levels = grid_levels(grid_levels_arg, samples, d_joint)
        n_runs = int(np.prod(levels))
    if method == "sobol" and n_runs & (n_runs - 1):
        notes.append(f"Sobol sequences are most uniform when the sample count is a power of 2 "
                     f"(e.g. {1 << max(0, n_runs.bit_length() - 1)} or {1 << n_runs.bit_length()}).")
    if d_joint == 0 and n_runs > 1:
        notes.append("There are no Sample Parameters and the start point is fixed, so every run is identical. "
                     "Use --start sampled for a multi-start study.")

    U = sample_unit(method, n_runs, d_joint, rng, levels)
    P = scale(U[:, :n_par], parameters)
    if start == "sampled":
        X0 = scale(U[:, n_par:n_par + n_var], variables)
    elif start == "random":
        X0 = scale(rng.random((n_runs, n_var)), variables)
    elif start == "center":
        X0 = scale(np.full((n_runs, n_var), 0.5), variables)
    else:  # current slider values
        X0 = np.tile([s.value for s in variables], (n_runs, 1)).astype(float)
    return Plan(variables, parameters, list(range(run_offset, run_offset + n_runs)), P, X0,
                method=method, start=start, seed=seed, grid_levels=levels, notes=notes)


def plan_header(plan: Plan) -> List[str]:
    return (["run"] + [f"param:{s.name}" for s in plan.parameters]
            + [f"x0:{s.name}" for s in plan.variables])


def plan_meta_path(plan_path: str) -> str:
    return os.path.splitext(plan_path)[0] + ".meta.json"


def write_plan(plan: Plan, path: str, document: str = "") -> str:
    """Write the plan CSV and a .meta.json next to it (bounds and settings). Returns the CSV path."""
    path = os.path.abspath(path)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(plan_header(plan))
        for k, run in enumerate(plan.runs):
            w.writerow([run] + [repr(float(v)) for v in plan.P[k]] + [repr(float(v)) for v in plan.X0[k]])
    meta = {
        "kind": "sampling_plan",
        "document": document,
        "created": time.strftime("%Y-%m-%d %H:%M:%S"),
        "variables": [vars(s) for s in plan.variables],
        "parameters": [vars(s) for s in plan.parameters],
        **plan.describe(),
    }
    with open(plan_meta_path(path), "w", encoding="utf-8") as fh:
        json.dump(meta, fh, indent=2)
    return path


def read_plan(path: str, variables: Sequence[Slider], parameters: Sequence[Slider]) -> Plan:
    """Read and check a plan CSV against the definition's sliders.

    Parameter columns may be named ``param:<name>`` or just ``<name>``; start-point columns are
    ``x0:<name>``. Missing or blank start values use the current slider value. Values outside the
    slider bounds are clipped (the sliders would clamp them anyway) and reported in ``plan.notes``."""
    variables, parameters = list(variables), list(parameters)
    if not os.path.exists(path):
        raise SystemExit(f"Plan file not found: {path}")
    with open(path, newline="", encoding="utf-8-sig") as fh:
        reader = csv.DictReader(fh)
        columns = [c.strip() for c in (reader.fieldnames or [])]
        rows = [{(k or "").strip(): (v or "").strip() for k, v in r.items()} for r in reader]
    rows = [r for r in rows if any(r.values())]
    if not rows:
        raise SystemExit(f"Plan file {path} has no rows.")
    notes: List[str] = []

    def column(s: Slider, prefix: str, allow_bare: bool) -> Optional[str]:
        if f"{prefix}{s.name}" in columns:
            return f"{prefix}{s.name}"
        if allow_bare and s.name in columns:
            return s.name
        return None

    p_cols = [column(s, "param:", True) for s in parameters]
    missing = [s.name for s, c in zip(parameters, p_cols) if c is None]
    if missing:
        raise SystemExit(f"Plan {path} has no column for Sample Parameter(s) {missing}. "
                         f"Expected columns named param:<name> (or <name>).")
    x_cols = [column(s, "x0:", False) for s in variables]
    no_start = [s.name for s, c in zip(variables, x_cols) if c is None]
    if no_start:
        notes.append(f"No x0 column for {no_start}; those runs start from the current slider value.")
    used = {"run"} | {c for c in p_cols + x_cols if c}
    unknown = [c for c in columns if c not in used]
    if unknown:
        notes.append(f"Ignoring unknown plan column(s): {unknown}")

    def number(text: str, what: str, line: int) -> float:
        try:
            return float(text)
        except ValueError:
            raise SystemExit(f"Plan {path}, data row {line}: '{text}' is not a number ({what}).") from None

    runs: List[int] = []
    P = np.zeros((len(rows), len(parameters)))
    X0 = np.zeros((len(rows), len(variables)))
    clipped = 0
    for k, r in enumerate(rows):
        line = k + 1
        runs.append(int(number(r["run"], "run", line)) if r.get("run") else k)
        for j, (s, c) in enumerate(zip(parameters, p_cols)):
            if not r.get(c):
                raise SystemExit(f"Plan {path}, data row {line}: no value for parameter '{s.name}'.")
            P[k, j] = number(r[c], s.name, line)
        for j, (s, c) in enumerate(zip(variables, x_cols)):
            X0[k, j] = number(r[c], "x0:" + s.name, line) if c and r.get(c) else s.value
    for M, sliders in ((P, parameters), (X0, variables)):
        if sliders:
            lo = np.array([s.lo for s in sliders])
            hi = np.array([s.hi for s in sliders])
            clipped += int(np.sum((M < lo) | (M > hi)))
            np.clip(M, lo, hi, out=M)
    if clipped:
        notes.append(f"{clipped} plan value(s) were outside the slider bounds and were clipped to them.")
    if len(set(runs)) != len(runs):
        raise SystemExit(f"Plan {path} has duplicate run numbers.")
    plan = Plan(variables, parameters, runs, P, X0, source=os.path.abspath(path), notes=notes)
    # keep the settings of a plan made by make_plan/write_plan, for the results' .meta.json
    try:
        with open(plan_meta_path(path), encoding="utf-8") as fh:
            meta = json.load(fh)
        if meta.get("kind") == "sampling_plan":
            plan.method, plan.start = meta.get("method", "file"), meta.get("start", "file")
            plan.seed, plan.grid_levels = meta.get("seed"), meta.get("grid_levels")
    except (OSError, ValueError):
        pass
    return plan


# ============================================================================ CLI


def add_plan_arguments(ap: argparse.ArgumentParser, title: str = "sampling plan") -> None:
    """Sampling-plan options shared by this script and the optimization driver."""
    g = ap.add_argument_group(title)
    g.add_argument("--samples", type=int, default=50, help="number of optimization runs in the plan")
    g.add_argument("--sampling", default=None, metavar="METHOD",
                   help="how the Sample Parameters (and sampled start points) are drawn: "
                        + ", ".join(SAMPLING_METHODS) + ". Overrides the component's Sampling Method input; "
                        "default lhs")
    g.add_argument("--grid-levels", default=None,
                   help="for --sampling grid: levels per dimension, one value or a comma list "
                        "(default: the largest grid with at most --samples points, min 2 levels)")
    g.add_argument("--start", choices=START_MODES, default="auto",
                   help="starting point of each run: 'sampled' = drawn with the sampling method together with the "
                        "parameters ('lhs' is an alias); 'auto' = 'current' slider values when there are sample "
                        "parameters, otherwise 'sampled' (multi-start)")
    g.add_argument("--seed", type=int, default=0, help="random seed")
    g.add_argument("--list-sampling", action="store_true", help="list the sampling methods and exit")


def print_sampling_methods() -> None:
    for name, text in SAMPLING_METHODS.items():
        print(f"{name:14s} {text}")


def resolve_sampling(cli_value: Optional[str], component_value: Optional[str]):
    """--sampling first, then the component's Sampling Method, then lhs. Returns (method, source)."""
    if cli_value:
        return normalize_sampling(cli_value), "--sampling"
    if component_value:
        return normalize_sampling(component_value), "component Sampling Method"
    return "lhs", "default"


def log_plan(plan: Plan, log: Callable[..., None], preview: int = 0) -> None:
    if plan.source == "generated":
        log(f"  plan      : {len(plan)} run(s), sampling {plan.method}, start {plan.start}, seed {plan.seed}"
            + (f", grid levels {plan.grid_levels}" if plan.grid_levels else ""))
    else:
        log(f"  plan      : {len(plan)} run(s) from {plan.source}")
    for note in plan.notes:
        log(f"  note      : {note}")
    if preview:
        header = plan_header(plan)
        width = max(10, max(len(h) for h in header))
        log("  " + " ".join(h[:width].rjust(width) for h in header))
        for k in range(min(preview, len(plan))):
            vals = [str(plan.runs[k])] + [f"{v:.6g}" for v in plan.P[k]] + [f"{v:.6g}" for v in plan.X0[k]]
            log("  " + " ".join(v.rjust(width) for v in vals))
        if len(plan) > preview:
            log(f"  ... {len(plan) - preview} more row(s)")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        description="Create a sampling plan (design of experiments) for the BSE Optimization Sampler. "
                    "Run it afterwards with: bse_optimization_sampler.py --plan <file>",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter)
    ap.add_argument("--out", default="sampling_plan.csv", help="plan CSV to write")
    src = ap.add_argument_group("slider bounds (from Grasshopper, or offline from a .meta.json)")
    src.add_argument("--host", default="127.0.0.1")
    src.add_argument("--port", type=int, default=8765, help="must match the component's Port input")
    src.add_argument("--timeout", type=float, default=60.0)
    src.add_argument("--from-meta", default=None, metavar="FILE",
                     help="read the slider names and bounds from a .meta.json written by an earlier plan or "
                          "optimization study, instead of connecting to Grasshopper")
    add_plan_arguments(ap)
    ap.add_argument("--preview", type=int, default=5, help="number of plan rows to print")
    args = ap.parse_args(argv)
    if args.list_sampling:
        print_sampling_methods()
        return 0
    cli_sampling = normalize_sampling(args.sampling)  # fail fast on a bad name

    if args.from_meta:
        with open(args.from_meta, encoding="utf-8") as fh:
            info = json.load(fh)
        origin = f"meta file {os.path.abspath(args.from_meta)}"
    else:
        from bse_optimization_sampler import GrasshopperClient  # only needed when connecting
        with GrasshopperClient(args.host, args.port, timeout=args.timeout) as client:
            info = client.info()
        origin = f"Grasshopper on {args.host}:{args.port}"
    variables = [Slider.from_info(d) for d in info.get("variables", [])]
    parameters = [Slider.from_info(d) for d in info.get("parameters", [])]
    if not variables:
        raise SystemExit(f"No variable sliders found in {origin}.")

    method, method_source = resolve_sampling(cli_sampling, info.get("sampling_method"))
    print(f"Slider bounds from {origin}")
    print("  variables : " + ", ".join(f"{s.name}[{s.lo:g}, {s.hi:g}]" for s in variables))
    print("  parameters: " + (", ".join(f"{s.name}[{s.lo:g}, {s.hi:g}]" for s in parameters) or "(none)"))
    print(f"  sampling  : {method} (from {method_source})")
    plan = make_plan(variables, parameters, args.samples, method, args.start, args.seed, args.grid_levels)
    log_plan(plan, print, args.preview)
    out = write_plan(plan, args.out, info.get("document", ""))
    print(f"Wrote {len(plan)} run(s) to {out}")
    print(f"Run it with:  python bse_optimization_sampler.py --plan \"{out}\"")
    return 0


if __name__ == "__main__":
    sys.exit(main())
