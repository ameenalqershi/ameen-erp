# CASH02-01 — Bank-account persistence foundation / أساس بطاقات الحسابات البنكية

Authoritative source: `muhaseb-erp-analysis/docs/modules/CASH.md` and `CASH02-F01..F17`. Original files are unchanged.

## Structural deliverable

- `BankAccount` persists all 17 prescribed card fields (18 SQL columns including independent optimistic `Version`), including the bank name, text account number preserving leading zeros, nullable IBAN, the general ledger account, currency, branch, creator and UTC audit/revision metadata.
- The domain recognizes a supplied IBAN in its canonical uppercase unspaced form and verifies general ISO 13616 MOD-97 checksum. The country-specific rule determining **whether IBAN is mandatory** is intentionally not assumed; until an approved jurisdiction profile exists, `Iban` is nullable.
- `032_bank_account_cards.sql` requires schema 031, serializes via `sp_getapplock`, and applies atomically/idempotently. Composite foreign keys preserve tenant boundaries for company, ledger account, currency and branch. Unique indexes cover code, bank+account number, and non-null IBAN per company; stored timestamps and positive revision are checked.
- EF mapping aligns to the SQL schema. `ErpDbContext.ValidateChanges` **refuses all bank-account insert/update/delete operations** until conditional IBAN validation, beneficiary-change review, scopes and API acceptance can be completed. Thus no unapproved changes or payments are exposed.
- `BankAccountFoundationTests` exercise field fidelity, leading zeros, checksum/negative validation and revision/timing; `BankAccountSqlIntegration` tests failure atomicity, reapply idempotency, structural tenant keys and fail-closed EF writes against disposable SQL.

## Not delivered

CASH02-02 business lifecycle/API, beneficiary approval, jurisdiction-specific IBAN requirements, CASH07/CASH10 integration, bank reconciliation or payment/receipt operations. The 17 original fields are **partial structural bindings**, not user-available CRUD. Do not count CASH02 as completed until those gates are approved and verified.

## Evidence

Run `python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet` from the repo root for local domain/EF/OpenAPI tests. Live SQL and production HTTP/JWT acceptance run in GitHub Actions with a disposable SQL Server once the branch CI completes. There are no changes to the analysis files or original diagrams.
