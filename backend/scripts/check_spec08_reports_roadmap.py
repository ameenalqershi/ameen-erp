#!/usr/bin/env python3
"""SPEC08 read-only audit: validate report contracts and roadmap links without claiming execution."""
import csv
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "muhaseb-erp-analysis/docs/08-reports-and-roadmap.md"
CONTRACTS = ROOT / "docs/backend/core-spec08-report-contracts.csv"
PHASES = ROOT / "docs/backend/core-spec08-roadmap-phases.csv"
REPORT = ROOT / "docs/backend/core-spec08-reports-roadmap-audit-20261105.md"
EXPECTED_CONTRACTS = tuple(f"RC{n:02}" for n in range(1, 20))
EXPECTED_PHASES = tuple(f"PH{n:02}" for n in range(1, 8))
LINK = re.compile(r"https://app\.notion\.com/p/[a-f0-9]{32}")


def read_csv(path):
    with Path(path).open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def check(source=SOURCE, contracts=CONTRACTS, phases=PHASES, report=REPORT):
    source_text = Path(source).read_text(encoding="utf-8")
    if source_text.count("## عقد التقرير") != 1 or source_text.count("## مراحل تنفيذ لاحقة") != 1:
        raise ValueError("SPEC08 source sections missing or duplicated")
    report_rows = [line for line in source_text.splitlines() if line.startswith("|") and line.count("|") >= 3]
    if len(report_rows) - 2 != 19:
        raise ValueError(f"SPEC08 expected 19 source report contracts; found {len(report_rows) - 2}")
    contract_rows = read_csv(contracts)
    contract_ids = [row["contract_id"] for row in contract_rows]
    if tuple(contract_ids) != EXPECTED_CONTRACTS:
        raise ValueError("SPEC08 expected 19 unique report contracts")
    for row in contract_rows:
        if not row["report_name"].strip() or not row["acceptance_rule"].strip() or not LINK.fullmatch(row["notion_task_url"]):
            raise ValueError(f"SPEC08 incomplete report contract: {row['contract_id']}")
        if row["evidence_status"] != "not-verified":
            raise ValueError(f"SPEC08 cannot claim report acceptance: {row['contract_id']}")
    phase_rows = read_csv(phases)
    phase_ids = [row["phase_id"] for row in phase_rows]
    if tuple(phase_ids) != EXPECTED_PHASES:
        raise ValueError("SPEC08 expected seven unique roadmap phases")
    for row in phase_rows:
        links = LINK.findall(row["notion_task_refs"])
        if not links or not row["exit_condition"].strip():
            raise ValueError(f"SPEC08 incomplete phase mapping: {row['phase_id']}")
        if row["evidence_status"] != "not-verified":
            raise ValueError(f"SPEC08 cannot claim phase acceptance: {row['phase_id']}")
    report_text = Path(report).read_text(encoding="utf-8")
    for token in ("19/19", "7/7", "not-verified", "SUPERVISOR"):
        if token not in report_text:
            raise ValueError(f"SPEC08 report missing audit boundary: {token}")
    if any(token in report_text.lower() for token in ("deadline approved", "sla approved", "technology selected")):
        raise ValueError("SPEC08 audit must not invent a date, SLA or technology")
    print("PASS CORE SPEC08: 19 report contracts and seven phases linked; no runtime acceptance claimed")
    return len(contract_rows), len(phase_rows)


if __name__ == "__main__":
    check()
