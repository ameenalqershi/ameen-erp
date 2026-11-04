using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class RateAndCloseTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS05: quote selects latest effective date, fixes direction and preserves exact rate", async f =>
        {
            await Currency(f);
            var old = await Rate(f, Fixture.Date.AddDays(-10), 3.123456789m);
            var current = await Rate(f, Fixture.Date, 3.75m); await Rate(f, Fixture.Date.AddDays(10), 4m);
            var quote = await f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Limited, "usd", Fixture.Date.AddDays(-1), default);
            Ensure(quote.ExchangeRateId == old.ResourceId && quote.RateToFunctional == 3.123456789m && quote.FunctionalCurrency == "SAR" && quote.CurrencyCode == "USD");
            Ensure((await f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "USD", Fixture.Date, default)).ExchangeRateId == current.ResourceId);
        });
        test("SYS05-FG03: positive ten-place decimal rate and invalid precision reject atomically", async f =>
        {
            await Currency(f);
            var accepted = await Rate(f, Fixture.Date.AddDays(-1), 1.1234567891m);
            var stored = await f.Service.ExchangeRate.GetExchangeRate(f.CompanyId, accepted.ResourceId, f.Clerk, default);
            Ensure(stored.Rate.RateToFunctional == 1.1234567891m);
            var auditBefore = f.Store.Count<AuditEvent>();
            var recordsBefore = f.Store.Count<ExchangeRate>();
            var receiptBefore = f.Store.Count<IdempotencyRecord>();
            foreach (var invalid in new[] { 0m, -1m, 1.12345678901m, 1_000_000_000m })
                await Error("invalid_exchange_rate", () => f.Service.ExchangeRate.CreateExchangeRate(
                    f.CompanyId, f.Owner, Fixture.Key(),
                    new("USD", Fixture.Date, invalid, "رفض دقة غير صحيحة"), default));
            Ensure(f.Store.Count<ExchangeRate>() == recordsBefore &&
                f.Store.Count<AuditEvent>() == auditBefore &&
                f.Store.Count<IdempotencyRecord>() == receiptBefore);
        });
        test("SYS05-AC02: duplicate same-day exchange rate rejects atomically and original quote remains authoritative", async f =>
        {
            await Currency(f);
            var original = await Rate(f, Fixture.Date, 3.75m);
            var beforeRates = f.Store.Count<ExchangeRate>();
            var beforeAudit = f.Store.Count<AuditEvent>();
            var beforeReceipts = f.Store.Count<IdempotencyRecord>();
            await Error("duplicate_exchange_rate", () => f.Service.ExchangeRate.CreateExchangeRate(
                f.CompanyId, f.Owner, Fixture.Key(),
                new("USD", Fixture.Date, 4.25m, "سعر مكرر"), default));
            Ensure(f.Store.Count<ExchangeRate>() == beforeRates &&
                f.Store.Count<AuditEvent>() == beforeAudit &&
                f.Store.Count<IdempotencyRecord>() == beforeReceipts);
            var quote = await f.Service.ExchangeRate.QuoteExchangeRate(
                f.CompanyId, f.Clerk, "USD", Fixture.Date, default);
            Ensure(quote.ExchangeRateId == original.ResourceId && quote.RateToFunctional == 3.75m);
        });
        test("SYS05: functional quote is one without fabricated rate and missing historical quote fails", async f =>
        {
            var own = await f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "SAR", Fixture.Date, default);
            Ensure(own.IsFunctionalCurrency && own.ExchangeRateId is null && own.EffectiveDate is null && own.RateToFunctional == 1m);
            await Currency(f); await Rate(f, Fixture.Date.AddDays(1), 4m);
            await Error("exchange_rate_required", () => f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "USD", Fixture.Date, default));
        });
        test("SYS05: historical search and detail remain readable for disabled currency while quote rejects it", async f =>
        {
            var currency = await Currency(f); var rate = await Rate(f, Fixture.Date, 3.75m);
            await f.Service.Currency.SetCurrencyStatus(f.CompanyId, currency.ResourceId, f.Owner, Fixture.Key(), new(currency.Version, false, "تعطيل"), default);
            await Error("currency_inactive", () => f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "USD", Fixture.Date, default));
            Ensure((await f.Service.ExchangeRate.GetExchangeRate(f.CompanyId, rate.ResourceId, f.Clerk, default)).Rate.RateToFunctional == 3.75m);
            Ensure((await f.Service.ExchangeRate.SearchExchangeRates(f.CompanyId, f.Clerk, "USD", Fixture.Date, Fixture.Date, "مصدر", 0, 10, default)).TotalCount == 1);
        });
        test("SYS05: history filters pagination and creation audit retain source", async f =>
        {
            await Currency(f); var first = await Rate(f, Fixture.Date.AddDays(-1), 3m); await Rate(f, Fixture.Date, 4m);
            var page = await f.Service.ExchangeRate.SearchExchangeRates(f.CompanyId, f.Clerk, "USD", null, Fixture.Date, "مصدر", 1, 1, default);
            Ensure(page.TotalCount == 2 && page.Items[0].Id == first.ResourceId);
            var audit = await f.Service.ExchangeRate.ListExchangeRateAudit(f.CompanyId, first.ResourceId, f.Owner, 0, 10, default);
            Ensure(audit.TotalCount == 1 && audit.Items[0].Details.Contains("source", StringComparison.Ordinal));
            await Error("invalid_date_range", () => f.Service.ExchangeRate.SearchExchangeRates(f.CompanyId, f.Clerk, null, Fixture.Date.AddDays(1), Fixture.Date, null, 0, 10, default));
        });
        test("SYS05: usage includes cancelled and reversal journals once per document", async f =>
        {
            await Currency(f); var rate = await Rate(f, Fixture.Date, 3.75m); var request = Request(f, rate.ResourceId);
            var cancelled = await f.Draft(request); await f.Action(cancelled, "cancel", reason: "إلغاء");
            var posted = await f.Post(await f.Draft(request));
            await f.Service.Journal.ReverseJournal(f.CompanyId, posted.ResourceId, f.Clerk, Fixture.Key(), new(posted.Version, Fixture.Date, "عكس"), default);
            var usage = await f.Service.ExchangeRate.GetExchangeRateUsage(f.CompanyId, rate.ResourceId, f.Owner, 0, 2, default);
            Ensure(usage.TotalCount == 3 && usage.Items.Count == 2);
            await Error("all_branches_required", () => f.Service.ExchangeRate.GetExchangeRateUsage(f.CompanyId, rate.ResourceId, f.Limited, 0, 10, default));
            await Error("permission_denied", () => f.Service.ExchangeRate.ListExchangeRateAudit(f.CompanyId, rate.ResourceId, f.Clerk, 0, 10, default));
        });
        test("SYS05-GAP03: quote never reserves rate and a newer effective rate invalidates an old quote atomically", async f =>
        {
            await Currency(f);
            var historic = await Rate(f, Fixture.Date.AddDays(-1), 3m);
            var auditsBeforeQuote = f.Store.Count<AuditEvent>();
            var receiptsBeforeQuote = f.Store.Count<IdempotencyRecord>();
            var sequencesBeforeQuote = f.Store.Count<NumberSequence>();
            var allocationsBeforeQuote = f.Store.Count<DocumentNumberAllocation>();
            var journalsBeforeQuote = f.Store.Count<JournalEntry>();

            for (var i = 0; i < 3; i++)
            {
                var quote = await f.Service.ExchangeRate.QuoteExchangeRate(
                    f.CompanyId, f.Clerk, "USD", Fixture.Date, default);
                Ensure(quote.ExchangeRateId == historic.ResourceId && quote.RateToFunctional == 3m &&
                    quote.CurrencyCode == "USD" && quote.FunctionalCurrency == "SAR");
            }
            Ensure(f.Store.Count<AuditEvent>() == auditsBeforeQuote &&
                f.Store.Count<IdempotencyRecord>() == receiptsBeforeQuote &&
                f.Store.Count<NumberSequence>() == sequencesBeforeQuote &&
                f.Store.Count<DocumentNumberAllocation>() == allocationsBeforeQuote &&
                f.Store.Count<JournalEntry>() == journalsBeforeQuote);

            var latest = await Rate(f, Fixture.Date, 3.75m);
            var refreshed = await f.Service.ExchangeRate.QuoteExchangeRate(
                f.CompanyId, f.Clerk, "USD", Fixture.Date, default);
            Ensure(refreshed.ExchangeRateId == latest.ResourceId && refreshed.RateToFunctional == 3.75m);
            var auditAfterNewRate = f.Store.Count<AuditEvent>();
            var receiptsAfterNewRate = f.Store.Count<IdempotencyRecord>();
            await Error("stale_exchange_rate", () => f.Draft(Request(f, historic.ResourceId)));
            Ensure(f.Store.Count<JournalEntry>() == journalsBeforeQuote &&
                f.Store.Count<NumberSequence>() == sequencesBeforeQuote &&
                f.Store.Count<DocumentNumberAllocation>() == allocationsBeforeQuote &&
                f.Store.Count<AuditEvent>() == auditAfterNewRate &&
                f.Store.Count<IdempotencyRecord>() == receiptsAfterNewRate);

            var accepted = await f.Draft(Request(f, latest.ResourceId));
            var journal = await f.Journal(accepted.ResourceId);
            Ensure(journal.Lines.Any(x => x.ExchangeRateId == latest.ResourceId && x.ExchangeRate == 3.75m) &&
                !journal.Lines.Any(x => x.ExchangeRateId == historic.ResourceId));
        });
        test("SYS05: quote is advisory and newer rate requires refreshed draft choice", async f =>
        {
            await Currency(f); await Rate(f, Fixture.Date.AddDays(-1), 3m);
            var quote = await f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "USD", Fixture.Date, default);
            var newer = await Rate(f, Fixture.Date, 3.75m);
            await Error("stale_exchange_rate", () => f.Draft(Request(f, quote.ExchangeRateId!.Value)));
            await f.Draft(Request(f, newer.ResourceId));
        });
        test("SYS05: detail and quote cannot read another company", async f =>
        {
            var cid = Guid.NewGuid(); await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(cid, "OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default);
            await f.Service.Currency.CreateCurrency(cid, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
            var r = await f.Service.ExchangeRate.CreateExchangeRate(cid, f.Owner, Fixture.Key(), new("USD", Fixture.Date, 4m, "مصدر"), default);
            await Error("reference_not_found", () => f.Service.ExchangeRate.GetExchangeRate(f.CompanyId, r.ResourceId, f.Owner, default));
            await Error("permission_denied", () => f.Service.ExchangeRate.QuoteExchangeRate(cid, f.Clerk, "USD", Fixture.Date, default));
        });
        test("GL11-A05: execution result and immutable close/reopen history are paginated and audit-scoped", async f =>
        {
            // Fixture owner has Audit but not necessarily PeriodClose; grant
            // a dedicated actor the exact conjunction the query requires.
            var reviewer = new Actor("gl11-execution-auditor");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(reviewer.Subject, new[] { Permissions.Read, Permissions.Audit,
                    Permissions.PeriodClose }, true, Array.Empty<Guid>(), null), default);
            var period = Period(f);
            var empty = await f.Service.PeriodClose.PeriodExecutionHistory(f.CompanyId,
                period.Id, reviewer, 0, 10, default);
            Ensure(empty.PeriodId == period.Id && empty.PeriodCode == period.Code &&
                !empty.IsClosed && empty.History.TotalCount == 0 &&
                empty.History.Items.Count == 0);

            var close = await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id,
                f.Approver, Fixture.Key(), new(period.Version, "إقفال اختبار السجل"), true, default);
            var first = await f.Service.PeriodClose.PeriodExecutionHistory(f.CompanyId,
                period.Id, reviewer, 0, 1, default);
            Ensure(first.IsClosed && first.Version == close.Version &&
                first.History.TotalCount == 1 && first.History.Items.Count == 1 &&
                first.History.Items[0].Action == "period.closed" &&
                first.History.Items[0].ActorId == f.Approver.Subject &&
                System.Text.Json.JsonDocument.Parse(first.History.Items[0].Details).RootElement.GetProperty("reason").GetString() == "إقفال اختبار السجل");
            var reopened = await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id,
                f.Approver, Fixture.Key(), new(close.Version, "إعادة فتح اختبار السجل"), false, default);
            var firstPage = await f.Service.PeriodClose.PeriodExecutionHistory(f.CompanyId,
                period.Id, reviewer, 0, 1, default);
            var secondPage = await f.Service.PeriodClose.PeriodExecutionHistory(f.CompanyId,
                period.Id, reviewer, 1, 1, default);
            Ensure(!firstPage.IsClosed && firstPage.Version == reopened.Version &&
                firstPage.History.TotalCount == 2 && secondPage.History.TotalCount == 2 &&
                firstPage.History.Items.Count == 1 && secondPage.History.Items.Count == 1 &&
                firstPage.History.Items[0].Id != secondPage.History.Items[0].Id &&
                new[] { firstPage.History.Items[0].Action, secondPage.History.Items[0].Action }
                    .OrderBy(x => x).SequenceEqual(new[] { "period.closed", "period.reopened" }) &&
                // FixedClock gives both events the same instant: the stable ID
                // tie-breaker is not their chronological order.
                System.Text.Json.JsonDocument.Parse(
                    new[] { firstPage.History.Items[0], secondPage.History.Items[0] }
                    .Single(x => x.Action == "period.reopened").Details)
                    .RootElement.GetProperty("reason").GetString() == "إعادة فتح اختبار السجل");

            var reader = new Actor("gl11-close-reader-no-audit");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(reader.Subject, new[] { Permissions.Read, Permissions.PeriodClose }, true,
                    Array.Empty<Guid>(), null), default);
            await Error("permission_denied", () => f.Service.PeriodClose.PeriodExecutionHistory(
                f.CompanyId, period.Id, reader, 0, 10, default));
            await Error("permission_denied", () => f.Service.PeriodClose.PeriodExecutionHistory(
                f.CompanyId, period.Id, new Actor("gl11-outsider"), 0, 10, default));

            var auditCount = f.Store.Count<AuditEvent>();
            var receipts = f.Store.Count<IdempotencyRecord>();
            var journals = f.Store.Count<JournalEntry>();
            var outbox = f.Store.Count<OutboxEvent>();
            var numbers = f.Store.All<NumberSequence>().Sum(x => x.LastNumber);
            var emptyPage = await f.Service.PeriodClose.PeriodExecutionHistory(f.CompanyId,
                period.Id, reviewer, 2, 1, default);
            Ensure(emptyPage.History.TotalCount == 2 && emptyPage.History.Items.Count == 0 &&
                f.Store.Count<AuditEvent>() == auditCount &&
                f.Store.Count<IdempotencyRecord>() == receipts &&
                f.Store.Count<JournalEntry>() == journals &&
                f.Store.Count<OutboxEvent>() == outbox &&
                f.Store.All<NumberSequence>().Sum(x => x.LastNumber) == numbers);
        });
        test("GL12-A05: reopening decisions are independently authorized, paged and read-only", async f =>
        {
            var period = Period(f);
            var reader = new Actor("gl12-reopen-history-reader");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(reader.Subject, new[] { Permissions.Read, Permissions.Audit,
                    Permissions.PeriodReopen }, true, Array.Empty<Guid>(), null), default);
            var before = await f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                period.Id, reader, 0, 10, default);
            Ensure(!before.IsClosed && before.PeriodId == period.Id &&
                before.History.TotalCount == 0);

            var closed = await f.Service.PeriodClose.ChangePeriod(f.CompanyId,
                period.Id, f.Approver, Fixture.Key(),
                new(period.Version, "GL12-إغلاق أول"), true, default);
            var reopening = await f.Service.PeriodClose.ChangePeriod(f.CompanyId,
                period.Id, f.Approver, Fixture.Key(),
                new(closed.Version, "GL12-إعادة فتح أولى"), false, default);
            var first = await f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                period.Id, reader, 0, 10, default);
            Ensure(!first.IsClosed && first.Version == reopening.Version &&
                first.History.TotalCount == 1 && first.History.Items.Count == 1 &&
                first.History.Items[0].Action == "period.reopened" &&
                first.History.Items[0].ActorId == f.Approver.Subject &&
                System.Text.Json.JsonDocument.Parse(first.History.Items[0].Details)
                    .RootElement.GetProperty("reason").GetString() == "GL12-إعادة فتح أولى");

            var closedAgain = await f.Service.PeriodClose.ChangePeriod(f.CompanyId,
                period.Id, f.Approver, Fixture.Key(),
                new(reopening.Version, "GL12-إغلاق ثان"), true, default);
            var reopenedAgain = await f.Service.PeriodClose.ChangePeriod(f.CompanyId,
                period.Id, f.Approver, Fixture.Key(),
                new(closedAgain.Version, "GL12-إعادة فتح ثانية"), false, default);
            var a = await f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                period.Id, reader, 0, 1, default);
            var b = await f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                period.Id, reader, 1, 1, default);
            var empty = await f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                period.Id, reader, 2, 1, default);
            Ensure(!a.IsClosed && a.Version == reopenedAgain.Version &&
                a.History.TotalCount == 2 && b.History.TotalCount == 2 &&
                a.History.Items.Count == 1 && b.History.Items.Count == 1 &&
                a.History.Items[0].Id != b.History.Items[0].Id &&
                a.History.Items[0].Action == "period.reopened" &&
                b.History.Items[0].Action == "period.reopened" &&
                new[] { a.History.Items[0], b.History.Items[0] }
                    .Select(x => System.Text.Json.JsonDocument.Parse(x.Details)
                        .RootElement.GetProperty("reason").GetString())
                    .OrderBy(x => x).SequenceEqual(
                        new[] { "GL12-إعادة فتح أولى", "GL12-إعادة فتح ثانية" }.OrderBy(x => x)) &&
                empty.History.TotalCount == 2 && empty.History.Items.Count == 0);

            var closer = new Actor("gl12-close-auditor-no-reopen");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(closer.Subject, new[] { Permissions.Read, Permissions.Audit,
                    Permissions.PeriodClose }, true, Array.Empty<Guid>(), null), default);
            await Error("permission_denied", () =>
                f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                    period.Id, closer, 0, 10, default));
            var noAudit = new Actor("gl12-reopen-no-audit");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(noAudit.Subject, new[] { Permissions.Read, Permissions.PeriodReopen },
                    true, Array.Empty<Guid>(), null), default);
            await Error("permission_denied", () =>
                f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                    period.Id, noAudit, 0, 10, default));
            var limited = new Actor("gl12-reopen-branch-only");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(limited.Subject, new[] { Permissions.Read, Permissions.Audit,
                    Permissions.PeriodReopen }, false, new[] { f.BranchId }, null), default);
            await Error("all_branches_required", () =>
                f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                    period.Id, limited, 0, 10, default));
            await Error("permission_denied", () =>
                f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                    period.Id, new Actor("gl12-nonmember"), 0, 10, default));

            var audits = f.Store.Count<AuditEvent>();
            var receipts = f.Store.Count<IdempotencyRecord>();
            var journals = f.Store.Count<JournalEntry>();
            var outbox = f.Store.Count<OutboxEvent>();
            var numbers = f.Store.All<NumberSequence>().Sum(x => x.LastNumber);
            await f.Service.PeriodClose.ReopenExecutionHistory(f.CompanyId,
                period.Id, reader, 0, 10, default);
            Ensure(f.Store.Count<AuditEvent>() == audits &&
                f.Store.Count<IdempotencyRecord>() == receipts &&
                f.Store.Count<JournalEntry>() == journals &&
                f.Store.Count<OutboxEvent>() == outbox &&
                f.Store.All<NumberSequence>().Sum(x => x.LastNumber) == numbers);
        });
        test("GL11/GL12: fully authorized foreign-company reviewer cannot inspect another tenant period", async f =>
        {
            // The foreign reader deliberately has ALL required permissions in
            // the *requested* company. The guard must reject the period ID as
            // not found, not merely deny a missing membership before lookup.
            var period = Period(f);
            var close = await f.Service.PeriodClose.ChangePeriod(
                f.CompanyId, period.Id, f.Approver, Fixture.Key(),
                new(period.Version, "إقفال خاص بشركة المصدر"), true, default);
            await f.Service.PeriodClose.ChangePeriod(
                f.CompanyId, period.Id, f.Approver, Fixture.Key(),
                new(close.Version, "فتح خاص بشركة المصدر"), false, default);

            var other = Guid.NewGuid();
            await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(),
                new(other, "FOREIGNGL", "شركة اختبار منفصلة", "YE", "SAR", 2,
                    Fixture.Profile()), default);
            var foreignReader = new Actor("gl-foreign-company-period-reader");
            await f.Service.Membership.GrantMembership(other, f.Owner,
                Fixture.Key(), new(foreignReader.Subject, new[] {
                    Permissions.Read, Permissions.Audit,
                    Permissions.PeriodClose, Permissions.PeriodReopen
                }, true, Array.Empty<Guid>(), null), default);

            var auditBefore = f.Store.Count<AuditEvent>();
            var receiptBefore = f.Store.Count<IdempotencyRecord>();
            var journalBefore = f.Store.Count<JournalEntry>();
            var outboxBefore = f.Store.Count<OutboxEvent>();
            var versionBefore = Period(f).Version;
            await Error("reference_not_found", () =>
                f.Service.PeriodClose.PreviewPeriodClose(other,
                    period.Id, foreignReader, 0, 10, default));
            await Error("reference_not_found", () =>
                f.Service.PeriodClose.PeriodExecutionHistory(other,
                    period.Id, foreignReader, 0, 10, default));
            await Error("reference_not_found", () =>
                f.Service.PeriodClose.ReopenExecutionHistory(other,
                    period.Id, foreignReader, 0, 10, default));
            Ensure(f.Store.Count<AuditEvent>() == auditBefore &&
                f.Store.Count<IdempotencyRecord>() == receiptBefore &&
                f.Store.Count<JournalEntry>() == journalBefore &&
                f.Store.Count<OutboxEvent>() == outboxBefore &&
                Period(f).Version == versionBefore && !Period(f).IsClosed);
        });
        test("GL11: preview lists pending journals with accurate paginated blockers without writes", async f =>
        {
            await f.Draft(); await f.Draft(); var audit = f.Store.Count<AuditEvent>(); var period = Period(f);
            var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, period.Id, f.Approver, 1, 1, default);
            Ensure(!preview.CanCloseGeneralLedger && preview.GeneralLedgerOnly && preview.Issues.TotalCount == 2 && preview.Issues.Items.Count == 1 && preview.ExpectedVersion == period.Version);
            Ensure(f.Store.Count<AuditEvent>() == audit && !Period(f).IsClosed);
            await Error("unposted_journals", () => f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default));
        });
        test("GL11: posted/cancelled documents pass preview and close then closed preview blocks", async f =>
        {
            await f.Post(); await f.Action(await f.Draft(), "cancel", reason: "إلغاء"); var p = Period(f);
            var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Approver, 0, 10, default);
            Ensure(preview.CanCloseGeneralLedger && preview.PostedJournalCount == 1 && preview.UnpostedJournalCount == 0 && preview.Issues.TotalCount == 0);
            await f.Service.PeriodClose.ChangePeriod(f.CompanyId, p.Id, f.Approver, Fixture.Key(), new(preview.ExpectedVersion, "إقفال"), true, default);
            Ensure(!(await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Approver, 0, 10, default)).CanCloseGeneralLedger);
        });
        test("GL11: preview does not reserve period and close rechecks newly created drafts", async f =>
        {
            var p = Period(f); var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Approver, 0, 10, default);
            Ensure(preview.CanCloseGeneralLedger); await f.Draft();
            await Error("unposted_journals", () => f.Service.PeriodClose.ChangePeriod(f.CompanyId, p.Id, f.Approver, Fixture.Key(), new(preview.ExpectedVersion, "إقفال"), true, default));
        });
        test("GL11: preview requires close permission and all-branch read scope", async f =>
        {
            var p = Period(f);
            await Error("permission_denied", () => f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Owner, 0, 10, default));
            var member = f.Store.All<Membership>().Single(x => x.Subject == f.Limited.Subject);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(f.Limited.Subject, new[] { Permissions.Read, Permissions.PeriodClose }, false, new[] { f.BranchId }, member.Version), default);
            await Error("all_branches_required", () => f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Limited, 0, 10, default));
        });
        test("GL11: inactive company fails preview operational checks without hiding financial history", async f =>
        {
            var company = await f.Service.Company.GetCompany(f.CompanyId, f.Owner, default);
            await f.Service.Company.SetCompanyStatus(f.CompanyId, f.Owner, Fixture.Key(), new(company.Version, false, "إيقاف"), default);
            var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, Period(f).Id, f.Approver, 0, 10, default);
            Ensure(!preview.CanCloseGeneralLedger && !preview.Checks.Single(x => x.Code == "company_active").Passed);
        });
    }
    private static FiscalPeriod Period(Fixture f) => f.Store.All<FiscalPeriod>().Single(x => x.StartsOn <= Fixture.Date && x.EndsOn >= Fixture.Date);
    private static Task<CommandReceipt> Currency(Fixture f) => f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
    private static Task<CommandReceipt> Rate(Fixture f, DateOnly date, decimal rate) => f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", date, rate, "مصدر الاختبار"), default);
    private static CreateJournalRequest Request(Fixture f, Guid rate)
    {
        var r = f.Request(100, 375); r.Lines[0] = r.Lines[0] with { CurrencyCode = "USD", ExchangeRateId = rate }; return r;
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Rate/close assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
