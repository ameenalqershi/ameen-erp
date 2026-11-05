#!/usr/bin/env python3
"""SPEC04 read-only localization traceability audit; no legal policy is activated."""
import csv
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = "muhaseb-erp-analysis/docs/04-localization.md"
SCREENS = "docs/backend/screens-implementation.csv"
FIELDS = "docs/backend/fields-implementation.csv"
MATRIX = "docs/backend/core-spec04-localization-traceability.csv"
COUNTRIES = "docs/backend/core-spec04-country-gates.csv"

EXPECTED_COUNTRIES = ("اليمن", "السعودية", "الإمارات", "عمان", "البحرين", "قطر", "الكويت")
EXPECTED_REQUIREMENTS = tuple(f"LOC{n:02}" for n in range(1, 15))
EXPECTED_SCREEN_STATUS = {
    **{f"TAX{n:02}": "not-started" for n in range(1, 9)},
    "SYS04": "partial", "SYS05": "partial",
    "SYS12": "not-started", "SYS15": "not-started",
    "SAL06": "not-started", "PUR07": "not-started",
    "CASH03": "partial", "IMP06": "not-started",
    "HR05": "not-started", "GL08": "not-started",
}
EXPECTED_TAX_FIELDS = {
    "TAX01": 15, "TAX02": 19, "TAX03": 12, "TAX04": 15,
    "TAX05": 14, "TAX06": 16, "TAX07": 26, "TAX08": 16,
}


def _read_csv(path):
    with Path(path).open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def check(root: Path = ROOT):
    root = Path(root)
    text = (root / SOURCE).read_text(encoding="utf-8")
    for heading in (
        "## بنية ملف الدولة المقترحة",
        "## دورة الفوترة الإلكترونية",
        "## متطلبات إقليمية عملية مقترحة",
    ):
        if text.count(heading) != 1:
            raise ValueError(f"SPEC04: missing/duplicate source section: {heading}")
    if "جميع ملفات الدول غير مفعلة للإنتاج" not in text:
        raise ValueError("SPEC04: source activation boundary missing")
    if "لا تُثبت النسب داخل كود البرنامج" not in text:
        raise ValueError("SPEC04: no-hardcoded-rates rule missing")
    country_rows = [
        line for line in text.splitlines()
        if any(line.startswith(f"| {country} |") for country in EXPECTED_COUNTRIES)
    ]
    if len(country_rows) != 7 or any(
        sum(line.startswith(f"| {country} |") for line in country_rows) != 1
        for country in EXPECTED_COUNTRIES
    ):
        raise ValueError("SPEC04: original country table must contain exactly seven unique countries")

    screens = {row["screen_id"]: row for row in _read_csv(root / SCREENS)}
    fields = _read_csv(root / FIELDS)
    for screen_id, expected_status in EXPECTED_SCREEN_STATUS.items():
        row = screens.get(screen_id)
        if row is None:
            raise ValueError(f"SPEC04: screen missing from implementation matrix: {screen_id}")
        if row["status"] != expected_status:
            raise ValueError(
                f"SPEC04: {screen_id} status changed from audited {expected_status} to {row['status']}; refresh evidence"
            )
    tax_counts = {}
    for screen_id, expected_count in EXPECTED_TAX_FIELDS.items():
        group = [row for row in fields if row["screen_id"] == screen_id]
        pending = sum(row["status"] == "pending" for row in group)
        if len(group) != expected_count or pending != expected_count:
            raise ValueError(
                f"SPEC04: {screen_id} expected {expected_count} pending fields, found {len(group)} rows / {pending} pending"
            )
        tax_counts[screen_id] = pending

    matrix = _read_csv(root / MATRIX)
    ids = [row["requirement_id"] for row in matrix]
    if tuple(ids) != EXPECTED_REQUIREMENTS:
        raise ValueError("SPEC04: all 14 localization requirement groups must be uniquely mapped in order")
    for row in matrix:
        if not row["screen_refs"].strip() or not row["notion_task_refs"].strip():
            raise ValueError(f"SPEC04: unlinked requirement {row['requirement_id']}")
        refs = row["screen_refs"].split(";")
        missing = [ref for ref in refs if ref not in screens]
        if missing:
            raise ValueError(f"SPEC04: unknown screen reference for {row['requirement_id']}: {missing}")
        if not row["blocker"].strip() or not row["decision_owner"].strip():
            raise ValueError(f"SPEC04: blocker/decision-owner gap must be explicit for {row['requirement_id']}")
        if row["acceptance_evidence"] != "not-verified":
            raise ValueError(f"SPEC04: reference audit cannot claim runtime acceptance for {row['requirement_id']}")

    gates = _read_csv(root / COUNTRIES)
    if tuple(row["country"] for row in gates) != EXPECTED_COUNTRIES:
        raise ValueError("SPEC04: country gates must map all seven source countries in source order")
    for row in gates:
        if row["activation_state"] != "not-enabled" or row["acceptance_evidence"] != "not-verified":
            raise ValueError(f"SPEC04: country activation/acceptance overclaim for {row['country']}")
        if not row["decision_required_before_activation"].strip() or not row["linked_screens"].strip():
            raise ValueError(f"SPEC04: country decision or task links missing for {row['country']}")
        if row["source_reference_in_original_analysis"] != "not-cited-in-source" and (
            row["source_reference_in_original_analysis"] not in text
        ):
            raise ValueError(f"SPEC04: country source URL not present in original analysis: {row['country']}")

    print(
        f"PASS CORE SPEC04: {len(EXPECTED_REQUIREMENTS)} requirement groups, "
        f"{len(EXPECTED_COUNTRIES)} country gates, {sum(tax_counts.values())} TAX fields "
        "still pending on audited main; all references resolve and no production/legal acceptance is claimed"
    )
    return len(matrix), len(gates), sum(tax_counts.values())


if __name__ == "__main__":
    check()
