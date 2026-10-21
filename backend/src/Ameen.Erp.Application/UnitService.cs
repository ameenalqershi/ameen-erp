using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private static object UnitSnapshot(UnitOfMeasure x) => new
    { x.Version, x.Revision, x.Code, x.Name, x.StandardCode, x.QuantityScale, x.Description, x.EnglishName, x.Notes, x.IsActive };
    private async Task UniqueUnitCode(Guid cid, Guid id, string code, CancellationToken ct) =>
        Check.That(!(await store.Find<UnitOfMeasure>(cid, x => x.Code == code && x.Id != id, ct)).Any(),
            "duplicate_unit_code", "رمز الوحدة مستخدم في الشركة.", ErrorKind.Conflict);

    public Task<CommandReceipt> CreateUnit(Guid cid, Actor actor, string key, CreateUnitRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "unit.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var unit = new UnitOfMeasure(cid, request.Code, request.Name, request.StandardCode, request.QuantityScale, request.Description, request.EnglishName, request.Notes);
            await UniqueUnitCode(cid, unit.Id, unit.Code, token);
            store.Add(unit); Audit(cid, actor, "unit.created", unit, new { after = UnitSnapshot(unit) });
            return Receipt(unit, "Created");
        }, ct);
    public Task<CommandReceipt> EditUnit(Guid cid, Guid id, Actor actor, string key, EditUnitRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "unit.edit:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var unit = await Required<UnitOfMeasure>(cid, id, token); Check.Version(unit, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب التعديل", 500);
            var candidate = new UnitOfMeasure(cid, request.Code, request.Name, request.StandardCode, request.QuantityScale, request.Description, request.EnglishName, request.Notes);
            await UniqueUnitCode(cid, id, candidate.Code, token);
            // No ITEM_UNIT or stock documents exist yet. Adding those requires a usage lock here before enabling them.
            var before = UnitSnapshot(unit);
            unit.Configure(candidate.Code, candidate.Name, candidate.StandardCode, candidate.QuantityScale, candidate.Description, candidate.EnglishName, candidate.Notes);
            Audit(cid, actor, "unit.edited", unit, new { before, after = UnitSnapshot(unit), reason });
            return Receipt(unit, "Updated");
        }, ct);
    public Task<CommandReceipt> SetUnitStatus(Guid cid, Guid id, Actor actor, string key, SetUnitStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "unit.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var unit = await Required<UnitOfMeasure>(cid, id, token); Check.Version(unit, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب تغيير الحالة", 500); var before = UnitSnapshot(unit);
            unit.SetActive(request.IsActive); Audit(cid, actor, "unit.status-changed", unit, new { before, after = UnitSnapshot(unit), reason });
            return Receipt(unit, unit.IsActive ? "Active" : "Inactive");
        }, ct);
    public async Task<UnitOfMeasure> GetUnit(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct); return await Required<UnitOfMeasure>(cid, id, ct);
    }
    public async Task<Page<UnitOfMeasure>> SearchUnits(Guid cid, Actor actor, string? search, bool? isActive, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await Authorize(cid, actor, Permissions.Read, null, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح."); search = search?.Trim();
        return await store.ReadPage<UnitOfMeasure>(cid, x => (isActive == null || x.IsActive == isActive) &&
            (search == null || x.Code.Contains(search) || x.Name.Contains(search) || x.StandardCode.Contains(search) || (x.EnglishName != null && x.EnglishName.Contains(search))),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<Page<AuditEvent>> ListUnitAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await GetUnit(cid, id, actor, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id && (x.Action == "unit.created" || x.Action == "unit.edited" || x.Action == "unit.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
}
