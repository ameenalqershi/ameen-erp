# SYS09 dependency foundation for GL14 — partial, not an approval engine

Source: `muhaseb-erp-analysis/data/screens.json` SYS09 and `fields.csv` F01–F15. Original analysis and diagrams remain unchanged.

## Implemented in this batch

- `ApprovalRoute` header with company and optional branch, server state, document-type token, `decimal(20,6)` minimum and nullable maximum, tenant currency FK, creator/approver separation flag and server audit fields; `ApprovalRouteStage` with tenant route FK, positive unique stage number, required role identifier and optional nonnegative alert delay.
- SQL migration `028_approval_route_foundation.sql` creates two empty tables with composite tenant FKs, amount and revision constraints, unique stages, and adds the previously missing tenant FK from `JournalDocumentTypes.ApprovalRouteId` to SYS09. It is transactional, locked and idempotent. No policies, roles, routes or approval records are seeded.
- Domain validation rejects malformed amount scales/ranges, missing references, duplicate stage numbers and stale audit revisions before mutation. EF refuses all SYS09 route/stage writes, including direct internal callers, while role and conflict policy verification are absent. Existing GL14 write guard remains.
- Tests cover domain and EF metadata/guards. SQL integration covers rollback on DDL conflict, retry/idempotency, FKs, empty tables and no leaked writes. API/JWT/OpenAPI regression checks concern existing routes, not SYS09 endpoints.

## Explicitly incomplete

SYS07 has no persistent role catalog or reference with which to validate `ApproverRoleId`. The source lists `DocumentType` as a policy-defined enumeration but supplies no approved values. SYS09 also forbids overlapping routes for the same document and scope; that rule requires transaction-safe interval and branch-scope arbitration and a documented currency comparison policy. Consequently **no SYS09 create/edit/activate API, workflow dispatch, stage execution, timeout escalation or automatic approval is enabled**. The stored separation flag does not replace the existing independent journal separation rule. No GL14 card or GL03 journal is activated by this migration. A privileged direct SQL writer must still be restricted by deployment permissions.

## Next safe implementation steps

1. Implement SYS07 role catalog and role assignment/permission boundaries from its original F01–F14; preserve explicit deny and tenant/branch isolation. Then add the SYS09 role FK in a new forward-only migration.
2. Resolve SYS09 allowed document-kind values and overlapping interval/scope enforcement from approved policy (no arbitrary defaults); implement transactional validation and guarded API, then replace EF write block only after positive and negative acceptance.
3. Validate GL14 route activity, company/branch and document-kind compatibility, plus number series; then implement GL14 endpoints and bind GL03 without impersonating automatic sources.

This is a **schema foundation**, not accepted SYS09-FG01 or completed GL14.


## Verification

Local .NET 10 Release + domain/model + HTTP/OpenAPI: **339/339**. First isolated SQL CI [#41394192566](https://github.com/ameenalqershi/ameen-erp/actions/runs/41394192566) exposed a missing closing parenthesis in two SQL CHECK constraints; corrected in `temp: 8722f4d`. [CI #41394461191](https://github.com/ameenalqershi/ameen-erp/actions/runs/41394461191) passed **340/340**, including disposable SQL Server rollback/idempotency/tenant FKs/EF write guards and production-host HTTPS/JWT/OpenAPI. Source fingerprint: `3f257d87e69925921d1449bfeaca3c07d220bb3142483a5379dac9f9f6b0fd8a`. No SYS09 HTTP endpoint or end-to-end approval acceptance is claimed.

## Update: SYS07 role FK in SQL 029 (2 November 2026)

A persistent structural SYS07 role table and tenant FK for `ApproverRoleId` are introduced by [SYS07 foundation](sys07-role-foundation.md). This supersedes the earlier *no role FK* blocker only. No role assignment, active/scope policy or document-kind/overlap policy has been accepted. All SYS09 and GL14 writes remain blocked; no approval workflow is enabled.
