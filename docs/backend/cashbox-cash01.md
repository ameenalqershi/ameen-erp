# CASH01 — Cash-box master card / بطاقة الصندوق

Source: `muhaseb-erp-analysis/docs/modules/CASH.md`, `CASH01-F01..F16`, `BR02`, `BR09`, `BR18`. The original analysis and Mermaid diagrams remain unchanged.

## Delivered scope

- Master-card `CashBox` with all original 16 fields; server-generated entity ID, actor and UTC timestamps, integer revision and independent UUID concurrency token.
- `031_cashbox_cards.sql`: atomic schema-lock migration, idempotent version 31, composite tenant foreign keys for company, branch, account, custodian membership, and currency; unique code and account/branch/currency association; monetary `decimal(20,6)` and audit/positive-revision constraints.
- `CashBoxService` with Create, Edit, Status, Search, Get, Usage, Audit operations; the inherited command/transaction pipeline enforces request replay and atomic audit. Searches and details respect company membership and authorized branches; writes currently require `company.configure` with all-branch scope, pending SYS07 granular cash permissions.
- Server validates active complete branch, active posting non-control account, same-company active currency and account-currency alignment, active custodian membership with branch scope, and optional limit with the company's currency scale.
- OpenAPI routes `/api/v1/companies/{companyId}/cash-boxes` and subordinate paths use JWT and command idempotency keys. The usage endpoint counts journal references on the linked account+branch+currency: **it does not calculate cash balances**.
- Domain, application, OpenAPI and isolated SQL migration/persistence acceptance tests added; see CI for independent SQL/HTTPS/JWT confirmation.

## Explicitly not delivered

- Available-balance computation, withdrawals and overdraft blocking in CASH04, receipt and payment posting in CASH03/CASH04, and cash-only resource authorization under SYS07; neither the balance limit nor card status should be represented as a verified posting guard.
- CASH01 is a **partial screen**, even when its database task and card operations pass; other CASH screens are not implemented.
- No real customer/production data, no original analysis changes, no separate account balance ledger, and no fabricated cash policy.

## Verification instructions

```sh
cd backend
python3 scripts/verify.py --dotnet /root/.dotnet/dotnet
# For isolated SQL + production-host HTTPS/JWT checks, only on a disposable test SQL instance:
python3 scripts/verify.py --dotnet /root/.dotnet/dotnet --sql
```

The second command requires `AMEEN_TEST_SQL_CONNECTION` and creates/drops its own disposable database. Do not run against a production SQL server. Full acceptance requires successful branch CI on the exact pushed SHA.
