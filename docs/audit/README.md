# Alternative-Reinforcement Audit Summary

## Where are you okay, and where will you run into issues?

BeamShapeExplorer is usable for controlled exploratory comparisons of geometry, volume, mass, relative cost, and stiffness sensitivity when inputs, units, geometry, and outputs are independently checked.

Do not use its flexural, shear, ductility, reinforcement-design, cracking, or deflection outputs as design capacities or evidence of code compliance.

The repository does not specify supported editions of IS 456 or ACI 318. Equation-family references in this audit are diagnostic only; no compliance certification is made.

## Executive verdict

- **OK:** comparative geometry generation, validated volumes, metre-based density calculations, simple load-envelope benchmarking, and relative mass/cost ranking.
- **Caution:** shaped geometry, self-weight, gross stiffness, effective-inertia trends, and cost. These require controlled units, canonical axes, explicit local materials, convergence checks, and external validation.
- **Blocker:** authoritative flexure, shear, transverse-reinforcement design, ductility, cracking, cracked inertia, absolute deflection, code compliance, and all alternative/hybrid reinforcement capacity claims.
- Conventional steel is also affected by mixed equations, coordinate dependence, incorrect effective depths, and unclear nominal/design semantics.
- HSS/stainless, FRP, and hybrid reinforcement cannot be represented reliably because layer-specific compatible strains and constitutive laws are absent.

## Component/output matrix

| Component/output | Status | Suitable use and principal limitation |
|---|---|---|
| Material Properties | Caution | Local record with all values explicit; steel-centric, mutable, unversioned |
| Global Material Properties / Select Code | Blocker | Application-global state makes results session-dependent |
| Section Builder | Caution | Symmetric exploratory sections after geometry validation |
| Beam Builder | Caution | Visualization/volume after Brep and station validation |
| Uniform Load Envelope | Caution | Simple simply supported UDL benchmark only |
| Load Envelope from Curves | Blocker | Requires station-by-station validation |
| Beam Sections concrete contours | Caution | Geometry extraction only |
| Beam Sections automatic reinforcement | Blocker | IS-style sizing used under both settings |
| Selfweight Envelope | Caution | Preliminary concrete-only comparison |
| Total Cost | Caution | Relative ranking; possible embedded-volume double count |
| Gross transformed inertia | Caution | Trend level; transformed centroid/self-inertia incomplete |
| Cracking moment | Blocker | Code coefficient and world-origin defects |
| Cracked inertia | Blocker | Ultimate block used instead of service neutral axis |
| Effective inertia | Caution | Interpolation works but inherits invalid inputs |
| DefSS | Blocker | Half-span, symmetric, unsigned, uniform-spacing assumptions |
| Flexural Analysis V1 | Blocker | Assumes yielded tension resultant; no compatibility |
| Flexural Analysis V2 | Blocker | Same mechanics limits plus origin/cardinality/null defects |
| Shear Analysis | Blocker | Wrong depth, mixed equations, broken detailed term, NaNs |
| Vs Design | Blocker | Wrong depth, longitudinal fy, one leg, no code branch |
| Ductility Analysis | Blocker | Ratio limits only; no deformation/failure analysis |
| Raw Mn / Vn | Blocker | Nominal and design semantics are inconsistent |

## Material-specific applicability

| Material | Geometry/mass/cost | Flexural capacity | Shear/transverse | Serviceability | Overall |
|---|---|---|---|---|---|
| Conventional steel | Caution | Blocker for design | Blocker | Trend-only caution | Exploratory |
| HSS / stainless | Caution | Blocker: proof/rounded/hardening response absent | Blocker | Blocker without external model | Unsupported for capacity |
| FRP | Caution | Blocker: compatible elastic stress and rupture absent | Blocker: bend/bond/wrap limits absent | Blocker for absolute results | Unsupported for capacity |
| Hybrid | Caution | Blocker: one aggregate material/resultant | Blocker | Blocker | Unsupported |

## Highest-priority defects

### P0

1. IS-labelled shear uses section width as effective depth and mixes ACI capacity.
2. The detailed ACI-labelled shear term divides by `Vu` rather than the calculated `Mu`; zero shear can produce `NaN`.
3. Both ACI-labelled flexural components use IS-style compression equilibrium.
4. Flexural Analysis V2 uses absolute world-Z coordinates for its lever arm.

### P1

1. Beam Sections applies IS-style auto-sizing under ACI and has no explicit ACI resistance factor.
2. ACI-style `beta1` is unbounded.
3. Ductility uses assumed/incorrect effective depth.
4. Vs Design lacks code branching, leg count, separate transverse material, and correct depth.
5. Nominal and design strengths share ambiguous output labels.
6. ACI-labelled cracking uses an IS-style coefficient; concrete strength basis is unspecified.
7. Serviceability and V2 flexure contain world-coordinate dependencies.
8. Invalid values, code IDs, geometry, and list counts can produce exceptions or non-finite output.

## Safe exploratory operating envelope

Use BSE only when:

- the objective is comparative exploration, not acceptance;
- Rhino units are metres and tolerances/axes/signs/versions are recorded;
- canonical world-X/world-YZ orientation is maintained;
- all material values are explicit and document-local;
- geometry is valid and checked for station-density convergence;
- external `Mu`, `Vu`, reinforcement area, centroid, and layout are supplied at matching stations;
- automatic reinforcement and capacity outputs are bypassed for acceptance;
- serviceability is labelled sensitivity-only;
- all lists have identical station counts/order;
- outputs are labelled `EXPLORATORY`, `BSE_RAW`, or `BSE_COMPARE`;
- non-finite, substituted, sign-inconsistent, or unstable results stop the study.

## External validation requirements

Independently verify:

1. **Geometry:** topology, axes, station order, volume, convergence.
2. **Loads:** equilibrium, reactions, combinations, signed station demands.
3. **Materials:** strength definitions, constitutive laws, reductions, concrete basis.
4. **Reinforcement:** layers, area, centroid, cover, anchorage, constructability.
5. **Strength:** flexure, shear, interaction, failure, detailing, factors under a named edition.
6. **Serviceability:** cracking, stiffness, creep, shrinkage, duration, supports, full-span displacement.
7. **Cost:** net quantities, waste, fabrication, currency/year, functional basis.
8. **Ranking stability:** station density, tolerance, material uncertainty, alternate assumptions.

## Benchmark inventory

| Benchmark | Coverage | Key gates |
|---|---|---|
| [Rectangular steel control](../../benchmarks/rectangular-steel-control/manifest.json) | IS/ACI-labelled flexural characterization and corrected references | Finite, cardinality, characterization, corrected acceptance |
| [Low-E sensitivity](../../benchmarks/low-e-sensitivity/manifest.json) | Serviceability sensitivity from 50–205 GPa | Positivity, monotonicity, transformed-section reference, invariance |
| [Shaped geometry invariance](../../benchmarks/shaped-geometry-invariance/manifest.json) | Shape properties, volume, rigid translation | Geometry, analytic volume, characterization, corrected invariance |

Manifests, datasets, and a verifier are in the [benchmark guide](../../benchmarks/README.md). Valid `.gh` files must still be created in Rhino 8, saved, closed, reopened, recomputed, exported, and verified. No unvalidated Grasshopper archive was fabricated.

## Prioritized roadmap

### Phase 0: safety and documentation

- Declare exact code editions, concrete basis, units, applicability, and nominal/design semantics.
- Reject invalid IDs, properties, geometry, lists, and non-finite results.
- Make materials/code document-owned and versioned.
- Add structured status, warnings, units, provenance, and convergence.
- Build and run all Rhino/Grasshopper benchmark definitions.
- Disable or mark unsafe capacity claims.

### Phase 1: steel-code correctness

- Isolate edition-specific IS and ACI strategies.
- Correct flexure, shear, effective depth, bounded parameters, and cracking.
- Separate longitudinal/transverse materials.
- Add leg count, spacing, strength caps, maximum shear, minimum reinforcement, anchorage, and applicability checks.
- Remove world-coordinate dependence.
- Separate nominal capacity from design resistance.

### Phase 2: general strain-compatible core

- Introduce local axes and explicit reinforcement layers.
- Add versioned material profiles and constitutive-law contracts.
- Solve plane-section compatibility and axial equilibrium.
- Integrate concrete over general geometry.
- Report layer states, governing failure, residuals, and convergence.
- Replace ratio-only ductility with moment-curvature/failure classification.
- Rebuild serviceability around transformed cracked sections and full-span signed curvature.

### Phase 3: material and code extensions

- Add HSS, stainless, FRP, and hybrid constitutive models.
- Add proof stress, hardening, rupture, asymmetry, bond, bend, environment, fatigue, and creep rupture as applicable.
- Add transverse FRP stirrup/wrap anchorage and debonding.
- Add edition-specific golden examples and applicability boundaries.

## Release gates

- **Safety:** no exceptions, silent zeros, or non-finite output for invalid input.
- **Specification:** every calculation identifies code, edition, units, strength basis, and analysis mode.
- **Geometry:** rigid-motion invariance and shaped-section integration pass.
- **Mechanics:** equilibrium, compatible strains, failure labels, and analytic controls pass.
- **Code:** independent examples pass and factors are applied once.
- **Serviceability:** transformed-section and full-span controls pass.
- **Material:** conventional steel passes before each alternative material.
- **Hybrid:** multiple materials pass common-strain-plane equilibrium and failure sequencing.
- **Persistence:** Grasshopper save/reopen, copy/paste, clusters, migration, and serialization preserve results.
- **External review:** a qualified structural review confirms implementation and benchmarks.
- **Claims:** compliance language remains disabled until every applicable gate passes.

## Detailed reports

- [Code consistency and correctness](code-consistency.md)
- [Flexural capacity and strain compatibility](flexural-capacity.md)
- [Material model and Grasshopper schema](material-model.md)
- [Serviceability](serviceability.md)
- [Shear, transverse reinforcement, and ductility](shear-ductility.md)
- [Exploratory workflow guide](workflow-guide.md)
- [Benchmark guide](../../benchmarks/README.md)

