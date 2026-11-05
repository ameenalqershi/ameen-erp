#!/usr/bin/env python3
"""SPEC05 read-only audit: verify source sections and traceability links without inventing policy."""
import csv
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "muhaseb-erp-analysis/docs/05-data-and-controls.md"
MATRIX = ROOT / "docs/backend/core-spec05-data-controls-traceability.csv"
REPORT = ROOT / "docs/backend/core-spec05-data-controls-audit-20261105.md"
EXPECTED = tuple(f"DC{n:02}" for n in range(1, 15))
HEADINGS = (
    "## حدود الكيانات",
    "## أدوار مقترحة",
    "## الفصل بين المهام",
    "## جودة وأداء مقترحان للقياس لاحقًا",
    "## نقل البيانات والأرصدة الافتتاحية",
)
LINK = re.compile(r"https://app\.notion\.com/p/[a-f0-9]{32}")


def read_csv(path):
    with Path(path).open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def check(source=SOURCE, matrix=MATRIX, report=REPORT):
    source_text = Path(source).read_text(encoding="utf-8")
    for heading in HEADINGS:
        if source_text.count(heading) != 1:
            raise ValueError(f"SPEC05 missing/duplicate source heading: {heading}")
    rows = read_csv(matrix)
    ids = [row["requirement_id"] for row in rows]
    if tuple(ids) != EXPECTED:
        raise ValueError(f"SPEC05 expected 14 unique requirement groups; found {ids}")
    required = ("source_section", "requirement", "notion_task_refs", "blocker",
                "decision_owner", "acceptance_evidence")
    for row in rows:
        if any(not row[key].strip() for key in required):
            raise ValueError(f"SPEC05 incomplete traceability row: {row['requirement_id']}")
        links = LINK.findall(row["notion_task_refs"])
        if not links:
            raise ValueError(f"SPEC05 no Notion task link: {row['requirement_id']}")
        if row["acceptance_evidence"] != "not-verified":
            raise ValueError(f"SPEC05 reference audit cannot claim runtime acceptance: {row['requirement_id']}")
        if "غير معيّن" not in row["decision_owner"] and "UNASSIGNED" not in row["decision_owner"]:
            raise ValueError(f"SPEC05 decision owner must be named or explicitly unassigned: {row['requirement_id']}")
    report_text = Path(report).read_text(encoding="utf-8")
    for token in ("14/14", "not-verified", "RPO", "RTO", "SUPERVISOR"):
        if token not in report_text:
            raise ValueError(f"SPEC05 report missing audit boundary: {token}")
    if "CREATE TABLE" in report_text.upper() or "ALTER TABLE" in report_text.upper():
        raise ValueError("SPEC05 audit must not propose DDL")
    print(f"PASS CORE SPEC05: {len(rows)} requirement groups linked; all blockers/owners explicit; no runtime acceptance claimed")
    return len(rows)


if __name__ == "__main__":
    check()
