# SYS07-GAP01 — Fail-closed hydrated rule and snapshot invariants

## Approved scope

`muhaseb-erp-analysis/data/fields.csv` SYS07-F06/F07/F08/F09/F10 specifies screen, operation, explicit allowed flag, decimal(20,6) cap and conditional currency. This patch adds **no new operation codes or permissions** and leaves the original analysis untouched.

## Implementation

- `SecurityRole.HasValidRuleSnapshot`: refuse a loaded rule with a non-canonical code, invalid length, missing/empty currency for a cap, cap without currency, cap on a denial, or a malformed tenant/role reference. Existing duplicate checks remain.
- `SecurityRolePolicyEvaluator.Permits`: materialize the supplied assignment and role enumerables once and refuse any null row rather than throwing or granting on an incomplete snapshot. Existing explicit-deny, branch, tenant and amount policy remains unchanged.
- Regression tests mutate hydrated rules and restore them to prove invalid snapshots deny and valid snapshots still grant. Tests also cover null rows in role and assignment snapshots.

## Acceptance boundary

Pure domain-only safeguards. No database migration, API authorization, role administration, audit write path, runtime grant, SYS09 route activation, GL14 activation or production data change. EF write guards remain enabled. Local verification is not a substitute for isolated SQL CI.
