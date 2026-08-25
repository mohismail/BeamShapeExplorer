"""Compare exported Grasshopper observations with benchmark expectations."""

from __future__ import annotations

import argparse
import csv
import math
from pathlib import Path


KEY_FIELDS = ("benchmark_id", "case_id", "metric")


def read_rows(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


def row_key(row: dict[str, str]) -> tuple[str, str, str]:
    return tuple(row[field] for field in KEY_FIELDS)


def verify_package(package: Path, expectation_class: str) -> tuple[int, int]:
    expected_path = package / "expected.csv"
    observed_path = package / "observed.csv"
    if not expected_path.exists():
        return 0, 0
    if not observed_path.exists():
        print(f"SKIP {package.name}: observed.csv not found")
        return 0, 0

    expected_rows = [
        row
        for row in read_rows(expected_path)
        if row["expectation_class"] in (expectation_class, "geometry")
    ]
    observed_rows = {row_key(row): row for row in read_rows(observed_path)}
    passed = 0
    failed = 0

    for expected in expected_rows:
        key = row_key(expected)
        observed = observed_rows.get(key)
        if observed is None:
            print(f"FAIL {package.name} {key}: missing observation")
            failed += 1
            continue

        expected_value = float(expected["expected_value"])
        observed_value = float(observed["observed_value"])
        atol = float(expected["atol"])
        rtol = float(expected["rtol"])
        tolerance = atol + rtol * abs(expected_value)
        error = abs(observed_value - expected_value)
        finite = math.isfinite(observed_value)

        if finite and error <= tolerance:
            print(
                f"PASS {package.name} {key}: "
                f"observed={observed_value:.15g} error={error:.3g}"
            )
            passed += 1
        else:
            print(
                f"FAIL {package.name} {key}: "
                f"observed={observed_value!r} expected={expected_value:.15g} "
                f"error={error:.3g} tolerance={tolerance:.3g}"
            )
            failed += 1

    extra = sorted(set(observed_rows) - {row_key(row) for row in expected_rows})
    for key in extra:
        print(f"WARN {package.name} {key}: unexpected observation")

    return passed, failed


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("benchmarks"))
    parser.add_argument(
        "--expectation-class",
        choices=("characterization", "corrected_acceptance"),
        default="characterization",
    )
    args = parser.parse_args()

    passed = 0
    failed = 0
    for expected_path in sorted(args.root.glob("*/expected.csv")):
        package_passed, package_failed = verify_package(
            expected_path.parent, args.expectation_class
        )
        passed += package_passed
        failed += package_failed

    print(f"SUMMARY passed={passed} failed={failed}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())

