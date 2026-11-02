# GL14 source origin and optimistic revision — partial

Scope: GL14-01 domain-only safeguard, based on `muhaseb-erp-analysis/docs/modules/GL.md` and GL14-F06/F12.

- `JournalDocumentType.MatchesSource` requires an active type with an exact Manual/Automatic origin. Invalid enum values are rejected. This does not validate route/series/tenant or GL03 posting and is **not** an authorization boundary.
- Regression tests verify the already-present expected-revision overloads for edit and disable: stale versions do not alter name, number series, approval route, attachment, status, version, or audit. Valid revisions advance; stale timestamps fail.
- EF still rejects all GL14 writes pending SYS07/SYS09 audited role and route eligibility. SQL 027/028 and all original analysis files remain unchanged.
- Test: `python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet --artifacts backend/.artifacts/gl14-optimistic-revision-20261103`; branch CI with isolated SQL, HTTP/JWT and OpenAPI must pass before merge.
- No Notion task completion; GL14-01 remains in progress. Remaining: audited GL14 API, active SYS09 routes, valid SYS10 series, GL03 integration, reversal separation, negative acceptance and full traceability.

Local verification: 360/360 successful, zero failures, Release + HTTP/JWT + OpenAPI, no live SQL. Await branch CI.
