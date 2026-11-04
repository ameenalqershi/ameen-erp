"""SPEC02 source-to-audit matrix guards against invented completion claims."""
import csv
from pathlib import Path
import shutil
import sys
from tempfile import TemporaryDirectory
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]
ROOT = SCRIPTS.parents[1]
sys.path.insert(0, str(SCRIPTS))
import check_spec02_event_coverage as audit  # noqa: E402


class OriginalRulesCoverage(unittest.TestCase):
    def setUp(self):
        temp = TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        for name in (audit.SOURCE, audit.MATRIX, audit.RULES):
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / name, path)

    def edit(self, name, source, target):
        path = self.root / name
        original = path.read_text(encoding="utf-8")
        self.assertIn(source, original)
        path.write_text(original.replace(source, target, 1), encoding="utf-8")

    def test_all_20_rules_and_26_events_are_traced_without_runtime_claims(self):
        events, scopes = audit.check()
        self.assertEqual(events, 26)
        self.assertEqual(sum(scopes.values()), 20)
        self.assertEqual(scopes["not-started"], 7)

    def test_deleting_event_mapping_is_rejected(self):
        self.edit(audit.MATRIX, "رواتب,HR,BR09,not-verified\n", "")
        with self.assertRaisesRegex(ValueError, "event reference"):
            audit.check(self.root)

    def test_duplicate_event_mapping_is_rejected(self):
        path = self.root / audit.MATRIX
        path.write_text(path.read_text() +
                        "رواتب,HR,BR09,not-verified\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "event reference"):
            audit.check(self.root)

    def test_invented_owner_or_br_is_rejected(self):
        self.edit(audit.MATRIX, "رواتب,HR,BR09", "رواتب,UNKNOWN,BR09")
        with self.assertRaisesRegex(ValueError, "undefined module owner"):
            audit.check(self.root)
        self.edit(audit.MATRIX, "رواتب,UNKNOWN,BR09", "رواتب,HR,BR99")
        with self.assertRaisesRegex(ValueError, "unsupported BR reference"):
            audit.check(self.root)

    def test_claiming_accepted_workflow_is_rejected(self):
        self.edit(audit.MATRIX, "رواتب,HR,BR09,not-verified",
                  "رواتب,HR,BR09,implemented")
        with self.assertRaisesRegex(ValueError, "misleading event completion"):
            audit.check(self.root)

    def test_missing_br_status_is_rejected(self):
        self.edit(audit.RULES, "BR11,not-started", "BR99,not-started")
        with self.assertRaisesRegex(ValueError, "BR implementation traceability"):
            audit.check(self.root)

    def test_broken_accounting_event_table_is_rejected(self):
        self.edit(audit.SOURCE, "| شراء أصل |", "| |")
        with self.assertRaisesRegex(ValueError, "incomplete accounting example"):
            audit.check(self.root)

    def test_missing_unlabelled_source_section_is_rejected(self):
        self.edit(audit.SOURCE, "## الخصم والضريبة\n", "## خصم فقط\n")
        with self.assertRaisesRegex(ValueError, "missing or duplicated required section"):
            audit.check(self.root)


if __name__ == "__main__":
    unittest.main()
