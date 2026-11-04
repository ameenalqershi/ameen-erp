#!/usr/bin/env python3
"""SPEC02 original accounting rules and event traceability (NOT posted behavior)."""
import csv
from collections import Counter
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
SOURCE = "muhaseb-erp-analysis/docs/02-accounting-and-business-rules.md"
MATRIX = "docs/backend/core-spec02-event-ownership.csv"
RULES = "docs/backend/rules-implementation.csv"
OWNER_MODULES = frozenset({"GL", "SYS", "CASH", "INV", "SAL", "PUR",
                           "TAX", "FA", "HR", "MFG", "PRJ", "COST"})
STATUS = frozenset({"manual-journal", "partial", "not-started"})
SECTIONS = (
    "## ثوابت الترحيل",
    "## دورة المستند العامة",
    "## جدول الأثر المحاسبي",
    "## مثال متكامل قابل للحساب",
    "## العملة وأسعار الصرف",
    "## التكلفة والكميات",
    "## الخصم والضريبة",
)


def check(root: Path = ROOT):
    root = Path(root)
    text = (root / SOURCE).read_text(encoding="utf-8")
    for title in SECTIONS:
        if text.count(title + "\n") != 1:
            raise ValueError(f"SPEC02: missing or duplicated required section {title}")
    source_rules = re.findall(r"^\| (BR\d{2}) \| (.+?) \|$", text, re.M)
    rule_names = [name for name, _ in source_rules]
    expected = [f"BR{n:02}" for n in range(1, 21)]
    if rule_names != expected:
        raise ValueError("SPEC02: original BR01–BR20 rule sequence mismatch")
    with (root / RULES).open(encoding="utf-8-sig", newline="") as file:
        tracked_rules = list(csv.DictReader(file))
    if [row["rule_id"] for row in tracked_rules] != expected:
        raise ValueError("SPEC02: missing/duplicate BR implementation traceability")
    for rule in tracked_rules:
        if rule["scope"] not in STATUS or not rule["implemented_and_remaining"].strip():
            raise ValueError(f"SPEC02: unclear implementation status for {rule['rule_id']}")
        if rule["scope"] == "not-started" and rule["evidence"].strip().lower() not in {"none", ""}:
            raise ValueError(f"SPEC02: unsupported completed evidence on {rule['rule_id']}")
    block = text.split("## جدول الأثر المحاسبي\n", 1)[1].split(
        "## مثال متكامل قابل للحساب\n", 1)[0]
    events = []
    for line in block.splitlines():
        if not line.startswith("| ") or line.startswith("| الحدث"):
            continue
        cells = [part.strip() for part in line.strip().strip("|").split("|")]
        if all(set(cell) <= set(" -:") for cell in cells):
            continue
        if len(cells) != 4 or any(not cell for cell in cells):
            raise ValueError(f"SPEC02: incomplete accounting example event row {line}")
        events.append(cells[0])
    if not events or len(events) != len(set(events)):
        raise ValueError("SPEC02: empty or duplicate accounting event labels")
    with (root / MATRIX).open(encoding="utf-8-sig", newline="") as file:
        references = list(csv.DictReader(file))
    labels = [row["event"] for row in references]
    if len(labels) != len(set(labels)) or set(labels) != set(events):
        raise ValueError("SPEC02: missing/duplicate/unapproved event reference")
    for row in references:
        if row["owner_module"] not in OWNER_MODULES:
            raise ValueError(f"SPEC02: undefined module owner {row['event']}")
        refs = row["related_rules"].split(";")
        if not refs or any(name not in rule_names for name in refs):
            raise ValueError(f"SPEC02: unsupported BR reference for {row['event']}")
        if row["acceptance_evidence"] != "not-verified":
            raise ValueError(f"SPEC02: misleading event completion {row['event']}")
    categories = Counter(row["scope"] for row in tracked_rules)
    print(f"PASS CORE SPEC02: {len(expected)} BR traceability rows, "
          f"{len(events)} event-owner references, {len(SECTIONS)} source sections; "
          f"rule scopes={dict(categories)}; no runtime acceptance asserted")
    return len(events), categories


if __name__ == "__main__":
    check()
