# SAL02 — tenant-scoped price-list persistence foundation (2026-11-05)

Reference: `muhaseb-erp-analysis/docs/modules/SAL.md`, SAL02 and Notion SAL02-01 / SAL02-FG / SAL02-AC02.

- Schema migration 050 creates SalesPriceLists and SalesPriceListLines, then adds the same-company CustomerGroups.PriceListId FK. Header fields retain optional branch, opaque status, name, currency, effective dates, tax-included flag and audit. Lines retain item/unit pair, price, discount limit and optional quantity threshold.
- EF maps company composite keys, Version concurrency, same-company branch/currency/list and ItemUnit-pair FKs, decimal(20,6) and safe date/amount checks. Price-list line collection is queried explicitly; no mutable EF navigation is exposed.
- SQL integration covers migration rollback/idempotency, exact columns, FK composition, same-company item/unit pair, field roundtrip, invalid date/amount/refs and fail-closed EF insert/update/delete.
- No public write API or pricing resolution. The source rule requires customer > group > general priority and overlap/discount authorization, but SAL02 source fields do not define a scope/assignment field or sanctioned precedence resolver. No invented assignment table, default priority, or invoice mutation was added.
- SAL02 remains PARTIAL / operationally blocked pending SAL01 customer pricing assignment, SAL15 group precedence, SAL06 invoice integration, approved overlap/discount policies and authorized SQL/HTTP/JWT/OpenAPI acceptance. SAL15's required PriceListId now has a real same-company FK to SalesPriceLists.
