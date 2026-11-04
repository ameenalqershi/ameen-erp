#!/usr/bin/env python3
"""CORE DGM-28: audit the original conceptual ER diagram, never infer SQL DDL."""
import csv
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
EDGE = re.compile(
    r"^\s*(?P<source>[A-Z][A-Z0-9_]*)\s+"
    r"(?P<left>\|\||o\|)--(?P<right>o\{|\|\{|o\|)\s+"
    r"(?P<target>[A-Z][A-Z0-9_]*)\s*:\s*"
    r"(?P<label>[a-z][a-z0-9_]*)\s*$"
)
SOURCE = "muhaseb-erp-analysis/diagrams/source/data-model.mmd"
SVG = "muhaseb-erp-analysis/diagrams/svg/data-model.svg"

# Links explicitly present in the immutable conceptual analysis, not SQL policy.
REQUIRED = {
    ("COMPANY", "||", "o{", "BRANCH", "contains"),
    ("COMPANY", "||", "o{", "ACCOUNT", "owns"),
    ("COMPANY", "||", "o{", "DOCUMENT", "owns"),
    ("DOCUMENT", "||", "|{", "DOCUMENT_LINE", "contains"),
    ("DOCUMENT_LINE", "||", "o{", "LINE_LINK", "source"),
    ("DOCUMENT_LINE", "||", "o{", "LINE_LINK", "target"),
    ("DOCUMENT", "||", "o{", "JOURNAL_ENTRY", "source"),
    ("JOURNAL_ENTRY", "||", "|{", "JOURNAL_LINE", "contains"),
    ("DOCUMENT_LINE", "||", "o{", "STOCK_MOVEMENT", "generates"),
    ("DOCUMENT_LINE", "||", "o{", "TAX_SNAPSHOT", "taxes"),
    ("DOCUMENT", "||", "o{", "OPEN_ITEM", "creates"),
    ("DOCUMENT", "||", "o|", "EINVOICE", "fiscal_representation"),
    ("EINVOICE", "||", "o{", "DELIVERY_ATTEMPT", "sent_by"),
    ("DOCUMENT", "||", "o{", "APPROVAL", "approved_by"),
    ("DOCUMENT", "||", "o{", "AUDIT_EVENT", "tracked_by"),
    ("DOCUMENT", "||", "o{", "OUTBOX_EVENT", "publishes"),
}


def check(root: Path = ROOT):
    root = Path(root)
    diagrams = json.loads(
        (root / "muhaseb-erp-analysis/data/diagrams.json").read_text(
            encoding="utf-8"))
    found = [item for item in diagrams if item["id"] == "data-model"]
    if len(found) != 1:
        raise ValueError("DGM-28: missing/duplicate indexed ER model")
    if found[0]["mermaid"] != "diagrams/source/data-model.mmd" or (
            found[0]["svg"] != "diagrams/svg/data-model.svg"):
        raise ValueError("DGM-28: incorrect manifest path")
    with (root / "docs/backend/diagrams-implementation.csv").open(
            encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle))
    mapped = [item for item in rows if item["diagram_id"] == "data-model"]
    if len(mapped) != 1 or mapped[0]["analysis_source"] != SOURCE:
        raise ValueError("DGM-28: missing/incorrect ER traceability row")
    if mapped[0]["status"] not in {"partial", "not-started", "implemented"}:
        raise ValueError("DGM-28: unknown traceability status")
    ET.parse(root / SVG)
    lines = (root / SOURCE).read_text(encoding="utf-8").splitlines()
    if not lines or lines[0].strip() != "erDiagram":
        raise ValueError("DGM-28: expected conceptual erDiagram, not SQL DDL")
    relations = set()
    entities = set()
    for line in lines[1:]:
        if not line.strip():
            continue
        edge = EDGE.fullmatch(line)
        if not edge:
            raise ValueError(f"DGM-28: unsupported ER relation syntax: {line}")
        item = tuple(edge.group(key) for key in
                     ("source", "left", "right", "target", "label"))
        if item in relations:
            raise ValueError(f"DGM-28: duplicate ER relationship: {item}")
        relations.add(item)
        entities.update((item[0], item[3]))
    if not relations:
        raise ValueError("DGM-28: no ER relationships")
    missing = REQUIRED - relations
    if missing:
        raise ValueError(f"DGM-28: missing original ER relationship: {sorted(missing)}")
    # Self-relation ACCOUNT -> ACCOUNT is intentional; names are logical concepts.
    print(f"PASS CORE DGM-28: {len(entities)} conceptual entities, "
          f"{len(relations)} ER relationships, mapped status={mapped[0]['status']}; "
          "no database constraint/DDL inferred")
    return len(entities), len(relations), mapped[0]["status"]


if __name__ == "__main__":
    check()
