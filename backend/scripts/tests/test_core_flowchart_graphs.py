"""CORE DGM-16–19 flowchart traceability and negative cases."""
import csv
import importlib.util
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "check_core_flowchart_graphs.py"
SPEC = importlib.util.spec_from_file_location("check_core_flowchart_graphs", SCRIPT)
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)


class FlowchartGraphAcceptance(unittest.TestCase):
    def setUp(self):
        self.tmp = TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for path in ("muhaseb-erp-analysis/data", "muhaseb-erp-analysis/diagrams/source",
                     "muhaseb-erp-analysis/diagrams/svg", "docs/backend"):
            (self.root / path).mkdir(parents=True)
        self.diagrams = [{"id": "system-map", "mermaid": "diagrams/source/system-map.mmd",
                          "svg": "diagrams/svg/system-map.svg"}]
        self.text = ('flowchart TB\n  A["الأول"]\n  B["الثاني"]\n'
                     '  A -->|"أثر"| B\n')
        self.records = [{"diagram_id": "system-map",
                         "analysis_source": "muhaseb-erp-analysis/diagrams/source/system-map.mmd",
                         "status": "partial", "scope": "fixture"}]
        (self.root / "muhaseb-erp-analysis/diagrams/svg/system-map.svg").write_text(
            '<svg xmlns="http://www.w3.org/2000/svg"></svg>', encoding="utf-8")
        self.save()

    def save(self):
        a = self.root / "muhaseb-erp-analysis"
        (a / "data/diagrams.json").write_text(json.dumps(self.diagrams), encoding="utf-8")
        (a / "diagrams/source/system-map.mmd").write_text(self.text, encoding="utf-8")
        with (self.root / "docs/backend/diagrams-implementation.csv").open("w", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=["diagram_id", "analysis_source", "status", "scope"])
            writer.writeheader()
            writer.writerows(self.records)

    def check(self):
        return audit.check(self.root, names=("system-map",))

    def test_valid_labeled_graph(self):
        self.assertEqual(self.check()["system-map"], (2, 1, "partial"))

    def test_duplicate_node_rejected(self):
        self.text += '  A["مكرر"]\n'
        self.save()
        with self.assertRaisesRegex(ValueError, "duplicate graph node"):
            self.check()

    def test_dangling_stage_rejected(self):
        self.text += '  B --> C\n'
        self.save()
        with self.assertRaisesRegex(ValueError, "dangling flow edge"):
            self.check()

    def test_duplicate_edge_rejected(self):
        self.text += '  A -->|"أثر"| B\n'
        self.save()
        with self.assertRaisesRegex(ValueError, "duplicate graph edge"):
            self.check()

    def test_missing_traceability_rejected(self):
        self.records.clear()
        self.save()
        with self.assertRaisesRegex(ValueError, "traceability row"):
            self.check()

    def test_unverified_completion_rejected(self):
        self.records[0]["status"] = "unsupported"
        self.save()
        with self.assertRaisesRegex(ValueError, "unverified traceability status"):
            self.check()

    def test_disconnected_graph_rejected(self):
        self.text += '  C["غير متصل"]\n'
        self.save()
        with self.assertRaisesRegex(ValueError, "unreachable stages"):
            self.check()

    def test_legitimate_correction_cycle_supported(self):
        self.text += '  B --> A\n'
        self.save()
        self.check()

    def test_real_flowcharts_and_implementation_markers(self):
        counts = audit.check()
        self.assertEqual(set(counts), set(audit.NAMES))


if __name__ == "__main__":
    unittest.main()
