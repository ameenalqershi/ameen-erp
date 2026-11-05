# SAL09 / SAL15 — tenant-scoped persistence foundation (2026-11-05)

## Scope
This change advances the previously open domain-only foundations into a structural SQL/EF layer on the dedicated `sal` branch. It does not activate either master-data workflow.

- SAL09 SalesRepresentatives: all 16 original source fields persisted, company-scoped primary key, unique code per company, server audit metadata and a same-company custody-account FK.
- SAL15 CustomerGroups: all 17 original source fields persisted, company-scoped primary key, unique code per company, same-company parent-group self-FK, currency/payment-term/control-account FKs and decimal(20,6) credit limit.
- Migrations 049 and 050 are atomic, schema-locked, idempotent and refuse conflicting version owners.
- EF uses composite tenant keys and Version concurrency tokens. EF insert/update/delete are blocked for both entities pending policy and API work.

## Explicitly unimplemented / blocked
- SAL09 EmployeeId has no HR03 provider. SupervisorId target semantics are not approved. CommissionPolicyId has no SAL10 provider. These remain opaque GUID fields with zero-value checks only; no fictional FKs.
- SAL15 PriceListId now has a same-company FK to SalesPriceLists from migration 050. Currency, payment-term and control-account FKs enforce same-company existence, but active/reference authorization is not inferred. SYS16 payment-term writes remain blocked.
- No public API, customer-scope permission, employee lookup, commission calculation, customer-group assignment, credit override, invoice mutation, or operational write endpoint is enabled.
- Customer/representative/customer-group EF writes remain fail-closed. SQL integration tests use raw SQL only against the disposable test database to verify schema constraints and read mapping; this is not a production write path.

## Verification
- `python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet --artifacts /tmp/ameen-sal09-sal15-20261105`
- SQL Server acceptance runs only through `verify.py --sql` with the configured disposable SQL test server in CI. It must prove migration rollback/idempotency, tenant composite FKs, raw-SQL field roundtrip, invalid references, and EF write denial.
- Traceability is generated from the original analysis manifest and updated for SAL02/SAL09/SAL15. The original analysis and diagrams are unchanged.

## Notion state
SAL09-01 and SAL15-01 remain PARTIAL/BLOCKED operationally. Field groups move only to PARTIAL because schema fields now persist but APIs, active-provider checks and approval/permission policies remain missing. AC01/AC02, CTRL01 and OP tasks are not claimed complete.
