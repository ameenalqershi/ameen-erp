# SAL02 — price-list structural foundation (partial, 2026-11-05)

Source: `muhaseb-erp-analysis/docs/modules/SAL.md` → SAL02; Notion task `SAL02-01` in AMEEN ERP task database.

## Implemented in this isolated change
- Domain-only `SalesPriceList` with exact source header concepts: tenant company, optional branch, opaque system status, name, currency reference, effective date interval, tax-included flag and generated audit metadata.
- `SalesPriceListLine` carries item and unit references, monetary price, discount limit, optional quantity threshold and its parent/tenant identity.
- Validation is limited to nonempty required data, nonzero references, date ordering, decimal precision/range and audit time; `Status` deliberately does not impose an unapproved state machine.
- Domain tests verify values, optional fields, negative validation, and absence of accounting/outbox effects.

## Not implemented / acceptance gate
- Not a completed `SAL02-01` task. No SQL migration, EF persistence, API, JWT, OpenAPI, authorization, idempotency receipt or invoice price selection was created.
- Blocked dependencies: SAL15 customer grouping (draft PR #134); SAL01 customer operations and identity/credit authorization; INV04 item/unit pricing reference and SAL06 invoicing; approved selection priority customer → group → general, allowed date overlap, discount authority, and tax treatment.
- No rate selection or invoice posting is safe until the above contracts and SQL/HTTP/JWT/acceptance tests are implemented and reviewed by SUPERVISOR.
- Draft PR must remain partial and may not be merged into main by this SAL agent. The original analysis and diagrams remain unchanged.

## Validation
- Run `python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet --artifacts /tmp/ameen-sal02-20261105-chat` in the isolated worktree.
- Record the exact local result and CI run for the final commit SHA in the PR and Notion; do not infer SQL Server coverage from pure-domain tests.
