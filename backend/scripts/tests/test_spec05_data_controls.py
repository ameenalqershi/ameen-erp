"""Positive and negative tests for the CORE SPEC05 data/control traceability audit."""
import csv
from pathlib import Path
import shutil
import sys
from tempfile import TemporaryDirectory
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]
ROOT = SCRIPTS.parents[1]
sys.path.insert(0, str(SCRIPTS))
import check_spec05_data_controls as audit  # noqa: E402


class Spec05DataControlsAuditTests(unittest.TestCase):
    def setUp(self):
        temp = TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.source = self.root / "source.md"
        self.matrix = self.root / "matrix.csv"
        self.report = self.root / "report.md"
        shutil.copy2(audit.SOURCE, self.source)
        shutil.copy2(audit.MATRIX, self.matrix)
        shutil.copy2(audit.REPORT, self.report)

    def edit_csv(self, transform):
        with self.matrix.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        transform(rows)
        with self.matrix.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)

    def test_all_requirements_are_linked(self):
        self.assertEqual(audit.check(self.source, self.matrix, self.report), 14)

    def test_missing_source_section_rejected(self):
        self.source.write_text(self.source.read_text().replace("## أدوار مقترحة", "## قسم مفقود"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "missing/duplicate source heading"):
            audit.check(self.source, self.matrix, self.report)

    def test_duplicate_requirement_rejected(self):
        self.edit_csv(lambda rows: rows.__setitem__(1, dict(rows[0])))
        with self.assertRaisesRegex(ValueError, "expected 14 unique requirement groups"):
            audit.check(self.source, self.matrix, self.report)

    def test_unlinked_requirement_rejected(self):
        self.edit_csv(lambda rows: rows[0].update(notion_task_refs="https://example.com/unlinked"))
        with self.assertRaisesRegex(ValueError, "no Notion task link"):
            audit.check(self.source, self.matrix, self.report)

    def test_missing_blocker_rejected(self):
        self.edit_csv(lambda rows: rows[0].update(blocker=""))
        with self.assertRaisesRegex(ValueError, "incomplete traceability row"):
            audit.check(self.source, self.matrix, self.report)

    def test_unassigned_decision_owner_is_explicit(self):
        self.edit_csv(lambda rows: rows[0].update(decision_owner=""))
        with self.assertRaisesRegex(ValueError, "incomplete traceability row"):
            audit.check(self.source, self.matrix, self.report)

    def test_runtime_acceptance_overclaim_rejected(self):
        self.edit_csv(lambda rows: rows[0].update(acceptance_evidence="implemented"))
        with self.assertRaisesRegex(ValueError, "cannot claim runtime acceptance"):
            audit.check(self.source, self.matrix, self.report)

    def test_report_cannot_claim_ddl(self):
        self.report.write_text(self.report.read_text(encoding="utf-8") + "\nCREATE TABLE fake;\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "must not propose DDL"):
            audit.check(self.source, self.matrix, self.report)


if __name__ == "__main__":
    unittest.main()
