#!/usr/bin/env python3
"""SPEC07 read-only audit: ensure all original acceptance scenarios remain traceable and unclaimed."""
import csv
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "muhaseb-erp-analysis/docs/07-workflows-and-acceptance.md"
MATRIX = ROOT / "docs/backend/core-spec07-acceptance-traceability.csv"
REPORT = ROOT / "docs/backend/core-spec07-workflows-acceptance-audit-20261105.md"
EXPECTED = tuple(f"AT{n:02}" for n in range(1, 31))
LINK = re.compile(r"https://app\.notion\.com/p/[a-f0-9]{32}")


def read_csv(path):
    with Path(path).open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def check(source=SOURCE, matrix=MATRIX, report=REPORT):
    text = Path(source).read_text(encoding="utf-8")
    if "اختبارات قبول **لتنفيذ النظام مستقبلًا**" not in text:
        raise ValueError("SPEC07 source acceptance boundary missing")
    rows = read_csv(matrix)
    ids = [row["scenario_id"] for row in rows]
    if tuple(ids) != EXPECTED:
        raise ValueError(f"SPEC07 expected 30 unique AT scenarios; found {ids}")
    for row in rows:
        if not row["screen_ids"].strip() or not row["scenario"].strip() or not row["expected_result"].strip() or not row["blocker"].strip():
            raise ValueError(f"SPEC07 incomplete scenario mapping: {row['scenario_id']}")
        if not LINK.fullmatch(row["notion_task_url"]):
            raise ValueError(f"SPEC07 invalid Notion task URL: {row['scenario_id']}")
        if row["evidence_status"] != "not-verified":
            raise ValueError(f"SPEC07 cannot claim execution evidence: {row['scenario_id']}")
        if text.count(f"| {row['scenario_id']} |") != 1:
            raise ValueError(f"SPEC07 source scenario missing/duplicate: {row['scenario_id']}")
    report_text = Path(report).read_text(encoding="utf-8")
    for token in ("30/30", "not-verified", "SUPERVISOR", "مستقبلًا"):
        if token not in report_text:
            raise ValueError(f"SPEC07 report missing acceptance boundary: {token}")
    print(f"PASS CORE SPEC07: all {len(rows)} future acceptance scenarios linked; no test execution is claimed")
    return len(rows)


if __name__ == "__main__":
    check()
