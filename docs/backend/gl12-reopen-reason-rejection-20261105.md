# GL12-FG02 — mandatory reopen reason rejection (partial acceptance)

Source: original GL12 screen in `muhaseb-erp-analysis/docs/modules/GL.md` and `fields.csv` GL12-F05 reason (mandatory), GL12-F06 approval reference, GL12-F07 permission end.

Bounded test-only acceptance of the existing `POST /periods/{id}/reopen` route, checking blank, whitespace, over-500-character and control-character reason. Accepted positive reopening with the original closed-period version establishes that rejection did not silently reopen the period; both memory-domain and production-host HTTPS/JWT/SQL checks ensure no rejected audit/receipt/journal/outbox mutation. No production code, policy, API schema, or database migration changed.

**GL12-FG02 is not closed:** approval reference and expiry remain wholly unimplemented/unapproved. The broad GL12 year-end/tax/subledger dependency checks are not inferred; full authorization, independent approval and evidence remain required. The existing GeneralLedgerOnly reopening test is not a production authorisation for statutory reopening.
