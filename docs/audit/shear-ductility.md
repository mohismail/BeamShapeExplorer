# Shear, Transverse Reinforcement, and Ductility Audit

## Conclusion

The current shear and ductility paths are blockers for design use, including conventional steel:

1. IS shear uses horizontal section width as effective depth and mixes ACI capacity into its result.
2. The ACI detailed term divides by `Vu` rather than `Mu`, producing a dimensionally wrong term and `NaN` at zero shear.
3. `DesignVs` uses longitudinal `fy`, one implicit stirrup leg, the wrong depth direction, and `0.87` under every code.
4. "Ductility" is only a reinforcement-ratio limit report; it does not calculate strain, curvature, rotation, or failure mode.
5. Longitudinal and transverse materials cannot be represented separately.

No code edition is identified. Concrete strength basis and nominal/design output semantics are also unspecified, so the findings characterize implementation rather than certify compliance.

## Current data flow

```text
MaterialProperties
  -> one concrete profile
  -> one profile labeled longitudinal steel

BeamSections
  -> concrete contours Ag
  -> one equivalent circular reinforcement region As

Ag + As + loads + external Vs
  -> ShearAnalysis
  -> Vc_IS and Vc_ACI
  -> Vn

Ag + longitudinal fy + diameter/spacing/angle
  -> DesignVs
  -> Vs

Ag + As + ec/es/fc/fy
  -> DuctilityAnalysis
  -> rho, rhoMin, rhoMax
```

## Implemented shear model

### Geometry

`ShearAnalysis.cs:145-195` treats `V` as vertical/depth direction, samples contours along it, and takes the shortest retained contour as `bw`.

```text
IS:  d = length(U)
ACI: d = 0.96 length(V) - clearCover
```

For a rectangle, `U` is width. Other geometry risks:

- only the first contour segment is retained;
- missing cuts sort as zero length;
- a null shortest contour falls back to `U`;
- minimum chord is not necessarily web width;
- holes, multiple webs, and disconnected parts are not accumulated;
- surface parameterization is treated as a structural-axis contract.

### Reinforcement ratio

```text
rho = As / (bw d)
```

`As` is total supplied area. Tension/compression layers, material type, and multiple materials are not distinguished.

### IS-labeled concrete contribution

```text
pt = 100 rho
beta = max[0.8 fc / (6.89 pt), 1]
tauC = 0.85 sqrt(0.8 fc) [sqrt(1 + 5 beta) - 1] / (6 beta)
Vc_IS = tauC bw d / 1000
```

If `rho = 0`, the infinity arithmetic can yield `NaN`.

### ACI-labeled concrete contribution

`sectMu` is calculated but unused:

```text
Vc1 = [0.16 sqrt(fc) + 17 rho (Vu d / Vu)] bw d / 1000
Vc2 = 0.29 sqrt(fc) bw d / 1000
Vc_ACI = min(Vc1, Vc2)
```

For nonzero shear, the ratio collapses to `d`; for zero shear it is `0/0`. The apparent intended legacy family contains `Vu d / Mu`, but correction requires selection of a specific code edition and explicit `Mu = 0` handling.

### Final output

```text
IS, Vs≈0:  Vn = 0.5 min(Vc_IS, Vc_ACI)
IS, Vs≠0:  Vn = min(Vc_IS, Vc_ACI) + |Vs|

ACI, Vs≈0: Vn = 0.5 Vc_ACI
ACI, Vs≠0: Vn = Vc_ACI + |Vs|
```

The half factor is undocumented and disappears discontinuously for an arbitrarily small nonzero `Vs`. There is no maximum diagonal-compression limit, minimum transverse reinforcement, spacing check, or nominal/design distinction.

## Transverse reinforcement

`DesignVs.cs:31-90` implements:

```text
d = length(U) * 1000
Av = pi dv^2 / 4
Vs = 0.87 fy Av d [sin(theta) + cos(theta)] / (1000 s)
```

Blockers:

- `d` follows width rather than effective depth;
- `fy` is the longitudinal reinforcement strength;
- one circular bar is assumed and leg count is absent;
- `0.87` is used for all code settings;
- the component does not read the selected code;
- zero spacing and invalid diameter are not validated;
- anchorage, hooks, closure, bends, spacing, strength caps, and maximum shear are absent.

## "Ductility" model

The component computes:

```text
cmax / d = ec / (ec + es)
```

IS-labeled:

```text
rhoMax = [0.36 fc / (0.87 fy)] (cmax / d)
rhoMin = 0.25 sqrt(fc) / fy
```

ACI-labeled:

```text
beta1 = 0.85 - 0.05 (fc - 28) / 7
rhoMax = (0.85 fc / fy) beta1 (cmax / d)
rhoMin = max[0.25 sqrt(fc) / fy, 1.4 / fy]
```

`beta1` is unbounded. Section ratio again uses width as depth for IS and assumed `0.96h-cover` for ACI, while computed reinforcement centroids are ignored.

This is not deformation ductility. It omits compatible strains, neutral-axis equilibrium, yield/proof/hardening/rupture states, moment-curvature, plastic rotation, axial interaction, confinement, cyclic degradation, low-cycle fatigue, and failure classification.

## Longitudinal versus transverse effects

Longitudinal material may affect crack opening, aggregate interlock, dowel action, bond, axial stiffness, neutral-axis depth, and rupture/yield near diagonal cracks. The current concrete contribution sees total `rho` only; it ignores `Es`, bond, constitutive behavior, and material type.

Transverse material independently affects `Vs`, crack control, confinement, and cyclic behavior. Required distinctions include:

- yield/proof/ultimate strength;
- elastic modulus and effective strain;
- steel yielding versus FRP rupture;
- FRP bend reduction;
- hook/closure/anchorage;
- leg count and effective area;
- stirrup versus bonded wrap configuration;
- debonding/development;
- environmental/fatigue reductions;
- code-specific usable stress or strain caps.

## Separate transverse material recommendation

Introduce a dedicated transverse reinforcement/detailing object rather than adding overloaded fields to `MaterialProperties`.

Physical/material fields:

```text
material kind
strength definition
characteristic strength
elastic modulus
ultimate strain
constitutive law
bend strength/reduction model
density and environment modifiers
```

Detailing fields:

```text
bar or strip area
leg count
spacing
inclination and angle convention
open/closed stirrup or wrap configuration
hook, anchorage, and development
effective depth and crack-crossing geometry
FRP wrap width, thickness, layers, coverage, and bond length
```

Edition-specific strategies must separately supply resistance factors, usable stress/strain caps, minimum reinforcement, maximum spacing, diagonal-compression limits, and anchorage/bend rules.

## Missing failure modes

| Failure/effect | Coverage | Status |
|---|---|---|
| Diagonal tension | Broken empirical Vc+Vs only | Blocker |
| Diagonal concrete compression | No upper limit | Blocker |
| FRP rupture at bends | None | Blocker |
| Hook/weld failure | None | Blocker |
| Bond/development | None | Blocker |
| FRP wrap debonding | None | Blocker |
| Size effect | No explicit term/check | Blocker where applicable |
| Arching/deep-beam action | No a/d or strut-and-tie path | Blocker |
| Torsion interaction | No torsion input | Blocker |
| Confinement | None | Blocker |
| Openings/disturbed regions | Not identified | Blocker |
| Prestress/axial force | None | Blocker |
| Cyclic degradation/fatigue | None | Blocker |
| Multiple webs/hollow sections | First contour only | Blocker |

## Applicability matrix

| Capability | Steel | HSS/stainless | FRP longitudinal | FRP stirrups/wraps | Hybrid |
|---|---|---|---|---|---|
| Basic MPa/mm-to-kN product | OK | OK | OK | OK | OK |
| Material semantics | Caution | Blocker | Blocker | Blocker | Blocker |
| Effective depth | Blocker | Blocker | Blocker | Blocker | Blocker |
| Longitudinal effect on Vc | Blocker | Blocker | Blocker | Blocker | Blocker |
| Separate transverse material | Blocker | Blocker | Blocker | Blocker | Blocker |
| Stirrup/wrap geometry | Blocker | Blocker | Blocker | Blocker | Blocker |
| Finite zero-shear result | Blocker | Blocker | Blocker | Blocker | Blocker |
| Isolated code equation | Blocker | Blocker | Blocker | Blocker | Blocker |
| Nominal/design distinction | Blocker | Blocker | Blocker | Blocker | Blocker |
| Strain/failure ductility | Blocker | Blocker | Blocker | Blocker | Blocker |

## Baseline arithmetic

Inputs:

```text
b = 300 mm, h = 600 mm, cover = 40 mm
As = 2400 mm²
fc = 40 MPa, fy = 415 MPa
ec = 0.0035, es = 0.004
Vu = 100 kN, Mu = 200 kN-m
dv = 10 mm, s = 100 mm, theta = pi/2
```

| Quantity | Current result |
|---|---:|
| ACI assumed d | 536 mm |
| ACI rho | 0.0149253731343 |
| Erroneous Vc1 | 22,031.5181593 kN |
| Vc2 | 294.926663698 kN |
| ACI Vn, Vs=0 | 147.463331849 kN |
| Diagnostic Vc1 with Vu d/Mu | 173.652559282 kN |
| IS assumed d | 300 mm |
| IS rho | 0.0266666666667 |
| IS beta | 1.74165457184 |
| IS tauC | 0.973547925106 MPa |
| IS Vc before mixed minimum/factor | 87.6193132596 kN |
| IS Vn, Vs=0 | 43.8096566298 kN |
| DesignVs | 85.0704020684 kN |

The `Vu d/Mu` comparison diagnoses the coding error; it is not an edition-certified target.

## Deterministic regression cases

1. `Vu=0` must return a finite, explicitly handled result.
2. `Mu=0, Vu>0` must select an edition-defined alternate equation or applicability error.
3. Use the actual tension-reinforcement centroid as `d`.
4. Swap/reverse surface parameterization; `bw`, `d`, `rho`, and `Vc` must not change.
5. Rigidly translate/rotate section plus local axis; results must remain invariant.
6. IS results must not depend on ACI equation parameters.
7. `Vs=0` and `Vs=1e-9 kN` must not cause a near-twofold jump.
8. Two stirrup legs must produce twice one-leg contribution where the chosen equation is linear in `Av`.
9. Vary longitudinal `fy` with fixed transverse material; `Vs` must remain unchanged.
10. FRP bend strength and wrap bond length must deterministically limit contribution.
11. Hybrid longitudinal layers must remain separate.
12. Test explicit edition limits for `beta1`, minimum reinforcement, spacing, and maximum shear.
13. Cross deep-beam applicability boundary and switch methods or reject.
14. Nonzero torsion must invoke combined design or explicit unsupported error.
15. Multi-web/hollow sections must accumulate webs or reject unsupported topology.
16. Reject empty/mismatched lists, invalid Breps, nonpositive spacing/diameter/depth, non-finite properties, and unsupported code IDs.

## Remediation order

1. Declare code editions, strength semantics, and applicability boundaries.
2. Replace inferred U/V dimensions with section-local axes and actual reinforcement centroids.
3. Separate longitudinal and transverse material/detailing schemas.
4. Isolate edition-specific concrete and transverse shear strategies.
5. Add validation and finite-result checks.
6. Add maximum shear, minimum reinforcement, spacing, anchorage, bond, bend, and deep-beam rules.
7. Replace ratio-only "ductility" with strain compatibility and explicit failure classification.

