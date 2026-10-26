using Ameen.Erp.Api;
using Ameen.Erp.Application;
using Ameen.Erp.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Ameen.Erp.Api;

public static class ApiHost
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(ApiHost).Assembly.FullName });
        configure?.Invoke(builder);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 512 * 1024);
        var connection = builder.Configuration.GetConnectionString("Erp") ?? throw new InvalidOperationException("ConnectionStrings:Erp is required.");
        var authority = builder.Configuration["Authentication:Authority"] ?? throw new InvalidOperationException("Authentication:Authority is required.");
        var audience = builder.Configuration["Authentication:Audience"] ?? throw new InvalidOperationException("Authentication:Audience is required.");
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var issuer) || issuer.Scheme != "https")
            throw new InvalidOperationException("An HTTPS OIDC authority is required.");

        builder.Services.AddDbContext<ErpDbContext>(options => options.UseSqlServer(connection, sql => sql.CommandTimeout(30)));
        builder.Services.AddScoped<IErpStore, SqlErpStore>();
        builder.Services.AddScoped<ITransactionRunner, SqlTransactionRunner>();
        builder.Services.AddErpApplication();
        builder.Services.AddSingleton<IClock, SystemClock>();
        if (builder.Configuration.GetValue<bool>("RecurringScheduler:Enabled"))
        {
            var schedulerActor = builder.Configuration["RecurringScheduler:ActorSubject"];
            if (string.IsNullOrWhiteSpace(schedulerActor) || schedulerActor.Length > 200)
                throw new InvalidOperationException("RecurringScheduler requires a pre-provisioned ActorSubject with company membership, RecurringGenerate and Draft permissions.");
            builder.Services.AddHostedService<RecurringDraftScheduler>();
        }
        builder.Services.ConfigureHttpJsonOptions(options => JsonDefaults.Configure(options.SerializerOptions));
        builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = authority; options.Audience = audience;
            options.RequireHttpsMetadata = true; options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
                ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "sub", RoleClaimType = "roles"
            };
        });
        builder.Services.AddAuthorization();
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
        if (origins.Any(x => x.Contains('*') || !Uri.TryCreate(x, UriKind.Absolute, out var origin) ||
            (origin.Scheme != "https" && !(builder.Environment.IsDevelopment() && origin.Scheme == "http" && origin.IsLoopback))))
            throw new InvalidOperationException("CORS requires explicit HTTPS origins (loopback HTTP is allowed only in Development).");
        builder.Services.AddCors(options => options.AddPolicy("client", policy =>
        {
            if (origins.Length > 0)
                policy.WithOrigins(origins).WithMethods("GET", "POST", "PUT")
                    .WithHeaders("Authorization", "Content-Type", "Idempotency-Key");
        }));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi(options => options.ConfigureErp());
        var app = builder.Build();
        app.Use(ApiSupport.ErrorMiddleware);
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.UseHttpsRedirection();
        app.UseCors("client"); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/health/ready", async (ErpDbContext db, CancellationToken ct) =>
        {
            try
            {
                if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(503);
                if (!await db.Database.SqlQuery<int>($"SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version] = {ErpDbContext.SchemaVersion}").AnyAsync(ct))
                    return Results.StatusCode(503);
                await db.Set<Ameen.Erp.Domain.Company>().AsNoTracking().Select(x => x.Id).Take(1).ToListAsync(ct);
                return Results.Ok(new { status = "ready" });
            }
            catch { return Results.StatusCode(503); }
        }).RequireAuthorization();
        app.MapOpenApi().RequireAuthorization();
        app.MapErp();
        return app;
    }
}
