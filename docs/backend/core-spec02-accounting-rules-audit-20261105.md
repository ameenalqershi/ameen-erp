# CORE SPEC02 — Original accounting rules and effect traceability audit (2026-11-05)

## Source and acceptance boundary
- Immutable source: `muhaseb-erp-analysis/docs/02-accounting-and-business-rules.md`. Its opening warning explicitly classifies these as proposed design rules, **not** as undocumented legacy-system facts; national/accounting localization still requires formal approvals.
- The original contains 20 numbered rules BR01–BR20, 26 illustrative journal/effect events, and seven sections including the general document lifecycle, exchange rates, quantities/cost, tax and discount.
- Audited existing implementation traceability in `docs/backend/rules-implementation.csv` without modifying it. Current BR scope totals: `manual-journal=3`, `partial=10`, `not-started=7`. A manual-journal rule is **not** equivalent to full source-integrated posting.
- Added the non-normative mapping `docs/backend/core-spec02-event-ownership.csv`: every one of the 26 original event labels has a corresponding domain owner and related existing BR identifiers. The `acceptance_evidence` is explicitly `not-verified` for **all events**; owner/rule pointers are triage links, **not** authorization for a new financial policy or implementation.

## Mapping summary
| Event owners | Event count | Acceptance remaining |
|---|---:|---|
| PUR | 4 | Goods receipt versus invoice, GRNI allocation, return value and withholding/tax contracts |
| SAL | 8 | Invoice/delivery/returns/credit memo, revenue timing, tax snapshots, linked quantities, duplicate effects |
| CASH | 3 | Source-linked receipts/payments and internal transfers, FX capture, allocations and tenant isolation |
| INV | 4 | In-transit balances, part receipts, shortages/stock count and valuation |
| FA | 3 | Asset capitalization, depreciation and disposal with historical book and periods |
| HR | 1 | Payroll liability and contributions by approved payroll source |
| MFG | 2 | Materials/WIP/finished goods and explicit allocation of production variance |
| PRJ | 1 | Project invoice/certification and contractual revenue recognition |

## Unnumbered requirements and policy gates
1. **General document lifecycle:** draft/review/reject/approve/atomic-post/reverse is not proven across all document sources. Track against CORE02/CORE03 and SYS09 plus each source's domain work, not GL journal behavior alone.
2. **Accounting examples and 26 event entries:** all have source-owner references, but no full SQL/HTTP/JWT/tenant/idempotency acceptance evidence is asserted in this audit. Original journal entries are *illustrative*, with country-tax and business policy dependencies.
3. **Currency and FX:** original examples include historical rates, realized/unrealized FX and multi-currency lines. SYS05/GL foundations only; legal realized/unrealized FX policy, reconciliation and source settlements need approved tests.
4. **Cost and quantity:** unit conversion history, stock valuation, moving average, landed-cost allocation and retroactive updates depend on INV/IMP/COST provider contracts and period constraints. Do not silently activate negative stock or a new valuation method.
5. **Discounts and taxes:** discount apportionment, tax-inclusive, exempt/zero/out-of-scope, reverse charge and tax-version snapshots require the approved jurisdictional profile (TAX), not sample rates.
6. **Arithmetic example:** the original 10% rate and examples are explicit **test illustrations only**; they must never become default production tax policy.
7. **Cross-module readiness:** latest tracked BR states have 7 not-started and 10 partial rules; absence of operational evidence remains an acceptance blocker and must not be inferred from a passing structural check.

## Verification, ownership and open gates
- `python3 backend/scripts/check_spec02_event_coverage.py`: PASS, all 20 rules / 26 events / 7 sections covered.
- `python3 -m unittest discover -s backend/scripts/tests -p 'test_*.py'`: **55/55 local pass** including 8 new positive/negative tests.
- Required further steps: current SHA GitHub Actions .NET/isolated SQL; domain-owner source acceptance proofs, unresolved policy review and SUPERVISOR sign-off. Original analysis, existing rules status matrix and production code are untouched.
- This is a traceability/audit deliverable only. SPEC02 remains **جزئي** pending complete runtime evidence and explicit acceptance of currently unassigned or unresolved policies.
