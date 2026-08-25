# Exploratory Alternative-Reinforcement Workflow

## Intended use

Use BeamShapeExplorer to generate shaped-beam geometry and compare alternatives under controlled, identical assumptions.

Until calculations are aligned with named code editions and externally verified:

- Treat geometry, volume, mass, and cost as comparative.
- Treat serviceability outputs as sensitivity indicators only.
- Do not report BSE flexural, shear, ductility, or reinforcement outputs as design capacities.
- Obtain authoritative `As`, `Mu`, `Vu`, strength checks, and serviceability checks externally.
- Label each BSE-derived result `EXPLORATORY` or `BSE_RAW`.

## Safe and unsafe component map

### Conditionally suitable

| Component | Permitted use | Restriction |
|---|---|---|
| Material Properties | Document-local record | Override all ten values |
| Deconstruct Material Properties | Audit active values | Compare every output to manifest |
| Section Builder | Symmetric exploratory sections | Validate closed curve and units |
| Beam Builder | Visualization/volume geometry | Validate the resulting solid |
| Uniform Load Envelope | Simply supported positive UDL benchmark | Verify reactions and peak moment |
| Beam Sections | Extract concrete contours | Discard automatic reinforcement |
| Selfweight Envelope | Preliminary concrete-only comparison | Metre geometry; negative load output |
| Total Cost | Relative ranking | Validate rates, densities, and lofts |
| Ieff / DefSS chain | Sensitivity indicator | Externally validate serviceability |

### Bypass for capacity/compliance

- Beam Sections automatic `As`
- Flexural Analysis and Flexural Analysis V2
- Shear Analysis
- Vs Design
- Ductility Analysis
- Cracking Moment, IGross, ICracked, IEffective, and IEffective2 as authoritative calculations
- DefSS as absolute deflection
- Load Envelope from Curves unless station-by-station validated
- Global Material Properties and Select Code for reproducible studies
- TEST-panel components

## Grasshopper workflow

### 1. Establish the study contract

1. Set Rhino document units to metres.
2. Record Rhino absolute and angular tolerances.
3. Define station count `N`, coordinates, load case, sign convention, and result ID.
4. Create one local Material Properties component per alternative.
5. Connect it directly; do not pass through global material storage.
6. Deconstruct it and display all ten values in a provenance panel.

Global material and code components persist application settings, so another document or session can change results.

### 2. Build section stations

For each station:

1. Supply `b` and `h` in metres.
2. Supply `VPx` and `VPy` as flat lists.
3. Build one ordered `VSect` list.
4. Preserve left-to-right station order.

`SectionBuilder` repeats final coefficients when list lengths differ. Reject open, self-intersecting, zero-area, unexpectedly reversed, or out-of-envelope sections.

### 3. Build and validate the beam

Connect span and ordered sections to Beam Builder. Use at least four stations and equal point counts because degree-three interpolated longitudinal curves are created.

Externally verify:

- valid closed solid;
- one connected Brep;
- finite positive volume;
- no self-intersection;
- expected end sections;
- correct station order;
- volume convergence as stations increase.

The builder places the span on world X from `-L/2` to `+L/2`. Avoid rotating or elevating the canonical beam without checking world-coordinate-dependent components.

### 4. Supply demands externally

Preferred process:

1. Calculate `Mu` and `Vu` in an external structural model.
2. Sample at exactly the BSE station coordinates.
3. Flatten each load case to lists of length `N`.
4. Preserve signs and load-case labels.
5. Feed `Mu` to Beam Sections only to orient geometry.
6. Use BSE capacity components only as raw comparisons.

For the UDL benchmark, verify:

```text
Vu(0) = +qL/2
Vu(L) = -qL/2
Mu(0) = Mu(L) = 0
Mu(L/2) = -qL^2/8
```

Uniform Load Envelope rejects nonpositive `q`. Load Envelope from Curves uses first intersections, global-Z remapping, clipping, and tolerance substitutions, so it requires station-by-station comparison.

### 5. Supply external reinforcement

Use Beam Sections for `Ag`, but discard automatic `As`.

For station-varying external reinforcement:

1. Create one planar closed resultant curve on every `Ag` plane.
2. Convert `A_mm²` to `A_m² = A_mm² / 1,000,000`.
3. Under the current convention, use `radius = sqrt(A_m² / pi)`.
4. Place its centroid at the externally determined resultant.
5. Match direction, seam, station order, and count to `Ag`.
6. Feed these curves to comparative cost/stiffness workflows.

This represents one equivalent resultant, not a bar layout, and cannot encode multiple materials/layers.

### 6. Compare mass and cost

Feed ordered `Ag` and external `As` to Total Cost. Treat output as relative because:

- concrete and reinforcement are lofted independently;
- gross concrete plus reinforcement can double-count embedded volume;
- cost fields conflict with embodied-energy labels in the data type;
- component subtotals are not exposed.

Use `objective units` unless currency/year and functional basis are standardized.

## Material override checklist

Never accept defaults silently.

Concrete:

- [ ] `fc` in MPa, with characteristic/design basis recorded
- [ ] `Ec` in MPa, measured or code-consistent
- [ ] `ec` compatible with the external constitutive model
- [ ] `rhoC` in kg/m³
- [ ] `CostC` in the study's objective unit/kg

Reinforcement:

- [ ] `fy` in MPa, with yield/proof/ultimate meaning recorded
- [ ] `Es` in MPa
- [ ] `es` with explicit threshold/rupture meaning
- [ ] `rhoS` in kg/m³
- [ ] `CostS` on the same basis as `CostC`

Changing only strength is insufficient for an alternative material.

## Units, axes, and signs

- Geometry/span: metres
- Cover: millimetres
- Beam Sections area input: mm²
- Reinforcement-curve area: m²
- Strength/modulus: MPa
- Density: kg/m³
- Distributed load: kN/m
- Shear: kN
- Moment: kN·m
- Cost/objective factor: unit/kg
- Span axis: world X
- Section plane: world YZ
- Graph ordinate: world Z
- UDL sagging moment: negative
- UDL shear: positive left, negative right
- Self-weight `qw`: negative

Beam Sections uses moment sign to reverse reinforcement placement but absolute moment magnitude for area.

## List/cardinality contract

For each load case:

```text
Count(Mu) = Count(Vu) = Count(Ag) = Count(As) = N
```

Also require:

- at least four variable sections, each with the same point count;
- Uniform Load Envelope `N >= 3`;
- Load Envelope from Curves `N >= 2`;
- equal concrete/reinforcement station count, direction, and ordering for cost;
- all analysis lists exactly `N`, avoiding scalar broadcasting.

Several components silently repeat the first input if lengths differ. FlexAnalysis2 still indexes the original `Mu` list, so even intended broadcasting is unsafe. DefSS returns only approximately half the input stations.

## Output labels

Use provenance and claim level in every label:

```text
EXP_GEOM_BBrep_m
EXP_VOL_GROSS_m3
BSE_RAW_qw_concrete_only_kNpm
EXT_Mu_ULS_signed_kNm
EXT_Vu_ULS_signed_kN
EXT_As_resultant_mm2
BSE_COMPARE_Ieff_m4
BSE_COMPARE_deflection_halfspan_m
BSE_COMPARE_cost_objective_units
EXT_VALIDATED_Mn_kNm
EXT_VALIDATED_Vn_kN
GATE_GEOMETRY_PASS
GATE_STRENGTH_PASS
GATE_SERVICEABILITY_PASS
```

Never label raw BSE `Mn` or `Vn` as design capacity, code compliant, or validated.

## External validation gates

A candidate advances only when applicable gates pass:

1. **Geometry:** closed solid, finite volume, no self-intersection, correct stations/axes.
2. **Loads:** reactions/equilibrium and station-by-station demand comparison.
3. **Materials:** all properties and constitutive assumptions documented.
4. **Reinforcement:** area, centroid, cover, layout, anchorage, constructability.
5. **Strength:** flexure, shear, ductility, interaction, detailing, and factors under one named edition.
6. **Serviceability:** cracked stiffness, creep, shrinkage, duration, supports, and full-span displacement.
7. **Cost:** net quantities, waste, fabrication, currency/year, and functional unit.
8. **Ranking stability:** tighter tolerance, more stations, and plausible uncertainty.

Store external results beside BSE data in a station-keyed record:

```text
study_id
case_id
station_m
value
unit
method
software
code_and_edition
pass_fail
```

## Example studies

### Conventional versus alternative reinforcement

Hold geometry and external demands fixed. Override every reinforcement field, import externally designed `As`, and compare mass/cost objectives. Establish equivalence externally.

### Low-modulus reinforcement

Set all related properties, then use the stiffness chain only as a sensitivity indicator. Obtain final cracking, long-term deflection, creep rupture, and strength externally.

### Shaped-beam geometry

Hold material, external reinforcement strategy, load cases, stations, and validation method fixed. Vary only `b`, `h`, `VPx`, and `VPy`. Compare validated volume, self-weight, and cost. Recompute external loads when self-weight changes materially.

## Stop conditions

Reject the result when:

- a Brep is null, open, invalid, self-intersecting, or non-finite;
- a section is missing, duplicated, unexpectedly reversed, or zero-area;
- list counts/station coordinates differ;
- a required curve-plane intersection is missing or nonunique;
- outputs change materially with station density/tolerance;
- active global material/code state cannot be proven;
- material behavior falls outside the plugin's RC assumptions;
- any output is non-finite, tolerance-substituted, or sign-inconsistent;
- BSE and external equilibrium disagree;
- ranking changes under reasonable uncertainty;
- detailing, durability, fire, fatigue, stability, or constructability checks are absent;
- an absolute claim is proposed without every validation gate.

## Code-alignment priorities

1. Replace global material/code settings with document-local inputs.
2. Select one code edition per calculation and remove mixed equations.
3. Add strict station/cardinality validation.
4. Generalize reinforcement to explicit layers/materials.
5. Use local section axes and signs.
6. Return structured results with units, status, warnings, and provenance.
7. Separate gross and net material quantities.
8. Add prismatic-section, UDL, self-weight, strength, stiffness, and full-span-deflection benchmarks.
9. Document supported constitutive models and reject unsupported materials.
10. Keep absolute claims disabled until benchmarks and external references pass.

