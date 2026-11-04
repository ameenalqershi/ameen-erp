# CORE DGM-28 — Conceptual entity-relationship audit (2026-11-04)

## Authority and limits
- Original analysis: `muhaseb-erp-analysis/diagrams/source/data-model.mmd`, the indexed SVG, and `muhaseb-erp-analysis/docs/05-data-and-controls.md` explicitly say this ER model is **conceptual, not final SQL DDL**.
- DGM-28 is a distinct numbered Notion task under CORE-01. This check only validates the immutable entity/relation/cardinality snapshot, its indexed SVG, and the existing traceability entry. It does **not** add entities, migrations, posting logic, foreign keys, or a security policy.
- Original conceptual graph: 25 entities and 32 directed labelled relationships. The validator catches missing reference relations, mutated optionality/cardinality, malformed Mermaid syntax, duplicates, missing index/traceability, and broken SVG XML.

## Domain readiness review
| Concepts | Established scope or remaining evidence |
|---|---|
| COMPANY, BRANCH, ACCOUNT, FISCAL_PERIOD, JOURNAL_ENTRY/JOURNAL_LINE | Existing SYS/GL foundation; exact production relationship and tenant constraints require evidence from their own SQL/HTTP/JWT tests |
| ITEM, ITEM_UNIT, WAREHOUSE, LOT, STOCK_MOVEMENT, COST_ALLOCATION | Some INV master-data foundations exist; ledger movements, lots, valuation/allocation and link-to-document handling are incomplete |
| DOCUMENT, DOCUMENT_LINE, LINE_LINK | CORE-02/03 contracts are blocked; the diagram must not be converted into premature general-purpose DDL |
| PARTY, PARTY_ROLE, OPEN_ITEM, SETTLEMENT_ALLOCATION | Customer/vendor and settlement semantics depend on the source domain and remain incomplete |
| TAX_SNAPSHOT, EINVOICE, DELIVERY_ATTEMPT | TAX country-specific contracts, delivery lifecycle, immutability and acceptance are pending |
| APPROVAL, AUDIT_EVENT, OUTBOX_EVENT | Audit and outbox foundations exist for some GL operations; SYS09 approvals and generalized delivery worker remain partial or unavailable |

## Reproduction and closure
- `python3 backend/scripts/check_core_er_diagram.py`: PASS on the immutable original source (25 entities / 32 relationships).
- The 9 new acceptance tests, including negative syntax/traceability/cardinality cases, ran as part of the full Python suite: **63/63 passed locally**.
- Other local gates: `check_analysis_baseline.py`, `traceability.py --check` and DGM20–27 process graph checks. GitHub Actions .NET/SQL is a required separate gate on the final SHA.
- Do **not** close DGM-28 as operationally complete merely because the conceptual graph is well formed. Map every relation to approved source contracts, current code/SQL and acceptance tests; retain blocked providers, owner and remaining differences explicitly for SUPERVISOR.
