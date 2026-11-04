# CORE DGM-20–27 — process flow diagram contract audit (2026-11-04)

## Provenance and boundary
- Source of truth: immutable original `muhaseb-erp-analysis/diagrams/source/*.mmd`, `data/diagrams.json`, SVG and `docs/backend/diagrams-implementation.csv`.
- Notion: AMEEN ERP numbered tasks DGM-20 through DGM-27; parent CORE-01.
- Baseline: `main` 4ddca22724a0a7aba0f5d0e45643c3bf99f9207d at branch creation; CI backend verification #41909864903 succeeded on that SHA.
- Implemented here: an additive read-only, repeatable graph/index/traceability/branch-edge verifier and negative tests. No source diagrams, accounting policies, database schemas, API contracts or runtime code changed.
- Explicit exclusion: verifying the **graph shape** does not demonstrate operational workflow execution. Do not mark the eight end-to-end diagram review tasks complete on this evidence alone.

## Diagram-to-implementation readiness matrix

| Task | Original graph | Nodes | Edges | Existing status | Operational acceptance gaps |
|---|---|---:|---:|---|---|
| DGM-20 | stock-transfer | 8 | 8 | not-started | INV transfer, partial receipt, inventory loss and reconciliation through SQL/JWT and valuation not proven |
| DGM-21 | returns | 9 | 10 | not-started | SAL return/credit, receipt and TAX/settlement source contracts and E2E reversal not operational |
| DGM-22 | period-close | 8 | 8 | partial | GL period checks exist; bank, receivable, inventory, tax and year-end reconciliation not accepted end to end |
| DGM-23 | einvoice | 9 | 9 | not-started | Country-specific type/approval/reporting contracts and durable sender remain unresolved |
| DGM-24 | posting-sequence | 9 | 10 | partial | GL posting/Outbox foundations present; linked AR/AP/stock/TAX atomic writes and external sender not fully operational |
| DGM-25 | imports | 8 | 9 | not-started | IMP landed costs, approvals, allocation between sold and remaining stock not implemented |
| DGM-26 | extensions | 8 | 7 | not-started | HR/MFG/PRJ source postings and consolidated reporting remain unimplemented |
| DGM-27 | opening-migration | 7 | 7 | not-started | SYS13 opening import, de-duplication, balancing and cutover acceptance missing |

All eight indexed graphs are connected flowcharts and retain their original branching/ordering labels. The checker reuses `check_core_flowchart_graphs.py` to avoid parallel parsing rules, and guards the key original labels (failed close, external delivery only after commit, independent invoicing branches, shortage path, etc.). **66 nodes and 68 arrows** are structurally checked. No diagram status was promoted to `implemented`.

## Validation and handoff
- Local: `python3 backend/scripts/check_core_process_diagrams.py` PASS.
- Local: `python3 -m unittest discover -s backend/scripts/tests -p 'test_*.py'` **54/54 passed** (includes seven new positive/negative tests).
- Further gates: full `backend/scripts/verify.py` with .NET 10 + live isolated SQL **not available on this remote runtime**; confirm GitHub Actions on the branch's final HEAD before integration.
- No production data or secret was accessed. No changes in business behavior.
- Supervisor: review the additive script, test coverage, Notion DGM-20–27 readiness, final SHA and CI; merge only if green and conflict-free.
- The unrelated conceptual `data-model.mmd` ER diagram (DGM-28) is outside this **flowchart** checker and needs its own appropriate verification.
