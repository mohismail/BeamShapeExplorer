# BeamShapeExplorer Reference Benchmarks

These source-controlled benchmark packages define three Grasshopper tests without fabricating unvalidated `.gh` or `.ghx` archives:

1. `rectangular-steel-control` — current IS/ACI flexural characterization and corrected equation-family references.
2. `low-e-sensitivity` — serviceability response for `Es = 50–205 GPa`.
3. `shaped-geometry-invariance` — shaped-section geometry and rigid-translation behavior.

Rhino 8 and Grasshopper must be used to create the final definitions. Hand-authored GHX is not accepted.

## Dataset contract

Each package contains:

- `manifest.json` — inputs, Grasshopper construction steps, and gates.
- `expected.csv` — characterization and corrected-acceptance values.

Copy `observed.csv.example` from this directory into each package as `observed.csv` before exporting results.

Expected columns:

```text
benchmark_id,case_id,expectation_class,metric,expected_value,unit,atol,rtol,expected_current_status,reference_basis
```

Observed columns:

```text
benchmark_id,case_id,metric,observed_value,unit,rhino_version,grasshopper_version,bse_assembly_version,bse_git_revision
```

An observation passes when:

```text
abs(observed - expected) <= atol + rtol * abs(expected)
```

Run:

```powershell
uv run python benchmarks/tools/verify.py --root benchmarks
```

After remediation, compare the same exports against corrected targets:

```powershell
uv run python benchmarks/tools/verify.py --root benchmarks --expectation-class corrected_acceptance
```

## Creating Grasshopper archives

For each package:

1. Read its `manifest.json`.
2. Set Rhino model units and tolerances exactly as specified.
3. Build the listed geometry and component wiring.
4. Save the definition under `<benchmark>/grasshopper/<benchmark>.gh`.
5. Close and reopen it.
6. Recompute and export `observed.csv`.
7. Record Rhino, Grasshopper, plugin, and Git versions.
8. Run the verifier.

Do not place two active Select Code components in one definition. The selector writes application-global state; set one code, recompute, capture, then repeat.

## Expectation classes

- `characterization` records current implementation behavior. It protects reproducibility but may preserve a known bug.
- `corrected_acceptance` defines behavior required after remediation.
- `geometry` records independently derived shape properties.

Corrected equation-family references are not code certification because the repository does not identify supported IS 456 or ACI 318 editions.

