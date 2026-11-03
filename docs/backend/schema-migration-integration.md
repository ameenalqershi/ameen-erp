# SQL Server schema migration sequence — CI safety contract

Each module's migration file must have a unique, three-digit sequence prefix.
The prefix must match the literal version recorded **exactly once** in
`dbo.__AmeenSchemaVersions`, the sequence must be contiguous from 001, and
`ErpDbContext.SchemaVersion` must equal the latest SQL migration number.

`python3 backend/scripts/check_migration_versions.py` is invoked **before**
the build and live-SQL tests in `backend/scripts/verify.py`. It returns a
nonzero exit code on duplicate numbers, missing versions, a mismatched SQL
record, or a stale EF schema contract. Its six isolated Python acceptance
tests are also run by `verify.py` and never touch a database.

**Integration boundary:** checking each source branch separately cannot
detect two branches owning the same migration number. Run the full backend CI
on the exact combined tree before merging or deploying. A migration must be
renumbered and revalidated against the latest shared branch, never applied
to production from an unreviewed temporary integration branch.

For the 2026-11-03 SYS16 / CASH conflict, SYS16 initially declared 033 while
CASH independently declared 033 and more. SYS16's SQL also checks for an
unexpected prior owner so it fails closed. Keep SYS→main PR #72 in draft
until the final migration versions and SQL/HTTPS/JWT checks are reconciled.
