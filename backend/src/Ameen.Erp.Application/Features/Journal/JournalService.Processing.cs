using Ameen.Erp.Domain;
using System.Globalization;
namespace Ameen.Erp.Application;
public sealed partial class JournalService
{
    private static string ProcessingPermission(string action) => action switch
    {
        "approve" or "return" => Permissions.Approve,
        "post" => Permissions.Post,
        _ => throw new BusinessException("invalid_processing_action", "الإجراء يجب أن يكون approve أو return أو post.")
    };
    private async Task CheckProcessing(JournalEntry journal, Actor actor, string action, string? reason, CancellationToken ct)
    {
        var company = await Company(journal.CompanyId, ct);
        Check.That(company.IsActive, "company_inactive", "الشركة معطلة.", ErrorKind.Conflict);
        Check.That(company.IsProfileComplete, "company_profile_incomplete", "بيانات الشركة غير مكتملة.", ErrorKind.Conflict);
        Check.That(journal.Source != JournalSource.InventoryOpening, "source_workflow_required", "يجب معالجة هذا القيد من مستند مصدره.", ErrorKind.Conflict);
        journal.RequireState(action == "post" ? JournalState.Approved : JournalState.Submitted);
        if (action == "return") { Check.Text(reason, "سبب الإعادة", 500); return; }
        await ValidateForPosting(journal, ct);
        if (action == "approve")
            Check.That(!company.RequireSeparateApprover || journal.CreatedBy != actor.Subject,
                "self_approval_forbidden", "منشئ القيد لا يعتمد قيده.", ErrorKind.Forbidden);
        if (action == "post" && journal.ReversesJournalId is Guid originalId)
        {
            var original = await Required<JournalEntry>(journal.CompanyId, originalId, ct);
            Check.That(original.State == JournalState.Posted && original.ReversesJournalId is null &&
                original.ReversalJournalId is null && original.PendingReversalJournalId == journal.Id,
                "reversal_mismatch", "مصدر العكس أو حجزه غير متوافق.", ErrorKind.Conflict);
        }
    }
    private static JournalSummary Summary(JournalEntry j) => new(j.Id, j.Version, j.Number, j.BranchId, j.AccountingDate,
        j.Description, j.State, j.Lines.Sum(x => x.FunctionalDebit), j.Lines.Sum(x => x.FunctionalCredit), j.Source, j.RecurringTemplateId);
    private async Task<JournalProcessingPreview> PreviewProcessing(JournalEntry journal, Actor actor, string action, string? reason, CancellationToken ct)
    {
        string? error = null;
        try { await CheckProcessing(journal, actor, action, reason, ct); }
        catch (BusinessException ex) { error = ex.Code; }
        return new(Summary(journal), action, error is null, error, clock.UtcNow);
    }
    public async Task<JournalProcessingPreview> ProcessingPreview(Guid cid, Guid id, Actor actor, string action, string? reason, CancellationToken ct)
    {
        var permission = ProcessingPermission(action);
        await AuthorizeJournal(cid, id, actor, Permissions.Read, ct);
        await AuthorizeJournal(cid, id, actor, permission, ct);
        return await PreviewProcessing(await Required<JournalEntry>(cid, id, ct), actor, action, reason, ct);
    }
    public async Task<Page<JournalProcessingPreview>> ProcessingQueue(Guid cid, Actor actor, Guid periodId, Guid? branchId,
        JournalState? state, JournalSource? source, long? sequenceFrom, long? sequenceTo, string action, int offset, int limit, CancellationToken ct, string? numberRange = null)
    {
        Paging(offset, limit); var permission = ProcessingPermission(action);
        var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        await Authorize(cid, actor, permission, branchId, ct);
        var selectedPeriod = await Required<FiscalPeriod>(cid, periodId, ct);
        Check.That(state is null || Enum.IsDefined(state.Value), "invalid_state", "حالة غير معروفة.");
        Check.That(source is null || Enum.IsDefined(source.Value), "invalid_source", "مصدر غير معروف.");
        Check.That((sequenceFrom is null || sequenceFrom > 0) && (sequenceTo is null || sequenceTo > 0) &&
            (sequenceFrom is null || sequenceTo is null || sequenceFrom <= sequenceTo), "invalid_sequence_range", "نطاق أرقام غير صالح.");
        if (numberRange is not null)
        {
            Check.That(sequenceFrom is null && sequenceTo is null, "ambiguous_number_range",
                "لا تجمع بين نطاق الأرقام النصي وحدود التسلسل الرقمية.");
            Check.That(numberRange.Length is > 0 and <= 200, "invalid_journal_number_range",
                "نطاق أرقام القيود غير صالح.");
            var year = await Required<FiscalYear>(cid, selectedPeriod.FiscalYearId, ct);
            var prefix = $"JV-{year.Code}-";
            var parts = numberRange.Split("..", StringSplitOptions.None);
            Check.That(parts.Length is 1 or 2, "invalid_journal_number_range",
                "أدخل رقم قيد واحدًا أو نطاقًا من رقمين مفصولين بـ ..");
            long ParseNumber(string value)
            {
                var part = value.Trim();
                if (part.StartsWith("JV-", StringComparison.Ordinal))
                {
                    Check.That(part.StartsWith(prefix, StringComparison.Ordinal), "invalid_journal_number_range",
                        "رقم القيد لا يخص السنة المالية المختارة.");
                    part = part[prefix.Length..];
                }
                Check.That(part.Length > 0 && long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence > 0,
                    "invalid_journal_number_range", "رقم قيد غير صالح ضمن النطاق.");
                return sequence;
            }
            sequenceFrom = ParseNumber(parts[0]);
            sequenceTo = parts.Length == 2 ? ParseNumber(parts[1]) : sequenceFrom;
            Check.That(sequenceFrom <= sequenceTo, "invalid_journal_number_range",
                "بداية نطاق القيود بعد نهايته.");
        }
        var branches = await AllowedBranches(cid, member, branchId, ct);
        state ??= action == "post" ? JournalState.Approved : JournalState.Submitted;
        var page = await store.ReadPage<JournalEntry>(cid, x => x.FiscalPeriodId == periodId && branches.Contains(x.BranchId) &&
            x.State == state && (source == null || x.Source == source) && (sequenceFrom == null || x.Sequence >= sequenceFrom) &&
            (sequenceTo == null || x.Sequence <= sequenceTo), q => q.OrderBy(x => x.Sequence).ThenBy(x => x.Id), offset, limit, ct);
        var rows = new List<JournalProcessingPreview>();
        foreach (var j in page.Items) rows.Add(await PreviewProcessing(j, actor, action, null, ct));
        return new(rows, page.TotalCount, offset, limit);
    }
    // Each item owns its transaction and durable key. There is deliberately no outer batch transaction.
    public async Task<JournalProcessingResult> ProcessJournals(Guid cid, Actor actor, ProcessJournalsRequest request, CancellationToken ct)
    {
        var permission = ProcessingPermission(request.Action);
        Check.That(request.Items is { Length: >= 1 and <= 100 }, "invalid_processing_count", "اختر من 1 إلى 100 قيد.");
        Check.That(request.Items!.All(x => x is not null && x.JournalId != Guid.Empty && x.ExpectedVersion != Guid.Empty),
            "invalid_processing_item", "معرف القيد وإصداره مطلوبان.");
        var keys = request.Items.Select(x => Check.Text(x.IdempotencyKey, "مفتاح المستند", 100)).ToArray();
        Check.That(keys.Distinct(StringComparer.Ordinal).Count() == keys.Length && request.Items.Select(x => x.JournalId).Distinct().Count() == keys.Length,
            "duplicate_processing_item", "لا تكرر القيد أو مفتاحه داخل الدفعة.");
        await Authorize(cid, actor, permission, null, ct);
        var rows = new List<JournalProcessingRow>();
        foreach (var item in request.Items)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var receipt = await Command(cid, actor, item.IdempotencyKey, "journal.processing." + request.Action + ":" + item.JournalId,
                    new TransitionRequest(item.ExpectedVersion, item.Reason), token => AuthorizeJournal(cid, item.JournalId, actor, permission, token), async token =>
                {
                    var journal = await Required<JournalEntry>(cid, item.JournalId, token);
                    try
                    {
                        Check.Version(journal, item.ExpectedVersion);
                        await CheckProcessing(journal, actor, request.Action, item.Reason, token);
                    }
                    catch (BusinessException ex)
                    {
                        // Only validation before mutation is recorded as a rejected decision.
                        Audit(cid, actor, "journal.processing-rejected", journal,
                            new { Action = request.Action, ExpectedVersion = item.ExpectedVersion, Code = ex.Code, Key = item.IdempotencyKey });
                        return Receipt(journal, "Rejected:" + ex.Code, journal.Number);
                    }
                    var result = await ApplyTransition(cid, journal, actor, new(item.ExpectedVersion, item.Reason), request.Action, token);
                    Audit(cid, actor, "journal.processing-succeeded", journal,
                        new { Action = request.Action, ExpectedVersion = item.ExpectedVersion, Key = item.IdempotencyKey, Result = result });
                    return result;
                }, ct);
                var rejected = receipt.Status.StartsWith("Rejected:", StringComparison.Ordinal);
                rows.Add(new(item.JournalId, !rejected, rejected, receipt, rejected ? receipt.Status[9..] : null));
            }
            catch (BusinessException ex)
            {
                // Authorization/idempotency/storage conflicts expose no journal data and claim no saved rejection.
                rows.Add(new(item.JournalId, false, false, null, ex.Code));
            }
        }
        var passed = rows.Count(x => x.Succeeded);
        return new(request.Action, clock.UtcNow, passed == rows.Count, passed, rows.Count - passed, rows.ToArray());
    }
    public async Task<Page<AuditEvent>> ProcessingHistory(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        await AuthorizeJournal(cid, id, actor, Permissions.Read, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id &&
            (x.Action == "journal.processing-rejected" || x.Action == "journal.processing-succeeded"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
}
