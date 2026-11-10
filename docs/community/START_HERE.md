# Start here | ابدأ من هنا

## أهلاً بك!

مساهمتك لا تتطلب عضوية رسمية في الفريق. ابدأ بأحد المسارات: إصلاح توثيق، اختبار جديد، تحسين صغير واضح، أو مراجعة محتوى هندسي. للاستفسارات العامة ابحث في Issues أولاً.

## Pick your path

| خبرتك / Your focus | Start |
| --- | --- |
| New contributor | [Contributing](../../CONTRIBUTING.md) then [Issues](https://github.com/ameenalqershi/wow-erp/issues) |
| .NET developer | [Architecture](ARCHITECTURE.md) and [Local development](DEVELOPMENT.md) |
| QA / testing | [Backend tests](../../backend/README.md) and [Validation limits](../backend/validation.md) |
| Database engineer | [Migrations](../../backend/database/) and [Schema integration](../backend/schema-migration-integration.md) |
| Analyst / documentation | [Analysis](../../muhaseb-erp-analysis/README.md) and [Traceability](../backend/traceability.md) |
| Potential maintainer | [Governance](../../GOVERNANCE.md) and [Review playbook](MAINTAINER_PLAYBOOK.md) |

## Your first contribution — concrete walkthrough

1. Read the README and find a scoped Issue (documentation is a good first contribution).
2. Verify whether an existing PR already addresses it; comment about the plan before large work.
3. Fork the repository or create your own branch; do not push into someone else's feature branch.
4. Make the change, review the diff, run relevant checks and gather safe evidence.
5. Open a PR using the template; link the Issue and explain what remains untested.
6. Respond to code-review comments constructively; wait for maintainer review.

## What makes WOW ERP different?

Financial invariants matter: the absence of duplicate postings, precision of monetary values, accurate accounting dates, company and branch isolation, approved business rules, and an audit trail. A change that compiles but violates these boundaries cannot be accepted.

## Starter ideas (not automatic assignments)

- Verify and correct outdated documentation about migration numbering.
- Add clear error examples and documented authentication preconditions to an existing API contract.
- Improve reproducibility of current development checks with no real tenant data.
- Expand negative tests for existing implemented invariants without adding new business policy.

See [Contributors ladder](CONTRIBUTOR_LADDER.md) for review and membership paths. For any vulnerability, use [private reporting](../../SECURITY.md).
