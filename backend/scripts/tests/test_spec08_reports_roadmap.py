"""Positive and negative tests for the CORE SPEC08 report-contract and roadmap audit."""
import csv
from pathlib import Path
import shutil
import sys
from tempfile import TemporaryDirectory
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]
ROOT = SCRIPTS.parents[1]
sys.path.insert(0, str(SCRIPTS))
import check_spec08_reports_roadmap as audit  # noqa: E402


class Spec08ReportsRoadmapTests(unittest.TestCase):
    def setUp(self):
        temp = TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.source = self.root / "source.md"
        self.contracts = self.root / "contracts.csv"
        self.phases = self.root / "phases.csv"
        self.report = self.root / "report.md"
        for src, dst in ((audit.SOURCE, self.source), (audit.CONTRACTS, self.contracts),
                         (audit.PHASES, self.phases), (audit.REPORT, self.report)):
            shutil.copy2(src, dst)

    def edit_csv(self, path, transform):
        with path.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        transform(rows)
        with path.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)

    def test_all_contracts_and_phases_linked(self):
        self.assertEqual(audit.check(self.source, self.contracts, self.phases, self.report), (19, 7))

    def test_missing_contract_rejected(self):
        self.edit_csv(self.contracts, lambda rows: rows.pop())
        with self.assertRaisesRegex(ValueError, "expected 19 unique report contracts"):
            audit.check(self.source, self.contracts, self.phases, self.report)

    def test_duplicate_contract_rejected(self):
        self.edit_csv(self.contracts, lambda rows: rows.__setitem__(1, dict(rows[0])))
        with self.assertRaisesRegex(ValueError, "expected 19 unique report contracts"):
            audit.check(self.source, self.contracts, self.phases, self.report)

    def test_invalid_contract_link_rejected(self):
        self.edit_csv(self.contracts, lambda rows: rows[0].update(notion_task_url="https://example.com"))
        with self.assertRaisesRegex(ValueError, "incomplete report contract"):
            audit.check(self.source, self.contracts, self.phases, self.report)

    def test_acceptance_overclaim_rejected(self):
        self.edit_csv(self.contracts, lambda rows: rows[0].update(evidence_status="passed"))
        with self.assertRaisesRegex(ValueError, "cannot claim report acceptance"):
            audit.check(self.source, self.contracts, self.phases, self.report)

    def test_missing_phase_rejected(self):
        self.edit_csv(self.phases, lambda rows: rows.pop())
        with self.assertRaisesRegex(ValueError, "expected seven unique roadmap phases"):
            audit.check(self.source, self.contracts, self.phases, self.report)

    def test_missing_phase_links_rejected(self):
        self.edit_csv(self.phases, lambda rows: rows[0].update(notion_task_refs=""))
        with self.assertRaisesRegex(ValueError, "incomplete phase mapping"):
            audit.check(self.source, self.contracts, self.phases, self.report)

    def test_source_sections_required(self):
        self.source.write_text(self.source.read_text().replace("## عقد التقرير", "## عقد محذوف"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "source sections missing"):
            audit.check(self.source, self.contracts, self.phases, self.report)


if __name__ == "__main__":
    unittest.main()
