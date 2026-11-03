using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class UnitTests
{
    public static CreateUnitRequest Request() => new("001", "حبة", "EA", 0, "وحدة منفردة", "Each", "ملاحظات");
    public static EditUnitRequest Edit(UnitOfMeasure x) => new(x.Version, x.Code, x.Name, x.StandardCode, x.QuantityScale, x.Description, x.EnglishName, x.Notes, "تحديث الوحدة");
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV03-FG01: generated identity tenant header and required normalized fields", async f =>
        {
            var created = await Create(f, Request() with { Code = " ea-01 ", Name = "  حبة  " });
            var unit = await Get(f, created.ResourceId);
            Ensure(unit.Id == created.ResourceId && unit.Id != Guid.Empty &&
                unit.CompanyId == f.CompanyId && unit.Code == "EA-01" && unit.Name == "حبة");
            await Error("invalid_text", () => Create(f, Request() with { Name = "  " }));
            await Error("invalid_text", () => Create(f, Request() with { Code = "" }));
            await Error("invalid_code", () => Create(f, Request() with { Code = "invalid code" }));
            await Error("duplicate_unit_code", () => Create(f, Request() with { Code = "ea-01" }));
            Ensure((await f.Service.Unit.SearchUnits(f.CompanyId, f.Limited, "EA-01", true, 0, 10, default)).TotalCount == 1);
            Ensure(f.Store.Count<UnitOfMeasure>() == 1);
        });
        test("INV03-FG02: optional fields and status survive edits without silent defaults", async f =>
        {
            var created = await Create(f, Request() with { EnglishName = null, Notes = null, StandardCode = "ea" });
            var unit = await Get(f, created.ResourceId);
            Ensure(unit.EnglishName is null && unit.Notes is null && unit.StandardCode == "EA" && unit.IsActive);
            await Error("invalid_code", () => Create(f, Request() with { StandardCode = "bad code" }));
            await Error("invalid_text", () => Create(f, Request() with { Notes = new string('n', 2001) }));
            var edited = await f.Service.Unit.EditUnit(f.CompanyId, unit.Id, f.Owner, Fixture.Key(),
                Edit(unit) with { EnglishName = "Each", Notes = "label", Name = "قطعة" }, default);
            unit = await Get(f, unit.Id);
            Ensure(unit.EnglishName == "Each" && unit.Notes == "label" && unit.Name == "قطعة");
            await f.Service.Unit.SetUnitStatus(f.CompanyId, unit.Id, f.Owner, Fixture.Key(), new(edited.Version, false, "إيقاف"), default);
            unit = await Get(f, unit.Id);
            Ensure(!unit.IsActive && (await f.Service.Unit.SearchUnits(f.CompanyId, f.Limited, null, false, 0, 10, default)).TotalCount == 1);
        });
        test("INV03-FG03: scale limits and required description persist without global factor", async f =>
        {
            var low = await Create(f, Request() with { Code = "SCALE0", QuantityScale = 0, Description = "صفر" });
            var high = await Create(f, Request() with { Code = "SCALE6", QuantityScale = 6, Description = "ست" });
            Ensure((await Get(f, low.ResourceId)).Description == "صفر" &&
                (await Get(f, high.ResourceId)).QuantityScale == 6);
            await Error("invalid_quantity_scale", () => Create(f, Request() with { QuantityScale = -1 }));
            await Error("invalid_quantity_scale", () => Create(f, Request() with { QuantityScale = 7 }));
            await Error("invalid_text", () => Create(f, Request() with { Description = "" }));
            var unit = await Get(f, high.ResourceId);
            await Error("invalid_text", () => f.Service.Unit.EditUnit(f.CompanyId, unit.Id, f.Owner, Fixture.Key(),
                Edit(unit) with { Description = new string('d', 501) }, default));
            Ensure((await Get(f, high.ResourceId)).Version == high.Version);
        });
        test("INV03-FG04: audit actor timestamps revision and failed command silence", async f =>
        {
            var created = await Create(f); var unit = await Get(f, created.ResourceId);
            var createdEvents = await f.Service.Unit.ListUnitAudit(f.CompanyId, unit.Id, f.Owner, 0, 10, default);
            Ensure(createdEvents.TotalCount == 1 && createdEvents.Items[0].ActorId == f.Owner.Subject &&
                createdEvents.Items[0].Action == "unit.created" && createdEvents.Items[0].OccurredAt != default);
            using (var snapshot = JsonDocument.Parse(createdEvents.Items[0].Details))
                Ensure(snapshot.RootElement.GetProperty("after").GetProperty("revision").GetInt32() == 1);
            await Error("version_conflict", () => f.Service.Unit.EditUnit(f.CompanyId, unit.Id, f.Owner, Fixture.Key(),
                Edit(unit) with { ExpectedVersion = Guid.NewGuid() }, default));
            Ensure((await f.Service.Unit.ListUnitAudit(f.CompanyId, unit.Id, f.Owner, 0, 10, default)).TotalCount == 1);
            var changed = await f.Service.Unit.EditUnit(f.CompanyId, unit.Id, f.Owner, Fixture.Key(),
                Edit(unit) with { Description = "النسخة الثانية" }, default);
            var events = await f.Service.Unit.ListUnitAudit(f.CompanyId, unit.Id, f.Owner, 0, 10, default);
            Ensure((await Get(f, unit.Id)).Revision == 2 && changed.Version != created.Version &&
                events.TotalCount == 2 && events.Items.All(x => x.ActorId == f.Owner.Subject && x.OccurredAt != default));
            using var detail = JsonDocument.Parse(events.Items.Single(x => x.Action == "unit.edited").Details);
            Ensure(detail.RootElement.GetProperty("before").GetProperty("revision").GetInt32() == 1 &&
                detail.RootElement.GetProperty("after").GetProperty("revision").GetInt32() == 2);
        });
        test("INV03: bilingual unit with standard code and integer revision roundtrips", async f =>
        {
            var r = await Create(f); var unit = await Get(f, r.ResourceId);
            Ensure(unit.Code == "001" && unit.StandardCode == "EA" && unit.QuantityScale == 0 && unit.EnglishName == "Each" && unit.IsActive && unit.Revision == 1);
            Ensure((await f.Service.Unit.SearchUnits(f.CompanyId, f.Limited, "Each", true, 0, 10, default)).TotalCount == 1);
            Ensure((await f.Service.Unit.ListUnitAudit(f.CompanyId, unit.Id, f.Owner, 0, 10, default)).TotalCount == 1);
        });
        test("INV03: quantity precision is explicit and validated without a global conversion", async f =>
        {
            foreach (var scale in new[] { -1, 7 })
                await Error("invalid_quantity_scale", () => Create(f, Request() with { QuantityScale = scale }));
            await Create(f, Request() with { QuantityScale = 6 });
            var json = JsonSerializer.Serialize(Request(), JsonDefaults.Options).TrimEnd('}') + ",\"conversionFactor\":12}";
            try { JsonSerializer.Deserialize<CreateUnitRequest>(json, JsonDefaults.Options); throw new Exception("Global conversion accepted"); } catch (JsonException) { }
        });
        test("INV03: required description and standard code reject invalid input", async f =>
        {
            await Error("invalid_text", () => Create(f, Request() with { Description = " " }));
            await Error("invalid_code", () => Create(f, Request() with { StandardCode = "bad code" }));
            await Error("invalid_text", () => Create(f, Request() with { EnglishName = new string('x', 201) }));
            Ensure(f.Store.Count<UnitOfMeasure>() == 0);
        });
        test("INV03: unit code is unique per company and cannot collide during edit", async f =>
        {
            var first = await Create(f); await Error("duplicate_unit_code", () => Create(f));
            var second = await Create(f, Request() with { Code = "002" }); var unit = await Get(f, second.ResourceId);
            await Error("duplicate_unit_code", () => f.Service.Unit.EditUnit(f.CompanyId, unit.Id, f.Owner, Fixture.Key(), Edit(unit) with { Code = "001" }, default));
            Ensure((await Get(f, first.ResourceId)).Revision == 1);
        });
        test("INV03: edit and status preserve audit snapshots and explicit optional nulls", async f =>
        {
            var created = await Create(f); var unit = await Get(f, created.ResourceId);
            var edited = await f.Service.Unit.EditUnit(f.CompanyId, unit.Id, f.Owner, Fixture.Key(), Edit(unit) with { Name = "قطعة", EnglishName = null, Notes = null }, default);
            await f.Service.Unit.SetUnitStatus(f.CompanyId, unit.Id, f.Owner, Fixture.Key(), new(edited.Version, false, "إيقاف"), default);
            unit = await Get(f, unit.Id); Ensure(unit.Revision == 3 && !unit.IsActive && unit.EnglishName is null && unit.Notes is null);
            Ensure((await f.Service.Unit.SearchUnits(f.CompanyId, f.Owner, null, true, 0, 10, default)).TotalCount == 0);
            Ensure((await f.Service.Unit.ListUnitAudit(f.CompanyId, unit.Id, f.Owner, 0, 10, default)).TotalCount == 3);
        });
        test("INV03: create and edit replay cannot double-advance revision", async f =>
        {
            var key = Fixture.Key(); var created = await f.Service.Unit.CreateUnit(f.CompanyId, f.Owner, key, Request(), default);
            Ensure(await f.Service.Unit.CreateUnit(f.CompanyId, f.Owner, key, Request(), default) == created);
            var edit = Edit(await Get(f, created.ResourceId)) with { QuantityScale = 3 }; key = Fixture.Key();
            var changed = await f.Service.Unit.EditUnit(f.CompanyId, created.ResourceId, f.Owner, key, edit, default);
            Ensure(await f.Service.Unit.EditUnit(f.CompanyId, created.ResourceId, f.Owner, key, edit, default) == changed);
            await Error("version_conflict", () => f.Service.Unit.EditUnit(f.CompanyId, created.ResourceId, f.Owner, Fixture.Key(), edit, default));
            Ensure((await Get(f, created.ResourceId)).Revision == 2);
        });
        test("INV03: failed persistence rolls back unit revision audit and receipt", async f =>
        {
            var r = await Create(f); var request = Edit(await Get(f, r.ResourceId)) with { Description = "جديد" }; var key = Fixture.Key();
            var audits = f.Store.Count<AuditEvent>(); f.Store.FailBeforeCommit = true;
            try { await f.Service.Unit.EditUnit(f.CompanyId, r.ResourceId, f.Owner, key, request, default); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure((await Get(f, r.ResourceId)).Version == r.Version && f.Store.Count<AuditEvent>() == audits);
            await f.Service.Unit.EditUnit(f.CompanyId, r.ResourceId, f.Owner, key, request, default);
        });
        test("INV03: writes and audit enforce permissions and foreign unit isolation", async f =>
        {
            await Error("permission_denied", () => f.Service.Unit.CreateUnit(f.CompanyId, f.Limited, Fixture.Key(), Request(), default));
            var r = await Create(f);
            await Error("permission_denied", () => f.Service.Unit.ListUnitAudit(f.CompanyId, r.ResourceId, f.Limited, 0, 10, default));
            var other = Guid.NewGuid(); await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(other, "OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default);
            var foreign = await f.Service.Unit.CreateUnit(other, f.Owner, Fixture.Key(), Request(), default);
            await Error("reference_not_found", () => f.Service.Unit.GetUnit(f.CompanyId, foreign.ResourceId, f.Owner, default));
            Ensure((await f.Service.Unit.SearchUnits(f.CompanyId, f.Owner, null, null, 0, 10, default)).TotalCount == 1);
        });
    }
    private static Task<CommandReceipt> Create(Fixture f, CreateUnitRequest? request = null) => f.Service.Unit.CreateUnit(f.CompanyId, f.Owner, Fixture.Key(), request ?? Request(), default);
    private static Task<UnitOfMeasure> Get(Fixture f, Guid id) => f.Service.Unit.GetUnit(f.CompanyId, id, f.Owner, default);
    private static void Ensure(bool value) { if (!value) throw new Exception("Unit assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
