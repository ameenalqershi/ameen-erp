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
