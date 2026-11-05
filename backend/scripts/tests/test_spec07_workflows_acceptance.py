"""Positive and negative tests for the CORE SPEC07 workflow acceptance audit."""
import csv
from pathlib import Path
import shutil
import sys
from tempfile import TemporaryDirectory
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]
ROOT = SCRIPTS.parents[1]
sys.path.insert(0, str(SCRIPTS))
import check_spec07_workflows_acceptance as audit  # noqa: E402


class Spec07WorkflowAcceptanceTests(unittest.TestCase):
    def setUp(self):
        temp = TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.source = self.root / "source.md"
        self.matrix = self.root / "matrix.csv"
        self.report = self.root / "report.md"
        for src, dst in ((audit.SOURCE, self.source), (audit.MATRIX, self.matrix), (audit.REPORT, self.report)):
            shutil.copy2(src, dst)

    def edit_csv(self, transform):
        with self.matrix.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        transform(rows)
        with self.matrix.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)

    def test_all_30_scenarios_linked(self):
        self.assertEqual(audit.check(self.source, self.matrix, self.report), 30)

    def test_missing_scenario_rejected(self):
        self.edit_csv(lambda rows: rows.pop())
        with self.assertRaisesRegex(ValueError, "expected 30 unique AT scenarios"):
            audit.check(self.source, self.matrix, self.report)

    def test_duplicate_scenario_rejected(self):
        self.edit_csv(lambda rows: rows.__setitem__(1, dict(rows[0])))
        with self.assertRaisesRegex(ValueError, "expected 30 unique AT scenarios"):
            audit.check(self.source, self.matrix, self.report)

    def test_missing_expected_result_rejected(self):
        self.edit_csv(lambda rows: rows[0].update(expected_result=""))
        with self.assertRaisesRegex(ValueError, "incomplete scenario mapping"):
            audit.check(self.source, self.matrix, self.report)

    def test_invalid_notion_link_rejected(self):
        self.edit_csv(lambda rows: rows[0].update(notion_task_url="https://example.com/task"))
        with self.assertRaisesRegex(ValueError, "invalid Notion task URL"):
            audit.check(self.source, self.matrix, self.report)

    def test_execution_overclaim_rejected(self):
        self.edit_csv(lambda rows: rows[0].update(evidence_status="passed"))
        with self.assertRaisesRegex(ValueError, "cannot claim execution evidence"):
            audit.check(self.source, self.matrix, self.report)

    def test_source_scenario_removed_rejected(self):
        self.source.write_text(self.source.read_text().replace("| AT30 |", "| ATXX |"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "source scenario missing/duplicate"):
            audit.check(self.source, self.matrix, self.report)

    def test_future_acceptance_boundary_required(self):
        self.source.write_text(self.source.read_text().replace("لتنفيذ النظام مستقبلًا", "منفذة بالفعل"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "source acceptance boundary missing"):
            audit.check(self.source, self.matrix, self.report)


if __name__ == "__main__":
    unittest.main()
