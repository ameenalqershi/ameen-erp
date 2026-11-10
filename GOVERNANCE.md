# Governance and maintainer roles | الحوكمة

## Principles

Project direction is guided by financial correctness, privacy, maintainability, verifiable tests, and transparent decision records. The repository owner is the current final decision maker until a broader maintainer team is formally appointed.

## Roles

- **Contributor:** Anyone may propose fixes, documentation, tests, or features via Issues/PRs. No repository membership required.
- **Trusted reviewer (future role):** A contributor with demonstrated context may be asked to review work. This does not imply write access.
- **Maintainer (invited):** Maintains assigned areas, triages issues, reviews PRs, and supports release preparation within granted permissions.
- **Repository owner:** Controls membership, security, licensing, final releases, and conflict escalation.

No other maintainers or teams are claimed to exist until they are explicitly appointed.

## Decision and merge policy

Use Issues for scoped requirements and unresolved policies. PR reviews must verify acceptance criteria, tests, financial correctness, migrations, and documentation. An automated green check does not supersede unresolved policy decisions or production risk. Prefer at least one independent technical review for material changes when a reviewer is available; until then, the owner may review and decide. Avoid committing directly to `main` for routine external contributions.

For accounting or schema changes, document dependencies and preserve the original reference-analysis baseline unless the owner explicitly approves a change.

## Pathway to maintainer status

A sustained history of constructive PRs, responsible security handling, accurate documentation, willingness to review, and project need may prompt an invitation. There is **no fixed contribution count**, guarantee of membership, or automatic access escalation. Ownership of significant contributions remains credited via Git history.

## Conflict handling

Raise technical disagreement in the relevant Issue/PR with facts and tests. The owner resolves conflicts after considering the evidence. Conduct complaints are handled privately under [Code of Conduct](CODE_OF_CONDUCT.md).

## License and releases

Before applying an open-source license, the owner must confirm rights over code, analysis/reference materials, documentation, and assets, and review third-party terms. Release tags require verified build and test evidence, versioned migrations, security checks, and an explicit maturity label. See [Release Process](docs/community/RELEASE_PROCESS.md).
