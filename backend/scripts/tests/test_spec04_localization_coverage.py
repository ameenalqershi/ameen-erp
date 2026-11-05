"""Positive and negative tests for the SPEC04 country/localization traceability audit."""
import csv
from pathlib import Path
import shutil
import sys
from tempfile import TemporaryDirectory
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]
ROOT = SCRIPTS.parents[1]
sys.path.insert(0, str(SCRIPTS))
import check_spec04_localization_coverage as audit  # noqa: E402


class LocalizationCoverageAudit(unittest.TestCase):
    def setUp(self):
        temp = TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        for name in (audit.SOURCE, audit.SCREENS, audit.FIELDS, audit.MATRIX, audit.COUNTRIES):
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / name, target)

    def replace(self, name, old, new):
        path = self.root / name
        value = path.read_text(encoding="utf-8")
        self.assertIn(old, value)
        path.write_text(value.replace(old, new, 1), encoding="utf-8")

    def set_csv_value(self, name, key, match_key, match_value, new_value):
        path = self.root / name
        with path.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        found = False
        for row in rows:
            if row[match_key] == match_value:
                row[key] = new_value
                found = True
                break
        self.assertTrue(found)
        with path.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)

    def test_all_country_gates_and_requirements_resolve(self):
        groups, countries, pending = audit.check(self.root)
        self.assertEqual((groups, countries, pending), (14, 7, 133))

    def test_missing_country_is_rejected(self):
        self.replace(audit.SOURCE, "| الكويت |", "| دولة غير معتمدة |")
        with self.assertRaisesRegex(ValueError, "exactly seven unique countries"):
            audit.check(self.root)

    def test_missing_source_section_is_rejected(self):
        self.replace(audit.SOURCE, "## دورة الفوترة الإلكترونية", "## دورة غير معتمدة")
        with self.assertRaisesRegex(ValueError, "missing/duplicate source section"):
            audit.check(self.root)

    def test_missing_requirement_mapping_is_rejected(self):
        self.set_csv_value(audit.MATRIX, "requirement_id", "requirement_id", "LOC07", "LOST07")
        with self.assertRaisesRegex(ValueError, "14 localization requirement groups"):
            audit.check(self.root)

    def test_unknown_screen_reference_is_rejected(self):
        self.replace(audit.MATRIX, "TAX01;TAX02", "TAX01;UNKNOWN")
        with self.assertRaisesRegex(ValueError, "unknown screen reference"):
            audit.check(self.root)

    def test_runtime_completion_claim_is_rejected(self):
        self.replace(audit.MATRIX, "not-verified", "implemented")
        with self.assertRaisesRegex(ValueError, "cannot claim runtime acceptance"):
            audit.check(self.root)

    def test_country_activation_claim_is_rejected(self):
        self.replace(audit.COUNTRIES, "not-enabled", "production-enabled")
        with self.assertRaisesRegex(ValueError, "country activation/acceptance overclaim"):
            audit.check(self.root)

    def test_main_screen_status_change_requires_reaudit(self):
        self.set_csv_value(audit.SCREENS, "status", "screen_id", "TAX01", "partial")
        with self.assertRaisesRegex(ValueError, "TAX01 status changed"):
            audit.check(self.root)

    def test_tax_pending_field_count_is_guarded(self):
        path = self.root / audit.FIELDS
        with path.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        rows = [row for row in rows if not (row["screen_id"] == "TAX01" and row["field_id"].endswith("F15"))]
        with path.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
        with self.assertRaisesRegex(ValueError, "TAX01 expected 15 pending fields"):
            audit.check(self.root)

    def test_unassigned_policy_owner_is_explicit_not_silently_omitted(self):
        self.replace(audit.MATRIX, "UNASSIGNED — named country-policy owner required", "")
        with self.assertRaisesRegex(ValueError, "blocker/decision-owner gap"):
            audit.check(self.root)


if __name__ == "__main__":
    unittest.main()
