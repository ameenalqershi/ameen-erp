using Ameen.Erp.Api;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

if (args.Contains("--schema", StringComparer.Ordinal))
{
    var options = new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer("Server=localhost;Database=SchemaOnly;Integrated Security=true;TrustServerCertificate=true").Options;
    await using var db = new ErpDbContext(options);
    Console.Write(db.Database.GenerateCreateScript());
    return;
}

await ApiHost.Build(args).RunAsync();

public partial class Program { }
