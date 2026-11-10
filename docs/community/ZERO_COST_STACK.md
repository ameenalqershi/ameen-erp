# WOW ERP — Free and Open-Source Setup | خطة العمل دون اشتراكات

This is the **cost policy for collaborating on the source**. It does not promise free production infrastructure, commercial support, or that every external vendor service has unlimited quotas.

## Licensing — what is currently true?

- The repository is public, but **a valid LICENSE is not yet adopted on `main`**. Public does not automatically mean open source.
- PR [#172](https://github.com/ameenalqershi/wow-erp/pull/172) proposes **MIT** (no royalties, no license fee, permits commercial use, modification and redistribution subject to preservation of license/copyright notices).
- **Before merge**, rights holders must verify original-source provenance, imported or generated materials, diagrams, third-party assets and the reference-analysis package: [#173](https://github.com/ameenalqershi/wow-erp/issues/173). Run a historical secrets/privacy audit: [#174](https://github.com/ameenalqershi/wow-erp/issues/174).
- MIT is permissive; it does not require derived software to remain open source. If copyleft is desired, evaluate GPL/AGPL and the license compatibility implications separately, with explicit owner approval.
- Contributors must follow eventual third-party notices and dependencies' individual licenses. No legal audit or legal advice is provided by this guide.

## Practical $0 contribution stack

| Component | Free path | Scope and limits |
| --- | --- | --- |
| .NET SDK, C# | .NET 10 SDK (free to develop with) | SDK requires runtime/tool installation; check individual dependencies |
| Git + GitHub | GitHub Free **public** repo, forks, Issues, PRs, Discussions, wiki/pages (where enabled) | Public standard GitHub-hosted Actions runners have no per-minute charge; specialized runners/external providers may incur costs |
| Dependency security | GitHub Dependency Graph, Dependabot alerts/security updates, public repo secret scanning/private vulnerability reporting | These are enabled/configured in project settings; advanced/commercial security extras must be evaluated before activating |
| .NET CI | Standard public GitHub Actions jobs | Check actual checks and runner limits. Do not assume CI green without exact SHA evidence |
| Database for local test | Microsoft SQL Server Express (free) or eligible Developer edition for non-production only | Express resource/feature limits; Developer edition is **not licensed for production** |
| Docs and planning | Notion Free for a solo owner; public contributor docs in GitHub | Free Notion becomes block-limited with multiple workspace members and has file upload limits; don't invite everyone as workspace members |
| File storage | Google Drive standard free personal allowance | Up to 15 GB shared across Drive/Gmail/Photos, subject to account quota |
| Design/diagrams | Markdown, Mermaid, open-source tooling | Respect the individual tools' licenses and attribution |

## Recommended architecture for community collaboration

**GitHub as the public source of truth:** source, LICENSE, CONTRIBUTING, Issues, Discussions, PR reviews, doc checks and CI.

**Notion as a private coordination tool:** project planning, architecture meeting notes, program priorities. Public-facing contributor documentation should live in GitHub to avoid requiring contributors to join a Notion paid workspace.

**Google Drive for owner-managed work files:** no contributor requires access to it to write a PR. Avoid putting credentials or private financial data in public attachments.

## What is NOT necessarily $0?

- Commercial production servers, domains, managed databases, SMS/email gateways, payment gateways, cloud AI APIs and backup/monitoring services.
- Unlimited Notion team members with unrestricted blocks, unlimited Google Drive storage, premium GitHub runners, paid code security and third-party integrations.
- Legal review or security audits by external professionals when required.

## Practical contributor agreement

We welcome contributions without charging any membership/application/contributor license fee. No maintainer role, salary, reimbursement or support SLA is guaranteed. The repo owner decides licensing and release readiness transparently under [GOVERNANCE.md](../../GOVERNANCE.md).

## Official pricing and licensing references

- [GitHub plans](https://github.com/pricing)
- [Notion plan comparison](https://www.notion.com/pricing)
- [Microsoft SQL Server licensing guidance](https://www.microsoft.com/licensing/guidance/SQL)
- [Google Drive storage policies](https://support.google.com/drive/answer/9312312?hl=en)

**Status:** Documentation under review in Draft PR #172, not a representation that the project's proposed MIT license has been merged or that it has passed independent legal and security audits.
