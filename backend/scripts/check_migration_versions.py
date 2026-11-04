#!/usr/bin/env python3
"""Fail closed on duplicate/gapped SQL migrations and stale EF schema version.

Run this on the *combined branch/tree* before SQL migrations. CI on separate
feature branches cannot prove future merge ordering.
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MIGRATIONS = ROOT / "backend/database"
EF_CONTEXT = ROOT / "backend/src/Ameen.Erp.Infrastructure/Persistence/ErpDbContext.cs"

INSERT_VERSION = re.compile(
    r"\bINSERT(?:\s+INTO)?\s+(?:\[?dbo\]?\.)?\[?__AmeenSchemaVersions\]?\s*"
    r"\(\s*\[?Version\]?\s*,\s*\[?AppliedAt\]?\s*\)\s*"
    r"VALUES\s*\(\s*(\d+)\s*,",
    re.IGNORECASE | re.MULTILINE,
)
EF_VERSION = re.compile(r"\bconst\s+int\s+SchemaVersion\s*=\s*(\d+)\s*;")


def check(migration_dir: Path, ef_context: Path) -> int:
    errors = []
    # Inventory all SQL files rather than silently omitting a malformed migration.
    # runtime-role.sql is a non-versioned deployment support script, not a migration.
    all_sql = sorted(migration_dir.glob("*.sql"))
    files = []
    for candidate in all_sql:
        if candidate.name == "runtime-role.sql":
            continue
        if not re.fullmatch(r"[0-9]{3}_[A-Za-z0-9_]+\.sql", candidate.name):
            errors.append(f"unrecognized migration filename: {candidate.name}")
            continue
        if candidate.is_symlink() or not candidate.is_file():
            errors.append(f"migration must be a regular file: {candidate.name}")
            continue
        files.append(candidate)
    by_version: dict[int, list[str]] = {}
    for f in files:
        number = int(f.name[:3])
        by_version.setdefault(number, []).append(f.name)
        actual = [int(n) for n in INSERT_VERSION.findall(f.read_text(encoding="utf-8"))]
        if actual != [number]:
            errors.append(f"{f.name}: expected exactly one recorded schema version {number}, found {actual}")
    for number, names in sorted(by_version.items()):
        if len(names) > 1:
            errors.append(f"duplicate schema version {number:03d}: {', '.join(names)}")
    latest = max(by_version, default=0)
    missing = sorted(set(range(1, latest + 1)) - set(by_version))
    if missing:
        errors.append("missing migration versions: " + ", ".join(f"{n:03d}" for n in missing))
    ef_matches = [int(n) for n in EF_VERSION.findall(ef_context.read_text(encoding="utf-8"))]
    if ef_matches != [latest]:
        errors.append(f"ErpDbContext.SchemaVersion should be {latest}, found {ef_matches}")
    if not files:
        errors.append("no versioned SQL migrations were found")
    if errors:
        raise ValueError("; ".join(errors))
    return latest


def main() -> int:
    try:
        latest = check(MIGRATIONS, EF_CONTEXT)
    except (OSError, ValueError) as exc:
        print(f"FAIL migration sequence: {exc}")
        return 1
    print(f"PASS schema migration sequence 001–{latest:03d}: unique, contiguous, SQL records match EF SchemaVersion")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
