"""Positive and negative tests for the CORE SPEC06 reference-parity audit."""
import csv
import json
from pathlib import Path
import shutil
import sys
from tempfile import TemporaryDirectory
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]
ROOT = SCRIPTS.parents[1]
sys.path.insert(0, str(SCRIPTS))
import check_spec06_reference_parity as audit  # noqa: E402


class Spec06ReferenceParityTests(unittest.TestCase):
    def setUp(self):
        temp = TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.source = self.root / "source.md"
        self.matrix = self.root / "matrix.csv"
        self.sources = self.root / "sources.json"
        self.report = self.root / "report.md"
        for src, dst in ((audit.SOURCE, self.source), (audit.MATRIX, self.matrix),
                         (audit.SOURCES, self.sources), (audit.REPORT, self.report)):
            shutil.copy2(src, dst)

    def edit_matrix(self, transform):
        with self.matrix.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        transform(rows)
        with self.matrix.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)

    def test_all_150_screens_remain_unverified(self):
        self.assertEqual(audit.check(self.source, self.matrix, self.sources, self.report), (150, 8))

    def test_missing_gap_rejected(self):
        self.source.write_text(self.source.read_text().replace("| GAP08 |", "| GAPXX |"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "original gap missing/duplicate"):
            audit.check(self.source, self.matrix, self.sources, self.report)

    def test_missing_screen_rejected(self):
        self.edit_matrix(lambda rows: rows.pop())
        with self.assertRaisesRegex(ValueError, "expected 150 unique screen rows"):
            audit.check(self.source, self.matrix, self.sources, self.report)

    def test_duplicate_screen_rejected(self):
        self.edit_matrix(lambda rows: rows.__setitem__(1, dict(rows[0])))
        with self.assertRaisesRegex(ValueError, "expected 150 unique screen rows"):
            audit.check(self.source, self.matrix, self.sources, self.report)

    def test_parity_overclaim_rejected(self):
        self.edit_matrix(lambda rows: rows[0].update({"حالة الدليل": "مطابق تفصيليًا"}))
        with self.assertRaisesRegex(ValueError, "reference parity overclaim"):
            audit.check(self.source, self.matrix, self.sources, self.report)

    def test_reference_build_without_evidence_rejected(self):
        self.edit_matrix(lambda rows: rows[0].update({"إصدار المرجع": "V5"}))
        with self.assertRaisesRegex(ValueError, "identity/evidence unexpectedly populated"):
            audit.check(self.source, self.matrix, self.sources, self.report)

    def test_accessible_pdf_status_overclaim_rejected(self):
        data = json.loads(self.sources.read_text(encoding="utf-8"))
        next(row for row in data if row["id"] == "S04")["status"] = "مقروء"
        self.sources.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "inaccessible reference status changed"):
            audit.check(self.source, self.matrix, self.sources, self.report)

    def test_report_must_keep_unverified_boundary(self):
        self.report.write_text(self.report.read_text(encoding="utf-8").replace("8/8 فجوات", "7/8 فجوات"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "missing audit boundary"):
            audit.check(self.source, self.matrix, self.sources, self.report)


if __name__ == "__main__":
    unittest.main()
