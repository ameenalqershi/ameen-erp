#!/usr/bin/env python3
"""Validate original CORE DGM-20–27 process diagrams; not runtime E2E acceptance.

Reuse the graph/index/traceability validator from DGM-16–19. Required
labelled edges are an explicit original-analysis regression contract.
"""
from pathlib import Path
from contextlib import redirect_stdout
from io import StringIO
from check_core_flowchart_graphs import ROOT, EDGE, check as check_graph

NAMES = (
    "stock-transfer", "returns", "period-close", "einvoice",
    "posting-sequence", "imports", "extensions", "opening-migration",
)

# Existing original graph edges, not newly approved financial policies.
REQUIRED_EDGES = {
    "stock-transfer": {("T", "R", ""), ("T", "X", ""), ("X", "D", "قرار فرق")},
    "returns": {("V", "S", ""), ("V", "Q", ""),
                ("Q", "A", "قيمة معتمدة"), ("A", "R", "قرار مستقل")},
    "period-close": {("T", "F", "فشل"), ("T", "C", "نجاح"),
                     ("C", "Y", "عند نهاية السنة")},
    "einvoice": {("T", "C", "حسب النوع"), ("T", "S", "حسب النوع"),
                 ("Q", "F", "تعذر"), ("Q", "V", "نجاح")},
    "posting-sequence": {("L", "J", ""), ("L", "S", ""), ("L", "O", ""),
                         ("J", "C", ""), ("S", "C", ""), ("O", "C", ""),
                         ("C", "E", "بعد الالتزام"), ("C", "R", "")},
    "imports": {("A", "S", ""), ("A", "I", ""),
                ("S", "E", ""), ("I", "E", "")},
    "extensions": {("W", "G", ""), ("I", "G", ""), ("C", "G", "")},
    "opening-migration": {("C", "F", "فشل"), ("C", "P", "نجاح")},
}


def check(root: Path = ROOT):
    root = Path(root)
    # Avoid emitting the DGM-16–19 banner from the reused checker.
    with redirect_stdout(StringIO()):
        counts = check_graph(root, names=NAMES)
    for name in NAMES:
        lines = (root / "muhaseb-erp-analysis/diagrams/source"
                 / f"{name}.mmd").read_text(encoding="utf-8").splitlines()
        edges = set()
        for line in lines:
            match = EDGE.fullmatch(line)
            if match:
                source, label, target = match.groups()
                edges.add((source, target, label or ""))
        missing = REQUIRED_EDGES[name] - edges
        if missing:
            raise ValueError(
                f"CORE DGM-20–27: missing original process edge {name}: "
                f"{sorted(missing)}"
            )
    print("PASS CORE DGM-20–27 original process graph/branch contracts; "
          "runtime source integrations NOT asserted")
    return counts


if __name__ == "__main__":
    check()
