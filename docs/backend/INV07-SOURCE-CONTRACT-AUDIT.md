# INV07-01-PRE01 — source/GRNI contract audit (read-only)

Status: **audit deliverable only**. INV07-01 remains **BLOCKED**; this document does not authorize posting, a source enum, a tolerance value, or an account-routing policy.

## Evidence and boundaries

- Canonical INV07 specification: `muhaseb-erp-analysis/docs/modules/INV.md`, section `INV07` (source type and conditional source document, warehouse, conditional supplier, item/unit/quantity, conditional provisional cost, lot/serial and accepted/rejected line state).
- Purchase order: `muhaseb-erp-analysis/docs/modules/PUR.md`, `PUR06` (supplier, delivery warehouse, receipt tolerance, remaining quantity and version after partial receipt).
- Purchase invoice: `muhaseb-erp-analysis/docs/modules/PUR.md`, `PUR07` (direct-versus-receipt purchase, conditional receipt reference, supplier invoice duplicate control and matching).
- Imports: `muhaseb-erp-analysis/docs/modules/IMP.md`, `IMP05` (shipment, warehouse, mandatory receipt reference, shipped/received/claimed quantity, exactly one receipt effect), `IMP06` (cost distribution after receipt).
- Accounting: `muhaseb-erp-analysis/docs/modules/GL.md`, `GL06` (atomic document posting, per-document failure and replay control); `INV07` specifies inventory versus GRNI **or an approved source account**.
- Current implementation: `backend/src/Ameen.Erp.Domain/Inventory/Posting/StockMovement.cs` is explicitly scoped to **inventory opening** via mandatory `OpeningId`; `backend/src/Ameen.Erp.Application/Features/InventoryOpening/InventoryOpeningService.Posting.cs` has an opening-only clearing account and journal path. Neither is a generic receipt contract. No INV07 posting API, durable receipt entity, or GRNI source route is established by these classes.
- V1 task: https://app.notion.com/p/4053830ba34d81c3b0c4cbbad0cc1028
- V2 preflight: https://app.notion.com/p/40e3830ba34d8116af6cc13662d1548a

## Source ownership and required contract map

| Proposed integration boundary (not an approved enum) | Owner | Source evidence | Contract required before implementation |
| --- | --- | --- | --- |
| Purchase order receipt | PUR06 / INV07 | PUR06 order, supplier, delivery warehouse, approved receipt tolerance; INV07 conditional source document | Stable source header and line IDs, company/branch, order version, approved supplier, item/unit, original and remaining base quantities, already-accepted receipts, tolerance approval provenance |
| Purchase invoice against receipt | PUR07 / INV07 | PUR07 direct/against-receipt discriminator and conditional receipt reference | Invoice-to-receipt/line links, supplier invoice uniqueness scope, accepted quantity and prior invoice allocation; one economic effect, not two stock receipts |
| Direct purchase invoice | PUR07 / INV07 | PUR07 purchase type and INV07 source-dependent effect | Explicit decision whether invoice creates an INV07 receipt or uses a distinct route; accounting source account and deduplication key; no automatic double posting |
| Shipment / shortage receipt | IMP05 / INV07 | IMP05 mandatory receipt reference, shipment and inspection report | Shipment ID, receipt ID, shipped/received/claimed quantities, shortage approval and a one-to-one stock-effect ownership rule |
| Landed-cost distribution | IMP06 / INV07 | IMP06 cost-allocation and sold/remaining shares | Approved valuation method, original receipt/stock-layer reference and separate accounting adjustment route; not a second quantity receipt |
| Journal posting / reversal | GL06 / INV07 | GL06 atomic source document and per-document results | Source registry/route ownership, immutable source identity, journal link, GRNI versus approved source account, rollback/replay and reversal policy |
| Tenant/branch and numbering | SYS09 / SYS10 / INV07 | INV07 company/branch and generated document number | Tenant membership, authorized branch, number reservation and source reference isolation; reject cross-tenant/branch spoofing |

## Mandatory acceptance gates (not implementation assumptions)

1. **Source identity:** source type and source document/line are validated against a published, versioned owner contract; no invented enum values. Company, branch, warehouse, supplier and active item/unit/identity must agree.
2. **Quantity and concurrency:** normalize units using approved item conversion; validate accepted/rejected quantities and remaining order quantity against the approved tolerance; perform concurrency-safe compare-and-set and reject repeated receipts or duplicate source-line allocations.
3. **Cost and posting:** provisional cost and currency/FX require source-specific approval; a posted receipt must atomically create the inventory movement and the approved GRNI/source-account journal exactly once. Do not reuse the inventory-opening clearing account as GRNI.
4. **Document links:** preserve immutable source-document, source-line, supplier and shipment provenance, and link receipt to invoices/claims without duplicating stock or GL.
5. **Authorization and audit:** verify tenant/branch, JWT permissions, period state, idempotency replay, version conflicts, audit/outbox and negative rollback; preserve reversal provenance.
6. **Tests:** positive/negative SQL, HTTP/JWT, OpenAPI and concurrent duplicate-post/partial-receipt tests must run on isolated SQL Server in CI; no production data.

## Decisions still missing; explicit owners

- **PUR06 owner:** approved receipt-tolerance calculation and remaining-quantity semantics across order revisions and partial receipts.
- **PUR07 owner:** direct invoice versus invoice-against-receipt source ownership, matching and invoice duplicate uniqueness scope.
- **IMP05/IMP06 owner:** shipment/receipt single-effect rule, shortage claim route, and landed-cost adjustments to sold versus remaining goods.
- **GL06/accounting owner:** approved GRNI or source-account selection, journal/reversal routing and valuation/FX contract.
- **SYS09/SYS10 owner:** source-document permission and numbering contracts where not already covered.
- **INV07 owner:** final source type values, accepted/rejected line status values, provisional cost obligation, stock-identity rules, and source-to-receipt immutable keys.

**Result:** preflight contract map and gap ownership are complete. This is a read-only audit; no receipt, movement, GRNI journal, SQL migration or financial policy has been implemented. Resume INV07-01 only when the above owners publish matching contracts and the required SQL/JWT/OpenAPI acceptance gates can be implemented without guesswork.
