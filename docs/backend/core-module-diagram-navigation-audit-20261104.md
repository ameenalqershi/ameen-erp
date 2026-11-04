# CORE DGM-01–15 — immutable module-navigation contract audit (2026-11-04)

Baseline: `main` commit `88f52b725deb82ca3208a925184857499c0945b8`.
This is a **source-to-registry navigation audit only**. It does not certify any backend
screen implementation, approval policy, posting sequence, financial control or ERP
production readiness. The original analysis is immutable and untouched.

## Scope and evidence

- Authoritative screens: `muhaseb-erp-analysis/data/screens.json` (150 screen entries).
- Authoritative diagram index: `muhaseb-erp-analysis/data/diagrams.json`
  (28 diagrams, 15 module navigation maps).
- Source navigation maps: `muhaseb-erp-analysis/diagrams/source/module-*.mmd`;
  generated drawings: `muhaseb-erp-analysis/diagrams/svg/module-*.svg`.
- New acceptance checker: `backend/scripts/check_module_diagram_navigation.py`.
  It compares **every approved screen identifier and exact Arabic label** with its module
  source map, resolves the navigation parent of each screen to its original menu
  category, verifies the root menu title, rejects foreign or missing screen nodes,
  duplicate nodes, dangling edges, multiple navigation parents, disconnected nodes,
  cycles, misplaced categories, unexpected paths or malformed SVGs.
- New regression suite: `backend/scripts/tests/test_module_diagram_navigation.py`.
  Positive baseline plus negative cases for missing screens, incorrect labels,
  cross-category navigation, dangling edges, invalid parentage/cycles and invalid SVG.
  Automatically included by the existing `verify.py` unittest-discovery gate.

| Notion audit | Module | Approved navigation nodes |
| --- | --- | ---: |
| DGM-01 | SYS | 17 |
| DGM-02 | GL | 14 |
| DGM-03 | CASH | 11 |
| DGM-04 | INV | 16 |
| DGM-05 | SAL | 15 |
| DGM-06 | PUR | 13 |
| DGM-07 | POS | 6 |
| DGM-08 | IMP | 6 |
| DGM-09 | FA | 6 |
| DGM-10 | COST | 5 |
| DGM-11 | HR | 7 |
| DGM-12 | MFG | 6 |
| DGM-13 | PRJ | 6 |
| DGM-14 | TAX | 8 |
| DGM-15 | RPT | 14 |
| **Total** | **15 maps** | **150** |

## Reproduction and limitations

Run:

```sh
python3 backend/scripts/check_analysis_baseline.py
python3 backend/scripts/check_module_diagram_navigation.py
python3 -m unittest discover -s backend/scripts/tests -p 'test_*.py'
python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet
```

The approved maps explicitly describe **navigation**, not a dependency graph.
A diagram edge is never evidence that the target screen can be executed, that a
permission is granted, or that a financial/source document can be posted.
Unfinished module-specific gaps in Notion remain unchanged. The audit has no
SQL or API changes, so existing SQL/HTTPS/JWT/OpenAPI checks serve as
regression coverage for the unmodified backend. Only SUPERVISOR may merge.

Local verification on .NET 10.0.403: `verify.py` **530/530 passed**, 0 failed; `liveSqlRequested=false`, with regression tests and HTTP/OpenAPI included. Live isolated SQL remains a CI acceptance gate, not a local claim.

The DGM-01–15 task statuses must remain **under verification** until CI on the final
head SHA and SUPERVISOR review. Do not count the 15 navigation audits as 15
completed feature implementations.
