#!/usr/bin/env python3
"""Read-only acceptance audit of the original CASH navigation diagram.

Checks the Mermaid source, SVG labels, all original screen headings and field
dictionary records against the backend's explicit implementation map.
Never updates any approved source or business state.
"""
from collections import Counter, defaultdict, deque
import csv
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "muhaseb-erp-analysis"
MODEL = SOURCE / "diagrams/source/module-CASH.mmd"
SVG = SOURCE / "diagrams/svg/module-CASH.svg"
MODULE = SOURCE / "docs/modules/CASH.md"
FIELDS = SOURCE / "data/fields.csv"
SCREENS = ROOT / "docs/backend/screens-implementation.csv"
EXPECTED = {
    "CASH01": 16, "CASH02": 17, "CASH03": 24, "CASH04": 24,
    "CASH05": 22, "CASH06": 23, "CASH07": 22, "CASH08": 22,
    "CASH09": 13, "CASH10": 17, "CASH11": 13,
}
ROOT_NODE = "root"
CATEGORIES = {"cat0", "cat1", "cat2"}
ROUTES = {
    "cat0": ("CASH01", "CASH02"),
    "cat1": ("CASH03", "CASH04", "CASH05", "CASH06", "CASH07", "CASH08"),
    "cat2": ("CASH09", "CASH10", "CASH11"),
}


def ensure(test: bool, message: str) -> None:
    if not test:
        raise AssertionError("CASH diagram audit: " + message)


def read_rows(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as file:
        return list(csv.DictReader(file))


def check() -> None:
    diagram = MODEL.read_text(encoding="utf-8")
    module = MODULE.read_text(encoding="utf-8")
    svg = SVG.read_text(encoding="utf-8")
    nodes = dict(re.findall(r'^\s*(\w+)\["([^"]+)"\]\s*$', diagram, re.M))
    edges = re.findall(r"^\s*(\w+)\s+-->\s*(?:\|[^|]+\|\s*)?(\w+)\s*$", diagram, re.M)
    expected_nodes = {ROOT_NODE, *CATEGORIES, *EXPECTED}
    ensure(set(nodes) == expected_nodes and len(nodes) == 15, "15 original nodes do not match")
    ensure(len(edges) == len(set(edges)) == 14, "14 original directed links do not match")
    ensure(all(a in nodes and b in nodes for a, b in edges), "edge points to missing node")
    ensure({b for a, b in edges if a == ROOT_NODE} == CATEGORIES, "root category links differ")
    actual_routes = {}
    for cat in sorted(CATEGORIES):
        result = []
        current = cat
        visited = set()
        while True:
            outgoing = [b for a, b in edges if a == current]
            ensure(len(outgoing) <= 1, f"{current} has ambiguous route")
            if not outgoing:
                break
            next_node = outgoing[0]
            ensure(next_node not in visited, "cyclic navigation route")
            visited.add(next_node)
            ensure(next_node in EXPECTED, "unexpected screen in route")
            result.append(next_node)
            current = next_node
        actual_routes[cat] = tuple(result)
    ensure(actual_routes == ROUTES, "navigation categories/chains differ from audited source")

    queue = deque([ROOT_NODE])
    reached = set()
    while queue:
        current = queue.popleft()
        if current in reached:
            continue
        reached.add(current)
        queue.extend(b for a, b in edges if a == current)
    ensure(reached == expected_nodes, "orphaned navigation node")

    rows = read_rows(FIELDS)
    cash_rows = [x for x in rows if x["الشاشة"] in EXPECTED]
    counts = Counter(x["الشاشة"] for x in cash_rows)
    ensure(dict(counts) == EXPECTED, f"field counts changed: {dict(counts)}")
    ensure(len(cash_rows) == 213, "original CASH field total must be 213")
    ensure(len({x["معرف الحقل"] for x in cash_rows}) == 213, "duplicate field ID")
    for code, total in EXPECTED.items():
        ids = {x["معرف الحقل"] for x in cash_rows if x["الشاشة"] == code}
        ensure(ids == {f"{code}-F{n:02d}" for n in range(1, total + 1)},
               f"missing or non-contiguous field ID in {code}")
        ensure(len(re.findall(rf"^##\s+{code}\s+—", module, re.M)) == 1,
               f"missing or duplicate specification heading {code}")
        ensure(len(re.findall(rf'\b{code}\b', svg)) == 1,
               f"missing or duplicate SVG screen label {code}")
        ensure(nodes[code].startswith(code + " • "), f"wrong Mermaid screen label {code}")

    mappings = read_rows(SCREENS)
    actual = {r["screen_id"]: r for r in mappings if r["screen_id"] in EXPECTED}
    ensure(set(actual) == set(EXPECTED), "missing CASH screen implementation-map record")
    ensure(all(r["module"] == "CASH" for r in actual.values()), "wrong module ownership")
    for code, record in actual.items():
        ensure(record["status"] in {"partial", "not-started"}, f"unsupported status for {code}")
        if code in {"CASH01", "CASH02", "CASH03", "CASH07", "CASH08", "CASH11"}:
            ensure(record["status"] == "partial" and bool(record["implemented_scope"]),
                   f"structural/operational scope must be partial: {code}")
            ensure(bool(record["remaining"]), f"missing operational blockers {code}")
            tests = [x.strip() for x in record["test_file"].split(";") if x.strip()]
            ensure(bool(tests) and all((ROOT / x).is_file() for x in tests),
                   f"missing recorded acceptance file for {code}")
        else:
            ensure(record["status"] == "not-started", f"false operational completion {code}")

    print("PASS DIAG-module-CASH: 15 nodes, 14 directed navigation links, 3 category paths")
    print("PASS DIAG-module-CASH: 11 screen headings, 213 contiguous original field IDs")
    print("PASS DIAG-module-CASH: 11 matching SVG labels, 11 backend screen records")
    print("PASS DIAG-module-CASH: CASH01/02/03/07/08/11 structural partial; CASH04/05/06/09/10 operational not-started")


if __name__ == "__main__":
    check()
