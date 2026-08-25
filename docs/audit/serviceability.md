# Serviceability Audit

## Conclusion

The interpolation formulas are reproducible, and changing `Es/Ec` produces the expected monotonic stiffness trend. However, the inputs to those formulas are not generally valid:

1. Cracked inertia uses an ultimate-strength compression block instead of a service transformed-section neutral axis.
2. Cracking moment and cracked inertia depend on the model's global Z origin.
3. The deflection component analyzes only half the span and assumes symmetry.
4. Alternative and hybrid reinforcement are represented by one scalar `Es`.

The current path is therefore suitable only for cautious comparative exploration, not code-aligned serviceability design.

## Implemented data flow

```text
MaterialProperties (fc, Ec, Es)
  -> BeamSections: one equivalent reinforcement curve
  -> FlexuralAnalysis: ultimate Acomp
  -> IEffective2: Iu, Icr, Mcr, Ieff
  -> DispSS: half-span integration of M / (Ec Ieff)
```

The critical mismatch is using ultimate `Acomp`, derived from:

```text
Acomp,target = As (0.87 fy) / (0.36 fc)
```

as the service-load cracked compression depth.

## Implemented equations

Let `n = Es / Ec`.

### Gross transformed inertia

```text
Iu = Ig + (n - 1) As (d - xu)^2
```

This is translation invariant, but it is evaluated about the concrete centroid rather than the transformed centroid, omits reinforcement centroidal inertia, and assumes the concrete outline already includes reinforcement area.

### Cracked inertia

Standalone:

```text
Icr = b c^3 / 3 + (n - 1) As (|zs| - c)^2
```

Combined:

```text
Icr = b c^3 / 3 + (n - 1) As (zs + c)^2
```

These agree only under an undocumented global-coordinate convention. A service transformed section instead requires:

```text
b c^2 / 2 = n As (d - c)
Icr,ref = b c^3 / 3 + n [As (d - c)^2 + Is]
```

The current calculation also uses `n - 1` after tensile concrete has already been removed.

### Cracking moment

```text
Mcr = 1000 (0.7 sqrt(fc) Ig) / (h + xu)
```

The unit factor is consistent for MPa and metre-based geometry. The denominator equals the extreme-fiber distance only for a particular global origin. A centered rectangle uses `h` rather than `h/2`, halving `Mcr`.

### Effective inertia

Branson:

```text
Ie = Iu (Mcr / M)^3 + Icr [1 - (Mcr / M)^3]
```

Bischoff-Gross implementation:

```text
r = Mcr / M
epsilon = 1 - sqrt(1 - r)
nu = 1 - Icr / Iu
gamma = (1.6 epsilon^3 - 0.6 epsilon^4) / r^2 + 2.4 ln(2 - epsilon)
Ie = Icr / (1 - gamma nu r^2)
```

The formulas are numerically reproducible, but inherit invalid `Icr` and `Mcr`. The older `IEffective` also lacks the uncracked cap used in `IEffective2`.

## Reference section and independent computation

All calculations were independently reproduced with Python's standard library through `uv run python`.

- Rectangle: `b = 0.300 m`, `h = 0.600 m`
- Reinforcement depth: `d = 0.550 m`
- `As = 2000 mm² = 0.002 m²`
- Equivalent circular reinforcement inertia: `Is = As² / (4 pi)`
- `fc = 40 MPa`
- `Ec = 30,000 MPa`
- `fy = 500 MPa`
- Span `L = 6.0 m`
- `Ig = 0.0054 m⁴`
- Reference `Mcr = 79.689397 kN-m`

## Es sensitivity

The table supplies an independently solved service neutral axis to isolate the plugin's inertia formulas. Effective inertias use `M / Mcr = 1.5`.

| Es (GPa) | n | c (m) | Code Iu (m⁴) | Code Icr (m⁴) | Reference Icr (m⁴) | Icr error | Branson Ie (m⁴) | B-G Ie (m⁴) |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 50 | 1.667 | 0.1000 | 0.0054833 | 0.0003700 | 0.0007755 | -52.29% | 0.0018851 | 0.0008188 |
| 75 | 2.500 | 0.1198 | 0.0055875 | 0.0007271 | 0.0010981 | -33.79% | 0.0021672 | 0.0014878 |
| 100 | 3.333 | 0.1357 | 0.0056917 | 0.0010509 | 0.0013952 | -24.68% | 0.0024259 | 0.0020180 |
| 150 | 5.000 | 0.1610 | 0.0059000 | 0.0016279 | 0.0019321 | -15.74% | 0.0028937 | 0.0028342 |
| 205 | 6.833 | 0.1829 | 0.0061292 | 0.0021841 | 0.0024558 | -11.06% | 0.0033530 | 0.0035132 |

Both methods predict decreasing deflection as `Es` increases. Because the cracked-inertia error grows as `Es` falls, low-modulus reinforcement is disproportionately penalized.

The wired ultimate-strength block has `c = 0.201389 m` for every `Es`. Its error relative to the service transformed section ranges from `+26.2%` at 50 GPa to `-9.0%` at 205 GPa. Near-agreement at an intermediate modulus is accidental.

## Numerical verification

Direct formula reproduction tolerance: `rtol = 1e-9`, `atol = 1e-12`.

| Quantity | Implemented | Independent reference | Absolute error | Relative error | Verdict |
|---|---:|---:|---:|---:|---|
| Iu, Es=100 GPa | 0.005691667 m⁴ | 0.005685039 m⁴ | 6.628e-6 m⁴ | 0.1166% | Caution |
| Icr, Es=100 GPa | 0.001050886 m⁴ | 0.001395242 m⁴ | 3.444e-4 m⁴ | 24.68% | Fail |
| Mcr, centered section | 39.844699 kN-m | 79.689397 kN-m | 39.844699 kN-m | 50.0% | Fail |
| Branson Ie, M/Mcr=1.5 | 0.002425932 m⁴ | 0.002425932 m⁴ | <1e-12 m⁴ | <1e-9 | Formula pass |
| B-G Ie, M/Mcr=1.5 | 0.002018050 m⁴ | 0.002018050 m⁴ | <1e-12 m⁴ | <1e-9 | Formula pass |

## Translation invariance

Translate the reference section `+1.0 m` in Z:

| Quantity | Original | Translated | Change |
|---|---:|---:|---:|
| Iu | 0.005691667 | 0.005691667 | 0% |
| Standalone Icr | 0.001050886 | 0.000710869 | -32.36% |
| Combined Icr | 0.001050886 | 0.001850719 | +76.11% |
| Mcr | 79.689397 kN-m | 18.389861 kN-m | -76.92% |

Only `Iu` passes rigid-translation invariance.

## Deflection audit

`DispSS` uses first-order rectangular integration:

```text
deltaTheta[i] = |M[i]| / (Ec |I[i]| 1000) deltaX
```

Limitations:

- returns only `ceil(Mu.Count / 2)` values;
- assumes midpoint zero rotation and symmetry;
- erases curvature sign;
- assumes uniform station spacing;
- does not validate `Mu.Count == Ieff.Count`;
- uses first-order integration.

For a 6 m simply supported beam, `w = 20 kN/m`, `Ec = 30 GPa`, and constant `I = 0.01 m⁴`:

| Samples | Code support rotation | Exact | Error | Code midspan deflection | Exact | Error |
|---:|---:|---:|---:|---:|---:|---:|
| 21 | 0.0006435 | 0.0006000 | 7.25% | 1.0692 mm | 1.1250 mm | 4.96% |
| 101 | 0.00060894 | 0.0006000 | 1.49% | 1.11555 mm | 1.1250 mm | 0.840% |
| 1001 | 0.000600899 | 0.0006000 | 0.150% | 1.12410 mm | 1.1250 mm | 0.0804% |

The trend converges, but a general solver requires signed curvature, actual station coordinates, both-span integration, and boundary-condition solution.

## Status matrix

| Area | Status | Finding |
|---|---|---|
| Basic units | OK | MPa/metre conversions are dimensionally consistent under the metre assumption |
| Es/Ec trend | OK | Scalar modular-ratio changes propagate monotonically |
| Gross transformed inertia | Caution | Omits transformed centroid and reinforcement self-inertia |
| Cracked inertia | Blocker | Wrong neutral axis/model, coordinate-dependent distances, rectangular approximation |
| Cracking moment | Blocker | Extreme-fiber distance depends on world origin |
| Branson | Caution | Formula works, inputs do not; V1 lacks capping |
| Bischoff-Gross | Caution | Formula is feasible for M>=Mcr but inherits bad inputs |
| Deflection | Blocker | Half-span, symmetric, uniformly spaced, unsigned-curvature only |
| Arbitrary shapes | Blocker | Bounding-box/rectangular assumptions do not generalize |
| Hybrid reinforcement | Blocker | One area, centroid, and Es cannot represent layers |

## Missing serviceability physics

- crack width and bar spacing;
- bond-dependent tension stiffening;
- sustained-load creep and shrinkage;
- age-dependent concrete modulus;
- load history and cyclic stiffness;
- FRP creep rupture/environmental degradation;
- prestress, decompression, and losses;
- separate short- and long-term deflections;
- multiple reinforcement layers and moduli.

## Deterministic regression cases

1. Centered and translated sections must produce identical `Iu`, `Icr`, and `Mcr`.
2. Reproduce the reference transformed-section table within `rtol = 1e-9`.
3. Require `Ieff = Iu` for `M <= Mcr`, bounded by `Icr` and `Iu`, approaching `Icr` at high moment.
4. Constant-EI UDL results must converge to:
   - `thetaA = w L^3 / (24 E I)`
   - `deltaMid = 5 w L^4 / (384 E I)`
5. Signed antisymmetric moments must preserve curvature sign.
6. Solve service neutral axes independently for 50 GPa and 205 GPa reinforcement.
7. Explicitly support hybrid layers or reject them with a clear diagnostic.

