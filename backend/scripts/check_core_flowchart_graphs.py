#!/usr/bin/env python3
"""CORE DGM-16–19: verify source graph integrity, not business completion."""
import csv
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
NAMES = ("system-map", "document-lifecycle", "purchase-to-pay", "sale-to-cash")
NODE = re.compile(r'^\s*(\w+)\["([^"]+)"\]\s*$')
EDGE = re.compile(r'^\s*(\w+)\s*-->\s*(?:\|"([^"]+)"\|\s*)?(\w+)\s*$')


def check(root: Path = ROOT, names=NAMES):
    root = Path(root)
    source = root / "muhaseb-erp-analysis"
    maps = json.loads((source / "data/diagrams.json").read_text(encoding="utf-8"))
    selected = [d for d in maps if d["id"] in set(names)]
    if len(selected) != len(names) or {d["id"] for d in selected} != set(names):
        raise ValueError("CORE DGM: missing/duplicate indexed flowchart")
    with (root / "docs/backend/diagrams-implementation.csv").open(encoding="utf-8-sig", newline="") as file:
        records = list(csv.DictReader(file))
    counts = {}
    for diagram in selected:
        name = diagram["id"]
        indexed = [row for row in records if row["diagram_id"] == name]
        if len(indexed) != 1:
            raise ValueError(f"CORE DGM: missing/duplicate traceability row {name}")
        record = indexed[0]
        source_path = f"diagrams/source/{name}.mmd"
        svg_path = f"diagrams/svg/{name}.svg"
        if diagram["mermaid"] != source_path or diagram["svg"] != svg_path:
            raise ValueError(f"CORE DGM: unexpected index location {name}")
        if record["analysis_source"] != f"muhaseb-erp-analysis/{source_path}":
            raise ValueError(f"CORE DGM: incorrect traceability source {name}")
        if record["status"] not in ("partial", "not-started", "implemented"):
            raise ValueError(f"CORE DGM: unverified traceability status {name}")
        ET.parse(source / svg_path)
        lines = (source / source_path).read_text(encoding="utf-8").splitlines()
        if not lines or lines[0].strip() != "flowchart TB":
            raise ValueError(f"CORE DGM: unsupported graph form {name}")
        nodes = {}
        edges = set()
        incoming = {}
        for line in lines[1:]:
            if not line.strip():
                continue
            n = NODE.fullmatch(line)
            e = EDGE.fullmatch(line)
            if n:
                key, label = n.groups()
                if key in nodes:
                    raise ValueError(f"CORE DGM: duplicate graph node {key}")
                if not label.strip():
                    raise ValueError(f"CORE DGM: empty graph label {key}")
                nodes[key] = label
            elif e:
                frm, label, to = e.groups()
                edge = (frm, to, label or "")
                if edge in edges:
                    raise ValueError(f"CORE DGM: duplicate graph edge {edge}")
                edges.add(edge)
                incoming[to] = incoming.get(to, 0) + 1
            else:
                raise ValueError(f"CORE DGM: unrecognized source graph line: {line}")
        if not nodes or not edges:
            raise ValueError(f"CORE DGM: empty graph {name}")
        for a, b, _ in edges:
            if a not in nodes or b not in nodes:
                raise ValueError(f"CORE DGM: dangling flow edge {a}->{b}")
        connected = {node for a, b, _ in edges for node in (a, b)}
        if connected != set(nodes):
            raise ValueError(f"CORE DGM: unreachable stages {name} {sorted(set(nodes)-connected)}")
        roots = {n for n in nodes if n not in incoming}
        # Document lifecycle legitimately contains a correction loop; do not reject cycles.
        if not roots:
            roots = {next(iter(nodes))}
        visited = set(roots)
        while True:
            reached = visited | {b for a, b, _ in edges if a in visited}
            if reached == visited:
                break
            visited = reached
        if visited != set(nodes):
            raise ValueError(f"CORE DGM: unreachable stages {name} {sorted(set(nodes)-visited)}")
        counts[name] = (len(nodes), len(edges), record["status"])
    report = "; ".join(f"{name}: {n} nodes / {e} edges ({state})" for name, (n, e, state) in counts.items())
    print("PASS CORE DGM-16–19: " + report)
    return counts


if __name__ == "__main__":
    check()
