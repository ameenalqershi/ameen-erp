using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// These integration checks run solely inside the disposable SQL database created by SqlIntegration.
public static class CustomerSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,"database","048_customers_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [Customers] ([Conflict] int NOT NULL)");
        await RejectSql("SAL01 pre-existing table conflict",db.Database.ExecuteSqlRawAsync(script),2714);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=48")==0,
            "SAL01 rolled back transaction still recorded schema version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [Customers]");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(48,SYSUTCDATETIME())");
        await RejectSql("SAL01 wrong schema owner",db.Database.ExecuteSqlRawAsync(script),51002);
        await db.Database.ExecuteSqlRawAsync("DELETE dbo.__AmeenSchemaVersions WHERE [Version]=48");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=48")==1,
            "SAL01 replay changed schema version");
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Customers')")==29,
            "SAL01 missing original fields or system concurrency field");
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('dbo.Customers')")==3,
            "SAL01 expected company/account/currency references");
        Ensure(await Count(db,@"SELECT COUNT(*) AS [Value] FROM sys.foreign_key_columns fc
          JOIN sys.foreign_keys fk ON fk.object_id=fc.constraint_object_id
          WHERE fk.parent_object_id=OBJECT_ID('dbo.Customers')
          AND fk.name IN ('FK_Customer_ControlAccount','FK_Customer_Currency')")==4,
          "SAL01 account and currency FKs must include CompanyId");
        Ensure(await db.Set<Customer>().CountAsync()==0, "SAL01 migration seeded fake customer");
        Console.WriteLine("  SQL PASS SAL01-01 atomic/replayed migration, 29 columns, tenant references, no seed");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid controlAccountId, string actor)
    {
        var currencyId=await db.Set<Currency>().Where(x=>x.CompanyId==companyId && x.Code=="SAR")
            .Select(x=>x.Id).SingleAsync();
        var now=new DateTimeOffset(2026,11,4,0,0,0,TimeSpan.Zero);
        var customerId=Guid.NewGuid(); var category=Guid.NewGuid(); var country=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
          INSERT INTO dbo.Customers
          (CompanyId,Id,Version,Code,Name,EnglishName,IsActive,LegalName,
           CustomerCategoryId,ControlAccountId,TransactionCurrencyId,CreditLimit,
           CreditDays,CountryId,CreatedBy,CreatedAt,Revision,LastModifiedAt,BuildingNumber,PostalCode)
          VALUES({companyId},{customerId},{Guid.NewGuid()},{"00045"},{"عميل"}, {"Customer"},{true},{"اسم قانوني"},
                 {category},{controlAccountId},{currencyId},CAST(12.123456 AS decimal(20,6)),
                 {5},{country},{actor},{now},{1},{now},{"0007"},{"0050"})");
        db.ChangeTracker.Clear();
        var saved=await db.Set<Customer>().AsNoTracking().SingleAsync(x=>x.CompanyId==companyId && x.Id==customerId);
        var checkedFields = new (string Name, bool Match)[] {
          ("code",saved.Code=="00045"),("arabic-name",saved.Name=="عميل"),
          ("english-name",saved.EnglishName=="Customer"),
          ("legal-name",saved.LegalName=="اسم قانوني"),
          ("credit-limit",saved.CreditLimit==12.123456m),
          ("credit-days",saved.CreditDays==5),
          ("building",saved.BuildingNumber=="0007"),
          ("postal",saved.PostalCode=="0050"),
          ("category",saved.CustomerCategoryId==category),
          ("country",saved.CountryId==country),
          ("account",saved.ControlAccountId==controlAccountId),
          ("currency",saved.TransactionCurrencyId==currencyId),
          ("creator",saved.CreatedBy==actor),
          ("created-at",saved.CreatedAt==now),
          ("modified-at",saved.LastModifiedAt==now),
          ("revision",saved.Revision==1)
        };
        var mismatched=string.Join(",",checkedFields.Where(x=>!x.Match).Select(x=>x.Name));
        Ensure(mismatched.Length==0,"SAL01 EF/SQL roundtrip mismatch: "+mismatched);
        await RejectSql("other account",db.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE dbo.Customers SET ControlAccountId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={customerId}"),547);
        await RejectSql("other currency",db.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE dbo.Customers SET TransactionCurrencyId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={customerId}"),547);
        await RejectSql("credit negative",db.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE dbo.Customers SET CreditLimit={-1m} WHERE CompanyId={companyId} AND Id={customerId}"),547);
        await RejectSql("credit days negative",db.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE dbo.Customers SET CreditDays={-1} WHERE CompanyId={companyId} AND Id={customerId}"),547);
        await RejectSql("audit backwards",db.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE dbo.Customers SET LastModifiedAt={now.AddDays(-1)} WHERE CompanyId={companyId} AND Id={customerId}"),547);
        // A customer's code is unique per company, preserving leading zeroes.
        await RejectSql("duplicate customer code",db.Database.ExecuteSqlInterpolatedAsync($@"
          INSERT INTO dbo.Customers
          (CompanyId,Id,Version,Code,Name,IsActive,LegalName,
           CustomerCategoryId,ControlAccountId,TransactionCurrencyId,
           CreditLimit,CreditDays,CountryId,CreatedBy,CreatedAt,Revision,LastModifiedAt)
          VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{"00045"},{"duplicate"},
            {true},{"duplicate"},{category},{controlAccountId},{currencyId},
            CAST(0 AS decimal(20,6)),{0},{country},{actor},{now},{1},{now})"),2601);
        // A matching code from another tenant must not be returned by a scoped query.
        Ensure(await db.Set<Customer>().AsNoTracking()
          .CountAsync(x=>x.CompanyId==Guid.NewGuid() && x.Code=="00045")==0,
          "SAL01 customer visible to unrelated tenant");
        Ensure(await db.Set<Customer>().AsNoTracking()
          .CountAsync(x=>x.CompanyId==companyId && x.Code=="00045")==1,
          "SAL01 tenant-scoped customer read missing");
        // SQL cannot silently accept invalid audit revision or a missing company.
        await RejectSql("invalid revision",db.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE dbo.Customers SET Revision={0} WHERE CompanyId={companyId} AND Id={customerId}"),547);
        await RejectSql("invalid tenant",db.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE dbo.Customers SET CompanyId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={customerId}"),547);
        // Optimistic concurrency is a company-scoped version token in the EF mapping.
        var entityType=db.Model.FindEntityType(typeof(Customer))
          ?? throw new Exception("SAL01 customer EF entity missing");
        var versionProperty=entityType.FindProperty(nameof(Customer.Version))
          ?? throw new Exception("SAL01 customer Version property missing");
        Ensure(versionProperty.IsConcurrencyToken,"SAL01 EF concurrency token disabled");
        var key=entityType.FindPrimaryKey()
          ?? throw new Exception("SAL01 tenant primary key missing");
        Ensure(key.Properties.Select(x=>x.Name).SequenceEqual(new[]
          {nameof(Customer.CompanyId),nameof(Customer.Id)}),
          "SAL01 tenant compound key changed");
        var audits=await db.Set<AuditEvent>().CountAsync();
        var outbox=await db.Set<OutboxEvent>().CountAsync();
        var journals=await db.Set<JournalEntry>().CountAsync();
        var rejected = new Customer(companyId,"NEW","عميل جديد","قانوني",
          category,controlAccountId,currencyId,0,0,country,actor,now);
        db.Add(rejected);
        try {await db.SaveChangesAsync();throw new Exception("SAL01 unauthorized EF write succeeded");}
        catch(BusinessException e) when(e.Code=="customer_policy_unavailable") { }
        db.Entry(rejected).State=EntityState.Detached;
        // Every direct EF mutation must remain unavailable until the country,
        // credit override and customer scope policies are approved.
        db.Attach(saved);
        db.Entry(saved).State=EntityState.Modified;
        try {await db.SaveChangesAsync();throw new Exception("SAL01 EF update bypassed policy");}
        catch(BusinessException e) when(e.Code=="customer_policy_unavailable") { }
        db.Entry(saved).State=EntityState.Detached;
        db.Attach(saved);
        db.Entry(saved).State=EntityState.Deleted;
        try {await db.SaveChangesAsync();throw new Exception("SAL01 EF delete bypassed policy");}
        catch(BusinessException e) when(e.Code=="customer_policy_unavailable") { }
        db.Entry(saved).State=EntityState.Detached;
        Ensure(await db.Set<Customer>().CountAsync()==1 &&
          await db.Set<AuditEvent>().CountAsync()==audits &&
          await db.Set<OutboxEvent>().CountAsync()==outbox &&
          await db.Set<JournalEntry>().CountAsync()==journals,
          "SAL01 unauthorized EF write changed rows, audit, outbox, or ledger");
        Console.WriteLine("  SQL PASS SAL01-01 roundtrip, tenant FK, invalid credit/audit and fail-closed EF");
    }

    private static async Task<int> Count(ErpDbContext db,string sql)=>
        await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static async Task RejectSql(string description,Task action,int number)
    {
        try {await action; throw new Exception(description+" unexpectedly succeeded");}
        catch(SqlException e) when(e.Errors.Cast<SqlError>().Any(x=>x.Number==number)) { }
    }
    private static void Ensure(bool ok,string message) {if(!ok)throw new Exception(message);}
}
