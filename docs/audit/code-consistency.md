# Code Consistency and Correctness Audit

## Scope

This audit reviews every compiled consumer of the `BSEBuildingCode` setting, with emphasis on:

- `FlexuralAnalysis.cs`
- `FlexAnalysis2.cs`
- `BeamSections.cs`
- `DesignVs.cs`
- `ShearAnalysis.cs`
- `DuctilityAnalysis.cs`

Code `0` is identified in the plugin as IS 456 and code `1` as ACI 318. The repository does not identify the supported edition of either standard. References to expected behavior therefore describe the equation family apparent from the implementation rather than certifying code compliance.

## Executive result

The code switch does not produce isolated IS and ACI calculations. Critical paths mix equations and factors from both systems, some geometric calculations use the wrong axis or the world origin, and nominal and design strengths are exposed through the same output labels. Several deterministic inputs also produce `NaN`, exceptions, or zero-valued results without a useful error.

These defects affect conventional steel analysis before alternative reinforcement is considered.

## P0: Critical findings

### P0-1: IS shear uses width as effective depth and mixes ACI capacity

In `ShearAnalysis.cs:188-230`, the IS branch assigns `d = U.GetLength()` even though `V` is the vertically oriented section curve. It then returns the minimum of the IS and ACI concrete capacities:

```text
IS, no Vs:  Vn = 0.5 min(Vc_IS, Vc_ACI)
IS, with Vs: Vn = min(Vc_IS, Vc_ACI) + |Vs|
```

For a rectangular section, the implementation can therefore use approximately `b²` instead of `b d` and is not an IS-only result. The unexplained `0.5` multiplier further changes the no-stirrup result.

### P0-2: The ACI detailed shear term divides by Vu instead of Mu

`ShearAnalysis.cs:200-212` calculates `sectMu` but never uses it:

```text
Vc1 = (0.16 sqrt(fc) + 17 rho Vu d / Vu) bw d
```

For nonzero shear, `Vu` cancels and the reinforcement term becomes `17 rho d`. For zero shear, the expression evaluates `0 / 0`, producing `NaN`. The apparent intended legacy ACI term is proportional to `Vu d / Mu`.

The later NaN guard tests `sectVn` immediately after initializing it to zero, so it cannot catch this failure.

### P0-3: Both ACI flexure components use IS compression equilibrium

`FlexuralAnalysis.cs:160` and `FlexAnalysis2.cs:152` use:

```text
Ac,target = As (0.87 fy) / (0.36 fc)
```

for both code settings, while only the final tension force branches. An ACI nominal rectangular-block equilibrium would instead use:

```text
Ac,target = As fy / (0.85 f'c)
```

The implemented ACI target area is approximately 2.054 times that value before geometric discretization. The resulting compression centroid, lever arm, and moment capacity are wrong.

### P0-4: FlexAnalysis2 moment capacity depends on the world Z origin

`FlexAnalysis2.cs:185-202` computes:

```text
Z = |compressionCentroid.Z| - |steelCentroid.Z|
```

rather than the distance between resultants. Translating an unchanged section vertically changes the result and can reverse its sign. All section capacities must be invariant under rigid translation.

## P1: High-severity findings

### P1-1: BeamSections auto-sizing uses IS design constants for ACI

Although `rhoMax` branches by code, `BeamSections.cs:144-201` uses the following for both:

```text
rhoDes = 0.66 rhoMax
sConst = 0.87 fy (1 - 1.005 rhoDes fy / fc)
As = |Mu| / (sConst d)
```

This is an IS-style design expression and applies no ACI strength-reduction factor. Auto-sized areas under code `1` are not ACI design results.

### P1-2: ACI beta1 is not bounded

`FlexuralAnalysis.cs`, `FlexAnalysis2.cs`, `BeamSections.cs`, and `DuctilityAnalysis.cs` use:

```text
beta1 = 0.85 - 0.05 (fc - 28) / 7
```

without limiting it to `0.85` below 28 MPa and `0.65` above 56 MPa. Values outside the expected range directly affect reinforcement limits.

### P1-3: DuctilityAnalysis uses assumed or incorrect effective depth

The IS branch uses `U.GetLength()` even though `V` is the vertical curve. The ACI branch uses `0.96` times overall depth minus cover. The actual reinforcement centroid is computed but ignored. Reinforcement ratios should use the distance from the compression face to the tension-reinforcement centroid.

### P1-4: DesignVs has no code branch or stirrup leg count

`DesignVs.cs:85-90` always uses:

```text
Vs = 0.87 fy Av d (sin(theta) + cos(theta)) / s
```

It never reads the code setting, uses the longitudinal reinforcement strength as the transverse strength, and treats `Av` as one circular bar without a leg-count input.

### P1-5: Nominal and design strengths are mixed

- IS flexure uses reduced steel strength `0.87 fy`.
- ACI flexure uses full `fy` but no `phi`.
- ACI shear returns nominal `Vc + Vs` but no `phi`.
- All are labeled as `Mn` or `Vn`, with no explicit design-strength output.

This allows factored demand to be compared with nominal ACI capacity without warning.

### P1-6: ACI cracking uses the IS modulus-of-rupture coefficient

`CrackingMoment.cs` and `IEffective2.cs` read the selected code but always use `fr = 0.7 sqrt(fc)`. The material model also does not distinguish IS cube strength from ACI cylinder strength.

### P1-7: Serviceability properties depend on world coordinates

`CrackingMoment.cs`, `IEffective2.cs`, and `ICracked.cs` use global Z coordinates in section-relative distances. Rigidly translating a section can change `Mcr`, `Icr`, and `Ieff`.

### P1-8: Invalid code values are persisted

`SelectCode.cs` accepts any integer, labels unsupported values `NULL`, and persists them. Most consumers have no final `else`, leaving code-dependent variables at zero and allowing zero capacity, division by zero, infinity, or `NaN`.

## P2: Robustness findings

1. `FlexAnalysis2.cs:136-140` indexes `Mu[i]` after creating a normalized `newMu`; one moment with multiple sections can throw.
2. Empty `Mu`, `Vu`, or `Vs` lists are indexed at position zero without validation.
3. Concrete and reinforcement geometry counts are not checked before parallel indexing.
4. `FlexAnalysis2` averages disconnected-region centroids without area weighting.
5. A failed compression trim can leave `Ac_srf` null before enumeration.
6. The shear NaN guard checks a newly initialized zero rather than the computed terms.
7. `DispSS` returns only the modeled half-span despite describing displacement along the beam.
8. Zero or negative strengths, moduli, stirrup spacing, and dimensions are not rejected.
9. Compression-block capacity depends on the subdivision count without a convergence warning.

## Confirmed non-issues

- All compiled settings readers use the same settings namespace and keys.
- Missing settings consistently default to code `0`.
- `IGross` and `DispSS` do not intrinsically require a code branch after their material/stiffness inputs are established.
- Removing `0.87` from ACI tension force is appropriate for nominal strength; the defects are the retained IS block and missing distinction between nominal and design outputs.
- `CodeFile1.cs` duplicates a selector class and GUID but is excluded from compilation.
- `As [m²] × fy [MPa] × 1000` correctly converts force to kN.

## Deterministic regression cases

Use a `0.300 m × 0.600 m` rectangular section, `As = 2000 mm²`, cover `40 mm`, `fc = 30 MPa`, `fy = 500 MPa`, `Ec = 27,500 MPa`, `Es = 205,000 MPa`, `ec = 0.0035`, `es = 0.004`, and `m = 100`.

1. **Selector validation:** reject `-1`, `2`, and `Int32.MaxValue` in the selector and every consumer.
2. **Flexure equilibrium:** under ACI, assert target area `As fy / (0.85 fc)`. The current target is 2.0541667 times larger.
3. **Translation invariance:** translate all section geometry `+10 m` in Z; `Mn`, `Mcr`, `Icr`, `Ieff`, and `Ig` must remain unchanged to relative tolerance `1e-9`.
4. **Single moment expansion:** supply three sections and one `Mu`; both flexure components must return three results.
5. **IS shear depth:** assert effective depth follows the reinforcement centroid, not the section width.
6. **IS no-stirrup result:** assert the selected, documented IS equation without an unexplained half factor or ACI minimum.
7. **ACI detailed shear:** assert that the detailed term uses `Vu d / Mu`.
8. **ACI zero shear:** assert that no output is `NaN`.
9. **Strength semantics:** expose and test nominal and design capacities separately.
10. **Ductility depth:** place steel 0.55 m from the compression face and assert that both branches use that depth.
11. **DesignVs branching:** test code-specific behavior and explicit stirrup leg count.
12. **Beta1 bounds:** for `fc = 20, 28, 56, 70 MPa`, assert `0.85, 0.85, 0.65, 0.65`.
13. **Cardinality and invalid inputs:** empty loads, mismatched geometry counts, failed planar Breps, zero material properties, zero spacing, and disconnected compression regions must produce Grasshopper runtime errors rather than exceptions or non-finite outputs.

## Remediation order

1. Define supported code editions and whether each output is nominal or design resistance.
2. Correct section-relative geometry and enforce translation invariance.
3. Isolate IS and ACI flexure and shear equations.
4. Validate code selection, material inputs, and list cardinalities.
5. Add the deterministic regression cases before adding alternative-reinforcement equations.

