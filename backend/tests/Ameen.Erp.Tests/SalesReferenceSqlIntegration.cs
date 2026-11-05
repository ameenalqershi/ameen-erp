using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// SQL schema tests use only SqlIntegration's disposable database. EF writes
// stay blocked until HR03/SAL10/SAL02/SYS16/customer-scope policies are live.
public static class SalesReferenceSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "049_sales_references_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE dbo.SalesRepresentatives ([Conflict] int NOT NULL)");
        await RejectSql("SAL09/SAL15 pre-existing table conflict",
            db.Database.ExecuteSqlRawAsync(script), 2714);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=49") == 0,
            "SAL09/SAL15 failed migration recorded schema version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE dbo.SalesRepresentatives");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(49,SYSUTCDATETIME())");
        await RejectSql("SAL09/SAL15 wrong schema owner",
            db.Database.ExecuteSqlRawAsync(script), 51002);
        await db.Database.ExecuteSqlRawAsync("DELETE dbo.__AmeenSchemaVersions WHERE [Version]=49");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=49") == 1,
            "SAL09/SAL15 migration replay changed schema version");
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.SalesRepresentatives')") == 17,
            "SAL09 SQL field count mismatch");
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.CustomerGroups')") == 18,
            "SAL15 SQL field count mismatch");
        Ensure(await Count(db, @"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
            WHERE parent_object_id IN (OBJECT_ID('dbo.SalesRepresentatives'),OBJECT_ID('dbo.CustomerGroups'))") == 7,
            "SAL09/SAL15 expected company, custody, parent, currency, payment-term and account FKs");
        Ensure(await Count(db, @"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk
            WHERE fk.name IN ('FK_SalesRepresentative_CustodyAccount','FK_CustomerGroup_Parent',
              'FK_CustomerGroup_Currency','FK_CustomerGroup_PaymentTerm','FK_CustomerGroup_ControlAccount')
            AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                 WHERE fc.constraint_object_id=fk.object_id)=2
            AND EXISTS(SELECT 1 FROM sys.foreign_key_columns fc
                 JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
                 WHERE fc.constraint_object_id=fk.object_id AND c.name='CompanyId')") == 5,
            "SAL09/SAL15 composite reference FKs must include CompanyId");
        Ensure(await db.Set<SalesRepresentative>().CountAsync() == 0 &&
            await db.Set<CustomerGroup>().CountAsync() == 0,
            "SAL09/SAL15 migration must not seed master records");

        var repType = db.Model.FindEntityType(typeof(SalesRepresentative))
            ?? throw new Exception("SAL09 EF entity missing");
        var groupType = db.Model.FindEntityType(typeof(CustomerGroup))
            ?? throw new Exception("SAL15 EF entity missing");
        Ensure(repType.GetTableName() == "SalesRepresentatives" &&
            groupType.GetTableName() == "CustomerGroups",
            "SAL09/SAL15 EF table mapping mismatch");
        Ensure(repType.FindPrimaryKey()!.Properties.Select(x => x.Name)
            .SequenceEqual(new[] { nameof(SalesRepresentative.CompanyId), nameof(SalesRepresentative.Id) }) &&
            groupType.FindPrimaryKey()!.Properties.Select(x => x.Name)
            .SequenceEqual(new[] { nameof(CustomerGroup.CompanyId), nameof(CustomerGroup.Id) }),
            "SAL09/SAL15 composite tenant key missing");
        Ensure(repType.FindProperty(nameof(SalesRepresentative.Version))!.IsConcurrencyToken &&
            groupType.FindProperty(nameof(CustomerGroup.Version))!.IsConcurrencyToken,
            "SAL09/SAL15 optimistic concurrency token missing");
        Ensure(groupType.FindProperty(nameof(CustomerGroup.DefaultCreditLimit))!.GetPrecision() == 20 &&
            groupType.FindProperty(nameof(CustomerGroup.DefaultCreditLimit))!.GetScale() == 6,
            "SAL15 credit limit precision mismatch");
        Ensure(!groupType.GetForeignKeys().Any(fk =>
                fk.Properties.Any(p => p.Name == nameof(CustomerGroup.PriceListId))) &&
            !repType.GetForeignKeys().Any(fk => fk.Properties.Any(p =>
                p.Name is nameof(SalesRepresentative.EmployeeId) or nameof(SalesRepresentative.SupervisorId) or
                    nameof(SalesRepresentative.CommissionPolicyId))),
            "SAL09/SAL15 unavailable providers must not get fictional foreign keys");
        Console.WriteLine("  SQL PASS SAL09/SAL15 atomic/idempotent 049 migration, tenant FKs, EF mapping and no seeds");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid accountId, string actor)
    {
        var currencyId = await db.Set<Currency>().Where(x => x.CompanyId == companyId && x.Code == "SAR")
            .Select(x => x.Id).SingleAsync();
        var termId = await db.Set<PaymentTerm>().Where(x => x.CompanyId == companyId && x.Code == "P-0007")
            .Select(x => x.Id).SingleAsync();
        var now = new DateTimeOffset(2026, 11, 5, 13, 0, 0, TimeSpan.Zero);
        var repId = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var supervisor = Guid.NewGuid();
        var commission = Guid.NewGuid();
        var priceList = Guid.NewGuid();
        var parentGroupId = Guid.NewGuid();
        var childGroupId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT dbo.SalesRepresentatives
            (CompanyId,Id,Version,Code,Name,EnglishName,IsActive,Notes,EmployeeId,Region,
             SupervisorId,CustodyAccountId,CommissionPolicyId,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{repId},{Guid.NewGuid()},{"REP-0001"},{"مندوب"},{"Representative"},
             {true},{"notes"},{employee},{"المنطقة"},{supervisor},{accountId},{commission},
             {actor},{now},{1},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT dbo.CustomerGroups
            (CompanyId,Id,Version,Code,Name,EnglishName,IsActive,Notes,ParentGroupId,PriceListId,
             DefaultCreditLimit,CreditCurrencyId,PaymentTermId,ControlAccountId,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{parentGroupId},{Guid.NewGuid()},{"GROUP-P"},{"مجموعة أب"},{null},
             {true},{null},{null},{priceList},{100.123456m},{currencyId},{termId},{accountId},
             {actor},{now},{1},{now});
            INSERT dbo.CustomerGroups
            (CompanyId,Id,Version,Code,Name,EnglishName,IsActive,Notes,ParentGroupId,PriceListId,
             DefaultCreditLimit,CreditCurrencyId,PaymentTermId,ControlAccountId,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{childGroupId},{Guid.NewGuid()},{"GROUP-C"},{"مجموعة فرعية"},{"Child group"},
             {true},{"notes"},{parentGroupId},{priceList},{0m},{currencyId},{termId},{accountId},
             {actor},{now},{1},{now})");
        db.ChangeTracker.Clear();

        var rep = await db.Set<SalesRepresentative>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == repId);
        Ensure(rep.Code == "REP-0001" && rep.Name == "مندوب" &&
            rep.EnglishName == "Representative" && rep.Region == "المنطقة" &&
            rep.EmployeeId == employee && rep.SupervisorId == supervisor &&
            rep.CustodyAccountId == accountId && rep.CommissionPolicyId == commission &&
            rep.CreatedBy == actor && rep.CreatedAt == now && rep.LastModifiedAt == now &&
            rep.Revision == 1 && rep.IsActive,
            "SAL09 SQL/EF roundtrip mismatch");
        var group = await db.Set<CustomerGroup>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == childGroupId);
        Ensure(group.Code == "GROUP-C" && group.Name == "مجموعة فرعية" &&
            group.EnglishName == "Child group" && group.ParentGroupId == parentGroupId &&
            group.PriceListId == priceList && group.DefaultCreditLimit == 0m &&
            group.CreditCurrencyId == currencyId && group.PaymentTermId == termId &&
            group.ControlAccountId == accountId && group.CreatedBy == actor &&
            group.CreatedAt == now && group.LastModifiedAt == now && group.Revision == 1,
            "SAL15 SQL/EF roundtrip mismatch");

        await RejectSql("SAL09 foreign custody account",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesRepresentatives SET CustodyAccountId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={repId}"), 547);
        await RejectSql("SAL09 zero employee reference",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesRepresentatives SET EmployeeId={Guid.Empty}
              WHERE CompanyId={companyId} AND Id={repId}"), 547);
        await RejectSql("SAL09 backwards audit",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesRepresentatives SET LastModifiedAt={now.AddDays(-1)}
              WHERE CompanyId={companyId} AND Id={repId}"), 547);
        await RejectSql("SAL09 duplicate code",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              INSERT dbo.SalesRepresentatives
              (CompanyId,Id,Version,Code,Name,IsActive,Region,CreatedBy,CreatedAt,Revision,LastModifiedAt)
              VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{"REP-0001"},{"duplicate"},
                     {true},{"region"},{actor},{now},{1},{now})"), 2601, 2627);
        await RejectSql("SAL15 negative credit limit",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CustomerGroups SET DefaultCreditLimit={-1m}
              WHERE CompanyId={companyId} AND Id={childGroupId}"), 547);
        await RejectSql("SAL15 zero price list reference",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CustomerGroups SET PriceListId={Guid.Empty}
              WHERE CompanyId={companyId} AND Id={childGroupId}"), 547);
        await RejectSql("SAL15 foreign currency",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CustomerGroups SET CreditCurrencyId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={childGroupId}"), 547);
        await RejectSql("SAL15 foreign payment term",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CustomerGroups SET PaymentTermId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={childGroupId}"), 547);
        await RejectSql("SAL15 foreign control account",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CustomerGroups SET ControlAccountId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={childGroupId}"), 547);
        await RejectSql("SAL15 missing parent group",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CustomerGroups SET ParentGroupId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={childGroupId}"), 547);
        await RejectSql("SAL15 backwards audit",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CustomerGroups SET LastModifiedAt={now.AddDays(-1)}
              WHERE CompanyId={companyId} AND Id={childGroupId}"), 547);
        await RejectSql("SAL15 duplicate code",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              INSERT dbo.CustomerGroups
              (CompanyId,Id,Version,Code,Name,IsActive,ParentGroupId,PriceListId,DefaultCreditLimit,
               CreditCurrencyId,PaymentTermId,ControlAccountId,CreatedBy,CreatedAt,Revision,LastModifiedAt)
              VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{"GROUP-C"},{"duplicate"},
                {true},{null},{priceList},{0m},{currencyId},{termId},{accountId},{actor},{now},{1},{now})"),
            2601, 2627);

        db.ChangeTracker.Clear();
        var auditCount = await db.Set<AuditEvent>().CountAsync();
        var outboxCount = await db.Set<OutboxEvent>().CountAsync();
        var journalCount = await db.Set<JournalEntry>().CountAsync();
        var blockedRep = new SalesRepresentative(companyId, "REP-BLOCKED", "محجوب", "region",
            actor, now, custodyAccountId: accountId);
        db.Add(blockedRep);
        await RejectBusiness("sales_representative_policy_unavailable", () => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var blockedGroup = new CustomerGroup(companyId, "GROUP-BLOCKED", "محجوب",
            priceList, 0m, currencyId, termId, accountId, actor, now);
        db.Add(blockedGroup);
        await RejectBusiness("customer_group_policy_unavailable", () => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var updateRep = await db.Set<SalesRepresentative>().SingleAsync(x => x.CompanyId == companyId && x.Id == repId);
        db.Entry(updateRep).State = EntityState.Modified;
        await RejectBusiness("sales_representative_policy_unavailable", () => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var deleteGroup = await db.Set<CustomerGroup>().SingleAsync(x => x.CompanyId == companyId && x.Id == childGroupId);
        db.Entry(deleteGroup).State = EntityState.Deleted;
        await RejectBusiness("customer_group_policy_unavailable", () => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        Ensure(await db.Set<SalesRepresentative>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<CustomerGroup>().CountAsync(x => x.CompanyId == companyId) == 2 &&
            await db.Set<AuditEvent>().CountAsync() == auditCount &&
            await db.Set<OutboxEvent>().CountAsync() == outboxCount &&
            await db.Set<JournalEntry>().CountAsync() == journalCount,
            "SAL09/SAL15 denied EF writes changed master data, audit, outbox or ledger");
        Console.WriteLine("  SQL PASS SAL09/SAL15 field roundtrip, tenant FK guards and fail-closed EF writes");
    }

    private static async Task<int> Count(ErpDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<int>(sql).SingleAsync();

    private static async Task RejectSql(string description, Task action, params int[] numbers)
    {
        try { await action; throw new Exception(description + " unexpectedly succeeded"); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => numbers.Contains(x.Number))) { }
    }

    private static async Task RejectBusiness(string code, Func<Task> action)
    {
        try { await action(); throw new Exception(code + " unexpectedly succeeded"); }
        catch (BusinessException e) when (e.Code == code) { }
    }

    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
