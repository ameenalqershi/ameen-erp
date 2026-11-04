#!/usr/bin/env python3
"""CORE DGM-01..15: verify that immutable module maps mirror the approved screen registry.

Navigation edges are menu/grouping paths, NOT implementation dependencies.
"""
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ANALYSIS = Path(__file__).resolve().parents[2] / "muhaseb-erp-analysis"
NODE = re.compile(r'^\s*(root|cat\d+|[A-Z]+\d{2})\["([^"]+)"\]\s*$')
EDGE = re.compile(r'^\s*(\w+)\s*-->\s*(?:\|"[^"]+"\|\s*)?(\w+)\s*$')


def check(root: Path = ANALYSIS, expected_modules=None):
    root = Path(root)
    screens = json.loads((root / "data/screens.json").read_text(encoding="utf-8"))
    diagrams = json.loads((root / "data/diagrams.json").read_text(encoding="utf-8"))
    if expected_modules is None:
        expected_modules = {"SYS", "GL", "CASH", "INV", "SAL", "PUR", "POS", "IMP",
                            "FA", "COST", "HR", "MFG", "PRJ", "TAX", "RPT"}
    expected_modules = set(expected_modules)
    per_module = {}
    for screen in screens:
        module = screen["module"]
        if module in expected_modules:
            per_module.setdefault(module, {})[screen["id"]] = screen
    maps = [d for d in diagrams if d["id"].startswith("module-")]
    if {d["id"][7:] for d in maps} != expected_modules or len(maps) != len(expected_modules):
        raise ValueError("CORE DGM: missing or duplicate module diagram")
    if set(per_module) != expected_modules:
        raise ValueError("CORE DGM: screen registry missing a module")
    seen_screen_ids = set()
    for diagram in maps:
        module = diagram["id"][7:]
        source = f"diagrams/source/module-{module}.mmd"
        svg = f"diagrams/svg/module-{module}.svg"
        if diagram["mermaid"] != source or diagram["svg"] != svg:
            raise ValueError(f"CORE DGM: unexpected diagram path for {module}")
        ET.parse(root / svg)
        lines = (root / source).read_text(encoding="utf-8").splitlines()
        if not lines or lines[0].strip() != "flowchart TB":
            raise ValueError(f"CORE DGM: unsupported module diagram header {module}")
        nodes, edges = {}, []
        for line in lines[1:]:
            if not line.strip():
                continue
            node = NODE.fullmatch(line)
            edge = EDGE.fullmatch(line)
            if node:
                key, label = node.groups()
                if key in nodes:
                    raise ValueError(f"CORE DGM: duplicate node {key}")
                nodes[key] = label
            elif edge:
                edges.append(edge.groups())
            else:
                raise ValueError(f"CORE DGM: unrecognized Mermaid line in {module}: {line}")
        actual_ids = {key for key in nodes if re.fullmatch(r"[A-Z]+\d{2}", key)}
        expected = per_module[module]
        if actual_ids != set(expected):
            raise ValueError(f"CORE DGM: screen ID mismatch in {module}: "
                             f"missing={sorted(set(expected) - actual_ids)}, "
                             f"extra={sorted(actual_ids - set(expected))}")
        for code, screen in expected.items():
            if code in seen_screen_ids:
                raise ValueError(f"CORE DGM: repeated screen {code}")
            seen_screen_ids.add(code)
            if nodes[code] != f"{code} • {screen['name']}":
                raise ValueError(f"CORE DGM: screen label mismatch {code}")
        parents = {}
        for parent, child in edges:
            if parent not in nodes or child not in nodes:
                raise ValueError(f"CORE DGM: dangling navigation edge {parent}->{child}")
            if child == "root" or child in parents:
                raise ValueError(f"CORE DGM: invalid/multiple navigation parents for {child}")
            parents[child] = parent
        if "root" in parents or any(key != "root" and key not in parents for key in nodes):
            raise ValueError(f"CORE DGM: disconnected nodes in {module}")
        for code, screen in expected.items():
            path = screen["menu"].split(" ← ")
            if len(path) != 3 or nodes["root"] != path[0]:
                raise ValueError(f"CORE DGM: menu root mismatch {code}")
            if path[2] != screen["name"]:
                raise ValueError(f"CORE DGM: registry menu/screen mismatch {code}")
            node, visited = code, set()
            while node not in visited and not node.startswith("cat") and node != "root":
                visited.add(node)
                node = parents.get(node)
                if node is None:
                    break
            if node in visited or node is None or nodes.get(node) != path[1] or parents.get(node) != "root":
                raise ValueError(f"CORE DGM: wrong category/cycle for {code}")
        for cat in (key for key in nodes if key.startswith("cat")):
            if parents.get(cat) != "root":
                raise ValueError(f"CORE DGM: category not attached to root {cat}")
    print(f"PASS CORE DGM: {len(maps)} immutable module maps, "
          f"{len(seen_screen_ids)} unique screen nodes; menu labels and navigation categories match")


if __name__ == "__main__":
    check()
