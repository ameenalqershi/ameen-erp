# CORE-07 — fail-closed SQL migration inventory acceptance (partial)

Baseline: main `8c767dea816c3d6ab3664b203f0b3cddbb496449`. The original analysis and existing database migrations are unchanged.

The previous `check_migration_versions.py` only scanned filenames matching `[0-9][0-9][0-9]_*.sql`. An accidentally renamed migration (for example `046-extra.sql`, `0046_extra.sql`, or `046_extra.SQL`) could be silently omitted while the remaining sequence passed, risking an incomplete deployment. The gate now inventories all SQL files (case-insensitive suffix), rejects unrecognized migration filenames, excludes only the existing deployment support file `runtime-role.sql`, and preserves contiguous version, unique number, one SQL version INSERT, and EF SchemaVersion matching checks. It rejects symlinked/non-regular versioned migrations.

Positive and negative regression cases cover the valid baseline, runtime-role exemption, malformed future migration names, uppercase extension, unknown SQL script, duplicate version INSERT, version gap, wrong INSERT, duplicate number, and stale EF version.

Run `python3 -m unittest discover -s backend/scripts/tests -p 'test_*.py'` and `python3 backend/scripts/check_migration_versions.py`. The normal backend CI runs these plus disposable SQL Server integration, HTTPS/JWT/OpenAPI tests. CI is required on the final commit SHA.

**Important scope:** This does not create or modify migrations, prove restore/backup integrity, simulate a failed DDL rollout, or close CORE-07 end-to-end. Those remain separate partial acceptance dependencies. Do not claim deployment or production readiness from this guard alone. Only SUPERVISOR may merge the PR.
