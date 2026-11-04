# SAL04 — sales order structural foundation only (2026-11-05)

Source: `muhaseb-erp-analysis/docs/modules/SAL.md`, section SAL04. Notion task SAL04-01 / SAL04-AC02.

- `SalesOrder` and `SalesOrderLine` are pure domain contracts: company/branch/year, document number/dates, currency and FX, description, optional external reference/quotation, customer, delivery and payment terms, item/unit/quantity/price/discount/optional tax reference and audit metadata.
- The only validation is required references, required dates and strings, representable positive exchange rate, decimal amount bounds/precision and nonzero quantity. Status remains opaque pending approved state and approval policies.
- Unit tests cover field fidelity, nullable quotation and tax references, negative validation and absence of inventory, journal and outbox side effects.
- This is not an operational order. No reserve-stock policy, executed-quantity accounting, quote conversion, status workflow, SQL migrations, EF mapping, API or JWT/OpenAPI created.
- Parent SAL04-01 stays partial until approved SAL14 quotation/version link, SAL02 pricing, INV11 reservation, SAL05/SAL06 fulfillment, SYS authorization, SQL/HTTP/JWT/integration/acceptance and Supervisor review.
- Source analysis/diagrams untouched; SAL agent must not merge main.

Local validation: `python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet --artifacts /tmp/ameen-sal04-20261105-chat`; attach exact test and CI evidence to PR.
