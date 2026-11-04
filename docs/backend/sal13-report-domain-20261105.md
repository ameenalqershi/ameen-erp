# SAL13 report contract — read-only partial foundation (2026-11-05)

Source of truth: `muhaseb-erp-analysis/docs/modules/SAL.md` SAL13, Notion SAL13-01 and SAL13-AC02.

Implemented: `SalesMovementReportFilter` captures the original company, authorized branches, date range, display currency/mode, document state, detail level, optional customer, salesperson, item and sales kind. Branches requested outside an externally verified authorized branch set are refused. String selectors remain opaque because no approved catalogs are present.

`SalesMovementReportVisibility.ForAuthorizedViewer` hides cost and profit from a caller without the separately validated permission. It does not itself authenticate the caller or grant the permission: API, JWT, SQL predicates and server-side authorization still must supply that decision. It deliberately does not calculate the net of returns or margins, since SAL06/SAL07 source movements and business policy are missing.

Three new tests cover complete filter fidelity, invalid branches/values and masking of confidential figures. No data was read from production; no SQL migrations, EF mapping, report endpoint, export, print, posting or source drill-down introduced. Keep SAL13-01 and acceptance tasks partial pending SAL06, SAL07, RPT05, approved permission contract, active catalog values and live SQL/HTTP/JWT/OpenAPI integration.

Work is isolated in `temp/sal13-report-filters-20261105-chat` for SUPERVISOR review. SAL does not merge.
