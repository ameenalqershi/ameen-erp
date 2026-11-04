# SAL12 — collection follow-up and credit exception request structure (partial)

Reference: `muhaseb-erp-analysis/docs/modules/SAL.md` SAL12 and Notion SAL12-01 / SAL12-AC02.

Structural domain `CustomerCollectionFollowUp` models optional branch, opaque system status, required customer/date/responsible reference, optional promised payment date and optional requested override amount and conditional reason, plus audit fields. It checks nonzero required IDs, valid date values, positive representable request amounts and reason presence if request exists. No credit exposure policy, exception duration, approval workflow or receivables calculation has been guessed.

New tests cover exact source field fidelity, optional data, error codes and no accounting/outbox effects. No SQL/EF/API/JWT/OpenAPI or production data writes. Important source gap: the screen says exceptions require both amount and duration, but no duration field is defined in the SAL12 header. Do not activate exceptions until the authorized policy/field contract is resolved.

Operational dependencies: SAL01 customer management, SAL11 receivables, SYS authorization and role limits, order obligations and approved credit exposure rules. Treat SAL12-01 and SAL12-AC02 as **PARTIAL**, not DONE, until source integrations plus acceptance testing on isolated SQL/HTTP/JWT and SUPERVISOR review/merge. Original analysis unchanged.
