# Flexural Capacity and Strain-Compatibility Audit

## Executive finding

The current flexural components are not general strain-compatibility solvers. They assume one tensile resultant acting at full `fy` or `0.87 fy`, locate an equivalent concrete compression area, and multiply the assumed force by a discretely selected lever arm.

Consequently, reinforcement stiffness, yield strain, hardening, rupture strain, compression reinforcement, multiple material types, and layer-specific strains do not affect calculated capacity. This blocks reliable capacity analysis for HSS/stainless, FRP, and hybrid reinforcement. Even conventional steel is reliable only if the user independently establishes that the tensile reinforcement yields before concrete crushing.

The plugin does not identify supported IS 456 or ACI 318 editions, so this audit describes implemented equation families and does not certify code compliance.

## Implemented model

### Material representation

`MaterialProperties` stores one concrete set and one reinforcement set:

```text
(fc, Ec, ec, rhoC, EEC)
(fy, Es, es, rhoS, EES)
```

There are no fields for ultimate strength, proof stress, hardening modulus, rupture strain separate from a ductility threshold, tension/compression asymmetry, constitutive-law type, or multiple reinforcement materials.

### Reinforcement limits

Both flexural components calculate:

```text
cmax / d = ec / (ec + es)
```

and code-specific `rhoMax`. These values do not participate in final moment capacity. `Ec` and `Es` are extracted but unused, so stiffness has no effect on `Mn`.

The unbounded ACI-style `beta1` is also used outside its expected range:

```text
beta1 = 0.85 - 0.05 (fc - 28) / 7
```

### Compression and tension resultants

Both code settings use:

```text
Ac,target = As (0.87 fy) / (0.36 fc)
```

The final tensile force alone branches:

```text
IS:  T = 0.87 As fy
ACI: T = As fy
```

The ACI path therefore uses a compression area inconsistent with its tensile force and never verifies equilibrium.

### Geometry solution

`FlexuralAnalysis.cs:131-216`:

1. Creates planar concrete and reinforcement Breps.
2. Infers depth from a surface isocurve and world Z.
3. Creates `m` discrete cuts.
4. Selects the first split area exceeding the target.
5. Uses `z = zSteel - zCompression`.
6. Returns `Mn = T z`.

`FlexAnalysis2.cs:125-218` uses a bounding-box edge and trimming planes. It introduces additional defects:

- disconnected-region centroids are averaged without area weighting;
- `z = |zCompression| - |zSteel|` depends on the world origin;
- section orientation uses `Mu[i]` instead of the expanded `newMu`, so one moment with multiple sections can throw;
- failed trimming can leave a null collection that is later enumerated.

`BeamSections.cs:124-217` auto-sizes with the same IS-style design expression under both codes and represents total reinforcement as one equivalent circular region at cover depth.

## Applicability matrix

| Capability | Conventional steel | HSS / stainless | FRP | Hybrid |
|---|---|---|---|---|
| Known yielded tensile resultant | Caution | Blocker | Not applicable | Blocker |
| Stiffness affects equilibrium | Blocker | Blocker | Blocker | Blocker |
| Elastic stress below yield/proof stress | Blocker | Blocker | Blocker | Blocker |
| Yield plateau | Caution | Blocker | Not applicable | Blocker |
| Hardening or rounded response | Blocker | Blocker | Not applicable | Blocker |
| Rupture strain limit | Blocker | Blocker | Blocker | Blocker |
| Concrete crushing versus reinforcement failure | Blocker | Blocker | Blocker | Blocker |
| Single reinforcement region/resultant | Caution | Caution | Caution | Blocker |
| Multiple or compression layers | Blocker | Blocker | Blocker | Blocker |
| Translation invariance, V1 | OK | OK | OK | OK |
| Translation invariance, V2 | Blocker | Blocker | Blocker | Blocker |
| Arbitrary bending axis | Blocker | Blocker | Blocker | Blocker |
| Convex shaped section with convergence study | Caution | Caution | Caution | Blocker |
| Non-convex/disconnected compression zone | Blocker | Blocker | Blocker | Blocker |
| Explicit nominal/design distinction | Blocker | Blocker | Blocker | Blocker |

## Failure-mode implications

### Conventional steel

The solver jumps directly to yielded tension steel. It does not determine whether concrete crushes while steel is still elastic, account for hardening, or check steel rupture. Over-reinforced sections can be overestimated.

### HSS and stainless

Concrete may crush before high-strength reinforcement reaches yield or proof stress. Stainless response can be rounded rather than plateau-based. Because `Es` is ignored and only `fy` is used, these states cannot be represented.

### FRP

FRP stress commonly follows:

```text
ff = Ef epsilonF, for |epsilonF| <= epsilonFu
```

with rupture at its strain limit. Substituting FRP rupture strength into `fy` applies full rupture stress regardless of compatible strain and cannot distinguish concrete-crushing from FRP-rupture control.

### Hybrid reinforcement

At one section state, steel may be yielded, HSS elastic/hardening, and FRP elastic or ruptured. A single aggregate `As`, centroid, `fy`, `Es`, and `es` cannot represent this. Each layer must be evaluated under a common strain plane.

## Geometry and sign risks

- Compression orientation depends on world Z rather than a supplied local bending axis.
- V1 selects `splitSrfs[1]` without proving it is the compression side.
- Reinforcement is always treated as tension regardless of neutral-axis location.
- The first area above target is selected without interpolation or convergence tolerance.
- `BeamSections` keeps only the first contour/Brep at each station, risking loss of holes or disconnected parts.
- Section and beam builders do not fully validate topology, point counts, or self-intersection.

## Remediation options

### 1. Full strain-compatibility solver — recommended

Implement plane-section compatibility, layer-specific constitutive laws, concrete integration, axial equilibrium, and explicit failure detection.

Required inputs:

- section geometry, including holes/disconnected regions;
- section-local origin and bending axis;
- target axial force, default zero;
- moment direction/sign;
- concrete constitutive law or explicit block parameters;
- reinforcement layers with area, local position, material ID, and optional prestrain;
- per-material modulus, yield/proof strength, hardening/curve data, tension/compression strain limits, and rupture behavior;
- nominal or design analysis mode and explicit resistance/partial factors;
- equilibrium and geometry tolerances.

Required outputs:

- neutral axis, curvature, and reference strain;
- concrete force and centroid;
- per-layer strain, stress, force, and state;
- internal axial force/moment;
- nominal capacity and separately identified design resistance;
- governing failure mode;
- equilibrium residual, integration error, and convergence status.

Recommended numerical approach:

1. Express section strain as `epsilon(y) = epsilon0 + kappa y`.
2. Evaluate reinforcement layer stresses from their constitutive laws.
3. Integrate concrete stress over clipped polygons or adaptive fibers.
4. Solve `Ninternal - Ntarget = 0`.
5. Partition searches at yield/proof/rupture events.
6. Use bracketed Brent or bisection iterations.
7. Evaluate concrete crushing and reinforcement rupture candidate states.
8. Select the first physically reachable limit state.
9. Refine concrete integration until force and moment changes satisfy tolerance.

### 2. Externally supplied stress — temporary only

Allow a caller to supply a compatible reinforcement stress:

```text
T = As fOverride
Ac,target = T / fc,block
```

This can repair force consistency for one external resultant, but it remains incapable of identifying failure modes, handling multiple layers/materials, or enforcing compatible strain. It must be labeled an externally supplied resultant, not an alternative-reinforcement capacity solver.

## Deterministic benchmarks

The following use explicit solver-test block inputs `alpha = 0.85`, `beta = 0.85`, `fc = 30 MPa`, and `epsilonCu = 0.003`. They are not code-edition claims.

### B1: yielded conventional steel

- Rectangle `300 × 500 mm`
- `As = 2000 mm²` at `d = 450 mm`
- `Es = 200,000 MPa`, `fy = 500 MPa`

Expected:

```text
a = 130.718954 mm
c = 153.787005 mm
epsilonS = 0.005778375 > fy / Es
Mn = 384.640523 kN-m
```

### B2: high-strength reinforcement remains elastic

- Same section and `d`
- `As = 6000 mm²`
- `Es = 200,000 MPa`, `fy = 1000 MPa`

Expected at concrete crushing:

```text
c = 293.939366 mm
epsilonS = 0.001592784
fs = 318.556788 MPa
Mn = 621.330558 kN-m
```

The current solver incorrectly uses `1000 MPa`.

### B3: balanced FRP

- Same section and `d`
- `Ef = 50,000 MPa`, `epsilonFu = 0.020`, `ffu = 1000 MPa`
- `Af = 381.668478 mm²`

Expected:

```text
c = 58.695652 mm
a = 49.891304 mm
Mn = 162.229845 kN-m
```

Reducing `Af` by 5% must classify FRP rupture first; increasing it by 5% must classify concrete crushing first.

### B4: hybrid reinforcement

- Steel: `As = 1000 mm²`, `d = 450 mm`, `Es = 200,000 MPa`, `fy = 500 MPa`
- FRP: `Af = 200 mm²`, `d = 400 mm`, `Ef = 50,000 MPa`, `epsilonFu = 0.020`

Expected at concrete crushing:

```text
c = 92.278523 mm
steel stress = 500 MPa
FRP stress = 500.205468 MPa
C = T = 600.041094 kN
Mn = 241.483803 kN-m
```

### Additional invariance and geometry cases

- Two coincident identical layers must equal one layer with summed area.
- Rigid translation and rotation with the supplied local axis must not change capacity magnitude or material states.
- Mirroring reinforcement and reversing moment sign must transform signed moment consistently.
- L-, T-, and U-sections must match independently calculated clipped areas and area-weighted centroids.
- Successive nonlinear concrete fiber refinements (`h/20`, `h/40`, `h/80`, `h/160`) must converge or return an explicit non-convergence diagnostic.

## Acceptance tolerances

- Axial equilibrium: `|RN| <= max(1e-3 N, 1e-8 sum(|Fi|))`
- Analytic rectangle capacity: relative error `<= 1e-6`, absolute error `<= 1e-3 kN-m`
- Layer stress/strain: relative error `<= 1e-7`
- Exact polygon area/centroid: relative error `<= 1e-8`, subject to geometry tolerance
- Final fiber-refinement capacity change: `<= 1e-4` relative
- Rigid-motion invariance: `<= 1e-9` analytic or `<= 1e-7` for tolerance-based Breps
- Failure-mode label must match exactly; no layer may exceed its limit by more than `1e-10`
- Invalid geometry, material, cardinality, or convergence states must return diagnostics rather than exceptions, non-finite values, or silent zero capacity

