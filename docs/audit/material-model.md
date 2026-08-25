# Material Model and Grasshopper Data-Schema Audit

## Executive finding

The current `MaterialProperties` object is a ten-number container designed around one concrete and one conventional reinforcing-steel material. Its wiring is simple and internally consistent, but it cannot unambiguously represent high-strength/stainless steel, FRP, distinct transverse reinforcement, or hybrid reinforcement.

Primary blockers:

1. Flexural `Mn` depends only on `fC` and `fY`; `ES` is unused and `eS` only enters intermediate calculations that do not reach final capacity.
2. `DesignVs` uses longitudinal `fY` for transverse reinforcement.
3. Material properties and code resistance factors are mixed inside analysis components.
4. Persistence relies on application-level settings rather than document-owned, versioned data.
5. The generic CLR object has no Grasshopper serialization contract or schema version.

## Current schema and data flow

`MaterialProperties.cs:9-33` exposes ten mutable public doubles:

| Field | UI meaning | Unit | Concern |
|---|---|---|---|
| `fC` | Concrete characteristic strength | MPa | Cube/cylinder basis unspecified |
| `EC` | Concrete elastic modulus | MPa | Independent of `fC` |
| `eC` | Concrete maximum strain | dimensionless | Material/code-limit semantics mixed |
| `rhoC` | Concrete mass density | kg/m³ | Valid use |
| `EEC` | Concrete cost in UI | currency/kg | Printed as embodied energy |
| `fY` | Longitudinal steel strength | MPa | Assumes yield semantics |
| `ES` | Reinforcement elastic modulus | MPa | Ignored by flexural capacity |
| `eS` | Reinforcement maximum strain | dimensionless | Ambiguous and ignored by final `Mn` |
| `rhoS` | Reinforcement mass density | kg/m³ | Valid use |
| `EES` | Steel cost in UI | currency/kg | Printed as embodied energy |

The constructor, global writer/reader, and deconstructor consistently preserve this positional order:

```text
fC, EC, eC, rhoC, EEC, fY, ES, eS, rhoS, EES
```

Normal flow:

```text
10 numeric inputs
  -> plain MaterialProperties CLR object
  -> generic Grasshopper parameter
  -> analysis/design/load/cost consumers
```

Global flow:

```text
Material Properties Global
  -> GH_SettingsServer "MPInitial"
  -> Read MP
  -> new MaterialProperties object
```

The global writer clears and rewrites the namespace on each solve. The reader defaults missing values to zero. There is no `GH_Goo`, archive reader/writer, schema version, or migration implementation.

## Active field-use matrix

Legend: **U** affects active output, **D** is used only in dead intermediate work, **X** is unpacked but unused, **O** is exposed.

| Consumer | fC | EC | eC | rhoC | EEC | fY | ES | eS | rhoS | EES |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Deconstruct | O | O | O | O | O | O | O | O | O | O |
| BeamSections | U | X | U | X | X | U | X | U | X | X |
| CostCalculation | X | X | X | U | U | X | X | X | U | U |
| CrackingMoment | U | X | X | X | X | X | X | X | X | X |
| DesignVs | X | X | X | X | X | U | X | X | X | X |
| DispSS | X | U | X | X | X | X | X | X | X | X |
| DuctilityAnalysis | U | X | U | X | X | U | X | U | X | X |
| FlexuralAnalysis | U | X | D | X | X | U | X | D | X | X |
| FlexAnalysis2 | U | X | D | X | X | U | X | D | X | X |
| ICracked | X | U | X | X | X | X | U | X | X | X |
| IGross | X | U | X | X | X | X | U | X | X | X |
| IEffective2 | U | U | X | X | X | X | U | X | X | X |
| SelfWeightEnvelope | X | X | X | U | X | X | X | X | X | X |
| ShearAnalysis | U | X | X | X | X | X | X | X | X | X |

This confirms that changing `ES`, `eS`, or `eC` does not change final flexural capacity. They only affect service stiffness or dead flexural intermediate values.

## OK / Caution / Blocker findings

### OK

- Constructor, global storage, reconstruction, and deconstruction preserve field order.
- Legacy component GUIDs are explicit and stable.
- Basic stress, modulus, strain, and density units are visible in the UI.
- Density calculations are dimensionally coherent if Rhino geometry is authored in metres.

### Caution

- Public mutable fields permit unvalidated mutation after construction.
- Zero, negative, non-finite, or physically inconsistent values are accepted.
- `eS` conflates yield/proof/rupture/code-threshold semantics.
- Steel-specific naming obscures FRP and mixed-material meaning.
- `rhoC/rhoS` mass density conflicts conceptually with `rho` reinforcement ratio.
- Length conversion assumes Rhino model units are metres.
- Boilerplate unpacking hides real component dependencies.
- No test project exists.

### Blocker

- One reinforcement material cannot represent layers or hybrids.
- Flexural capacity is not constitutive-law based.
- Transverse design reuses longitudinal `fY`.
- Code constants and resistance factors are embedded in solvers.
- Application-global settings make document results session-dependent.
- The material object has no versioned Grasshopper serialization contract.
- `EEC/EES` have conflicting cost versus embodied-energy meanings.

## Proposed minimal V2 schema

Keep physical materials, reinforcement geometry, code factors, and analysis settings separate.

```csharp
enum ReinforcementMaterialKind
{
    CarbonSteel,
    HighStrengthSteel,
    StainlessSteel,
    FRP,
    Other
}

enum StrengthDefinition
{
    Yield,
    Proof,
    Ultimate
}

enum ConstitutiveLawKind
{
    LinearElasticBrittle,
    ElasticPerfectlyPlastic,
    Bilinear
}

sealed class ConcreteProfile
{
    int SchemaVersion = 1;
    string Id;
    string Name;

    double CharacteristicCompressiveStrengthMpa;
    double ElasticModulusMpa;
    double UltimateCompressionStrain;
    double DensityKgPerM3;

    double? MeasuredTensileStrengthMpa;
    double? UnitCostPerKg;
    double? EmbodiedEnergyMjPerKg;
}

sealed class ReinforcementProfile
{
    int SchemaVersion = 1;
    string Id;
    string Name;

    ReinforcementMaterialKind MaterialKind;
    double ElasticModulusMpa;
    double CharacteristicStrengthMpa;
    StrengthDefinition StrengthDefinition;
    double UltimateTensileStrain;

    double DensityKgPerM3;
    double? UnitCostPerKg;
    double? EmbodiedEnergyMjPerKg;

    ConstitutiveLawKind ConstitutiveLaw;
    double? HardeningModulusMpa;
}
```

For FRP, `CharacteristicStrengthMpa` is ultimate strength and the law is brittle. For steel, it can be yield or proof strength. `StrengthDefinition` prevents a solver from assuming all strengths are `fy`.

### Constitutive-law contract

```csharp
interface IUniaxialConstitutiveLaw
{
    double StressMpa(double strain);
    double TangentModulusMpa(double strain);
    bool HasFailed(double strain);
}
```

Solvers must request stress at the compatible layer strain. Code-specific caps and resistance factors belong outside this contract.

### Reinforcement layers

```csharp
enum ReinforcementRole
{
    Longitudinal,
    Transverse
}

sealed class ReinforcementLayer
{
    string Id;
    ReinforcementRole Role;
    ReinforcementProfile Material;

    double AreaMm2;
    double Ymm;
    double Zmm;

    int? LegCount;
    double? SpacingMm;
    double? InclinationRadians;
}
```

Rules:

- Longitudinal layers require positive area and valid section location.
- Transverse layers additionally require positive leg count and spacing.
- Hybrid reinforcement is multiple layers referencing different profiles.
- `DesignVs` selects transverse layers only.
- Flexure integrates longitudinal layers only.

## Separate code and analysis settings

Material profiles contain physical/constitutive data only.

```csharp
sealed class AnalysisSettings
{
    int SectionSlices;
    double GeometryToleranceMm;
    EffectiveInertiaMethod EffectiveInertiaMethod;
    bool IncludeConcreteTension;
    bool EnforceMaterialFailure;
}

sealed class CodeResistanceSettings
{
    string CodeId;
    string Edition;

    double ConcretePartialFactor;
    double ReinforcementPartialFactor;
    double FlexureResistanceFactor;
    double ShearResistanceFactor;

    double Alpha1;
    double Beta1;
    double? MaximumUsableTransverseStrengthMpa;
    double? TensionControlledStrainLimit;
}
```

Minimum/maximum reinforcement, code-derived block parameters, and transverse-strength caps should be implemented by an edition-specific strategy rather than copied into components.

## Units

Canonical analysis units:

- stress/modulus: MPa;
- strain: dimensionless with one documented sign convention;
- section coordinates and reinforcement area: mm and mm²;
- density: kg/m³;
- force: kN;
- moment: kN·m;
- cost: explicit currency/kg;
- embodied energy: MJ/kg;
- angle: radians.

Convert Rhino geometry from `RhinoDoc.ModelUnitSystem` at component boundaries. Do not assume metres.

Legacy `EEC/EES` should migrate as unit cost because that matches the UI and active calculation. New embodied-energy values should initially be unset.

## Validation

Reject with actionable Grasshopper runtime errors:

- non-finite numeric values;
- non-positive strengths, moduli, density, layer area, spacing, or leg count;
- concrete compression strain outside `(0, 0.02]`;
- reinforcement tensile strain outside `(0, 1]`;
- brittle laws without finite rupture strain;
- bilinear laws without valid hardening modulus;
- duplicate/blank IDs or missing profile references;
- transverse fields on longitudinal layers and vice versa;
- unsupported code edition without explicit opt-in;
- geometry that cannot be converted to canonical units.

Warn, rather than reject, unusual but possible values or missing cost/environmental metadata.

## Grasshopper serialization

Introduce explicit immutable goo types and persistent parameter classes:

```csharp
sealed class ConcreteProfileGoo : GH_Goo<ConcreteProfile> { }
sealed class ReinforcementProfileGoo : GH_Goo<ReinforcementProfile> { }
sealed class ReinforcementLayerGoo : GH_Goo<ReinforcementLayer> { }
```

Each should:

- provide useful validity diagnostics;
- support controlled casts from legacy `MaterialProperties`;
- serialize named fields with `SchemaVersion`;
- tolerate missing optional fields;
- reject unsupported future major versions clearly;
- preserve document-owned data rather than treating application settings as authoritative.

## Compatibility and migration

Existing definitions depend on CLR and Grasshopper identities as well as numeric values. Preserve:

- legacy component GUIDs;
- port ordering/access modes/defaults;
- `BeamShapeExplorer.DataTypes.MaterialProperties`;
- public fields and constructor for script compatibility;
- existing global setting keys.

Migration sequence:

1. Freeze and characterize legacy behavior.
2. Mark legacy APIs obsolete without removing them.
3. Add V2 components with new GUIDs and typed parameters.
4. Add explicit legacy-to-profile and restricted reverse adapters.
5. Map legacy fields deterministically; warn that `eS` is ambiguous.
6. Read document-owned profiles first; import `MPInitial` only when absent.
7. Keep old consumers while V2 solvers consume profiles/layers.
8. Offer opt-in document upgrade beside disabled legacy components.
9. Never reuse a component GUID for changed semantics.
10. Version profile archives independently from assembly versions.

Reverse conversion must refuse hybrid or FRP configurations that cannot be represented safely.

## Required tests

### Legacy compatibility

- Load `.gh`/`.ghx` fixtures for every legacy material component.
- Assert GUIDs, port order, defaults, wires, and unchanged legacy outputs.
- Test global settings absent, zeroed, and populated.
- Test copy/paste, clusters, internalization, save/reopen, and headless recompute.

### Serialization and migration

- Round-trip every profile/layer through Grasshopper archives.
- Read optional-field omissions and reject unsupported major versions.
- Verify culture-independent floating-point serialization.
- Verify IDs/references through duplication and document merge.
- Assert `EEC/EES` migrate as cost and ambiguous `eS` emits a warning.

### Dependency and mechanics

- Perturb each legacy field and verify only matrix-listed outputs change.
- Characterize that legacy `Mn` is insensitive to `ES/eS`.
- Require V2 flexure to respond to modulus, rupture strain, hardening, and layer position.
- Verify transverse capacity responds only to the transverse profile.

### Constitutive laws, units, and code factors

- Elastic-perfectly-plastic steel.
- High-strength steel with separately supplied code cap.
- Stainless proof/bilinear response.
- FRP linear-elastic rupture.
- Hybrid layer equilibrium and failure sequencing.
- Equivalent models authored in metres, millimetres, and inches.
- Golden examples per supported code edition.
- Verify resistance/partial factors are applied exactly once.

