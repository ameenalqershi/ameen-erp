# AMEEN ERP resumable checkpoint — SYS07/SYS08 membership-role link (2026-11-02)

## Preflight verified
- Repo ameenalqershi/ameen-erp; clean main and origin/main both 7aacbe6d058e925560732b247c4164ac0f703799 after fetch; no git/dotnet/verify active before checkout. Two unrelated long-lived kimi-code sessions exist; no tracked worktree modifications.
- Read AGENTS.md, docs/backend/PROGRESS.md, SYS07/SYS08 original fields and SYS.md, prior SYS07/SYS09/GL14 handoffs, Notion SYS07-01 and SYS07-GAP01. Prior 343/343 main CI #41400728451 accepted only guarded SYS07 foundation.
- Current branch: temp/sys07-membership-role-assignment-20261102 (created from main). Do not overwrite concurrent changes; recheck git status and origin/main before any push.

## Task and implementation
- Task SYS07-01 / SYS07-GAP01 dependency on SYS08-F05 user-role reference, **partial structural scope only**.
- Added backend/src/Ameen.Erp.Domain/Organization/SecurityRoleAssignment.cs; backend/database/030_security_role_assignments.sql; backend/tests/Ameen.Erp.Tests/SecurityRoleAssignmentSqlIntegration.cs; docs/backend/sys07-membership-role-assignment.md.
- Modified backend/src/Ameen.Erp.Infrastructure/Persistence/Configurations/SecurityRoleModel.cs; Persistence/ErpDbContext.cs (SchemaVersion 30); Persistence/ErpDbContext.Validation.cs (assignment write guard); backend/tests/Ameen.Erp.Tests/SecurityRoleFoundationTests.cs and SqlIntegration.cs; backend/scripts/traceability.py; generated docs/backend/fields-implementation.csv, screens-implementation.csv, implementation-map.json and PROGRESS.md; this checkpoint.
- Security: company composite FKs to Membership and SecurityRole; unique membership-role; no seed; EF rejects all assignment writes. Pure evaluator filters membership/company/branch/active roles, denies before grants, amount/currency caps; **NOT** connected to runtime authorization because snapshot completeness and policy audit are not accepted. Existing membership grants and SYS07/SYS09/GL14 guards unchanged.
- Local /root/.dotnet/dotnet .NET 10 Release + tests + HTTP/JWT/OpenAPI + traceability: **344/344 passed, 0 failed**, source SHA256 a47d5d1c3322248597fc5314fe8d5793a3d66821d981723c1a91454647d46e06; evidence backend/.artifacts/sys07-assignment-20261102/verification.json. SQL not available locally; CI SQL 030 negative rollback/retry and tenant FK must pass before main.
- Code branch commit SHA: b6cc9590458acef53668d90c411c206e98577aab. Branch CI #41417654362 on that SHA SUCCESS **345/345** with isolated SQL 030 rollback/retry, tenant FKs, guard, HTTP/JWT, OpenAPI, Release. Evidence copied to docs/backend/verification-results.json. Documentation commit SHA 3b6f8776fb7827d21c05494480aa41cbe0563066 passed branch CI #41418066078 (345/345). Fast-forward to main succeeded, remote main verified at 3b6f8776fb7827d21c05494480aa41cbe0563066 before this handoff update. Notion SYS07-01, SYS07-GAP01, SYS08-FG02 and SYS09-01 updated as partial/in-progress. Main CI for final SHA must be checked; no main CI success claim yet.

## Exact next steps (resume after interruption)
1. Check git status --short --branch, git fetch origin main, git rev-parse HEAD origin/main and active processes. Reconcile concurrent edits; never reset another actor's work.
2. Verify local 344/344 result and source fingerprint, python3 backend/scripts/progress.py --check, git diff --check and no muhaseb-erp-analysis diff.
3. DONE: code commit b6cc959 pushed to temp/sys07-membership-role-assignment-20261102, CI #41417654362 successful 345/345 including SQL 030.
4. DONE: SQL CI evidence copied into docs/backend/verification-results.json and PROGRESS.md; docs commit 3b6f877 passed branch CI #41418066078; fast-forwarded main and verified origin/main at 3b6f877. This handoff-only documentation update will be a further temp: commit on main; check the final main CI SHA before reporting it as green.
5. DONE: Notion SYS07-01, SYS07-GAP01, SYS08-FG02, SYS08-01 dependency and SYS09-01 updated, no completion claim. Once final main CI succeeds, append its exact SHA/run to Notion; do not repeat earlier updates.
6. NEXT functional task: audited role/assignment administration with concurrency and explicit permission registry, then complete tenant snapshot and negative API acceptance for deny/branch/amount/currency and cost/export/post independence. Preserve legacy grants until verified. Then SYS09 active-role/stage/overlap and GL14/GL03.

Do not touch production SQL, credentials or original analysis. Do not claim SYS07/SYS08/SYS09/GL14 functional acceptance from structural CI.
