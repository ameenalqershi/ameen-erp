# GL08-FG03 — guarded monetary line difference (partial)

- **Origin:** immutable `muhaseb-erp-analysis/docs/modules/GL.md`, GL08, and original field dictionary GL08-F08/F09/F10/F11.
- **Scope:** pure `CurrencyRevaluationLinePreflight` checks already calculated book and new amounts against the documented `Decimal(20,6)` storage bounds, computes their exact signed difference without rounding and fails closed if the difference exceeds storage precision.
- **Safety:** this is **not** exchange-rate selection, a foreign-currency valuation run, or proof that an open item is monetary or eligible. The future GL08 service must validate item ownership, active tenant-scoped sources and SYS05 rate type, apply the approved currency precision, enforce uniqueness/idempotency per cycle, choose approved gain/loss accounts and persist any authorized journal atomically. No such service, API, schema or posting is activated here.
- **Acceptance:** three direct positive/negative in-memory tests cover signed exact decimals, overprecision and out-of-range values, out-of-range differences and zero. General repository verification and SQL-backed CI establish regression status only; no GL08-specific operational SQL/HTTP/JWT/OpenAPI claims.
- **Notion:** GL08-FG03 stays **partial**, even on green CI. GL08-01 remains blocked by the unapproved rate-type catalogue and source-ledger eligibility contracts.

No original analysis/diagram or production data was modified.
