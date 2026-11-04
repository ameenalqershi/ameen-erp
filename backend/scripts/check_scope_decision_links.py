#!/usr/bin/env python3
"""SPEC01 audit: keep recorded decision/Notion linkage complete without creating policy."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "muhaseb-erp-analysis/docs/01-scope-and-decisions.md"
REPORT = ROOT / "docs/backend/core-spec01-decisions-audit-20261104.md"

SOURCE_CODE = re.compile(r"^\|\s*(D\d{2})\s*\|", re.MULTILINE)
REPORT_ROW = re.compile(
    r"^\|\s*(D\d{2})\s*\|\s*\[[^\]]+\]"
    r"\(https://app\.notion\.com/p/[a-f0-9]{32}\)"
    r"\s*\|\s*(لم يبدأ|محجوب|قيد التنفيذ|قيد التحقق|جزئي|مكتمل)\s*\|", re.MULTILINE
)


def _unique(codes, kind):
    if len(codes) != len(set(codes)):
        raise ValueError(f"SPEC01 duplicate {kind} decision")
    return set(codes)


def check(source: Path = SOURCE, report: Path = REPORT):
    source_codes = _unique(SOURCE_CODE.findall(Path(source).read_text(encoding="utf-8")), "original")
    if not source_codes:
        raise ValueError("SPEC01 original decisions missing")
    report_text = Path(report).read_text(encoding="utf-8")
    # regex returns (code, state) pairs; check duplicated IDs, not only (ID,state).
    rows = REPORT_ROW.findall(report_text)
    ids = [code for code, state in rows]
    _unique(ids, "report")
    if source_codes != set(ids):
        raise ValueError(f"SPEC01 decision coverage mismatch: "
                         f"missing={sorted(source_codes - set(ids))}, "
                         f"extra={sorted(set(ids) - source_codes)}")
    print(f"PASS SPEC01: {len(ids)} original decisions linked to Notion, "
          "no missing tracked decision (online status not asserted)")


if __name__ == "__main__":
    check()
