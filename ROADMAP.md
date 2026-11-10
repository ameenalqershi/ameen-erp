# Roadmap | خارطة الطريق

This is a **directional roadmap**, not a commitment to dates or a claim that all listed work is ready to implement. The precise screen-by-screen backlog and dependency decisions remain in [PROGRESS](docs/backend/PROGRESS.md), [Traceability](docs/backend/traceability.md) and [Completion plan](docs/backend/completion-plan.md).

## Current foundation (implemented in part)

- .NET 10 API and modular monolith layering.
- Identity/permission boundaries, transactional accounting core, selected general-ledger workflows, inventory foundations, reference entities, testing and traceability.
- Detailed reference analysis and diagrams for a broader ERP scope.

## Community readiness

- [ ] Review code and analysis rights and adopt an approved OSI-compliant license.
- [ ] Security and secret-history review; enable private vulnerability reporting.
- [ ] Publish accurate, reproducible onboarding and migration-version instructions.
- [ ] Enable issue taxonomy, review protections, optional Discussions and maintainer roles.
- [ ] Validate documentation CI and contributor contribution process.

## Engineering directions (proposed; require scoping)

1. Complete documented accounting and permission gaps without inventing financial policy.
2. Strengthen SQL migration verification, long-running integration tests, and reproducible setup.
3. Audit API contract examples, traces, reports, and large-data performance.
4. Extend inventory, sales, purchasing, cash, reports, and other ERP modules only after cross-module dependencies are approved.
5. Explore Flutter web/mobile clients after stable contracts, accessibility and Arabic/RTL design decisions.

## How to help

Pick a task with clear prerequisites and acceptance criteria in [Issues](https://github.com/ameenalqershi/wow-erp/issues). Don't assume an item is ready merely because it appears on this roadmap. New proposals should link to relevant module docs and identify dependencies and tests.

## Release maturity

There is no production-ready release. Milestones and versions will be announced only after documented acceptance and security review.
