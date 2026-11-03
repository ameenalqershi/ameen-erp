using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class InventoryOpeningPrintTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV06-OP04: print saved opening lines and totals before and after source posting without side effects", async f =>
        {
            var (request, approved) = await InventoryOpeningPostingTests.Setup(f, ItemTracking.Lot);
            var beforeAudits = f.Store.Count<AuditEvent>();
            var beforeReceipts = f.Store.Count<IdempotencyRecord>();
            var beforeNumbers = f.Store.All<NumberSequence>().Sum(x => x.LastNumber);
            var beforeOutbox = f.Store.Count<OutboxEvent>();
            var printed = await f.Service.InventoryOpening.Print(f.CompanyId, approved.ResourceId, f.Owner, default);
            var repeated = await f.Service.InventoryOpening.Print(f.CompanyId, approved.ResourceId, f.Owner, default);
            Ensure(printed == repeated || printed.OpeningId == repeated.OpeningId &&
                printed.Revision == repeated.Revision && printed.TotalDocumentValue == repeated.TotalDocumentValue);
            Ensure(printed.OpeningId == approved.ResourceId && printed.Number == approved.Number &&
                printed.CompanyName.Length > 0 && printed.BranchName.Length > 0 &&
                printed.State == nameof(InventoryOpeningState.Approved) &&
                printed.CurrencyCode == "SAR" && printed.ExchangeRate == 1m &&
                printed.DocumentDate == request.DocumentDate &&
                printed.AccountingDate == request.AccountingDate &&
                printed.CutoffDate == request.CutoffDate && printed.CountReference == request.CountReference &&
                printed.JournalId is null && printed.TotalDocumentValue == 10.5m &&
                printed.SignedEffectValue == 10.5m && printed.Lines.Count == 1);
            var line = printed.Lines[0];
            Ensure(line.ItemCode.Length > 0 && line.UnitCode.Length > 0 &&
                line.LotNumber == "ID-1" && line.SerialNumber == null &&
                line.Quantity == 2m && line.BaseQuantity == 2m &&
                line.UnitCost == 5.25m && line.Value == 10.5m && line.SignedEffectValue == 10.5m);
            Ensure(f.Store.Count<AuditEvent>() == beforeAudits &&
                f.Store.Count<IdempotencyRecord>() == beforeReceipts &&
                f.Store.Count<OutboxEvent>() == beforeOutbox &&
                f.Store.All<NumberSequence>().Sum(x => x.LastNumber) == beforeNumbers);
            var posted = await InventoryOpeningPostingTests.Post(f, approved,
                await InventoryOpeningPostingTests.Plan(f, approved));
            printed = await f.Service.InventoryOpening.Print(f.CompanyId, posted.ResourceId, f.Owner, default);
            Ensure(printed.State == nameof(InventoryOpeningState.Posted) &&
                printed.JournalId is not null && printed.PostedBy is not null &&
                printed.PostedAt is not null && printed.TotalDocumentValue == 10.5m &&
                printed.SignedEffectValue == 10.5m && printed.Lines.Count == 1);
        });
        test("INV06-OP04: print requires real tenant and source-branch permission", async f =>
        {
            var (_, source) = await InventoryOpeningPostingTests.Setup(f);
            var foreignReader = new Actor("inv-print-other-branch");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(foreignReader.Subject, new[] { Permissions.Read }, false,
                    new[] { f.OtherBranchId }, null), default);
            await Error("branch_access_denied", () => f.Service.InventoryOpening.Print(f.CompanyId,
                source.ResourceId, foreignReader, default));
            await Error("permission_denied", () => f.Service.InventoryOpening.Print(f.CompanyId,
                source.ResourceId, new Actor("outside-tenant"), default));
            await Error("reference_not_found", () => f.Service.InventoryOpening.Print(f.CompanyId,
                Guid.NewGuid(), f.Owner, default));
            Ensure((await f.Service.InventoryOpening.Print(f.CompanyId,
                source.ResourceId, f.Limited, default)).OpeningId == source.ResourceId);
        });
    }

    private static void Ensure(bool success)
    {
        if (!success) throw new Exception("Inventory opening print snapshot acceptance failed");
    }

    private static async Task Error(string code, Func<Task> work)
    {
        try { await work(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected inventory opening print rejection " + code);
    }
}
