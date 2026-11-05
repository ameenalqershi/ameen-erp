"""SPEC01 decision registry traceability acceptance tests."""
import importlib.util
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "check_scope_decision_links.py"
SPEC = importlib.util.spec_from_file_location("check_scope_decision_links", SCRIPT)
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)

LINK = "https://app.notion.com/p/" + "1" * 32


class ScopeDecisionAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.source = Path(self.temp.name) / "scope.md"
        self.report = Path(self.temp.name) / "audit.md"
        self.rows = ["D01", "D02"]
        self.save()

    def save(self):
        self.source.write_text("\n".join(f"| {code} | decision |" for code in self.rows), encoding="utf-8")
        self.report.write_text("\n".join(
            f"| {code} | [task]({LINK}) | محجوب | no invented policy |" for code in self.rows
        ), encoding="utf-8")

    def check(self):
        audit.check(self.source, self.report)

    def test_matching_source_and_tracked_notions(self):
        self.check()

    def test_missing_source_decision_rejected(self):
        self.report.write_text(f"| D01 | [task]({LINK}) | محجوب | missing D02 |", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "coverage mismatch"):
            self.check()

    def test_unapproved_decision_added_rejected(self):
        self.report.write_text(self.report.read_text() + f"\n| D03 | [task]({LINK}) | محجوب | extra |", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "coverage mismatch"):
            self.check()

    def test_duplicate_decision_link_rejected(self):
        text = self.report.read_text()
        self.report.write_text(text + "\n" + text.splitlines()[0], encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate"):
            self.check()

    def test_missing_notional_link_rejected(self):
        self.report.write_text(self.report.read_text().replace(LINK, "https://not-a-notion-link"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "coverage mismatch"):
            self.check()

    def test_unrecognized_decision_status_rejected(self):
        self.report.write_text(self.report.read_text().replace("محجوب", "مقبول تلقائيًا"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "coverage mismatch"):
            self.check()

    def test_original_baseline_mapping(self):
        audit.check()


if __name__ == "__main__":
    unittest.main()
