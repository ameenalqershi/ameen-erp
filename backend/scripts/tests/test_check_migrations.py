"""Schema sequence checks only; never open or modify a SQL Server connection."""
import importlib.util
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

MODULE = Path(__file__).resolve().parents[1] / "check_migration_versions.py"
SPEC = importlib.util.spec_from_file_location("check_migration_versions", MODULE)
migration_check = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(migration_check)


class MigrationSequenceAcceptance(unittest.TestCase):
    def setUp(self):
        self.tmp = TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.sql = self.root / "database"
        self.sql.mkdir()
        self.ef = self.root / "ErpDbContext.cs"
        self.ef.write_text("public const int SchemaVersion = 2;", encoding="utf-8")
        self.make_sql("001_initial.sql", 1)
        self.make_sql("002_users.sql", 2)

    def make_sql(self, name, number):
        (self.sql / name).write_text(
            f"INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES({number},SYSUTCDATETIME());",
            encoding="utf-8")

    def test_positive_sequence(self):
        self.assertEqual(migration_check.check(self.sql, self.ef), 2)

    def test_duplicate_number_different_module(self):
        self.make_sql("002_payment.sql", 2)
        with self.assertRaisesRegex(ValueError, "duplicate schema version 002"):
            migration_check.check(self.sql, self.ef)

    def test_gap_blocks_incomplete_deployment(self):
        (self.sql / "002_users.sql").rename(self.sql / "003_users.sql")
        self.make_sql("003_users.sql", 3)
        with self.assertRaisesRegex(ValueError, "missing migration versions: 002"):
            migration_check.check(self.sql, self.ef)

    def test_wrong_sql_version_is_rejected(self):
        self.make_sql("002_users.sql", 1)
        with self.assertRaisesRegex(ValueError, "expected exactly one recorded schema version 2"):
            migration_check.check(self.sql, self.ef)

    def test_missing_sql_record_is_rejected(self):
        (self.sql / "002_users.sql").write_text("SELECT 1;", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, r"found \[\]"):
            migration_check.check(self.sql, self.ef)

    def test_ef_schema_contract_must_match_latest_migration(self):
        self.ef.write_text("public const int SchemaVersion = 1;", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "SchemaVersion should be 2"):
            migration_check.check(self.sql, self.ef)


if __name__ == "__main__":
    unittest.main()
