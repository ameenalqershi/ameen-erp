# Maintainer playbook | دليل المشرفين

This document is for the current owner and future maintainers; it does not claim a current maintainer team.

## Issue triage

1. Check for security-sensitive content; ask for private reporting rather than public details.
2. Confirm the reproducible problem, source-of-truth documentation, dependencies and owner of the decision.
3. Label scope/priority/readiness when labels are configured. Suggested taxonomy: `type:bug`, `type:docs`, `type:feature`, `area:backend`, `area:database`, `area:analysis`, `good first issue`, `help wanted`, `status:blocked`, `priority:high`.
4. Give a clear Definition of Done and distinguish **blocked** from **ready**.
5. Avoid duplicating the existing detailed Notion engineering backlog as thousands of GitHub issues; publish only scoped public contribution opportunities.

## Pull request review

- Check the Issue, diff, CI SHA and linked evidence.
- Verify tenant isolation, permissions, accounting consistency, migration safety and negative tests when the area is sensitive.
- Request scoped changes. Protect historical migrations and original reference-analysis artifacts.
- Avoid merging during unresolved cross-module or financial-policy dependencies.
- Record decisions and follow-up issues transparently.

## Repo settings to configure in GitHub UI

- Protect `main` via ruleset/branch protection, requiring pull requests and relevant passing checks **after** confirming check names.
- Consider requiring one approving review when independent maintainers exist.
- Enable private vulnerability reporting and optionally Discussions.
- Enable Issues and apply a consistent label taxonomy.
- Invite trusted collaborators with least privilege; do not grant write/admin to new contributors just to receive PRs.
- Configure About description, homepage and repository topics once public landing and rights are approved.

These UI/admin settings are separate from repository files and are **not** enabled merely by merging these documents.

## Release checklist

See [Release Process](RELEASE_PROCESS.md); never call a prototype production-ready just because the latest build passed.
