# AMEEN ERP resumable development checkpoint — SYS07 dependency (2026-11-02)

## Preflight (verified before changes)

- Repository: `ameenalqershi/ameen-erp`, private GitHub origin. `main` and `origin/main` both `e6a500b1309f4fa1a3af2dc86e26711a28f352e3` after fetch. Working tree clean; no active dotnet/git process or uncommitted work. `AGENTS.md`, `PROGRESS.md`, SYS07 original source and SYS09/GL14 prior handoff read.
- Existing accepted work: SQL 028 SYS09 foundation CI #41395016694 340/340 on main; GL14 and SYS09 remain partial. Do not rerun SQL 027/028 or reopen prior tasks.
- Current branch: `temp/sys07-role-foundation-20261102` created from main; only this branch is edited.

## Current task and scope

- Task: `SYS07-01 / SYS07-GAP01` prerequisite for SYS09/GL14. **Partial structural foundation, not accepted role administration**.
- Added: `backend/src/Ameen.Erp.Domain/Organization/SecurityRole.cs`, `backend/src/Ameen.Erp.Infrastructure/Persistence/Configurations/SecurityRoleModel.cs`, `backend/database/029_security_role_foundation.sql`, `backend/tests/Ameen.Erp.Tests/SecurityRoleFoundationTests.cs`, `SecurityRoleSqlIntegration.cs`, `docs/backend/sys07-role-foundation.md`.
- Modified: `ErpDbContext.cs` (SchemaVersion 29), `ErpDbContext.Validation.cs` (SYS07 write guard), `ApprovalRouteModel.cs` (role FK), `Program.cs`, `SqlIntegration.cs`, `backend/scripts/traceability.py`, generated `docs/backend/{implementation-map.json,fields-implementation.csv,screens-implementation.csv,PROGRESS.md}`, `sys09-approval-route-foundation.md`, `gl14-01-foundation.md`.
- Design: tenant/branch and currency FKs, role/rule version/audit fields, explicit-deny-first pure evaluation, capped grants with currency, SQL 029 transaction and idempotency, SYS09 role FK; EF blocks role/SYS09/GL14 writes pending full policy. No seed, no public endpoints, no original diagram or analysis modifications.
- Local verification before final SQL-negative-test addition: **342/342** .NET 10 Release, domain/EF, HTTP/JWT/OpenAPI, traceability; SQL not available locally. Final local verification: `backend/.artifacts/sys07-role-final-20261102/verification.json`, **342/342**, source SHA256 `661549d14ff032b646ffff22a1d8eecab1434f94c03f884dc4068ad91f4b2a6a`. Branch code SHA `d3aa045b170183a377769757eb2bef9667dc428a` passed [isolated SQL CI #41400101494](https://github.com/ameenalqershi/ameen-erp/actions/runs/41400101494) **343/343**; live SQL, HTTP/JWT/OpenAPI and Release. Artifact evidence copied into `docs/backend/verification-results.json`.
- Code commit SHA `d3aa045b170183a377769757eb2bef9667dc428a`, branch CI [#41400101494](https://github.com/ameenalqershi/ameen-erp/actions/runs/41400101494) successful 343/343. Documentation commit SHA, its CI and main SHA **pending**. Do not push to main until documentation SHA CI is green.

## Blockers and exact next action

1. Check `git status --short --branch`, `git fetch origin main`, `git rev-parse HEAD origin/main` and active sessions; if another actor has modified the worktree, reconcile rather than overwrite.
2. Confirm final local `verify.py` is **342/342**, `progress.py --check`, no `muhaseb-erp-analysis` diff. If failing, fix only failed tests.
3. **Done:** code committed `temp:` and pushed to `temp/sys07-role-foundation-20261102`; disposable SQL CI #41400101494 passed 343/343.
4. **In progress:** CI artifact copied into `docs/backend/verification-results.json`, CI run/SHA and count recorded in PROGRESS and this checkpoint. Commit `temp:` documentation on the existing branch, rerun CI on documentation SHA, fast-forward main only if both code and final CI are green; verify remote SHA.
5. Update Notion `SYS07-01` and `SYS07-GAP01` to **قيد التنفيذ** (not مكتمل), with commit/CI links and blockers. Update SYS09 dependency note. Do not mark SYS07-FG tasks accepted.
6. Next functional work: user-to-role assignment, explicit deny and branch/amount/currency authorization, independent cost/export/post rights, audit/concurrency, then SYS09 active-role scope and route overlap/document-kind policy, then GL14/GL03. Preserve current membership grants until replacement is accepted.

No production data, credentials or original analysis may be changed. On interruption preserve exact failed test, SHA, branch, CI URL, changed files and next command here.
