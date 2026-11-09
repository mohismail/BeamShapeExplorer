#!/usr/bin/env python3
"""
Mock of the Grasshopper "Optimization Sampler" component, for testing the Python driver without Rhino.

It speaks the same newline-delimited JSON protocol and mimics slider behaviour (clamping to Min/Max
and rounding to the slider's decimal places). The toy problem is a simply supported shaped beam:

  variables  : depth h0..h4 (m) at 5 stations along half the span (symmetric)       [0.10, 0.80]
  parameters : span L (m) [4, 12], line load w (kN/m) [5, 40]
  objectives : concrete volume per metre width (m3), "steel" proxy (weighted 0.0 -> recorded only)
  constraints: flexural utilisation Mu/phiMn <= 1 at each station, and h0 >= L/60 (end depth)

Run:  python mock_gh_server.py --port 8765
"""

import argparse
import json
import socketserver

import numpy as np

B = 1.0            # width (m)
FC = 30e3          # kPa
STATIONS = np.linspace(0.05, 0.5, 5)   # fraction of span

VARIABLES = [dict(name=f"h{i}", min=0.10, max=0.80, value=0.50, decimals=3, integer=False) for i in range(5)]
OUTPUT_PATH = ""
PARAMETERS = [dict(name="L", min=4.0, max=12.0, value=8.0, decimals=2, integer=False),
              dict(name="w", min=5.0, max=40.0, value=20.0, decimals=2, integer=False)]


def snap(slider, v):
    v = min(max(float(v), slider["min"]), slider["max"])
    return round(v, 0 if slider["integer"] else slider["decimals"])


def solve():
    h = np.array([s["value"] for s in VARIABLES])
    L, w = (s["value"] for s in PARAMETERS)
    xs = STATIONS * L
    mu = w * xs * (L - xs) / 2.0                      # kNm
    phimn = 0.9 * 0.12 * FC * B * (0.9 * h) ** 2      # kNm, crude capacity ~ h^2
    util = mu / phimn
    # volume of symmetric beam with linear interpolation between stations (+ end at h0)
    xh = np.concatenate([[0.0], xs, [L - x for x in xs[::-1][1:]], [L]])
    hh = np.concatenate([[h[0]], h, h[::-1][1:], [h[0]]])
    volume = B * float(np.sum((hh[1:] + hh[:-1]) * np.diff(xh)) / 2.0)
    steel = float(np.sum(mu / (0.9 * 500e3 * 0.9 * h)))
    objectives = [volume, steel]
    constraints = list(util) + [h[0]]
    limits = [1.0] * 5 + [L / 60.0]
    types = ["<="] * 5 + [">="]
    return objectives, constraints, limits, types


def violations(c, lim, t):
    return [max(0, a - b) if k == "<=" else max(0, b - a) if k == ">=" else abs(a - b) for a, b, k in zip(c, lim, t)]


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        evals = 0
        for raw in self.rfile:
            req = json.loads(raw)
            cmd = req.get("cmd")
            try:
                if cmd == "ping":
                    resp = {"ok": True, "message": "pong"}
                elif cmd == "info":
                    o, c, lim, t = solve()
                    resp = {"ok": True, "document": "mock_shaped_beam", "output_path": OUTPUT_PATH, "variables": VARIABLES,
                            "parameters": PARAMETERS, "objective_names": ["Volume", "Steel"],
                            "weights": [1.0, 0.0], "objectives": o,
                            "constraint_names": [f"util[{i}]" for i in range(5)] + ["h_end"],
                            "limits": lim, "types": t, "constraints": c}
                elif cmd in ("evaluate", "set"):
                    x, p = req.get("variables"), req.get("parameters")
                    if x is not None:
                        if len(x) != len(VARIABLES):
                            raise ValueError("wrong number of variables")
                        for s, v in zip(VARIABLES, x):
                            s["value"] = snap(s, v)
                    if p:
                        for s, v in zip(PARAMETERS, p):
                            s["value"] = snap(s, v)
                    o, c, lim, t = solve()
                    evals += cmd == "evaluate"
                    resp = {"ok": True, "variables": [s["value"] for s in VARIABLES],
                            "parameters": [s["value"] for s in PARAMETERS], "objectives": o,
                            "weights": [1.0, 0.0], "objective": o[0], "constraints": c, "limits": lim,
                            "types": t, "violations": violations(c, lim, t), "solve_time": 0.0,
                            "gh_errors": [], "evaluation": evals}
                elif cmd == "close":
                    self.wfile.write(b'{"ok": true, "message": "bye"}\n')
                    return
                else:
                    resp = {"ok": False, "error": f"Unknown cmd {cmd}"}
            except Exception as err:
                resp = {"ok": False, "error": str(err)}
            self.wfile.write((json.dumps(resp) + "\n").encode())


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--output-path", default="", help="mimics the component's Output Path input")
    a = ap.parse_args()
    OUTPUT_PATH = a.output_path
    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("127.0.0.1", a.port), Handler) as srv:
        print(f"mock Optimization Sampler listening on 127.0.0.1:{a.port}", flush=True)
        srv.serve_forever()
