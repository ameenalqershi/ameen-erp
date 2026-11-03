using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Ameen.Erp.Api;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Ameen.Erp.Tests;

// Independent production-host gate for GL03-GP05: real HTTPS, signed JWT, OIDC and SQL persistence.
public static class ScheduledReversalHostedIntegration
{
    public static async Task Run(string connection, Guid companyId, CreateJournalRequest journalRequest)
    {
        using var tlsKey = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(san.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(2));
        using var signingKey = RSA.Create(2048);
        var key = new RsaSecurityKey(signingKey) { KeyId = "ameen-scheduled-reversal-integration" };
        var publicKey = signingKey.ExportParameters(false);
        var issuerBuilder = WebApplication.CreateBuilder();
        issuerBuilder.Logging.ClearProviders();
        issuerBuilder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
        await using var issuerApp = issuerBuilder.Build();
        var issuer = "";
        issuerApp.MapGet("/.well-known/openid-configuration", () => new
        {
            issuer, jwks_uri = issuer + "/jwks", id_token_signing_alg_values_supported = new[] { "RS256" },
            response_types_supported = new[] { "token" }, subject_types_supported = new[] { "public" }
        });
        issuerApp.MapGet("/jwks", () => new { keys = new[] { new
        {
            kty = "RSA", use = "sig", kid = key.KeyId, alg = "RS256",
            n = Base64UrlEncoder.Encode(publicKey.Modulus!), e = Base64UrlEncoder.Encode(publicKey.Exponent!)
        } } });
        await issuerApp.StartAsync(); issuer = issuerApp.Urls.Single();
        const string audience = "ameen-scheduled-reversal-api";
        try
        {
            // GL07-02: fail-closed configuration checks never start a worker.
            // The separate runtime acceptance at the end uses a dedicated
            // branch-scoped actor on the disposable integration SQL database.
            WebApplication OptedInHost(string? actorSubject, int? minutes = null) =>
                ApiHost.Build(Array.Empty<string>(), b =>
                {
                    b.Logging.ClearProviders();
                    b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Erp"] = connection,
                        ["Authentication:Authority"] = issuer,
                        ["Authentication:Audience"] = audience,
                        ["ReversalScheduler:Enabled"] = "true",
                        ["ReversalScheduler:ActorSubject"] = actorSubject,
                        ["ReversalScheduler:IntervalMinutes"] = minutes?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
                });
            foreach (var missingActor in new string?[] { null, "", "   ", new string('a', 201) })
            {
                try
                {
                    await using var invalidHost = OptedInHost(missingActor);
                    throw new Exception("GL07-02 reversal worker accepted missing/invalid actor at build");
                }
                catch (InvalidOperationException e) when (e.Message.Contains("ActorSubject", StringComparison.Ordinal)) { }
            }
            foreach (var invalidMinutes in new[] { 0, -1, 1441 })
            {
                try
                {
                    await using var invalidHost = OptedInHost("sql-clerk", invalidMinutes);
                    throw new Exception("GL07-02 reversal worker accepted an invalid interval at build");
                }
                catch (InvalidOperationException e) when (e.Message.Contains("IntervalMinutes", StringComparison.Ordinal)) { }
            }
            await using (var optedIn = OptedInHost("sql-clerk", 15))
            {
                Ensure(optedIn.Services.GetServices<IHostedService>().Any(x =>
                    x.GetType().Name == "ReversalDraftScheduler"),
                    "GL07-02 explicit safe opt-in did not register reversal draft worker");
            }
            Console.WriteLine("  HOST PASS GL07-02 reversal scheduler opt-in actor/interval guard and worker registration (not launched)");
            await using var api = ApiHost.Build(Array.Empty<string>(), b =>
            {
                b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Erp"] = connection, ["Authentication:Authority"] = issuer,
                    ["Authentication:Audience"] = audience, ["AllowedHosts"] = "127.0.0.1;localhost",
                    ["ReversalScheduler:Enabled"] = "false"
                });
                b.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
                b.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                    o => o.BackchannelHttpHandler = PinnedTransport(certificate));
            });
            Ensure(!api.Services.GetServices<IHostedService>().Any(x =>
                x.GetType().Name == "ReversalDraftScheduler"),
                "GL07-02 reversal draft worker was enabled without explicit opt-in");
            await api.StartAsync();
            try
            {
                using var client = new HttpClient(PinnedTransport(certificate)) { BaseAddress = new Uri(api.Urls.Single()) };
                string Token(string subject)
                {
                    var now = DateTime.UtcNow;
                    var token = new JwtSecurityToken(issuer, audience, new[] { new Claim("sub", subject) },
                        notBefore: now.AddMinutes(-10), expires: now.AddMinutes(5),
                        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
                    return new JwtSecurityTokenHandler().WriteToken(token);
                }
                void Authenticate(string subject) => client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", Token(subject));
                var path = $"/api/v1/companies/{companyId}/journals";
                var scheduledRequest = journalRequest with
                {
                    Description = "GL03 scheduled reversal production gate",
                    AutomaticReversal = true,
                    ReversalDate = journalRequest.AccountingDate
                };
                Authenticate("sql-clerk");
                var original = await Command(client, path, scheduledRequest);
                original = await Command(client, path + $"/{original.ResourceId}/submit", new TransitionRequest(original.Version));
                Authenticate("sql-approver");
                original = await Command(client, path + $"/{original.ResourceId}/approve", new TransitionRequest(original.Version));
                original = await Command(client, path + $"/{original.ResourceId}/post", new TransitionRequest(original.Version));
                Ensure(original.Status == "Posted", "Scheduled original was not posted through production HTTP");

                client.DefaultRequestHeaders.Authorization = null;
                using (var response = await SendDue(client, path, journalRequest.AccountingDate))
                    Ensure(response.StatusCode == HttpStatusCode.Unauthorized, "Anonymous scheduled reversal passed production authentication");
                Authenticate("scheduled-reversal-outsider");
                using (var response = await SendDue(client, path, journalRequest.AccountingDate))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden, "Unrelated identity passed scheduled reversal authorization");

                Authenticate("sql-clerk");
                var due = await Due(client, path, journalRequest.AccountingDate);
                Ensure(due.SelectedCount == 1 && due.GeneratedCount == 1 && due.FailedCount == 0 &&
                    due.Rows.Single().OriginalJournalId == original.ResourceId && due.Rows.Single().Succeeded,
                    "Production due scan did not generate exactly one scheduled reversal");
                var reversalId = due.Rows.Single().ReversalJournalId!.Value;
                Guid reversalVersion;
                using (var detail = await client.GetAsync(path + $"/{reversalId}"))
                using (var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                {
                    Ensure(detail.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("state").GetString() == "Draft" &&
                        json.RootElement.GetProperty("source").GetString() == "Reversal" &&
                        json.RootElement.GetProperty("reversesJournalId").GetGuid() == original.ResourceId &&
                        !json.RootElement.GetProperty("automaticReversal").GetBoolean() &&
                        json.RootElement.GetProperty("automaticReversalDate").ValueKind == JsonValueKind.Null,
                        "Production scheduled inverse is not a linked, non-recurring historical draft");
                    reversalVersion = json.RootElement.GetProperty("version").GetGuid();
                }
                using (var impactResponse = await client.GetAsync(path + $"/{reversalId}/impact-preview"))
                using (var impact = JsonDocument.Parse(await impactResponse.Content.ReadAsStringAsync()))
                {
                    Ensure(impactResponse.StatusCode == HttpStatusCode.OK &&
                        impact.RootElement.GetProperty("source").GetString() == "Reversal" &&
                        impact.RootElement.GetProperty("version").GetGuid() == reversalVersion &&
                        impact.RootElement.GetProperty("canSubmit").GetBoolean() &&
                        impact.RootElement.GetProperty("isBalanced").GetBoolean() &&
                        impact.RootElement.GetProperty("difference").GetString() == "0" &&
                        impact.RootElement.GetProperty("lines").GetArrayLength() == journalRequest.Lines.Length,
                        "GL07-A05 production reversal impact preview changed historical draft");
                    Console.WriteLine("  HOST PASS GL07-A05 reversal preview over signed JWT/HTTPS/SQL");
                }
                var replay = await Due(client, path, journalRequest.AccountingDate);
                Ensure(replay.SelectedCount == 0 && replay.GeneratedCount == 0,
                    "Production due replay duplicated a reserved reversal");
                await Command(client, path + $"/{reversalId}/cancel", new TransitionRequest(reversalVersion, "إغلاق اختبار العكس المجدول"));
                var afterCancellation = await Due(client, path, journalRequest.AccountingDate);
                Ensure(afterCancellation.SelectedCount == 0 && afterCancellation.GeneratedCount == 0,
                    "Cancelled production reversal allowed scheduled regeneration");
                // GL07-02: end-to-end opt-in BACKGROUND worker acceptance.
                // One new branch and a dedicated member isolate this actor
                // from any other due journals in the disposable SQL fixture.
                Authenticate("sql-owner");
                var workerBranch = await Command(client,
                    $"/api/v1/companies/{companyId}/branches",
                    new CreateBranchRequest("GL07-WORKER-CI", "فرع عامل العكس", "مدينة", "عنوان"));
                const string workerSubject = "gl07-reversal-worker-ci";
                await Command(client, $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest(workerSubject,
                        new[] { Permissions.Reverse, Permissions.Draft }, false,
                        new[] { workerBranch.ResourceId }, null));
                Authenticate("sql-clerk");
                var dedicatedRequest = scheduledRequest with
                {
                    BranchId = workerBranch.ResourceId,
                    Description = "GL07 opt-in worker dedicated original",
                    ExternalReference = "GL07-WORKER-" + Fixture.Key(),
                    AutomaticReversal = true,
                    ReversalDate = journalRequest.AccountingDate
                };
                var workerOriginal = await Command(client, path, dedicatedRequest);
                workerOriginal = await Command(client, path + $"/{workerOriginal.ResourceId}/submit",
                    new TransitionRequest(workerOriginal.Version));
                Authenticate("sql-approver");
                workerOriginal = await Command(client, path + $"/{workerOriginal.ResourceId}/approve",
                    new TransitionRequest(workerOriginal.Version));
                Authenticate("sql-clerk");
                workerOriginal = await Command(client, path + $"/{workerOriginal.ResourceId}/post",
                    new TransitionRequest(workerOriginal.Version));
                Ensure(workerOriginal.Status == "Posted", "GL07-02 worker fixture original was not posted");

                await using (var workerApi = ApiHost.Build(Array.Empty<string>(), b =>
                {
                    b.Logging.ClearProviders();
                    b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Erp"] = connection,
                        ["Authentication:Authority"] = issuer,
                        ["Authentication:Audience"] = audience,
                        ["ReversalScheduler:Enabled"] = "true",
                        ["ReversalScheduler:ActorSubject"] = workerSubject,
                        ["ReversalScheduler:IntervalMinutes"] = "1440"
                    });
                    b.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0,
                        l => l.UseHttps(certificate)));
                    b.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                        o => o.BackchannelHttpHandler = PinnedTransport(certificate));
                }))
                {
                    await workerApi.StartAsync();
                    try
                    {
                        JournalEntry? linkedDraft = null;
                        JournalEntry? trackedOriginal = null;
                        // The worker scans immediately at startup. Bound the
                        // poll and fail closed; never assert an external VPS
                        // scheduler was activated by this disposable SQL test.
                        for (var attempt = 0; attempt < 100; attempt++)
                        {
                            await using var testScope = workerApi.Services.CreateAsyncScope();
                            var db = testScope.ServiceProvider.GetRequiredService<ErpDbContext>();
                            linkedDraft = await db.Set<JournalEntry>().AsNoTracking()
                                .FirstOrDefaultAsync(x => x.CompanyId == companyId &&
                                    x.ReversesJournalId == workerOriginal.ResourceId);
                            if (linkedDraft is not null)
                            {
                                trackedOriginal = await db.Set<JournalEntry>().AsNoTracking()
                                    .SingleAsync(x => x.CompanyId == companyId &&
                                        x.Id == workerOriginal.ResourceId);
                                break;
                            }
                            await Task.Delay(100);
                        }
                        Ensure(linkedDraft is not null && trackedOriginal is not null &&
                            linkedDraft.State == JournalState.Draft &&
                            linkedDraft.Source == JournalSource.Reversal &&
                            linkedDraft.BranchId == workerBranch.ResourceId &&
                            linkedDraft.ReversesJournalId == workerOriginal.ResourceId &&
                            trackedOriginal.PendingReversalJournalId == linkedDraft.Id &&
                            linkedDraft.Lines.Sum(x => x.FunctionalDebit) ==
                                dedicatedRequest.Lines.Sum(x => x.Credit) &&
                            linkedDraft.Lines.Sum(x => x.FunctionalCredit) ==
                                dedicatedRequest.Lines.Sum(x => x.Debit),
                            "GL07-02 started opt-in worker did not persist exactly one linked historical draft");
                    }
                    finally { await workerApi.StopAsync(); }
                }
                using (var remaining = await SendDue(client, path, journalRequest.AccountingDate))
                using (var result = JsonDocument.Parse(await remaining.Content.ReadAsStringAsync()))
                    Ensure(remaining.StatusCode == HttpStatusCode.OK &&
                        result.RootElement.GetProperty("generatedCount").GetInt32() == 0 &&
                        result.RootElement.GetProperty("failedCount").GetInt32() == 0,
                        "GL07-02 re-scan after real background worker duplicated a linked draft");
                Console.WriteLine("  HOST PASS GL07-02 started opt-in worker with branch-scoped actor: one linked Draft, no auto-post, no replay, isolated SQL");
                Console.WriteLine("  HOST PASS GL03-GP05 linked scheduled draft, replay, cancellation suppression and authorization over signed JWT/HTTPS/SQL");
            }
            finally { await api.StopAsync(); }
        }
        finally { await issuerApp.StopAsync(); }
    }

    private static async Task<ReversalDueRunResult> Due(HttpClient client, string path, DateOnly asOf)
    {
        using var response = await SendDue(client, path, asOf);
        var body = await response.Content.ReadAsStringAsync();
        Ensure(response.StatusCode == HttpStatusCode.OK, "Production scheduled reversal failed: " + body);
        return JsonSerializer.Deserialize<ReversalDueRunResult>(body, JsonDefaults.Options)!;
    }
    private static async Task<HttpResponseMessage> SendDue(HttpClient client, string path, DateOnly asOf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "/reversals/run-due");
        request.Content = new StringContent(JsonSerializer.Serialize(new RunDueReversalsRequest(asOf), JsonDefaults.Options),
            Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }
    private static async Task<CommandReceipt> Command(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", Fixture.Key());
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonDefaults.Options), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        Ensure(response.StatusCode == HttpStatusCode.OK, "Production HTTP command failed: " + json);
        return JsonSerializer.Deserialize<CommandReceipt>(json, JsonDefaults.Options)!;
    }
    private static HttpClientHandler PinnedTransport(X509Certificate2 expected) => new()
    {
        ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null &&
            certificate.GetCertHashString(HashAlgorithmName.SHA256) == expected.GetCertHashString(HashAlgorithmName.SHA256)
    };
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
