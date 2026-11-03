using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class CashBoxTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH01: complete card, actor/UTC audit, search and usage", async f =>
        {
            var custodian = Custodian(f);
            var receipt = await f.Service.CashBox.CreateCashBox(f.CompanyId, f.Owner, Fixture.Key(),
                Request(f, custodian), default);
            var box = await f.Service.CashBox.GetCashBox(f.CompanyId, receipt.ResourceId, f.Owner, default);
            Ensure(box.CompanyId == f.CompanyId && box.Id == receipt.ResourceId &&
                box.AccountId == f.CashId && box.BranchId == f.BranchId &&
                box.CustodianMembershipId == custodian && box.CurrencyCode == "SAR" &&
                box.BalanceLimit == 500m && box.Name == "صندوق التشغيل" &&
                box.EnglishName == "Cash Box" && box.Notes == "عهدة" && box.IsActive &&
                box.CreatedBy == f.Owner.Subject && box.CreatedAt == new FixedClock().UtcNow &&
                box.LastModifiedAt == box.CreatedAt && box.Revision == 1);
            Ensure((await f.Service.CashBox.SearchCashBoxes(f.CompanyId, f.Owner, "BOX", f.BranchId,
                true, 0, 20, default)).TotalCount == 1);
            var audit = await f.Service.CashBox.ListCashBoxAudit(f.CompanyId, box.Id, f.Owner, 0, 20, default);
            Ensure(audit.TotalCount == 1 && audit.Items.Single().Action == "cashbox.created" &&
                audit.Items.Single().ActorId == f.Owner.Subject);
            Ensure((await f.Service.CashBox.GetCashBoxUsage(f.CompanyId, box.Id, f.Owner, default)).RelatedJournalCount == 0);
            await f.Draft();
            var usage = await f.Service.CashBox.GetCashBoxUsage(f.CompanyId, box.Id, f.Owner, default);
            Ensure(usage.RelatedJournalCount == 1 && usage.HasRelatedJournals);
        });

        test("CASH01: duplicate code and account+branch+currency blocked, separated branch allowed", async f =>
        {
            var custodian = Custodian(f);
            await f.Service.CashBox.CreateCashBox(f.CompanyId, f.Owner, Fixture.Key(), Request(f, custodian), default);
            await Error("duplicate_code", () => f.Service.CashBox.CreateCashBox(f.CompanyId, f.Owner,
                Fixture.Key(), Request(f, custodian) with { AccountId = f.EquityId }, default));
            await Error("cashbox_account_already_linked", () => f.Service.CashBox.CreateCashBox(f.CompanyId,
                f.Owner, Fixture.Key(), Request(f, custodian) with { Code = "SECOND" }, default));
            var other = await f.Service.CashBox.CreateCashBox(f.CompanyId, f.Owner, Fixture.Key(),
                Request(f, custodian) with { Code = "OTHER", BranchId = f.OtherBranchId }, default);
            Ensure((await f.Service.CashBox.GetCashBox(f.CompanyId, other.ResourceId, f.Owner, default)).BranchId
                == f.OtherBranchId && f.Store.Count<CashBox>() == 2);
        });

        test("CASH01: invalid references, account state, custodian scope and decimal scale are atomic", async f =>
        {
            var custodian = Custodian(f);
            await Error("cashbox_reference_required", () => f.Service.CashBox.CreateCashBox(f.CompanyId,
                f.Owner, Fixture.Key(), Request(f, Guid.Empty), default));
            await Error("reference_not_found", () => f.Service.CashBox.CreateCashBox(f.CompanyId,
                f.Owner, Fixture.Key(), Request(f, custodian) with { AccountId = Guid.NewGuid() }, default));
            await Error("reference_not_found", () => f.Service.CashBox.CreateCashBox(f.CompanyId,
                f.Owner, Fixture.Key(), Request(f, custodian) with { BranchId = Guid.NewGuid() }, default));
            await Error("invalid_cashbox_currency", () => f.Service.CashBox.CreateCashBox(f.CompanyId,
                f.Owner, Fixture.Key(), Request(f, custodian) with { CurrencyCode = "EUR" }, default));
            await Error("invalid_amount", () => f.Service.CashBox.CreateCashBox(f.CompanyId,
                f.Owner, Fixture.Key(), Request(f, custodian) with { BalanceLimit = 1.001m }, default));
            await Error("invalid_amount", () => f.Service.CashBox.CreateCashBox(f.CompanyId,
                f.Owner, Fixture.Key(), Request(f, custodian) with { BalanceLimit = -1m }, default));
            var limited = f.Store.All<Membership>().Single(x => x.Subject == f.Limited.Subject);
            await Error("invalid_cashbox_custodian", () => f.Service.CashBox.CreateCashBox(f.CompanyId,
                f.Owner, Fixture.Key(), Request(f, limited.Id) with { BranchId = f.OtherBranchId }, default));
            Ensure(f.Store.Count<CashBox>() == 0 && !f.Store.All<AuditEvent>().Any(x => x.Action.StartsWith("cashbox.")));
        });

        test("CASH01: per-branch read and audit permissions isolate the card", async f =>
        {
            var custodian = Custodian(f);
            var main = await f.Service.CashBox.CreateCashBox(f.CompanyId, f.Owner, Fixture.Key(),
                Request(f, custodian), default);
            var other = await f.Service.CashBox.CreateCashBox(f.CompanyId, f.Owner, Fixture.Key(),
                Request(f, custodian) with { Code = "OTHER", BranchId = f.OtherBranchId }, default);
            Ensure((await f.Service.CashBox.SearchCashBoxes(f.CompanyId, f.Limited, null, null,
                null, 0, 20, default)).TotalCount == 1);
            Ensure((await f.Service.CashBox.GetCashBox(f.CompanyId, main.ResourceId, f.Limited, default)).Id == main.ResourceId);
            await Error("branch_access_denied", () => f.Service.CashBox.GetCashBox(f.CompanyId,
                other.ResourceId, f.Limited, default));
            await Error("branch_access_denied", () => f.Service.CashBox.SearchCashBoxes(f.CompanyId,
                f.Limited, null, f.OtherBranchId, null, 0, 20, default));
            await Error("permission_denied", () => f.Service.CashBox.ListCashBoxAudit(f.CompanyId,
                main.ResourceId, f.Limited, 0, 20, default));
            await Error("permission_denied", () => f.Service.CashBox.CreateCashBox(f.CompanyId,
                f.Limited, Fixture.Key(), Request(f, custodian) with { Code = "DENIED" }, default));
        });

        test("CASH01: edit, stale concurrency, replay and status preserve creator and revision", async f =>
        {
            var custodian = Custodian(f);
            var original = await f.Service.CashBox.CreateCashBox(f.CompanyId, f.Owner, Fixture.Key(),
                Request(f, custodian), default);
            var first = await f.Service.CashBox.GetCashBox(f.CompanyId, original.ResourceId, f.Owner, default);
            var edit = new EditCashBoxRequest(first.Version, "BOX2", "اسم محرر",
                first.AccountId, first.BranchId, first.CustodianMembershipId, first.CurrencyCode,
                10m, "Updated", "edited", "تغيير الاسم");
            var key = Fixture.Key();
            var edited = await f.Service.CashBox.EditCashBox(f.CompanyId, first.Id, f.Owner, key, edit, default);
            Ensure(await f.Service.CashBox.EditCashBox(f.CompanyId, first.Id, f.Owner, key, edit, default)
                == edited);
            var current = await f.Service.CashBox.GetCashBox(f.CompanyId, first.Id, f.Owner, default);
            Ensure(current.CreatedBy == first.CreatedBy && current.CreatedAt == first.CreatedAt &&
                current.Code == "BOX2" && current.Name == "اسم محرر" &&
                current.Revision == 2 && current.BalanceLimit == 10m);
            await Error("version_conflict", () => f.Service.CashBox.EditCashBox(f.CompanyId,
                first.Id, f.Owner, Fixture.Key(), edit, default));
            var status = await f.Service.CashBox.SetCashBoxStatus(f.CompanyId,
                current.Id, f.Owner, Fixture.Key(), new(current.Version, false, "تعطيل للصيانة"), default);
            Ensure(status.Status == "Inactive");
            var inactive = await f.Service.CashBox.GetCashBox(f.CompanyId, current.Id, f.Owner, default);
            Ensure(!inactive.IsActive && inactive.Revision == 3);
            await f.Service.CashBox.SetCashBoxStatus(f.CompanyId,
                current.Id, f.Owner, Fixture.Key(), new(inactive.Version, true, "إعادة تفعيل"), default);
            Ensure((await f.Service.CashBox.ListCashBoxAudit(f.CompanyId, current.Id,
                f.Owner, 0, 20, default)).TotalCount == 4);
        });

        test("CASH01: transaction failure does not leak cashbox, audit or idempotency receipt", async f =>
        {
            var custodian = Custodian(f);
            var count = f.Store.Count<AuditEvent>();
            var receipts = f.Store.Count<IdempotencyRecord>();
            var key = Fixture.Key(); f.Store.FailBeforeCommit = true;
            try
            {
                await f.Service.CashBox.CreateCashBox(f.CompanyId, f.Owner, key,
                    Request(f, custodian), default);
                throw new Exception("Injected failure unexpectedly succeeded");
            }
            catch (IOException) { }
            Ensure(f.Store.Count<CashBox>() == 0 && f.Store.Count<AuditEvent>() == count &&
                f.Store.Count<IdempotencyRecord>() == receipts);
            var created = await f.Service.CashBox.CreateCashBox(f.CompanyId, f.Owner, key,
                Request(f, custodian), default);
            Ensure(f.Store.Count<CashBox>() == 1 && created.ResourceId != Guid.Empty);
        });
    }

    private static Guid Custodian(Fixture f) =>
        f.Store.All<Membership>().Single(x => x.Subject == f.Clerk.Subject).Id;
    private static CreateCashBoxRequest Request(Fixture f, Guid custodian) =>
        new("BOX", "صندوق التشغيل", f.CashId, f.BranchId, custodian, "SAR", 500m,
            "Cash Box", "عهدة");
    private static void Ensure(bool value)
    {
        if (!value) throw new Exception("CASH01 acceptance assertion failed");
    }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected CASH01 error: " + code);
    }
}
