# GL12-02 — period-reopening screen completeness audit (2026-11-03)

**Scope:** Notion GL12-02 verifies **completeness of the original GL12 requirements**; it does not implement missing reopening approval, temporary permissions, expirations or automatic reclose. Baseline gl SHA 67e07c08ea4bb9c01a8b6d0a184f4a1162ea11b1 includes main SHA 7fbfbf7c6a27abedc6ec2310c74f8d0683ecc4ff. Existing CI #41587735802 was SUCCESS with 404/404 tests. A new PR and exact-head CI are required for this audit commit. The original analysis, production data and financial policy must remain unchanged.

## Source and field-level reconciliation

Source: muhaseb-erp-analysis/docs/modules/GL.md, GL12; original fields.csv; docs/backend/fields-implementation.csv GL12-F01..F11. Original relationships BR02/BR09/BR18, GL11, SYS14.

| Source fields | Verified existing contract | Status |
|---|---|---|
| GL12-F01 company; GL12-F04 period | Authorized route companyId, id on POST reopen and GET reopen-history | 2 implemented |
| GL12-F05 reason; GL12-F10 revision | ChangePeriodRequest.Reason; fiscal period.Version and stored decision | 2 implemented |
| GL12-F08 actor; GL12-F09 creation time | AuditEvent.ActorId and OccurredAt on period.reopened, *not independent GL12 card columns* | 2 audit-backed |
| GL12-F02 branch; GL12-F03 status; GL12-F06 approval reference; GL12-F07 allowed-until; GL12-F11 last-modified | No complete direct mapping in current GL12 fields inventory. Period.IsClosed is not an approval state, nor is Version a timestamp | **5 pending** |

**Total: 4 implemented, 2 audit-backed, 5 pending out of 11. GL12 screen remains PARTIAL.**

## Action, security, and negative-acceptance audit

| Original requirement | Existing implementation/test evidence | Unclosed part |
|---|---|---|
| Choose authorized fiscal period; run reopening | POST /api/v1/companies/{companyId}/periods/{id}/reopen calls PeriodCloseService.ChangePeriod with close=false, checks independent PeriodReopen permission, stores reason, writes period.reopened audit. RateAndCloseTests and PeriodReopenHistorySqlIntegration test existing decisions | No approved temporary exception, signed approval reference or end-of-allowance timestamp |
| Show decision and history | GET /api/v1/companies/{companyId}/periods/{id}/reopen-history returns only persisted period.reopened events with paged history. Requires Read + Audit + PeriodReopen and all-branch company scope. SQL integration proves history is real and read-only, HostedApiIntegration verifies HTTPS/JWT/SQL and OpenAPI with 403 for unauthorized viewers | GL12-A05 is complete for actual history, NOT for the full GL12 exception workflow |
| Preview, validation, independent approval | Existing close-preview is an **GL11 close preview** and not an GL12 reopen preview. No approved GL12 reopen dry-run, signed approval, expiry, or authorized exception state evidenced | GL12-A01/A02/A03/FG02/GP01 remain open; approval policy must be formally specified |
| Prevent bypass / dependency violations | Existing global PeriodReopen permission, membership and revision/idempotency safeguards for the basic operation; tests cover isolation and denial in its existing scope | Need rules and negative acceptance for closed SYS14 tax declarations, bank/AR/AP/inventory/FA reconciliation, GL11 annual-close reversals, expiration and safe automatic reclose. Never infer these from GL11 preview or audit log |

## Closure boundaries and verification

1. GL12-02 **audit task** may be accepted when the complete field/action/negative test gap inventory above is committed, CI on its exact new SHA succeeds with disposable SQL and production HTTPS/JWT/OIDC/OpenAPI, and traceability/progress checks pass. GL12-01, GL12-GP01, GL12-T01/T02 must not be marked fully implemented.
2. Retain GL12 state as **partial**. Remaining functional dependencies: approved approval-reference and expiry policies; SYS14 closed filing guard; GL11 ERP/year close; subsidiary ledger reconciliation and safe automatic reclose.
3. No changes are made to source analysis, SQL schemas, API/authorization code, financial policy or production data. On this server there is no local dotnet runtime; full tests are verified by GitHub Actions only.
