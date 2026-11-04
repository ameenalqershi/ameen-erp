# SAL14 — quotation structural foundation (partial, 2026-11-05)

Source: `muhaseb-erp-analysis/docs/modules/SAL.md`, SAL14; Notion SAL14-01.

## Domain-only deliverable
- `SalesQuotation` and `SalesQuotationLine` preserve the source document header, system status as opaque text, branch/year, currency and exchange rate, required customer-or-prospect name and optional customer card, expiry, delivery/payment terms, quotation issue marker, audit, item/service lines with quantity, price, optional discount and optional tax reference.
- Positive and negative tests prove field preservation, precision, required references, optional references, audit time and absence of GL/stock/outbox side effects.
- No transitions, conversion to sales order, reservation, pricing hierarchy, tax calculation or posting. No new database migration/EF entity registration, API or OpenAPI.
- Source acceptance requires SAL04 conversion with preserved version, SAL02 approved pricing, active customer/item/unit references, role/tenant authorization and SQL/HTTP/JWT/acceptance CI; those dependencies are unproven. This change is **not** a completed SAL14-01 task.
- Work in isolated `temp/sal14-domain-20261105-chat`; only SUPERVISOR may merge after review, no changes to original diagrams or analysis.

## Verification
- Run `python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet --artifacts /tmp/ameen-sal14-20261105-chat`; report exact result and CI tied to final SHA.
