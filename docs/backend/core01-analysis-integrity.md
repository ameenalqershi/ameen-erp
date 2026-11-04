# CORE-01 — approved analysis integrity gate

## Scope and source

Notion CORE-01 requires an independently reproducible stop gate for the SHA fingerprints of the 150 approved screens and 28 source diagrams. The original analysis directory is read-only. Baseline reviewed against main commit `8c767dea816c3d6ab3664b203f0b3cddbb496449` (4 November 2026).

The existing `backend/scripts/traceability.py --check` verifies each SHA-256 listed in `muhaseb-erp-analysis/manifest.json`, plus generated traceability. However, the existing verification did not pin the manifest itself: a coordinated source-and-manifest edit could otherwise pass the per-entry hash check without an explicit approval decision.

## Implemented fail-closed gate

- `backend/scripts/check_analysis_baseline.py` pins the approved Git blob SHA of the manifest (`dddfd0a3ad9b0da7daf4668f8c5f341675149fda`).
- Rechecks each listed file's byte size and SHA-256, rejects duplicate manifest paths, path escape, missing files and missing or duplicate Mermaid/SVG references.
- Requires exactly 150 unique screens, 2,759 fields and 28 unique diagram identifiers with 28 tracked Mermaid sources and 28 tracked SVG exports.
- `backend/scripts/verify.py` checks this before executing the remaining verification commands; the GitHub backend workflow fails before starting the disposable SQL Server if the baseline changes.
- `backend/scripts/tests/test_analysis_baseline.py` supplies positive, missing-file, source-tamper, coordinated-manifest-change and untracked-reference acceptance cases.

## Reproduce and approval boundary

Run `python3 backend/scripts/check_analysis_baseline.py`, then `python3 -m unittest discover -s backend/scripts/tests -p 'test_*.py'` and `python3 backend/scripts/verify.py`. The existing backend workflow additionally runs live SQL in a disposable test instance; no production connection or secrets are needed for the new baseline guard.

If a future change to the original analysis is explicitly approved, a reviewer must independently verify the new manifest and its file fingerprints, then update the pinned baseline in a separate reviewed CORE change. This implementation does not silently approve a new original-analysis revision and makes no claim to finish shared DOCUMENT, LINE_LINK, APPROVAL, cross-module E2E or production readiness work.

**Merge gate:** CI success must be observed on the final head SHA, then SUPERVISOR reviews and merges. Until then CORE-01 remains under verification, not merged.
