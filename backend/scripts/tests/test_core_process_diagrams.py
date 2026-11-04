"""Static CORE DGM-20–27 process audit: positive and negative acceptance."""
import json
from pathlib import Path
import shutil
import sys
from tempfile import TemporaryDirectory
import unittest

SCRIPT_DIR = Path(__file__).resolve().parents[1]
ROOT = SCRIPT_DIR.parents[1]
sys.path.insert(0, str(SCRIPT_DIR))
import check_core_process_diagrams as audit  # noqa: E402


class CoreProcessDiagramAcceptance(unittest.TestCase):
    def setUp(self):
        self.working = TemporaryDirectory()
        self.addCleanup(self.working.cleanup)
        self.root = Path(self.working.name)
        required = (
            "muhaseb-erp-analysis/data/diagrams.json",
            "docs/backend/diagrams-implementation.csv",
        )
        for relative in required:
            self.copy(relative)
        for name in audit.NAMES:
            self.copy(f"muhaseb-erp-analysis/diagrams/source/{name}.mmd")
            self.copy(f"muhaseb-erp-analysis/diagrams/svg/{name}.svg")

    def copy(self, relative):
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / relative, target)

    def replace(self, name, before, after):
        path = (self.root / "muhaseb-erp-analysis/diagrams/source"
                / f"{name}.mmd")
        content = path.read_text(encoding="utf-8")
        self.assertIn(before, content)
        path.write_text(content.replace(before, after, 1), encoding="utf-8")

    def test_all_eight_original_diagrams_have_indexed_graphs_and_branches(self):
        result = audit.check()
        self.assertEqual(set(result), set(audit.NAMES))
        self.assertEqual(len(result), 8)
        for nodes, edges, state in result.values():
            self.assertGreaterEqual(nodes, 2)
            self.assertGreaterEqual(edges, 1)
            self.assertIn(state, {"not-started", "partial", "implemented"})

    def test_relabeling_post_commit_delivery_is_rejected(self):
        self.replace("posting-sequence",
                     'C -->|"بعد الالتزام"| E', "C --> E")
        with self.assertRaisesRegex(ValueError, "missing original process edge"):
            audit.check(self.root)

    def test_relabeling_period_close_rejection_is_rejected(self):
        self.replace("period-close", 'T -->|"فشل"| F', 'T -->|"نجاح"| F')
        with self.assertRaisesRegex(ValueError, "missing original process edge"):
            audit.check(self.root)

    def test_deleting_inventory_shortage_branch_is_rejected(self):
        self.replace("stock-transfer", 'T --> X', 'T --> R')
        with self.assertRaisesRegex(ValueError, "duplicate graph edge"):
            audit.check(self.root)

    def test_unindexed_diagram_is_rejected(self):
        path = self.root / "muhaseb-erp-analysis/data/diagrams.json"
        records = json.loads(path.read_text(encoding="utf-8"))
        records = [row for row in records if row["id"] != "einvoice"]
        path.write_text(json.dumps(records, ensure_ascii=False), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "missing/duplicate indexed"):
            audit.check(self.root)

    def test_missing_traceability_row_is_rejected(self):
        path = self.root / "docs/backend/diagrams-implementation.csv"
        original = path.read_text(encoding="utf-8")
        path.write_text(
            "\n".join(line for line in original.splitlines()
                      if not line.startswith("imports,")) + "\n",
            encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "missing/duplicate traceability"):
            audit.check(self.root)

    def test_malformed_svg_is_rejected(self):
        path = self.root / "muhaseb-erp-analysis/diagrams/svg/returns.svg"
        path.write_text("<svg><broken>", encoding="utf-8")
        with self.assertRaises(Exception):
            audit.check(self.root)


if __name__ == "__main__":
    unittest.main()
