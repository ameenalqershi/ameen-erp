# SAL05 — delivery order structural foundation (2026-11-05)

Reference: muhaseb-erp-analysis/docs/modules/SAL.md (SAL05), Notion SAL05-01 / SAL05-AC02.

SalesDeliveryOrder and SalesDeliveryOrderLine preserve tenant/branch/year, document dates/number, optional currency/rate, customer, conditional sales-order source, warehouse, recipient/shipping, item/unit/quantity and optional lot/serial, plus audit. Negative tests cover invalid references, dates, amounts and absence of side effects.

No SQL/EF/API, inventory issue or accounting is enabled. Source control requires a delivery line linked to an order line, but SAL05 field table omits an explicit order-line reference. That link, recognition timing and warehouse issue policy remain blocked pending approved SAL04, INV08, SAL06, SYS and live SQL/HTTP/JWT/OpenAPI acceptance.

This is PARTIAL structural work; original analysis untouched; SUPERVISOR alone merges.
