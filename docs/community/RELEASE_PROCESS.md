# Release process | سياسة الإصدارات

## Current stage

There is no production-ready or security-supported stable release. Until the repository owner declares a release scope, all code is experimental.

## Candidate release gate (proposed)

1. Define precise feature scope and unresolved accounting/business policies.
2. Verify licensed ownership of code, reference analysis and assets; complete dependency and secret review.
3. Require successful build, executable .NET tests, isolated SQL integration, hosted HTTPS/JWT/OIDC scenarios and consistent OpenAPI.
4. Audit migration path from an existing database, disposable test restore and rollback policy.
5. Reconcile module acceptance criteria, traceability, security and operational hardening.
6. Review performance, monitoring, backup/restore, identity integration, and deployment prerequisites.
7. Publish versioned release notes documenting included features, omissions, known limitations and evidence references.
8. Only then tag a version, and label prerelease/stable status accurately.

## What not to claim

Do not interpret a passing test count, a completed foundation migration, or 150 analyzed screens as 150 production-ready screens. Do not publish an SLA, stable API promise, or a production deployment image absent verification.

## Changes and attribution

Record user-facing and schema behavior changes in PR descriptions, include references to Issues and completed tests, and preserve contributor authorship in Git history.
