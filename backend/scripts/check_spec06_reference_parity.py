#!/usr/bin/env python3
"""SPEC06 read-only reference-parity audit; never turns proposed screens into verified parity."""
import csv
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "muhaseb-erp-analysis/docs/06-reference-parity.md"
MATRIX = ROOT / "muhaseb-erp-analysis/data/parity-matrix.csv"
SOURCES = ROOT / "muhaseb-erp-analysis/data/sources.json"
REPORT = ROOT / "docs/backend/core-spec06-reference-parity-audit-20261105.md"
EXPECTED_GAPS = tuple(f"GAP{n:02}" for n in range(1, 9))
EXPECTED_REFERENCES = {f"S{n:02}" for n in range(1, 7)}


def read_csv(path):
    with Path(path).open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def check(source=SOURCE, matrix=MATRIX, sources=SOURCES, report=REPORT):
    source_text = Path(source).read_text(encoding="utf-8")
    for gap in EXPECTED_GAPS:
        if source_text.count(f"| {gap} |") != 1:
            raise ValueError(f"SPEC06 original gap missing/duplicate: {gap}")
    rows = read_csv(matrix)
    ids = [row["معرفنا"] for row in rows]
    if len(rows) != 150 or len(set(ids)) != 150:
        raise ValueError(f"SPEC06 expected 150 unique screen rows; found {len(rows)} rows / {len(set(ids))} IDs")
    for row in rows:
        if row["حالة الدليل"] != "غير متحقق":
            raise ValueError(f"SPEC06 reference parity overclaim for {row['معرفنا']}: {row['حالة الدليل']}")
        if any(row[key].strip() for key in ("اسم شاشة المرجع", "إصدار المرجع", "مرجع الصفحة أو اللقطة", "تاريخ التحقق")):
            raise ValueError(f"SPEC06 reference identity/evidence unexpectedly populated: {row['معرفنا']}")
    source_rows = json.loads(Path(sources).read_text(encoding="utf-8"))
    refs = {row["id"]: row for row in source_rows}
    if set(refs) < EXPECTED_REFERENCES:
        raise ValueError("SPEC06 expected reference sources S01–S06")
    for ref in ("S04", "S05", "S06"):
        if refs[ref]["status"] != "تعذر الجلب":
            raise ValueError(f"SPEC06 inaccessible reference status changed without re-audit: {ref}")
    report_text = Path(report).read_text(encoding="utf-8")
    for token in ("8/8", "150/150", "غير متحقق", "D01", "D02", "SUPERVISOR"):
        if token not in report_text:
            raise ValueError(f"SPEC06 report missing audit boundary: {token}")
    if "نسبة مطابقة" in report_text and "لا نسبة مطابقة" not in report_text:
        raise ValueError("SPEC06 must not claim a reference parity percentage")
    print("PASS CORE SPEC06: eight source gaps mapped; all 150 screen rows unique and remain unverified; S04–S06 inaccessible")
    return len(rows), len(EXPECTED_GAPS)


if __name__ == "__main__":
    check()
