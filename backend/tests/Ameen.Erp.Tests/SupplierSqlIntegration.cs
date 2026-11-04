using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// PUR01 isolated SQL acceptance. Called ONLY from SqlIntegration's disposable database.
public static class SupplierSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "049_suppliers_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [Suppliers] ([Conflict] int NOT NULL)");
        await RejectSql("PUR01 existing-table conflict",
            db.Database.ExecuteSqlRawAsync(script), 2714);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=49") == 0,
            "PUR01 failed deployment recorded schema version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [Suppliers]");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(49,SYSUTCDATETIME())");
        await RejectSql("PUR01 wrong schema owner", db.Database.ExecuteSqlRawAsync(script), 51002);
        await db.Database.ExecuteSqlRawAsync("DELETE dbo.__AmeenSchemaVersions WHERE [Version]=49");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=49") == 1,
            "PUR01 replay recorded multiple schema versions");
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Suppliers')") == 27,
            "PUR01 original supplier field count differs");
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('dbo.Suppliers')") == 3,
            "PUR01 missing company/account/currency references");
        Ensure(await Count(db, @"SELECT COUNT(*) AS [Value] FROM sys.foreign_key_columns fc
            JOIN sys.foreign_keys fk ON fk.object_id=fc.constraint_object_id
            WHERE fk.parent_object_id=OBJECT_ID('dbo.Suppliers')
            AND fk.name IN ('FK_Supplier_ControlAccount','FK_Supplier_Currency')") == 4,
            "PUR01 account and currency FKs must be tenant scoped");
        Ensure(await db.Set<Supplier>().CountAsync() == 0,
            "PUR01 deployment inserted invented suppliers");
        Console.WriteLine("  SQL PASS PUR01-01 atomic/replayed migration, 27 columns, tenant references, no seed");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid controlAccountId, string actor)
    {
        var currency = await db.Set<Currency>()
            .Where(x => x.CompanyId == companyId && x.Code == "SAR")
            .Select(x => x.Id).SingleAsync();
        var supplierId = Guid.NewGuid();
        var country = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT dbo.Suppliers
            (CompanyId,Id,Version,Code,Name,EnglishName,IsActive,Notes,LegalName,
             ControlAccountId,TransactionCurrencyId,PaymentTerms,DueDays,TaxNumber,CountryId,
             BankBeneficiary,Iban,CreatedBy,CreatedAt,Revision,LastModifiedAt,
             City,District,Street,BuildingNumber,PostalCode,AdditionalNumber)
            VALUES({companyId},{supplierId},{Guid.NewGuid()},{"00009"},{"مورد"},{"Supplier"},{true},
             {"ملاحظات"},{"شركة قانونية"},{controlAccountId},{currency},{"30 يوما"},{30},{"TAX-1"},{country},
             {"مستفيد"},{"YE-IBAN"},{actor},{now},{1},{now},
             {"صنعاء"},{"حي"},{"شارع"},{"0002"},{"00004"},{"0007"})");
        db.ChangeTracker.Clear();
        var saved = await db.Set<Supplier>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == supplierId);
        Ensure(saved.Code == "00009" && saved.Name == "مورد" &&
            saved.EnglishName == "Supplier" && saved.IsActive && saved.Notes == "ملاحظات" &&
            saved.LegalName == "شركة قانونية" &&
            saved.ControlAccountId == controlAccountId &&
            saved.TransactionCurrencyId == currency && saved.PaymentTerms == "30 يوما" &&
            saved.DueDays == 30 && saved.TaxNumber == "TAX-1" &&
            saved.CountryId == country && saved.BankBeneficiary == "مستفيد" &&
            saved.Iban == "YE-IBAN" && saved.CreatedBy == actor &&
            saved.CreatedAt == now && saved.LastModifiedAt == now && saved.Revision == 1 &&
            saved.City == "صنعاء" && saved.District == "حي" &&
            saved.Street == "شارع" && saved.BuildingNumber == "0002" &&
            saved.PostalCode == "00004" && saved.AdditionalNumber == "0007",
            "PUR01 SQL/EF field roundtrip lost an original supplier field");
        await RejectSql("PUR01 invalid account", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.Suppliers SET ControlAccountId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={supplierId}"), 547);
        await RejectSql("PUR01 invalid currency", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.Suppliers SET TransactionCurrencyId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={supplierId}"), 547);
        await RejectSql("PUR01 negative days", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.Suppliers SET DueDays={-1} WHERE CompanyId={companyId} AND Id={supplierId}"), 547);
        await RejectSql("PUR01 empty country", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.Suppliers SET CountryId={Guid.Empty} WHERE CompanyId={companyId} AND Id={supplierId}"), 547);
        await RejectSql("PUR01 backwards audit", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.Suppliers SET LastModifiedAt={now.AddDays(-1)} WHERE CompanyId={companyId} AND Id={supplierId}"), 547);
        await RejectSql("PUR01 duplicate supplier code", db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT dbo.Suppliers
            (CompanyId,Id,Version,Code,Name,IsActive,LegalName,
             ControlAccountId,TransactionCurrencyId,PaymentTerms,DueDays,CountryId,
             CreatedBy,CreatedAt,Revision,LastModifiedAt,City,Street)
            VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{"00009"},{"متكرر"},{true},
             {"متكرر"},{controlAccountId},{currency},{"نقدا"},{0},{country},
             {actor},{now},{1},{now},{"صنعاء"},{"شارع"})"), 2601);

        Ensure(await db.Set<Supplier>().AsNoTracking().CountAsync(x =>
            x.CompanyId == companyId && x.Code == "00009") == 1 &&
            await db.Set<Supplier>().AsNoTracking().CountAsync(x =>
                x.CompanyId == Guid.NewGuid() && x.Code == "00009") == 0,
            "PUR01 supplier read crosses tenant scope");
        var metadata = db.Model.FindEntityType(typeof(Supplier))
            ?? throw new Exception("PUR01 supplier entity missing");
        Ensure(metadata.FindPrimaryKey()?.Properties.Select(x => x.Name).SequenceEqual(
            new[] { nameof(Supplier.CompanyId), nameof(Supplier.Id) }) == true &&
            metadata.FindProperty(nameof(Supplier.Version))?.IsConcurrencyToken == true,
            "PUR01 tenant key or EF concurrency contract missing");

        var beforeAudit = await db.Set<AuditEvent>().CountAsync();
        var beforeOutbox = await db.Set<OutboxEvent>().CountAsync();
        var beforeJournal = await db.Set<JournalEntry>().CountAsync();
        var pending = new Supplier(companyId, "NEW", "مورد", "قانوني",
            controlAccountId, currency, "نقدا", 0, country, "صنعاء", "شارع",
            true, actor, now);
        db.Add(pending);
        await RejectPolicy(db, "create");
        db.Entry(pending).State = EntityState.Detached;
        db.Attach(saved);
        db.Entry(saved).State = EntityState.Modified;
        await RejectPolicy(db, "edit");
        db.Entry(saved).State = EntityState.Detached;
        db.Attach(saved);
        db.Entry(saved).State = EntityState.Deleted;
        await RejectPolicy(db, "delete");
        db.Entry(saved).State = EntityState.Detached;
        Ensure(await db.Set<Supplier>().CountAsync() == 1 &&
            await db.Set<AuditEvent>().CountAsync() == beforeAudit &&
            await db.Set<OutboxEvent>().CountAsync() == beforeOutbox &&
            await db.Set<JournalEntry>().CountAsync() == beforeJournal,
            "PUR01 forbidden EF writes changed supplier, audit, outbox or ledger");
        Console.WriteLine("  SQL PASS PUR01-01 supplier data, tenant constraints, rejected writes and side-effect guard");
    }

    private static async Task RejectPolicy(ErpDbContext db, string operation)
    {
        try
        {
            await db.SaveChangesAsync();
            throw new Exception("PUR01 supplier EF " + operation + " bypassed review policy");
        }
        catch (BusinessException e) when (e.Code == "supplier_policy_unavailable") { }
    }
    private static Task<int> Count(ErpDbContext db, string sql) =>
        db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static async Task RejectSql(string label, Task command, int number)
    {
        try { await command; throw new Exception(label + " unexpectedly succeeded"); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == number)) { }
    }
    private static void Ensure(bool valid, string label)
    {
        if (!valid) throw new Exception(label);
    }
}
