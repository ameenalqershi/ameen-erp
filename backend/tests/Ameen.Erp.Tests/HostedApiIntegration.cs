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
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Ameen.Erp.Tests;

// Runs the production host/DI/authentication pipeline, with a temporary HTTPS OIDC issuer.
// Only the test issuer's transport certificate is pinned; JWT signature/issuer/audience/lifetime validation stays enabled.
public static class HostedApiIntegration
{
    public static async Task Run(string connection, Guid companyId, CreateJournalRequest journalRequest)
    {
        using var tlsKey = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(san.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(2));
        using var signingKey = RSA.Create(2048);
        var key = new RsaSecurityKey(signingKey) { KeyId = "ameen-integration-key" };
        var publicKey = signingKey.ExportParameters(false);
        var issuerBuilder = WebApplication.CreateBuilder(); issuerBuilder.Logging.ClearProviders();
        issuerBuilder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
        await using var issuerApp = issuerBuilder.Build();
        string issuer = "";
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
        const string audience = "ameen-integration-api";
        try
        {
            // GL04-01: opt-in draft-only worker. Check host wiring without
            // starting the opted-in worker or touching scheduled SQL data.
            WebApplication ScheduledTemplateHost(string? actorSubject, int? minutes = null, bool enabled = true) =>
                ApiHost.Build(Array.Empty<string>(), b =>
                {
                    b.Logging.ClearProviders();
                    b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Erp"] = connection,
                        ["Authentication:Authority"] = issuer,
                        ["Authentication:Audience"] = audience,
                        ["RecurringScheduler:Enabled"] = enabled ? "true" : "false",
                        ["RecurringScheduler:ActorSubject"] = actorSubject,
                        ["RecurringScheduler:IntervalMinutes"] = minutes?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
                });
            await using (var disabledScheduler = ScheduledTemplateHost(null, -1, enabled: false))
                Ensure(!disabledScheduler.Services.GetServices<IHostedService>().Any(x =>
                    x.GetType().Name == "RecurringDraftScheduler"),
                    "GL04-01 recurring worker incorrectly enabled without explicit opt-in");
            foreach (var invalidActor in new string?[] { null, "", "   ", new string('a', 201) })
            {
                try
                {
                    await using var invalidScheduler = ScheduledTemplateHost(invalidActor);
                    throw new Exception("GL04-01 missing/invalid scheduler actor was accepted");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("ActorSubject", StringComparison.Ordinal)) { }
            }
            foreach (var invalidMinutes in new[] { 0, -1, 1441 })
            {
                try
                {
                    await using var invalidScheduler = ScheduledTemplateHost("sql-clerk", invalidMinutes);
                    throw new Exception("GL04-01 invalid scheduler interval was accepted");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("IntervalMinutes", StringComparison.Ordinal)) { }
            }
            await using (var configuredScheduler = ScheduledTemplateHost("sql-clerk", 15))
                Ensure(configuredScheduler.Services.GetServices<IHostedService>().Any(x =>
                    x.GetType().Name == "RecurringDraftScheduler"),
                    "GL04-01 explicit schedule configuration did not register draft worker");
            Console.WriteLine("  HOST PASS GL04-01 opt-in recurring scheduler actor/interval guard and registration (not launched)");
            await using var api = ApiHost.Build(Array.Empty<string>(), b =>
            {
                b.Logging.ClearProviders();
                b.Logging.AddSimpleConsole(); b.Logging.SetMinimumLevel(LogLevel.Error);
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Erp"] = connection, ["Authentication:Authority"] = issuer,
                    ["Authentication:Audience"] = audience, ["AllowedHosts"] = "127.0.0.1;localhost",
                    ["Cors:Origins:0"] = "https://permitted.example",
                    ["RecurringScheduler:Enabled"] = "false"
                });
                b.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
                b.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                    o => o.BackchannelHttpHandler = PinnedTransport(certificate));
            });
            Ensure(!api.Services.GetServices<IHostedService>().Any(x =>
                x.GetType().Name == "RecurringDraftScheduler"),
                "GL04-01 recurring draft worker started without opt-in");
            await api.StartAsync();
            try
            {
                using var client = new HttpClient(PinnedTransport(certificate)) { BaseAddress = new Uri(api.Urls.Single()) };
                using var alive = await client.GetAsync("/health/live"); Ensure(alive.StatusCode == HttpStatusCode.OK, "Production liveness failed");
                using var anonymous = await client.GetAsync("/api/v1/companies"); Ensure(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Anonymous request passed");
                using var anonymousSpec = await client.GetAsync("/openapi/v1.json"); Ensure(anonymousSpec.StatusCode == HttpStatusCode.Unauthorized, "OpenAPI exposed anonymously");

                string Token(string subject, string? role = null, string? overrideAudience = null, string? overrideIssuer = null, int minutes = 5, SecurityKey? alternateKey = null)
                {
                    var claims = new List<Claim> { new("sub", subject) };
                    if (role is not null) claims.Add(new Claim("roles", role));
                    var now = DateTime.UtcNow;
                    var token = new JwtSecurityToken(overrideIssuer ?? issuer, overrideAudience ?? audience, claims,
                        notBefore: now.AddMinutes(-10), expires: now.AddMinutes(minutes),
                        signingCredentials: new SigningCredentials(alternateKey ?? key, SecurityAlgorithms.RsaSha256));
                    return new JwtSecurityTokenHandler().WriteToken(token);
                }
                void Authenticate(string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                foreach (var invalid in new[]
                {
                    Token("sql-clerk", overrideAudience: "wrong-audience"),
                    Token("sql-clerk", overrideIssuer: "https://wrong-issuer.example"), Token("sql-clerk", minutes: -2)
                })
                {
                    Authenticate(invalid); using var rejected = await client.GetAsync("/api/v1/companies");
                    Ensure(rejected.StatusCode == HttpStatusCode.Unauthorized, "Invalid JWT was accepted");
                }
                using (var forged = RSA.Create(2048))
                {
                    Authenticate(Token("sql-clerk", alternateKey: new RsaSecurityKey(forged) { KeyId = key.KeyId }));
                    using var rejected = await client.GetAsync("/api/v1/companies");
                    Ensure(rejected.StatusCode == HttpStatusCode.Unauthorized, "Forged signature was accepted");
                }
                Authenticate(Token("sql-clerk"));
                using var ready = await client.GetAsync("/health/ready"); Ensure(ready.StatusCode == HttpStatusCode.OK, "Production readiness failed");
                using var spec = await client.GetAsync("/openapi/v1.json");
                Ensure(spec.StatusCode == HttpStatusCode.OK, "OpenAPI generation failed: " + await spec.Content.ReadAsStringAsync());
                var specJson = await spec.Content.ReadAsStringAsync();
                var artifacts = Environment.GetEnvironmentVariable("AMEEN_TEST_ARTIFACTS");
                if (!string.IsNullOrWhiteSpace(artifacts))
                {
                    Directory.CreateDirectory(artifacts);
                    using var snapshot = JsonDocument.Parse(specJson);
                    await File.WriteAllTextAsync(Path.Combine(artifacts, "openapi-v1.json"),
                        JsonSerializer.Serialize(snapshot.RootElement, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                }
                using (var doc = JsonDocument.Parse(specJson))
                {
                    var root = doc.RootElement;
                    Ensure(root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString() == "bearer", "Missing bearer schema");
                    var command = root.GetProperty("paths").GetProperty("/api/v1/companies/{companyId}/journals").GetProperty("post");
                    Ensure(command.GetProperty("parameters").EnumerateArray().Any(x =>
                        x.GetProperty("name").GetString() == "Idempotency-Key" && x.GetProperty("required").GetBoolean()), "Missing idempotency header contract");
                    Ensure(command.GetProperty("security").GetArrayLength() > 0, "Missing endpoint bearer requirement");
                    Ensure(specJson.Contains("\"decimal\"", StringComparison.Ordinal) && specJson.Contains("\"date\"", StringComparison.Ordinal), "Missing decimal/date schema");
                    Ensure(specJson.Contains("/inventory-openings/{id}/print", StringComparison.Ordinal),
                        "INV06 print OpenAPI path missing");
                    foreach (var route in new[] {
                        "/items/{id}/usage", "/items/{id}/stock-movements",
                        "/warehouses/{id}/usage", "/warehouses/{id}/stock-movements",
                        "/items/{itemId}/lots/{id}/usage", "/items/{itemId}/lots/{id}/stock-movements",
                        "/items/{itemId}/serials/{id}/usage", "/items/{itemId}/serials/{id}/stock-movements" })
                        Ensure(specJson.Contains(route, StringComparison.Ordinal),
                            "Missing INV01/04/05 stock reference OpenAPI path: " + route);
                }

                // SYS06-OP03: expose retained member references and audit only to authorized tenant staff.
                var usageMember = await Command(client,
                    $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest("sys06-http-usage-reader",
                        new[] { Permissions.Read, Permissions.Members }, true, Array.Empty<Guid>(), null));
                var usagePath = $"/api/v1/companies/{companyId}/memberships/{usageMember.ResourceId}/usage";
                var historyPath = $"/api/v1/companies/{companyId}/memberships/{usageMember.ResourceId}/audit-events";
                Ensure(specJson.Contains("/memberships/{id}/usage", StringComparison.Ordinal) &&
                    specJson.Contains("/memberships/{id}/audit-events", StringComparison.Ordinal),
                    "SYS06 usage/audit OpenAPI contract missing");
                Authenticate(Token("sys06-http-usage-reader"));
                using (var response = await client.GetAsync(usagePath))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("membershipId").GetGuid() == usageMember.ResourceId &&
                        json.RootElement.GetProperty("subject").GetString() == "sys06-http-usage-reader" &&
                        !json.RootElement.GetProperty("hasReferences").GetBoolean(),
                        "SYS06 HTTPS usage did not return only the tenant member");
                using (var response = await client.GetAsync(historyPath))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden,
                        "SYS06 member admin without Audit read sensitive history");
                Authenticate(Token("sql-clerk"));
                using (var response = await client.GetAsync(historyPath + "?limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("items").GetArrayLength() == 1,
                        "SYS06 privileged HTTPS audit did not read creation history");
                Authenticate(Token("sys06-op03-nonmember"));
                using (var response = await client.GetAsync(usagePath))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden,
                        "SYS06 nonmember leaked usage of a tenant member");
                Authenticate(Token("sql-clerk"));
                Console.WriteLine("  HOST PASS SYS06 membership usage/audit tenant and Audit permission over HTTPS/JWT/SQL");

                // SYS06-AC02: exercise the production HTTPS/OIDC/JWT endpoint and revocation boundary.
                var memberPath = $"/api/v1/companies/{companyId}/memberships";
                var subject = "sys06-http-revoked";
                var httpMember = await Command(client, memberPath,
                    new GrantMembershipRequest(subject, new[] { Permissions.Read },
                        true, Array.Empty<Guid>(), null));
                Authenticate(Token(subject));
                using (var canRead = await client.GetAsync($"/api/v1/companies/{companyId}/branches"))
                    Ensure(canRead.StatusCode == HttpStatusCode.OK,
                        "SYS06 active read-only JWT was incorrectly denied");
                Authenticate(Token("sql-clerk"));
                var revoked = await Command(client, memberPath + $"/{httpMember.ResourceId}/status",
                    new SetMembershipStatusRequest(httpMember.Version, false, "إلغاء صلاحية القراءة"));
                Authenticate(Token(subject));
                using (var revokedResponse = await client.GetAsync($"/api/v1/companies/{companyId}/branches"))
                    Ensure(revokedResponse.StatusCode == HttpStatusCode.Forbidden,
                        "SYS06 revoked JWT continued reading tenant branches");
                Authenticate(Token("sql-clerk"));
                using (var current = await client.GetAsync(memberPath + "?limit=200"))
                using (var json = JsonDocument.Parse(await current.Content.ReadAsStringAsync()))
                {
                    Ensure(current.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("items").EnumerateArray().Any(x =>
                            x.GetProperty("id").GetGuid() == httpMember.ResourceId &&
                            x.GetProperty("createdBy").GetString() == "sql-clerk" &&
                            x.GetProperty("createdAt").GetDateTimeOffset() != default &&
                            x.GetProperty("revision").GetInt32() == 2 &&
                            x.GetProperty("lastModifiedAt").GetDateTimeOffset() ==
                            x.GetProperty("createdAt").GetDateTimeOffset() &&
                            !x.GetProperty("isActive").GetBoolean()),
                        "SYS06 deactivated member was removed from the member register");
                    Ensure(!json.RootElement.GetRawText().Contains("\"password\":", StringComparison.OrdinalIgnoreCase),
                        "SYS06 membership listing exposed a password field");
                }
                await Command(client, memberPath + $"/{httpMember.ResourceId}/status",
                    new SetMembershipStatusRequest(revoked.Version, true, "إعادة تفعيل معتمدة"));
                Authenticate(Token(subject));
                using (var restored = await client.GetAsync($"/api/v1/companies/{companyId}/branches"))
                    Ensure(restored.StatusCode == HttpStatusCode.OK,
                        "SYS06 explicitly restored JWT could not read branches");
                Authenticate(Token("sql-clerk"));
                Console.WriteLine("  HOST PASS SYS06 membership revoke/restore, retained identity and no password field over HTTPS/JWT/SQL");

                var path = $"/api/v1/companies/{companyId}/journals";
                using var unknownField = new HttpRequestMessage(HttpMethod.Post, path);
                unknownField.Headers.Add("Idempotency-Key", Fixture.Key());
                var body = JsonSerializer.Serialize(journalRequest, JsonDefaults.Options);
                unknownField.Content = new StringContent(body[..^1] + ",\"unknownProperty\":true}", Encoding.UTF8, "application/json");
                using var bad = await client.SendAsync(unknownField); Ensure(bad.StatusCode == HttpStatusCode.BadRequest, "Unknown JSON field accepted");
                using var missingField = new HttpRequestMessage(HttpMethod.Post, path);
                missingField.Headers.Add("Idempotency-Key", Fixture.Key());
                missingField.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var missing = await client.SendAsync(missingField); Ensure(missing.StatusCode == HttpStatusCode.BadRequest, "Missing required fields accepted");
                using var forgedSource = new HttpRequestMessage(HttpMethod.Post, path);
                forgedSource.Headers.Add("Idempotency-Key", Fixture.Key());
                forgedSource.Content = new StringContent(body[..^1] + ",\"source\":\"Recurring\"}", Encoding.UTF8, "application/json");
                using var forgedSourceResult = await client.SendAsync(forgedSource);
                Ensure(forgedSourceResult.StatusCode == HttpStatusCode.BadRequest, "Manual API accepted forged automated provenance");

                var requestKey = Fixture.Key();
                var printJournalRequest = journalRequest with
                {
                    Description = "قيد <script>alert(1)</script> آمن",
                    ExternalReference = "000070-PRINT"
                };
                var receipt = await Command(client, path, printJournalRequest, requestKey);
                var manualPrintPath = path + $"/{receipt.ResourceId}/print";
                using (var printed = await client.GetAsync(manualPrintPath))
                {
                    var html = await printed.Content.ReadAsStringAsync();
                    Ensure(printed.StatusCode == HttpStatusCode.OK &&
                        printed.Content.Headers.ContentType?.MediaType == "text/html" &&
                        printed.Headers.TryGetValues("Content-Security-Policy", out var policy) &&
                        policy.Any(x => x.Contains("default-src 'none'", StringComparison.Ordinal)) &&
                        printed.Headers.TryGetValues("Cache-Control", out var cache) &&
                        cache.Any(x => x.Contains("no-store", StringComparison.Ordinal)) &&
                        html.Contains("lang=\"ar\" dir=\"rtl\"", StringComparison.Ordinal) &&
                        html.Contains("000070-PRINT", StringComparison.Ordinal) &&
                        html.Contains("&lt;script&gt;", StringComparison.Ordinal) &&
                        !html.Contains("<script>", StringComparison.OrdinalIgnoreCase) &&
                        html.Contains("قيد يومية", StringComparison.Ordinal),
                        "GL03 printable journal failed HTML escaping or protected content");
                }
                Authenticate(Token("journal-print-no-membership"));
                using (var printDenied = await client.GetAsync(manualPrintPath))
                    Ensure(printDenied.StatusCode == HttpStatusCode.Forbidden,
                        "GL03 print leaked to a JWT without tenant membership");
                Authenticate(Token("sql-clerk"));
                Console.WriteLine("  HOST PASS GL03-A07 script-safe printable manual journal with tenant-scoped HTTPS/JWT");
                using (var previewResponse = await client.GetAsync(path + $"/{receipt.ResourceId}/impact-preview"))
                using (var impact = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync()))
                {
                    Ensure(previewResponse.StatusCode == HttpStatusCode.OK &&
                        impact.RootElement.GetProperty("canSubmit").GetBoolean() &&
                        impact.RootElement.GetProperty("isBalanced").GetBoolean() &&
                        impact.RootElement.GetProperty("difference").GetString() == "0" &&
                        impact.RootElement.GetProperty("lines").GetArrayLength() == journalRequest.Lines.Length &&
                        impact.RootElement.GetProperty("functionalDebit").GetString() == "100",
                        "GL03-A05 journal impact preview failed production HTTPS/JWT/SQL snapshot");
                    Console.WriteLine("  HOST PASS GL03-A05 read-only journal impact preview over HTTPS/JWT/SQL");
                }
                using (var response = await client.GetAsync(path + "?source=Manual&search=" + Uri.EscapeDataString(receipt.Number ?? throw new Exception("Missing journal number"))))
                {
                    using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        result.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == receipt.ResourceId),
                        "GL03 list/search must find a manual journal by number over HTTPS/JWT/SQL");
                }
                using (var detail = await client.GetAsync(path + $"/{receipt.ResourceId}"))
                using (var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("revision").GetInt32() == 1 &&
                        json.RootElement.GetProperty("createdBy").GetString() == "sql-clerk" &&
                        json.RootElement.GetProperty("lastModifiedAt").GetDateTimeOffset() == json.RootElement.GetProperty("createdAt").GetDateTimeOffset(),
                        "GL03 creation audit fields differ over HTTPS/JWT/SQL");
                using (var forgedRevision = new HttpRequestMessage(HttpMethod.Post, path))
                {
                    forgedRevision.Headers.Add("Idempotency-Key", Fixture.Key());
                    forgedRevision.Content = new StringContent(body[..^1] + ",\"revision\":999}", Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(forgedRevision);
                    Ensure(rejected.StatusCode == HttpStatusCode.BadRequest, "GL03 accepted forged numeric revision");
                }
                var replay = await Command(client, path, printJournalRequest, requestKey);
                Ensure(receipt == replay, "Production HTTP/SQL replay differs");
                receipt = await Command(client, path + $"/{receipt.ResourceId}/submit", new TransitionRequest(receipt.Version));
                Authenticate(Token("sql-approver"));
                receipt = await Command(client, path + $"/{receipt.ResourceId}/approve", new TransitionRequest(receipt.Version));
                receipt = await Command(client, path + $"/{receipt.ResourceId}/post", new TransitionRequest(receipt.Version));
                Ensure(receipt.Status == "Posted", "Production HTTP posting failed");
                using (var detail = await client.GetAsync(path + $"/{receipt.ResourceId}"))
                using (var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("revision").GetInt32() == 4 &&
                        json.RootElement.GetProperty("lastModifiedAt").GetDateTimeOffset() >= json.RootElement.GetProperty("createdAt").GetDateTimeOffset(),
                        "GL03 HTTPS workflow revision was not persisted");
                Console.WriteLine("  HOST PASS GL03-FG08 server audit identity, integer revision, forged-input rejection and workflow over HTTPS/JWT/SQL");

                // GL03-T01: a posted manual journal must be visible to the
                // authorized account ledger under its exact document reference,
                // with historical functional/original amounts and no duplicate.
                var gl03LedgerPath = $"/api/v1/companies/{companyId}/reports/accounts/" +
                    journalRequest.Lines[0].AccountId +
                    "/ledger?from=2026-01-01&to=2026-12-31&branchId=" +
                    journalRequest.BranchId + "&externalReference=000070-PRINT";
                using (var postedLedger = await client.GetAsync(gl03LedgerPath))
                using (var ledgerBody = JsonDocument.Parse(
                    await postedLedger.Content.ReadAsStringAsync()))
                {
                    var ledgerRoot = ledgerBody.RootElement;
                    var entries = ledgerRoot.GetProperty("entries");
                    Ensure(postedLedger.StatusCode == HttpStatusCode.OK &&
                        ledgerRoot.GetProperty("companyId").GetGuid() == companyId &&
                        ledgerRoot.GetProperty("accountId").GetGuid() ==
                            journalRequest.Lines[0].AccountId &&
                        ledgerRoot.GetProperty("externalReference").GetString() == "000070-PRINT" &&
                        entries.GetProperty("totalCount").GetInt32() == 1 &&
                        entries.GetProperty("items").GetArrayLength() == 1 &&
                        entries.GetProperty("items")[0].GetProperty("journalId").GetGuid() ==
                            receipt.ResourceId &&
                        entries.GetProperty("items")[0].GetProperty("number").GetString() ==
                            receipt.Number &&
                        entries.GetProperty("items")[0].GetProperty("debit").GetString() == "100" &&
                        entries.GetProperty("items")[0].GetProperty("credit").GetString() == "0",
                        "GL03-T01 posted journal missing or duplicated in the HTTP account ledger");
                }
                Authenticate(Token("gl03-t01-no-membership"));
                using (var deniedLedger = await client.GetAsync(gl03LedgerPath))
                    Ensure(deniedLedger.StatusCode == HttpStatusCode.Forbidden,
                        "GL03-T01 account ledger leaked outside the JWT tenant");
                Authenticate(Token("sql-approver"));
                Console.WriteLine("  HOST PASS GL03-T01 balanced posted journal with exact account-ledger reference and JWT tenant denial");

                using var hierarchy = await client.GetAsync($"/api/v1/companies/{companyId}/reports/trial-balance?from=2026-01-01&to=2026-12-31&accountLevel=1&showZero=false");
                Ensure(hierarchy.StatusCode == HttpStatusCode.OK, "Production report filters failed");
                using (var report = JsonDocument.Parse(await hierarchy.Content.ReadAsStringAsync()))
                {
                    Ensure(report.RootElement.GetProperty("accountLevel").GetInt32() == 1 && !report.RootElement.GetProperty("showZero").GetBoolean(), "Report query binding differs");
                }

                Authenticate(Token("sql-clerk"));
                var processingDraft = await Command(client, path, journalRequest);
                var processingSubmitted = await Command(client, path + $"/{processingDraft.ResourceId}/submit", new TransitionRequest(processingDraft.Version));
                Authenticate(Token("sql-approver"));
                using (var response = await client.GetAsync(path + $"/{processingSubmitted.ResourceId}/processing-preview?action=approve"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("canExecute").GetBoolean(), "HTTP processing preview failed");
                using (var detail = await client.GetAsync(path + $"/{processingSubmitted.ResourceId}"))
                using (var journal = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                {
                    Ensure(detail.StatusCode == HttpStatusCode.OK, "GL06 textual range test journal missing");
                    var period = journal.RootElement.GetProperty("fiscalPeriodId").GetGuid();
                    var number = journal.RootElement.GetProperty("number").GetString()!;
                    using var queue = await client.GetAsync(path + "/processing-queue?periodId=" + period +
                        "&action=approve&numberRange=" + Uri.EscapeDataString(number));
                    using var queueBody = JsonDocument.Parse(await queue.Content.ReadAsStringAsync());
                    Ensure(queue.StatusCode == HttpStatusCode.OK &&
                        queueBody.RootElement.GetProperty("items").EnumerateArray().Any(x =>
                            x.GetProperty("journal").GetProperty("id").GetGuid() == processingSubmitted.ResourceId),
                        "GL06 textual number range missing expected journal through HTTPS/JWT/SQL");
                }
                var durableHttpKey = Fixture.Key();
                var batch = new ProcessJournalsRequest("approve", new[] { new JournalProcessingItem(processingSubmitted.ResourceId, processingSubmitted.Version, Fixture.Key()), new JournalProcessingItem(receipt.ResourceId, receipt.Version, Fixture.Key()) }, durableHttpKey);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var response = await client.PostAsync(path + "/process-batch", new StringContent(JsonSerializer.Serialize(batch, JsonDefaults.Options), Encoding.UTF8, "application/json"));
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Ensure(response.StatusCode == HttpStatusCode.OK && !json.RootElement.GetProperty("allSucceeded").GetBoolean() && json.RootElement.GetProperty("succeededCount").GetInt32() == 1 && json.RootElement.GetProperty("failedCount").GetInt32() == 1, "HTTP mixed processing/replay failed");
                }
                using (var response = await client.GetAsync(path + "/processing-batches/by-key/" + durableHttpKey))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("revision").GetInt32() == 3 &&
                        json.RootElement.GetProperty("createdBy").GetString() == "sql-approver" &&
                        json.RootElement.GetProperty("lastModifiedAt").GetDateTimeOffset() >= json.RootElement.GetProperty("createdAt").GetDateTimeOffset() &&
                        !json.RootElement.GetProperty("allSucceeded").GetBoolean() &&
                        json.RootElement.GetProperty("rows").GetArrayLength() == 2,
                        "GL06 durable processing batch not retrievable over HTTPS/JWT/SQL");
                // GL06-A05: the persisted result is scoped to its initiating
                // actor, even when a second authenticated member has Read and
                // has access to the source journal. JWT identity, not a query
                // parameter or batch key alone, controls history visibility.
                var durableHttpPath = path + "/processing-batches/by-key/" + durableHttpKey;
                var durableReadCounts = await ReadOnlyCounts(connection, companyId);
                Authenticate(Token("sql-clerk"));
                using (var otherActorBatch = await client.GetAsync(durableHttpPath))
                    Ensure(otherActorBatch.StatusCode == HttpStatusCode.NotFound,
                        "GL06-A05 other tenant member leaked another actor's batch and durable decisions");
                Authenticate(Token("audit-no-membership"));
                using (var nonmemberBatch = await client.GetAsync(durableHttpPath))
                    Ensure(nonmemberBatch.StatusCode == HttpStatusCode.Forbidden,
                        "GL06-A05 unrelated JWT subject disclosed a durable batch");
                Authenticate(Token("sql-approver"));
                using (var missingBatch = await client.GetAsync(
                    path + "/processing-batches/by-key/" + Fixture.Key()))
                    Ensure(missingBatch.StatusCode == HttpStatusCode.NotFound,
                        "GL06-A05 missing batch returned an unintended success");
                using (var recoveredBatch = await client.GetAsync(durableHttpPath))
                using (var recoveredJson = JsonDocument.Parse(await recoveredBatch.Content.ReadAsStringAsync()))
                    Ensure(recoveredBatch.StatusCode == HttpStatusCode.OK &&
                        recoveredJson.RootElement.GetProperty("revision").GetInt32() == 3 &&
                        recoveredJson.RootElement.GetProperty("createdBy").GetString() == "sql-approver" &&
                        recoveredJson.RootElement.GetProperty("rows").GetArrayLength() == 2 &&
                        !recoveredJson.RootElement.GetProperty("allSucceeded").GetBoolean(),
                        "GL06-A05 authorized owner did not recover the original durable result");
                Ensure(durableReadCounts == await ReadOnlyCounts(connection, companyId),
                    "GL06-A05 authorized/denied durable result reads mutated SQL journals, audit, receipts or outbox");
                Console.WriteLine("  HOST PASS GL06-A05 owner-only batch result, nonmember JWT denial, missing-key 404, stable retry and read-only SQL");
                using (var response = await client.GetAsync(path + $"/{receipt.ResourceId}/processing-history"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP processing history duplicated");
                // GL05-A05: historical decisions are visible to authorized auditors,
                // paginated over production HTTPS/JWT/SQL, and hidden from the author.
                Authenticate(Token("sql-owner"));
                using (var h = await client.GetAsync(path + $"/{processingSubmitted.ResourceId}/processing-history?offset=0&limit=1"))
                using (var j = JsonDocument.Parse(await h.Content.ReadAsStringAsync()))
                    Ensure(h.StatusCode == HttpStatusCode.OK &&
                        j.RootElement.GetProperty("totalCount").GetInt32() == 1 &&
                        j.RootElement.GetProperty("items").GetArrayLength() == 1 &&
                        j.RootElement.GetProperty("items")[0].GetProperty("action").GetString() ==
                            "journal.processing-succeeded",
                        "GL05 HTTP review decision not retrievable with auditor permission");
                // SQL fixture grants Permissions.All to sql-clerk, including Audit.
                // Use an explicit read-only member to test the intended audit boundary.
                await Command(client, $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest("gl05-review-readonly",
                        new[] { Permissions.Read }, true, Array.Empty<Guid>(), null));
                Authenticate(Token("gl05-review-readonly"));
                using (var gl05HistoryDenied = await client.GetAsync(path + $"/{processingSubmitted.ResourceId}/processing-history"))
                    Ensure(gl05HistoryDenied.StatusCode == HttpStatusCode.Forbidden,
                        "GL05 HTTP read-only member retrieved privileged review audit history");
                Authenticate(Token("sql-approver"));
                Console.WriteLine("  HOST PASS GL05-A05 scoped review outcome/history with paging and JWT audit permission");
                // Finish the additional approved journal so it cannot interfere with later close checks.
                using (var response = await client.GetAsync(path + $"/{processingSubmitted.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    await Command(client, path + $"/{processingSubmitted.ResourceId}/post", new TransitionRequest(json.RootElement.GetProperty("version").GetGuid()));
                // GL06-T01 positive acceptance: submit, approve and post through the
                // production HTTPS/JWT batch endpoint, then replay its durable key.
                Authenticate(Token("sql-clerk"));
                var positiveDraft = await Command(client, path, journalRequest);
                var positiveSubmitted = await Command(client, path + $"/{positiveDraft.ResourceId}/submit",
                    new TransitionRequest(positiveDraft.Version));
                Authenticate(Token("sql-approver"));
                var positiveApproved = await Command(client, path + $"/{positiveDraft.ResourceId}/approve",
                    new TransitionRequest(positiveSubmitted.Version));
                var positiveBatchKey = Fixture.Key();
                var positiveRequest = new ProcessJournalsRequest("post",
                    new[] { new JournalProcessingItem(positiveApproved.ResourceId, positiveApproved.Version, Fixture.Key()) }, positiveBatchKey);
                string? firstRow = null;
                for (var replayAttempt = 0; replayAttempt < 2; replayAttempt++)
                {
                    using var response = await client.PostAsync(path + "/process-batch",
                        new StringContent(JsonSerializer.Serialize(positiveRequest, JsonDefaults.Options), Encoding.UTF8, "application/json"));
                    using var positiveBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Ensure(response.StatusCode == HttpStatusCode.OK && positiveBody.RootElement.GetProperty("allSucceeded").GetBoolean() &&
                        positiveBody.RootElement.GetProperty("succeededCount").GetInt32() == 1 &&
                        positiveBody.RootElement.GetProperty("failedCount").GetInt32() == 0,
                        "GL06 positive HTTPS/JWT batch posting failed");
                    var row = positiveBody.RootElement.GetProperty("rows")[0].GetRawText();
                    Ensure(firstRow is null || firstRow == row, "GL06 positive HTTP posting replay changed receipt");
                    firstRow = row;
                }
                using (var response = await client.GetAsync(path + "/processing-batches/by-key/" + positiveBatchKey))
                using (var positiveBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        positiveBody.RootElement.GetProperty("revision").GetInt32() == 2 &&
                        positiveBody.RootElement.GetProperty("allSucceeded").GetBoolean() &&
                        positiveBody.RootElement.GetProperty("rows").GetArrayLength() == 1,
                        "GL06 positive durable batch revision or outcome differed on HTTPS/JWT/SQL");
                using (var response = await client.GetAsync(path + $"/{positiveDraft.ResourceId}"))
                using (var positiveBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        positiveBody.RootElement.GetProperty("state").GetString() == "Posted",
                        "GL06 positive batch did not persist posted journal state");
                Console.WriteLine("  HOST PASS GL06-T01 positive batch post, durable replay and posted state over HTTPS/JWT/SQL");
                Console.WriteLine("  HOST PASS GL05/06 preview, mixed batch with per-item keys, replay and rejection history over HTTPS/JWT/SQL");

                // GL02-C01: direct subsidiary control opening is forbidden;
                // reject without journal/audit/receipt/outbox side effects.
                Authenticate(Token("sql-owner"));
                var openingControl = await Command(client,
                    $"/api/v1/companies/{companyId}/accounts",
                    new CreateAccountRequest("HTTP-GL02-CTRL-GUARD", "رقابة افتتاح", AccountType.Asset,
                        null, true, true, false, null, NormalBalance.Debit));
                var openingGuardBefore = await ReadOnlyCounts(connection, companyId);
                Authenticate(Token("sql-clerk"));
                var forbiddenOpening = GeneralOpeningTests.Request(journalRequest) with
                {
                    ExternalReference = "GL02-CTRL-HTTP-" + Fixture.Key(),
                    Lines = journalRequest.Lines.Select((line, index) => index == 0
                        ? line with { AccountId = openingControl.ResourceId } : line).ToArray()
                };
                using (var deniedOpeningRequest = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/v1/companies/{companyId}/general-openings"))
                {
                    deniedOpeningRequest.Headers.Add("Idempotency-Key", Fixture.Key());
                    deniedOpeningRequest.Content = new StringContent(
                        JsonSerializer.Serialize(forbiddenOpening, JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var deniedOpening = await client.SendAsync(deniedOpeningRequest);
                    using var error = JsonDocument.Parse(await deniedOpening.Content.ReadAsStringAsync());
                    Ensure((int)deniedOpening.StatusCode == 422 &&
                        error.RootElement.GetProperty("code").GetString() == "control_account_protected",
                        "GL02-C01 HTTPS/JWT accepted forbidden direct control-account opening");
                }
                Ensure(openingGuardBefore == await ReadOnlyCounts(connection, companyId),
                    "GL02-C01 rejected HTTPS opening persisted journal/audit/receipt/outbox");
                using (var absentOpening = await client.GetAsync(
                    $"/api/v1/companies/{companyId}/general-openings?search=" +
                    Uri.EscapeDataString(forbiddenOpening.ExternalReference!)))
                using (var found = JsonDocument.Parse(await absentOpening.Content.ReadAsStringAsync()))
                    Ensure(absentOpening.StatusCode == HttpStatusCode.OK &&
                        found.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "GL02-C01 forbidden control opening remained visible after rejection");
                Console.WriteLine("  HOST PASS GL02-C01 control opening rejects 422 over HTTPS/JWT, no SQL writes or leaked document");
                const string printDescription = "افتتاح <script>alert(1)</script> & نهاية";
                var generalOpening = await Command(client, $"/api/v1/companies/{companyId}/general-openings",
                    GeneralOpeningTests.Request(journalRequest) with
                    {
                        Description = printDescription, ExternalReference = "0000048-PRINT"
                    });
                var printPath = $"/api/v1/companies/{companyId}/general-openings/{generalOpening.ResourceId}/print";
                using (var printable = await client.GetAsync(printPath))
                {
                    var html = await printable.Content.ReadAsStringAsync();
                    Ensure(printable.StatusCode == HttpStatusCode.OK &&
                        printable.Content.Headers.ContentType?.MediaType == "text/html" &&
                        printable.Headers.TryGetValues("Cache-Control", out var cache) &&
                        cache.Any(x => x.Contains("no-store", StringComparison.OrdinalIgnoreCase)) &&
                        printable.Headers.TryGetValues("Content-Security-Policy", out var csp) &&
                        csp.Any(x => x.Contains("default-src 'none'", StringComparison.Ordinal)) &&
                        html.Contains("lang=\"ar\" dir=\"rtl\"", StringComparison.Ordinal) &&
                        html.Contains("0000048-PRINT", StringComparison.Ordinal) &&
                        html.Contains("&lt;script&gt;", StringComparison.Ordinal) &&
                        !html.Contains("<script>", StringComparison.OrdinalIgnoreCase) &&
                        html.Contains("&amp;", StringComparison.Ordinal) &&
                        html.Contains("TB-2026", StringComparison.Ordinal) &&
                        html.Contains("100", StringComparison.Ordinal),
                        "GL02 print route failed HTML encoding, historical opening content or security headers");
                }
                Authenticate(Token("gl02-print-nonmember"));
                using (var printDenied = await client.GetAsync(printPath))
                    Ensure(printDenied.StatusCode == HttpStatusCode.Forbidden,
                        "GL02 printable opening was exposed to nonmember JWT");
                Authenticate(Token("sql-clerk"));
                using (var printWrongSource = await client.GetAsync(
                    $"/api/v1/companies/{companyId}/general-openings/{receipt.ResourceId}/print"))
                    Ensure((int)printWrongSource.StatusCode == 422,
                        "GL02 print accepted manual journal as a general opening");
                Console.WriteLine("  HOST PASS GL02-A07 RTL printable HTML, XSS escaping, scoped HTTPS/JWT and no mutation");
                using (var previewResponse = await client.GetAsync($"/api/v1/companies/{companyId}/general-openings/{generalOpening.ResourceId}/impact-preview"))
                using (var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync()))
                {
                    Ensure(previewResponse.StatusCode == HttpStatusCode.OK &&
                        preview.RootElement.GetProperty("source").GetString() == "GeneralOpening" &&
                        preview.RootElement.GetProperty("canSubmit").GetBoolean() &&
                        preview.RootElement.GetProperty("isBalanced").GetBoolean() &&
                        preview.RootElement.GetProperty("functionalDebit").GetString() == "100" &&
                        preview.RootElement.GetProperty("functionalCredit").GetString() == "100" &&
                        preview.RootElement.GetProperty("difference").GetString() == "0",
                        "GL02-A05 production opening impact preview failed HTTPS/JWT/SQL");
                    Console.WriteLine("  HOST PASS GL02-A05 general opening read-only impact preview over HTTPS/JWT/SQL");
                }
                generalOpening = await Command(client, $"/api/v1/companies/{companyId}/general-openings/{generalOpening.ResourceId}",
                    new EditGeneralOpeningRequest(generalOpening.Version, "تعديل رأس HTTP", "TB-HEADER-HTTP", journalRequest.Lines,
                        new(journalRequest.BranchId, Fixture.Date.AddDays(1), Fixture.Date, "HTTP-HEADER", Fixture.Date.AddDays(2))), method: HttpMethod.Put);
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/general-openings?search=TB-HEADER-HTTP"))
                {
                    using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid() == generalOpening.ResourceId,
                        "GL02 search must find saved opening by balance reference");
                }
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/general-openings?search=NOT-FOUND-GL02"))
                {
                    using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "GL02 search must exclude unrelated documents");
                }
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/general-openings/{generalOpening.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("source").GetString() == "GeneralOpening" && json.RootElement.GetProperty("openingRevision").GetInt32() == 2 && json.RootElement.GetProperty("documentDate").GetString() == "2026-10-21" && json.RootElement.GetProperty("operationStartsOn").GetString() == "2026-10-22", "HTTP general opening header metadata missing");
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/general-openings/{generalOpening.ResourceId}/settlement-preview"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && !json.RootElement.GetProperty("allSubledgersImplemented").GetBoolean(), "HTTP settlement coverage overstated");
                generalOpening = await Command(client, path + $"/{generalOpening.ResourceId}/submit", new TransitionRequest(generalOpening.Version));
                Authenticate(Token("sql-approver"));
                generalOpening = await Command(client, path + $"/{generalOpening.ResourceId}/approve", new TransitionRequest(generalOpening.Version));
                generalOpening = await Command(client, path + $"/{generalOpening.ResourceId}/post", new TransitionRequest(generalOpening.Version));
                Authenticate(Token("sql-clerk"));
                var generalInverse = await Command(client, path + $"/{generalOpening.ResourceId}/reverse", new ReverseJournalRequest(generalOpening.Version, Fixture.Date, "تصحيح افتتاح HTTP"));
                using (var inversePrint = await client.GetAsync(path + $"/{generalInverse.ResourceId}/print"))
                {
                    var html = await inversePrint.Content.ReadAsStringAsync();
                    Ensure(inversePrint.StatusCode == HttpStatusCode.OK &&
                        inversePrint.Content.Headers.ContentType?.MediaType == "text/html" &&
                        html.Contains("قيد عكسي مرتبط", StringComparison.Ordinal) &&
                        html.Contains(generalOpening.Number!, StringComparison.Ordinal) &&
                        html.Contains("تصحيح افتتاح HTTP", StringComparison.Ordinal),
                        "GL07 reversal print omitted historical original number and correction reason");
                }
                using (var sourceWrongPrint = await client.GetAsync(path + $"/{generalOpening.ResourceId}/print"))
                    Ensure(sourceWrongPrint.StatusCode == HttpStatusCode.Conflict,
                        "GL03 print misrepresented a generated source as a manual journal");
                Console.WriteLine("  HOST PASS GL07-A07 reversal printable original link/reason with source-only guards");
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/general-openings/{generalOpening.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("openingRevision").GetInt32() == 6 && json.RootElement.GetProperty("pendingReversalJournalId").GetGuid() == generalInverse.ResourceId, "HTTP opening reservation metadata missing");
                await Command(client, path + $"/{generalInverse.ResourceId}/cancel", new TransitionRequest(generalInverse.Version, "إلغاء تصحيح HTTP"));
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/general-openings/{generalOpening.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("openingRevision").GetInt32() == 7 && json.RootElement.GetProperty("pendingReversalJournalId").ValueKind == JsonValueKind.Null, "HTTP opening release metadata missing");
                Console.WriteLine("  HOST PASS GL02 source metadata, settlement scope and journal lifecycle over HTTPS/JWT/SQL");

                Authenticate(Token("sql-clerk"));
                var recurringPath = $"/api/v1/companies/{companyId}/recurring-journals";
                var recurringRequest = new CreateRecurringJournalRequest(journalRequest.BranchId, "HTTP recurring", Ameen.Erp.Domain.RecurringFrequency.Monthly,
                    Ameen.Erp.Domain.RecurringGenerationMode.Manual, new(2026, 1, 31), new(2026, 12, 31),
                    journalRequest.Lines.Select(x => new RecurringLineRequest(x.AccountId, x.CostCenterId, x.Description, x.CurrencyCode, x.Debit, x.Credit)).ToArray());
                var recurring = await Command(client, recurringPath, recurringRequest);
                Guid periodId, yearId;
                using (var periods = await client.GetAsync($"/api/v1/companies/{companyId}/periods"))
                using (var json = JsonDocument.Parse(await periods.Content.ReadAsStringAsync()))
                {
                    var period = json.RootElement.EnumerateArray().Single(x => x.GetProperty("startsOn").GetString() == "2026-10-01");
                    periodId = period.GetProperty("id").GetGuid(); yearId = period.GetProperty("fiscalYearId").GetGuid();
                }
                var generateKey = Fixture.Key(); var generateBody = new GenerateRecurringJournalRequest(recurring.Version, periodId);
                var generated = await Command(client, recurringPath + $"/{recurring.ResourceId}/generate", generateBody, generateKey);
                Ensure(generated.Status == "Draft", "Recurring HTTP request automatically posted");
                Ensure(await Command(client, recurringPath + $"/{recurring.ResourceId}/generate", generateBody, generateKey) == generated, "Recurring HTTP replay differs");
                using (var detail = await client.GetAsync(path + $"/{generated.ResourceId}"))
                using (var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                {
                    Ensure(json.RootElement.GetProperty("source").GetString() == "Recurring" &&
                        json.RootElement.GetProperty("recurringTemplateId").GetGuid() == recurring.ResourceId &&
                        json.RootElement.GetProperty("sourceTemplateVersion").GetGuid() == recurring.Version, "Recurring HTTP provenance differs");
                }
                using (var calendar = await client.GetAsync(recurringPath + $"/{recurring.ResourceId}/schedule?fiscalYearId={yearId}"))
                using (var json = JsonDocument.Parse(await calendar.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.EnumerateArray().Single(x => x.GetProperty("fiscalPeriodId").GetGuid() == periodId)
                        .GetProperty("generatedJournalId").GetGuid() == generated.ResourceId, "Recurring HTTP calendar differs");
                using (var history = await client.GetAsync(recurringPath + $"/{recurring.ResourceId}/generations"))
                using (var json = JsonDocument.Parse(await history.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "Recurring HTTP usage differs");
                Console.WriteLine("  HOST PASS recurring template, scheduled draft, source protection, replay and usage calendar over signed JWT/HTTPS/SQL");

                // SYS03-GAP03: an explicitly configured half-year contains multiple monthly
                // occurrences. The production generation endpoint must fail closed before
                // numbering, journal persistence, generation history or audit writes.
                Authenticate(Token("sql-owner"));
                var ambiguousYear = await Command(client, $"/api/v1/companies/{companyId}/fiscal-years",
                    new CreateFiscalYearRequest("2035", new(2035, 1, 1), new(2035, 12, 31),
                        new[] { new FiscalPeriodRequest("2035-H1", new(2035, 1, 1), new(2035, 6, 30)),
                            new FiscalPeriodRequest("2035-H2", new(2035, 7, 1), new(2035, 12, 31)) }));
                Guid ambiguousPeriodId;
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/fiscal-years/{ambiguousYear.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("periods").GetArrayLength() == 2,
                        "SYS03-GAP03 custom half-year not stored over SQL");
                    ambiguousPeriodId = json.RootElement.GetProperty("periods")[0].GetProperty("id").GetGuid();
                }
                Authenticate(Token("sql-clerk"));
                var ambiguousTemplate = await Command(client, recurringPath,
                    recurringRequest with { Name = "SYS03-GAP03 ambiguous period", StartsOn = new(2035, 1, 31),
                        EndsOn = new(2035, 12, 31) });
                var ambiguousHistoryPath = recurringPath + $"/{ambiguousTemplate.ResourceId}/generations";
                var ambiguousAuditPath = recurringPath + $"/{ambiguousTemplate.ResourceId}/audit-events";
                string historyBefore, ambiguousAuditSnapshot, journalBefore;
                using (var history = await client.GetAsync(ambiguousHistoryPath))
                using (var audit = await client.GetAsync(ambiguousAuditPath))
                using (var journals = await client.GetAsync(path + "?source=Recurring"))
                {
                    Ensure(history.StatusCode == HttpStatusCode.OK && audit.StatusCode == HttpStatusCode.OK &&
                        journals.StatusCode == HttpStatusCode.OK, "SYS03-GAP03 preflight reads failed");
                    historyBefore = await history.Content.ReadAsStringAsync();
                    ambiguousAuditSnapshot = await audit.Content.ReadAsStringAsync();
                    journalBefore = await journals.Content.ReadAsStringAsync();
                }
                using (var request = new HttpRequestMessage(HttpMethod.Post, recurringPath + $"/{ambiguousTemplate.ResourceId}/generate"))
                {
                    request.Headers.Add("Idempotency-Key", Fixture.Key());
                    request.Content = new StringContent(JsonSerializer.Serialize(
                        new GenerateRecurringJournalRequest(ambiguousTemplate.Version, ambiguousPeriodId), JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var refused = await client.SendAsync(request);
                    using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
                    Ensure((int)refused.StatusCode == 422 &&
                        problem.RootElement.GetProperty("code").GetString() == "ambiguous_recurring_period",
                        "SYS03-GAP03 HTTP did not reject multiple monthly due dates in one fiscal period");
                }
                using (var history = await client.GetAsync(ambiguousHistoryPath))
                using (var audit = await client.GetAsync(ambiguousAuditPath))
                using (var journals = await client.GetAsync(path + "?source=Recurring"))
                    Ensure(history.StatusCode == HttpStatusCode.OK && audit.StatusCode == HttpStatusCode.OK &&
                        journals.StatusCode == HttpStatusCode.OK &&
                        await history.Content.ReadAsStringAsync() == historyBefore &&
                        await audit.Content.ReadAsStringAsync() == ambiguousAuditSnapshot &&
                        await journals.Content.ReadAsStringAsync() == journalBefore,
                        "SYS03-GAP03 rejected due generation changed journal, generation history or audit");
                Console.WriteLine("  HOST PASS SYS03-GAP03 ambiguous custom period rejects without journal, audit or generation over HTTPS/JWT/SQL");


                var scheduledBody = JsonSerializer.SerializeToNode(
                    recurringRequest with { Name = "HTTP automatically scheduled", GenerationMode = RecurringGenerationMode.Scheduled }, JsonDefaults.Options)!.AsObject();
                scheduledBody.Remove("endsOn");
                var scheduledTemplate = await Command(client, recurringPath, scheduledBody);
                var scheduledEdit = JsonSerializer.SerializeToNode(new EditRecurringJournalRequest(scheduledTemplate.Version, "HTTP automated schedule revised",
                    recurringRequest.Frequency, RecurringGenerationMode.Scheduled, recurringRequest.StartsOn,
                    null, recurringRequest.Lines), JsonDefaults.Options)!.AsObject();
                scheduledEdit.Remove("endsOn");
                scheduledTemplate = await Command(client, recurringPath + $"/{scheduledTemplate.ResourceId}", scheduledEdit, method: HttpMethod.Put);
                using (var templateDetail = await client.GetAsync(recurringPath + $"/{scheduledTemplate.ResourceId}"))
                using (var templateJson = JsonDocument.Parse(await templateDetail.Content.ReadAsStringAsync()))
                    Ensure(templateDetail.StatusCode == HttpStatusCode.OK &&
                        templateJson.RootElement.GetProperty("endsOn").ValueKind == JsonValueKind.Null &&
                        templateJson.RootElement.GetProperty("createdBy").GetString() == "sql-clerk" &&
                        templateJson.RootElement.GetProperty("revision").GetInt32() == 2 &&
                        templateJson.RootElement.GetProperty("lastModifiedAt").GetDateTimeOffset() >=
                        templateJson.RootElement.GetProperty("createdAt").GetDateTimeOffset(),
                        "GL04 numeric revision/last modified not exposed over HTTPS/JWT/SQL");
                using (var run = new HttpRequestMessage(HttpMethod.Post, recurringPath + "/run-due"))
                {
                    run.Content = new StringContent(JsonSerializer.Serialize(
                        new RunDueRecurringRequest(new DateOnly(2026, 6, 30), 100), JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var result = await client.SendAsync(run);
                    using var dueJson = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
                    Ensure(result.StatusCode == HttpStatusCode.OK &&
                        dueJson.RootElement.GetProperty("generatedCount").GetInt32() == 6 &&
                        dueJson.RootElement.GetProperty("failedCount").GetInt32() == 0 &&
                        dueJson.RootElement.GetProperty("rows").EnumerateArray().All(x =>
                            x.GetProperty("templateId").GetGuid() == scheduledTemplate.ResourceId &&
                            x.GetProperty("succeeded").GetBoolean()),
                        "GL04 signed HTTPS due scan failed or generated manual templates");
                }
                using (var run = new HttpRequestMessage(HttpMethod.Post, recurringPath + "/run-due"))
                {
                    run.Content = new StringContent(JsonSerializer.Serialize(
                        new RunDueRecurringRequest(new DateOnly(2026, 6, 30)), JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var result = await client.SendAsync(run);
                    using var dueJson = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
                    Ensure(result.StatusCode == HttpStatusCode.OK &&
                        dueJson.RootElement.GetProperty("selectedCount").GetInt32() == 0,
                        "GL04 HTTPS recurring scheduler was not idempotent");
                }
                using (var generations = await client.GetAsync(recurringPath + $"/{scheduledTemplate.ResourceId}/generations"))
                using (var json = JsonDocument.Parse(await generations.Content.ReadAsStringAsync()))
                    Ensure(generations.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 6,
                        "GL04 scheduled generation ledger is missing occurrences");
                Console.WriteLine("  HOST PASS GL04-GP01 scheduled run-due, scoped actor, immutable drafts and replay over HTTPS/JWT/SQL");
                using (var request = new HttpRequestMessage(HttpMethod.Post, recurringPath))
                {
                    var forged = scheduledBody.DeepClone().AsObject(); forged["revision"] = 999;
                    request.Headers.Add("Idempotency-Key", Fixture.Key());
                    request.Content = new StringContent(forged.ToJsonString(), Encoding.UTF8, "application/json");
                    using var response = await client.SendAsync(request);
                    Ensure(response.StatusCode == HttpStatusCode.BadRequest, "Client-supplied numeric revision was accepted");
                }
                var inactiveBody = new SetRecurringStatusRequest(scheduledTemplate.Version, false, "HTTPS status audit");
                var inactiveKey = Fixture.Key();
                var inactiveTemplate = await Command(client, recurringPath + $"/{scheduledTemplate.ResourceId}/status", inactiveBody, inactiveKey);
                Ensure(inactiveTemplate == await Command(client, recurringPath + $"/{scheduledTemplate.ResourceId}/status", inactiveBody, inactiveKey),
                    "HTTPS status replay did not retain its receipt");
                using (var detail = await client.GetAsync(recurringPath + $"/{scheduledTemplate.ResourceId}"))
                using (var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("revision").GetInt32() == 3 && !json.RootElement.GetProperty("isActive").GetBoolean() &&
                        json.RootElement.GetProperty("endsOn").ValueKind == JsonValueKind.Null, "HTTPS status changed audit revision more than once");
                using (var changes = await client.GetAsync(recurringPath + $"/{scheduledTemplate.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await changes.Content.ReadAsStringAsync()))
                    Ensure(changes.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 3,
                        "HTTPS template change log omitted or duplicated an audit event");
                Console.WriteLine("  HOST PASS GL04-FG02/FG04 omitted end on create/edit, server audit identity, spoof rejection and status replay");

                Authenticate(Token("sql-owner"));
                var groupSales = await Command(client, $"/api/v1/companies/{companyId}/accounts", new CreateAccountRequest("HTTP-GROUP-SALES", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit));
                var groupsPath = $"/api/v1/companies/{companyId}/item-groups";
                var group = await Command(client, groupsPath, new CreateItemGroupRequest("HTTP-GROUP", "مجموعة", groupSales.ResourceId));
                group = await Command(client, groupsPath + $"/{group.ResourceId}", new EditItemGroupRequest(group.Version, "HTTP-GROUP", "مجموعة معدلة", groupSales.ResourceId, null, null, null, "HTTP item group", null, "تعديل"), method: HttpMethod.Put);
                using (var response = await client.GetAsync(groupsPath + "?search=HTTP&isActive=true"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP group search failed");
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/accounts/{groupSales.ResourceId}/item-groups"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP account group references failed");
                await Command(client, groupsPath + $"/{group.ResourceId}/status", new SetItemGroupStatusRequest(group.Version, false, "تعطيل"));
                using (var response = await client.GetAsync(groupsPath + $"/{group.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(!json.RootElement.GetProperty("isActive").GetBoolean() && json.RootElement.GetProperty("revision").GetInt32() == 3, "HTTP group status failed");
                using (var response = await client.GetAsync(groupsPath + $"/{group.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 3, "HTTP group audit failed");
                Console.WriteLine("  HOST PASS INV02 create/edit/search/status/detail/audit and account references over HTTPS/JWT/SQL");
                var unitsPath = $"/api/v1/companies/{companyId}/units";
                var unitCard = await Command(client, unitsPath, UnitTests.Request() with { Code = "HTTP-UNIT" });
                unitCard = await Command(client, unitsPath + $"/{unitCard.ResourceId}",
                    new EditUnitRequest(unitCard.Version, "HTTP-UNIT", "كيلوغرام", "KGM", 3, "وحدة كتلة", "HTTP kilogram", null, "تحديث"), method: HttpMethod.Put);
                using (var response = await client.GetAsync(unitsPath + "?search=kilogram&isActive=true"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("items")[0].GetProperty("quantityScale").GetInt32() == 3, "HTTP unit search differs");
                await Command(client, unitsPath + $"/{unitCard.ResourceId}/status", new SetUnitStatusRequest(unitCard.Version, false, "إيقاف"));
                using (var response = await client.GetAsync(unitsPath + $"/{unitCard.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(!json.RootElement.GetProperty("isActive").GetBoolean() && json.RootElement.GetProperty("revision").GetInt32() == 3, "HTTP unit status differs");
                using (var response = await client.GetAsync(unitsPath + $"/{unitCard.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 3, "HTTP unit audit differs");
                using (var invalid = new HttpRequestMessage(HttpMethod.Post, unitsPath))
                {
                    invalid.Headers.Add("Idempotency-Key", Fixture.Key());
                    invalid.Content = new StringContent(JsonSerializer.Serialize(UnitTests.Request() with { Code = "FORBIDDEN-FACTOR" }, JsonDefaults.Options).TrimEnd('}') + ",\"conversionFactor\":12}", Encoding.UTF8, "application/json");
                    using var response = await client.SendAsync(invalid); Ensure(response.StatusCode == HttpStatusCode.BadRequest, "Global unit conversion accepted over HTTP");
                }
                Console.WriteLine("  HOST PASS INV03 create/edit/search/status/detail/audit and rejection of global conversion over HTTPS/JWT/SQL");
                // A dedicated active group/unit; earlier card lifecycle checks intentionally disabled their cards.
                var itemGroup = await Command(client, groupsPath, new CreateItemGroupRequest("HTTP-ITEM-G", "خدمات", groupSales.ResourceId));
                var itemUnit = await Command(client, unitsPath, new CreateUnitRequest("HTTP-ITEM-U", "وحدة", "EA", 0, "وحدة"));
                var itemsPath = $"/api/v1/companies/{companyId}/items";
                var itemRequest = new CreateItemRequest("HTTP-ITEM", "خدمة", ItemType.Service, ItemTracking.None, itemGroup.ResourceId, itemUnit.ResourceId,
                    new[] { new ItemUnitRequest(itemUnit.ResourceId, 1m, "000-HTTP-ITEM", 10m) });
                var itemCard = await Command(client, itemsPath, itemRequest);
                itemCard = await Command(client, itemsPath + $"/{itemCard.ResourceId}", new EditItemRequest(itemCard.Version, itemRequest.Code, "خدمة معدلة", itemRequest.Type, itemRequest.Tracking,
                    itemRequest.GroupId, itemRequest.BaseUnitId, itemRequest.Units, null, null, "HTTP service", null, "تعديل"), method: HttpMethod.Put);
                using (var response = await client.GetAsync(itemsPath + "?search=000-HTTP-ITEM"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("items")[0].GetProperty("units")[0].GetProperty("barcode").GetString() == "000-HTTP-ITEM", "HTTP item barcode failed");
                using (var response = await client.GetAsync(itemsPath + $"/{itemCard.ResourceId}/convert-quantity?unitId={itemUnit.ResourceId}&quantity=2"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("baseQuantity").GetString() == "2", "HTTP conversion failed");
                using (var response = await client.GetAsync(itemsPath + $"/{itemCard.ResourceId}/convert-quantity?unitId={itemUnit.ResourceId}&quantity=1.00000000000000000000000000001"))
                    Ensure(response.StatusCode == HttpStatusCode.BadRequest, "HTTP quantity silently rounded");
                using (var response = await client.GetAsync(unitsPath + $"/{itemUnit.ResourceId}/items"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP unit items failed");
                await Command(client, itemsPath + $"/{itemCard.ResourceId}/status", new SetItemStatusRequest(itemCard.Version, false, "تعطيل"));
                using (var response = await client.GetAsync(itemsPath + $"/{itemCard.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 3, "HTTP item audit failed");
                using (var response = await client.GetAsync(groupsPath + $"/{itemGroup.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("structureLocked").GetBoolean()
                        && !json.RootElement.GetProperty("hasDeactivationBlockers").GetBoolean()
                        && json.RootElement.GetProperty("subtreeItems").GetInt32() == 1, "HTTP group usage failed");
                using (var response = await client.GetAsync(groupsPath + $"/{itemGroup.ResourceId}/items?includeDescendants=false&isActive=false&offset=0&limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1
                        && json.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid() == itemCard.ResourceId, "HTTP group items failed");
                using (var response = await client.GetAsync(groupsPath + $"/{itemGroup.ResourceId}/items?limit=201"))
                    Ensure(response.StatusCode == HttpStatusCode.UnprocessableEntity, "HTTP invalid group page accepted");
                Console.WriteLine("  HOST PASS INV02 usage and disabled item references with paging over HTTPS/JWT/SQL");
                Console.WriteLine("  HOST PASS INV04 item lifecycle, barcode search, exact conversion, unit usage and audit over HTTPS/JWT/SQL");
                var warehouseBranch = await Command(client, $"/api/v1/companies/{companyId}/branches", new CreateBranchRequest("HTTP-WH-B", "فرع مخزن", "مدينة", "عنوان"));
                var warehouseAccount = await Command(client, $"/api/v1/companies/{companyId}/accounts", new CreateAccountRequest("HTTP-WH-A", "مخزون", AccountType.Asset, null, true, true, false, null, NormalBalance.Debit));
                Guid warehouseKeeper;
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/memberships?limit=200"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    warehouseKeeper = json.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("subject").GetString() == "sql-owner").GetProperty("id").GetGuid();
                var warehousesPath = $"/api/v1/companies/{companyId}/warehouses";
                var whRequest = new CreateWarehouseRequest("HTTP-WH", "مخزن", warehouseBranch.ResourceId, warehouseKeeper, warehouseAccount.ResourceId,
                    WarehouseType.Quarantine, true, new[] { new WarehouseLocationRequest("001", "رف") });
                var wh = await Command(client, warehousesPath, whRequest);
                wh = await Command(client, warehousesPath + $"/{wh.ResourceId}", new EditWarehouseRequest(wh.Version, whRequest.Code, "مخزن معدل", whRequest.BranchId,
                    whRequest.KeeperMembershipId, whRequest.InventoryAccountId, whRequest.Type, true, new[] { new WarehouseLocationRequest("002", "عزل") }, null, null, "تعديل"), method: HttpMethod.Put);
                using (var response = await client.GetAsync(warehousesPath + $"/{wh.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("locations")[0].GetProperty("code").GetString() == "002", "HTTP warehouse locations failed");
                using (var response = await client.GetAsync(warehousesPath + "?search=HTTP-WH"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP warehouse search failed");
                await Command(client, warehousesPath + $"/{wh.ResourceId}/status", new SetWarehouseStatusRequest(wh.Version, false, "إيقاف"));
                using (var response = await client.GetAsync(warehousesPath + $"/{wh.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 3, "HTTP warehouse audit failed");
                Console.WriteLine("  HOST PASS INV01 create/edit/locations/search/status/audit over HTTPS/JWT/SQL");
                var identityCost = await Command(client, $"/api/v1/companies/{companyId}/accounts", new CreateAccountRequest("HTTP-ID-C", "تكلفة", AccountType.Expense, null, true, false, false, null, NormalBalance.Debit));
                var identityGroup = await Command(client, groupsPath, new CreateItemGroupRequest("HTTP-ID-G", "دفعات", groupSales.ResourceId, InventoryAccountId: warehouseAccount.ResourceId, CostAccountId: identityCost.ResourceId));
                var identityItem = await Command(client, itemsPath, new CreateItemRequest("HTTP-ID", "صنف دفعات", ItemType.Goods, ItemTracking.Lot, identityGroup.ResourceId, itemUnit.ResourceId, new[] { new ItemUnitRequest(itemUnit.ResourceId, 1, null, null) }));
                var lotsPath = itemsPath + $"/{identityItem.ResourceId}/lots";
                var lotRequest = new CreateInventoryIdentityRequest("HTTP-LOT", "دفعة", new(2026, 1, 1), new(2027, 1, 1), InventoryQualityStatus.Quarantine, LotNumber: "HTTP-BATCH-NUMBER");
                var lot = await Command(client, lotsPath, lotRequest);
                lot = await Command(client, lotsPath + $"/{lot.ResourceId}", new EditInventoryIdentityRequest(lot.Version, lotRequest.Code, "دفعة معدلة", lotRequest.ProductionDate, lotRequest.ExpiryDate, InventoryQualityStatus.Available, null, null, "تصحيح"), method: HttpMethod.Put);
                using (var response = await client.GetAsync(lotsPath + "?qualityStatus=Available&limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP lot search failed");
                using (var response = await client.GetAsync(lotsPath + $"/{lot.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("revision").GetInt32() == 2 && json.RootElement.GetProperty("lotNumber").GetString() == "HTTP-BATCH-NUMBER" && json.RootElement.GetProperty("code").GetString() == "HTTP-LOT", "HTTP lot detail failed");
                await Command(client, lotsPath + $"/{lot.ResourceId}/status", new SetInventoryIdentityStatusRequest(lot.Version, false, "إيقاف"));
                using (var response = await client.GetAsync(lotsPath + $"/{lot.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 3, "HTTP lot audit failed");
                using (var response = await client.GetAsync(itemsPath + $"/{identityItem.ResourceId}/serials/{lot.ResourceId}"))
                    Ensure(response.StatusCode == HttpStatusCode.NotFound, "HTTP serial route exposed lot");
                using (var response = await client.GetAsync(lotsPath + $"/{lot.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("movementCount").GetInt32() == 0 &&
                        !json.RootElement.GetProperty("hasStockMovements").GetBoolean(),
                        "HTTP lot usage before posting differs");
                using (var response = await client.GetAsync(lotsPath + $"/{lot.ResourceId}/stock-movements"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "HTTP lot movement page before posting differs");
                using (var response = await client.GetAsync(itemsPath + $"/{identityItem.ResourceId}/serials/{lot.ResourceId}/usage"))
                    Ensure(response.StatusCode == HttpStatusCode.NotFound, "HTTP serial usage exposed a lot");
                Authenticate(Token("audit-no-membership"));
                using (var unauthorizedUsageResponse = await client.GetAsync(lotsPath + $"/{lot.ResourceId}/usage"))
                    Ensure(unauthorizedUsageResponse.StatusCode == HttpStatusCode.Forbidden,
                        "HTTP unauthorized lot usage exposed a tenant's references");
                Authenticate(Token("sql-owner"));
                Console.WriteLine("  HOST PASS INV05 lot create/edit/search/detail/status/audit and route isolation over HTTPS/JWT/SQL");
                var openingWarehouse = await Command(client, warehousesPath, whRequest with { Code = "HTTP-OPEN-WH", Type = WarehouseType.Standard, TrackLocations = false, Locations = Array.Empty<WarehouseLocationRequest>() });
                var openingItem = await Command(client, itemsPath, new CreateItemRequest("HTTP-OPEN-I", "صنف افتتاح", ItemType.Goods, ItemTracking.None, identityGroup.ResourceId, itemUnit.ResourceId, new[] { new ItemUnitRequest(itemUnit.ResourceId, 1, null, null) }));
                var openingsPath = $"/api/v1/companies/{companyId}/inventory-openings";
                var openingRequest = new CreateInventoryOpeningRequest(warehouseBranch.ResourceId, openingWarehouse.ResourceId, Fixture.Date, Fixture.Date, Fixture.Date, "SAR", 1, "HTTP-COUNT", "افتتاح", new[] { new InventoryOpeningLineRequest(openingItem.ResourceId, itemUnit.ResourceId, 2, 7.5m) });
                Authenticate(Token("sql-clerk"));
                var opening = await Command(client, openingsPath, openingRequest);
                using (var response = await client.GetAsync(openingsPath + $"/{opening.ResourceId}/valuation-preview"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && decimal.Parse(json.RootElement.GetProperty("totalValue").GetString()!, System.Globalization.CultureInfo.InvariantCulture) == 15m && !json.RootElement.GetProperty("canPost").GetBoolean(), "HTTP opening valuation failed");
                opening = await Command(client, openingsPath + $"/{opening.ResourceId}/submit", new TransitionRequest(opening.Version));
                Authenticate(Token("sql-approver"));
                opening = await Command(client, openingsPath + $"/{opening.ResourceId}/return", new TransitionRequest(opening.Version, "تصحيح"));
                Authenticate(Token("sql-clerk"));
                opening = await Command(client, openingsPath + $"/{opening.ResourceId}", new EditInventoryOpeningRequest(opening.Version, "افتتاح معدل", openingRequest.Lines, "تصحيح"), method: HttpMethod.Put);
                opening = await Command(client, openingsPath + $"/{opening.ResourceId}/submit", new TransitionRequest(opening.Version));
                Authenticate(Token("sql-approver"));
                opening = await Command(client, openingsPath + $"/{opening.ResourceId}/approve", new TransitionRequest(opening.Version));
                using (var response = await client.GetAsync(openingsPath + "?state=Approved&search=HTTP-COUNT"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP opening search failed");
                using (var response = await client.GetAsync(openingsPath + $"/{opening.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("state").GetString() == "Approved" && json.RootElement.GetProperty("lines").GetArrayLength() == 1, "HTTP opening detail failed");
                using (var response = await client.GetAsync(openingsPath + $"/{opening.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 6, "HTTP opening audit failed");
                Authenticate(Token("sql-owner"));
                using (var response = await client.GetAsync(openingsPath + $"/{opening.ResourceId}/print"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("openingId").GetGuid() == opening.ResourceId &&
                        json.RootElement.GetProperty("state").GetString() == nameof(InventoryOpeningState.Approved) &&
                        json.RootElement.GetProperty("journalId").ValueKind == JsonValueKind.Null &&
                        json.RootElement.GetProperty("lines")[0].GetProperty("itemCode").GetString() == "HTTP-OPEN-I" &&
                        decimal.Parse(json.RootElement.GetProperty("totalDocumentValue").GetString()!,
                            System.Globalization.CultureInfo.InvariantCulture) == 15m,
                        "HTTP INV06 approved opening print lost identity/line/value");
                Authenticate(Token("audit-no-membership"));
                using (var response = await client.GetAsync(openingsPath + $"/{opening.ResourceId}/print"))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden, "INV06 print exposed to nonmember");
                Authenticate(Token("sql-owner"));
                Console.WriteLine("  HOST PASS INV06 draft/valuation/submit/return/correct/approve/search/detail/audit over HTTPS/JWT/SQL");
                var clearingAccount = await Command(client, $"/api/v1/companies/{companyId}/accounts", new CreateAccountRequest("HTTP-OPEN-CLEAR", "تسوية افتتاح", AccountType.Equity, null, true, false, false, null, NormalBalance.Credit));
                await Command(client, openingsPath + "/posting-settings", new ConfigureInventoryOpeningPostingRequest(null, clearingAccount.ResourceId, "إعداد"), method: HttpMethod.Put);
                Authenticate(Token("sql-clerk"));
                PostInventoryOpeningRequest postOpening;
                using (var response = await client.GetAsync(openingsPath + $"/{opening.ResourceId}/valuation-preview"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("canPost").GetBoolean(), "HTTP posting preview blocked");
                    var plan = json.RootElement.GetProperty("postingPlan");
                    postOpening = new(opening.Version, plan.GetProperty("settingsVersion").GetGuid(), plan.GetProperty("warehouseVersion").GetGuid());
                }
                opening = await Command(client, openingsPath + $"/{opening.ResourceId}/post", postOpening);
                Guid postedOpeningJournalId;
                using (var response = await client.GetAsync(openingsPath + $"/{opening.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    postedOpeningJournalId = json.RootElement.GetProperty("journalId").GetGuid();
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("state").GetString() == "Posted" &&
                        postedOpeningJournalId != Guid.Empty, "HTTP posted source missing journal");
                }
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/stock-movements?warehouseId={openingWarehouse.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP stock movement missing");
                using (var response = await client.GetAsync(itemsPath + $"/{openingItem.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("movementCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("openingDocumentCount").GetInt32() == 1,
                        "HTTP item usage after SQL posting differs");
                using (var response = await client.GetAsync(warehousesPath + $"/{openingWarehouse.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("movementCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("reversalMovementCount").GetInt32() == 0,
                        "HTTP warehouse usage after SQL posting differs");
                using (var response = await client.GetAsync(warehousesPath + $"/{openingWarehouse.ResourceId}/stock-movements?offset=0&limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("items").GetArrayLength() == 1,
                        "HTTP warehouse movement page after SQL posting differs");
                using (var response = await client.GetAsync(itemsPath + $"/{openingItem.ResourceId}/stock-movements?offset=1&limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("items").GetArrayLength() == 0,
                        "HTTP item movement pagination differs");
                Authenticate(Token("sql-owner"));
                using (var response = await client.GetAsync(openingsPath + $"/{opening.ResourceId}/print"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("state").GetString() == nameof(InventoryOpeningState.Posted) &&
                        json.RootElement.GetProperty("journalId").GetGuid() == postedOpeningJournalId &&
                        decimal.Parse(json.RootElement.GetProperty("signedEffectValue").GetString()!,
                            System.Globalization.CultureInfo.InvariantCulture) == 15m,
                        "HTTP INV06 posted opening print lost actual GL source");
                Console.WriteLine("  HOST PASS INV06 configure/preview/post/linked journal/stock movement over HTTPS/JWT/SQL");
                Authenticate(Token("sql-clerk"));
                // GL07-C01: never reverse an INV06 accounting journal independently of its
                // stock source. The approved source workflow below must remain available.
                var journalPath = $"/api/v1/companies/{companyId}/journals/{postedOpeningJournalId}";
                Guid postedOpeningJournalVersion;
                using (var response = await client.GetAsync(journalPath))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("source").GetString() == "InventoryOpening",
                        "GL07-C01 posted stock journal was not source linked");
                    postedOpeningJournalVersion = json.RootElement.GetProperty("version").GetGuid();
                }
                using (var reverseRequest = new HttpRequestMessage(HttpMethod.Post, journalPath + "/reverse"))
                {
                    reverseRequest.Headers.Add("Idempotency-Key", Fixture.Key());
                    reverseRequest.Content = new StringContent(JsonSerializer.Serialize(
                        new ReverseJournalRequest(postedOpeningJournalVersion, Fixture.Date, "عكس مباشر ممنوع"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(reverseRequest);
                    var denialBody = await rejected.Content.ReadAsStringAsync();
                    Ensure(rejected.StatusCode == HttpStatusCode.Conflict &&
                        denialBody.Contains("source_reversal_required", StringComparison.Ordinal),
                        "GL07-C01 source-owned stock journal was reversed through journal API");
                }
                Authenticate(Token("audit-no-membership"));
                using (var forbidden = await client.GetAsync(journalPath))
                    Ensure(forbidden.StatusCode == HttpStatusCode.Forbidden,
                        "GL07-C01 cross-member JWT disclosed source journal");
                Authenticate(Token("sql-clerk"));
                Console.WriteLine("  HOST PASS GL07-C01 source-owned INV06 journal direct reverse denied; source reversal remains available");
                var reversalOpening = await Command(client, openingsPath + $"/{opening.ResourceId}/reversals", new ReverseInventoryOpeningRequest(opening.Version, Fixture.Date, "تصحيح HTTP"));
                reversalOpening = await Command(client, openingsPath + $"/{reversalOpening.ResourceId}/submit", new TransitionRequest(reversalOpening.Version));
                Authenticate(Token("sql-approver"));
                reversalOpening = await Command(client, openingsPath + $"/{reversalOpening.ResourceId}/approve", new TransitionRequest(reversalOpening.Version));
                Authenticate(Token("sql-clerk"));
                reversalOpening = await Command(client, openingsPath + $"/{reversalOpening.ResourceId}/post-reversal", new TransitionRequest(reversalOpening.Version));
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/stock-movements?warehouseId={openingWarehouse.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 2 &&
                        json.RootElement.GetProperty("items").EnumerateArray().Sum(x => decimal.Parse(x.GetProperty("baseQuantity").GetString()!, System.Globalization.CultureInfo.InvariantCulture)) == 0, "HTTP reversal stock balance differs");
                Authenticate(Token("sql-owner"));
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/reports/inventory-balances?from=2026-01-01&to=2026-12-31&warehouseId={openingWarehouse.ResourceId}&itemId={openingItem.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("rows").GetProperty("totalCount").GetInt32() == 1 && json.RootElement.GetProperty("closingValue").GetString() == "0" && json.RootElement.GetProperty("inventoryOpeningsOnly").GetBoolean(), "HTTP inventory balance result differs");
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/reports/items/{openingItem.ResourceId}/ledger?from=2026-01-01&to=2026-12-31&warehouseId={openingWarehouse.ResourceId}&offset=1&limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("rows").GetProperty("totalCount").GetInt32() == 2, "HTTP item ledger paging differs");
                    var row = json.RootElement.GetProperty("rows").GetProperty("items")[0];
                    Ensure(row.GetProperty("runningQuantity").GetString() == "0" && row.GetProperty("openingId").GetGuid() == reversalOpening.ResourceId && row.GetProperty("documentUnits").GetArrayLength() == 1, "HTTP stock drilldown or running balance differs");
                }
                Console.WriteLine("  HOST PASS RPT08/09 balances and paged item ledger over HTTPS/JWT/SQL");
                Console.WriteLine("  HOST PASS INV06 create/submit/approve/post reversal and net-zero stock over HTTPS/JWT/SQL");



                var auditPath = $"/api/v1/companies/{companyId}/audit-events";
                Guid eventId;
                using (var response = await client.GetAsync(auditPath + $"?from=2026-01-01&to=2026-12-31&actorId=sql-owner&action=warehouse.created&resourceId={wh.ResourceId}&limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP audit filters failed");
                    eventId = json.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
                }
                using (var response = await client.GetAsync(auditPath + $"/{eventId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("resourceId").GetGuid() == wh.ResourceId, "HTTP audit event detail failed");
                // SYS14-GAP02: old clients may omit both dates, or provide one UTC bound.
                foreach(var suffix in new[]{
                    $"?actorId=sql-owner&action=warehouse.created&resourceId={wh.ResourceId}",
                    $"?from=2026-01-01&actorId=sql-owner&action=warehouse.created&resourceId={wh.ResourceId}",
                    $"?to=2026-12-31&actorId=sql-owner&action=warehouse.created&resourceId={wh.ResourceId}"})
                {
                    using var response=await client.GetAsync(auditPath+suffix);
                    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Ensure(response.StatusCode==HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32()==1 &&
                        json.RootElement.GetProperty("items")[0].GetProperty("resourceId").GetGuid()==wh.ResourceId,
                        "SYS14 optional/one-sided date request failed HTTPS JWT tenant read");
                }
                foreach(var suffix in new[]{
                    $"?from=2027-01-01&action=warehouse.created&resourceId={wh.ResourceId}",
                    $"?to=2025-12-31&action=warehouse.created&resourceId={wh.ResourceId}"})
                {
                    using var response=await client.GetAsync(auditPath+suffix);
                    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Ensure(response.StatusCode==HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32()==0,
                        "SYS14 one-sided UTC date limit ignored");
                }
                Console.WriteLine("  HOST PASS SYS14-GAP02 no/one-sided UTC date filters over HTTPS/JWT/SQL");
                // SYS14-AC01: audit details link to a real tenant-scoped source via resourceId/action.
                Guid companyEditEvent;
                using (var response=await client.GetAsync(auditPath+
                    $"?actorId=sql-owner&action=company.edited&resourceId={companyId}&limit=1"))
                using (var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode==HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32()>0,
                        "SYS14 HTTP could not find a real company edit event");
                    companyEditEvent=json.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
                }
                using (var response=await client.GetAsync(auditPath+$"/{companyEditEvent}"))
                using (var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode==HttpStatusCode.OK &&
                        json.RootElement.GetProperty("actorId").GetString()=="sql-owner" &&
                        json.RootElement.GetProperty("action").GetString()=="company.edited" &&
                        json.RootElement.GetProperty("resourceId").GetGuid()==companyId &&
                        json.RootElement.GetProperty("companyId").GetGuid()==companyId &&
                        json.RootElement.GetProperty("occurredAt").GetDateTimeOffset()!=default,
                        "SYS14 HTTP audit event lost action, actor, resource or time");
                    using var details=JsonDocument.Parse(json.RootElement.GetProperty("details").GetString()!);
                    Ensure(details.RootElement.TryGetProperty("before",out _) &&
                        details.RootElement.TryGetProperty("after",out _),
                        "SYS14 HTTP company edit missing before-after history");
                }
                using (var response=await client.GetAsync($"/api/v1/companies/{companyId}"))
                using (var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode==HttpStatusCode.OK &&
                        json.RootElement.GetProperty("id").GetGuid()==companyId,
                        "SYS14 HTTP source drill could not resolve the event's company");
                Console.WriteLine("  HOST PASS SYS14-AC01 action/actor/time/before-after/source drill via HTTPS/JWT/SQL");

                using (var response = await client.GetAsync(auditPath + "?from=2026-12-31&to=2026-01-01"))
                    Ensure(response.StatusCode == HttpStatusCode.UnprocessableEntity, "HTTP reversed audit dates accepted");
                using (var response = await client.GetAsync(auditPath + "?from=bad-date"))
                    Ensure(response.StatusCode == HttpStatusCode.UnprocessableEntity, "HTTP malformed audit date accepted");
                Authenticate(Token("audit-no-membership"));
                using (var response = await client.GetAsync(auditPath + $"/{eventId}"))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden, "HTTP audit detail exposed without permission");
                Authenticate(Token("sql-owner"));
                // SYS14-AC02: audit endpoints are read-only even for an authorized JWT;
                // denied mutation attempts leave the historical event and event count intact.
                var untouchedEventPath=auditPath+$"/{eventId}";
                string originalEvent;
                using(var response=await client.GetAsync(untouchedEventPath))
                {
                    Ensure(response.StatusCode==HttpStatusCode.OK,"SYS14 cannot read event before mutation denial");
                    originalEvent=await response.Content.ReadAsStringAsync();
                }
                foreach(var verb in new[]{HttpMethod.Post,HttpMethod.Put,HttpMethod.Patch,HttpMethod.Delete})
                {
                    foreach(var auditTarget in new[]{auditPath,untouchedEventPath})
                    {
                        using var request=new HttpRequestMessage(verb,auditTarget);
                        if(verb!=HttpMethod.Delete)
                            request.Content=new StringContent("{}",Encoding.UTF8,"application/json");
                        using var refused=await client.SendAsync(request);
                        Ensure(refused.StatusCode==HttpStatusCode.MethodNotAllowed,
                            "SYS14 accepted a write verb on the read-only audit API");
                    }
                }
                using(var response=await client.GetAsync(untouchedEventPath))
                    Ensure(response.StatusCode==HttpStatusCode.OK &&
                        await response.Content.ReadAsStringAsync()==originalEvent,
                        "SYS14 refused write changed persisted audit detail");
                using(var response=await client.GetAsync(auditPath+
                    $"?action=warehouse.created&resourceId={wh.ResourceId}"))
                using(var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode==HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32()==1,
                        "SYS14 refused audit mutation changed history count");
                using(var auditContract=JsonDocument.Parse(specJson))
                {
                    var routes=auditContract.RootElement.GetProperty("paths");
                    foreach(var auditRoute in new[]{
                        "/api/v1/companies/{companyId}/audit-events",
                        "/api/v1/companies/{companyId}/audit-events/{id}"})
                        Ensure(routes.GetProperty(auditRoute).EnumerateObject().All(x=>x.Name=="get"),
                            "SYS14 OpenAPI exposes an unapproved audit mutation");
                }
                var auditorMember=await Command(client,
                    $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest("sys14-http-revoked-auditor",
                        new[]{Permissions.Read,Permissions.Audit},true,Array.Empty<Guid>(),null));
                Authenticate(Token("sys14-http-revoked-auditor"));
                using(var response=await client.GetAsync(untouchedEventPath))
                    Ensure(response.StatusCode==HttpStatusCode.OK,
                        "SYS14 authorized auditor could not read source event");
                Authenticate(Token("sql-owner"));
                await Command(client,
                    $"/api/v1/companies/{companyId}/memberships/{auditorMember.ResourceId}/status",
                    new SetMembershipStatusRequest(auditorMember.Version,false,"إلغاء وصول المدقق"));
                Authenticate(Token("sys14-http-revoked-auditor"));
                using(var response=await client.GetAsync(auditPath))
                    Ensure(response.StatusCode==HttpStatusCode.Forbidden,
                        "SYS14 disabled auditor listed audit history with still-valid JWT");
                using(var response=await client.GetAsync(untouchedEventPath))
                    Ensure(response.StatusCode==HttpStatusCode.Forbidden,
                        "SYS14 disabled auditor read audit details with still-valid JWT");
                Authenticate(Token("sql-owner"));
                Console.WriteLine("  HOST PASS SYS14-AC02 read-only verbs/OpenAPI, revocation and preserved audit via HTTPS/JWT/SQL");
                Console.WriteLine("  HOST PASS SYS14 audit filters, event detail, invalid dates and authorization over HTTPS/JWT/SQL");
                var ratesPath = $"/api/v1/companies/{companyId}/exchange-rates";
                await Command(client, $"/api/v1/companies/{companyId}/currencies", new CreateCurrencyRequest("NZD", 2, "دولار نيوزيلندي"));
                var rate = await Command(client, ratesPath, new CreateExchangeRateRequest("NZD", Fixture.Date, 1.25m, "HTTP source"));
                await Command(client, ratesPath, new CreateExchangeRateRequest("NZD", Fixture.Date.AddDays(1), 1.5m, "HTTP future"));
                // SYS05-OP01: a valid JWT for an unrelated tenant cannot
                // search for or provision an effective exchange rate.
                Authenticate(Token("sys04-unrelated-user"));
                using (var deniedRateSearch = await client.GetAsync(ratesPath + "/search?currencyCode=NZD"))
                    Ensure(deniedRateSearch.StatusCode == HttpStatusCode.Forbidden,
                        "SYS05-OP01 unrelated JWT searched another company's rate history");
                using (var deniedRateCreate = new HttpRequestMessage(HttpMethod.Post, ratesPath))
                {
                    deniedRateCreate.Headers.Add("Idempotency-Key", Fixture.Key());
                    deniedRateCreate.Content = new StringContent(JsonSerializer.Serialize(
                        new CreateExchangeRateRequest("NZD", Fixture.Date.AddDays(7),
                            1.2345m, "SYS05-OP01-DENIED"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var unauthorizedRateResponse = await client.SendAsync(deniedRateCreate);
                    Ensure(unauthorizedRateResponse.StatusCode == HttpStatusCode.Forbidden,
                        "SYS05-OP01 unrelated JWT provisioned another company's rate");
                }
                Authenticate(Token("sql-owner"));
                using (var rateSearch = await client.GetAsync(ratesPath + "/search?source=SYS05-OP01-DENIED"))
                using (var rateSearchJson = JsonDocument.Parse(await rateSearch.Content.ReadAsStringAsync()))
                    Ensure(rateSearch.StatusCode == HttpStatusCode.OK &&
                        rateSearchJson.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "SYS05-OP01 denied rate creation persisted a SQL record");
                Console.WriteLine("  HOST PASS SYS05-OP01 rate search/create authorization and denied foreign JWT over HTTPS/SQL");
                // SYS05-FG03: the public contract forbids a zero rate or
                // excessive decimal precision before reserving a SQL record.
                foreach (var invalidRate in new[] { 0m, 1.12345678901m })
                {
                    using var attempt = new HttpRequestMessage(HttpMethod.Post, ratesPath);
                    attempt.Headers.Add("Idempotency-Key", Fixture.Key());
                    attempt.Content = new StringContent(JsonSerializer.Serialize(
                        new CreateExchangeRateRequest("NZD", Fixture.Date.AddDays(6),
                            invalidRate, "SYS05-invalid"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(attempt);
                    using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                    Ensure(rejected.StatusCode == HttpStatusCode.UnprocessableEntity &&
                        error.RootElement.GetProperty("code").GetString() == "invalid_exchange_rate",
                        "SYS05-FG03 HTTPS accepted zero or overprecision exchange rate");
                }
                using (var invalidSearch = await client.GetAsync(ratesPath + "/search?currencyCode=NZD&source=SYS05-invalid"))
                using (var invalidJson = JsonDocument.Parse(await invalidSearch.Content.ReadAsStringAsync()))
                    Ensure(invalidSearch.StatusCode == HttpStatusCode.OK &&
                        invalidJson.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "SYS05-FG03 rejected exchange rate persisted in SQL");
                Console.WriteLine("  HOST PASS SYS05-FG03 invalid decimal rate refusal over HTTPS/JWT/SQL");
                using (var response = await client.GetAsync(ratesPath + $"/quote?currencyCode=NZD&date={Fixture.Date:yyyy-MM-dd}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("exchangeRateId").GetGuid() == rate.ResourceId &&
                        json.RootElement.GetProperty("rateToFunctional").GetString() == "1.25", "SQL/HTTP effective quote differs");
                using (var response = await client.GetAsync(ratesPath + $"/search?currencyCode=NZD&to={Fixture.Date:yyyy-MM-dd}&source=HTTP&limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "SQL/HTTP historical rate filter differs");
                using (var response = await client.GetAsync(ratesPath + $"/{rate.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("functionalCurrency").GetString() == "SAR", "SQL/HTTP rate direction differs");
                using (var response = await client.GetAsync(ratesPath + $"/{rate.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "SQL/HTTP rate audit differs");
                Authenticate(Token("sql-clerk"));
                await Command(client, path, journalRequest with { Lines = journalRequest.Lines.Select(x => x with { CurrencyCode = "NZD", ExchangeRateId = rate.ResourceId }).ToArray() });
                using (var response = await client.GetAsync(ratesPath + $"/{rate.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "SQL/HTTP rate usage should count journal once");

                // SYS05-OP03: usage and history are read-only and must not leak through JWT role/tenant boundaries.
                var readCountsBefore = await ReadOnlyCounts(connection, companyId);
                var rateUsagePath = ratesPath + $"/{rate.ResourceId}/usage";
                var rateAuditPath = ratesPath + $"/{rate.ResourceId}/audit-events";
                Ensure(specJson.Contains("/api/v1/companies/{companyId}/exchange-rates/{id}/usage", StringComparison.Ordinal) &&
                    specJson.Contains("/api/v1/companies/{companyId}/exchange-rates/{id}/audit-events", StringComparison.Ordinal),
                    "SYS05-OP03 usage/audit OpenAPI routes missing");
                using (var page = await client.GetAsync(rateUsagePath + "?offset=1&limit=1"))
                using (var json = JsonDocument.Parse(await page.Content.ReadAsStringAsync()))
                    Ensure(page.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("items").GetArrayLength() == 0,
                        "SYS05-OP03 SQL-backed usage paging is unstable");
                // SQL fixture sql-clerk has Permissions.All, including Audit.
                // Reuse the explicit read-only member established for GL05 acceptance.
                Authenticate(Token("gl05-review-readonly"));
                using (var deniedAudit = await client.GetAsync(rateAuditPath))
                    Ensure(deniedAudit.StatusCode == HttpStatusCode.Forbidden,
                        "SYS05-OP03 read-only member accessed protected rate audit");
                Authenticate(Token("audit-no-membership"));
                using (var deniedUsage = await client.GetAsync(rateUsagePath))
                    Ensure(deniedUsage.StatusCode == HttpStatusCode.Forbidden,
                        "SYS05-OP03 non-member accessed rate usage");
                using (var deniedAudit = await client.GetAsync(rateAuditPath))
                    Ensure(deniedAudit.StatusCode == HttpStatusCode.Forbidden,
                        "SYS05-OP03 non-member accessed rate audit");
                Authenticate(Token("sql-owner"));
                using (var audit = await client.GetAsync(rateAuditPath))
                using (var json = JsonDocument.Parse(await audit.Content.ReadAsStringAsync()))
                    Ensure(audit.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("items")[0].GetProperty("action").GetString() == "exchange-rate.created",
                        "SYS05-OP03 owner audit history missing persisted create event");
                using (var missingUsage = await client.GetAsync(ratesPath + $"/{Guid.NewGuid()}/usage"))
                    Ensure(missingUsage.StatusCode == HttpStatusCode.NotFound,
                        "SYS05-OP03 non-existent rate usage exposed");
                Ensure(readCountsBefore == await ReadOnlyCounts(connection, companyId),
                    "SYS05-OP03 read-only usage/audit changed journal, audit, receipt or outbox SQL");
                Console.WriteLine("  HOST PASS SYS05-OP03 usage/audit paging, role/tenant rejection, SQL nonmutation and OpenAPI");
                Authenticate(Token("sql-approver"));
                Guid gl11CloseVersion;
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/periods/{periodId}/close-preview?limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK && !json.RootElement.GetProperty("canCloseGeneralLedger").GetBoolean() &&
                        json.RootElement.GetProperty("generalLedgerOnly").GetBoolean() && json.RootElement.GetProperty("issues").GetProperty("items").GetArrayLength() == 1,
                        "SQL/HTTP close preview did not expose pending blockers");
                    gl11CloseVersion = json.RootElement.GetProperty("expectedVersion").GetGuid();
                }

                // GL11-T02/A01: a valid signed JWT and PeriodClose on only one
                // branch cannot authorize the company-wide close operation.
                // The service must deny these BEFORE the pending-journal guard
                // without recording any failed receipt, audit or ledger effect.
                const string gl11LimitedSubject = "gl11-http-branch-only-closer";
                var gl11ClosePath = $"/api/v1/companies/{companyId}/periods/{periodId}/close";
                using (var openApi = JsonDocument.Parse(specJson))
                {
                    var closeContract = openApi.RootElement.GetProperty("paths")
                        .GetProperty("/api/v1/companies/{companyId}/periods/{id}/close")
                        .GetProperty("post");
                    Ensure(closeContract.GetProperty("security").GetArrayLength() > 0,
                        "GL11-T02 close API lost its JWT OpenAPI security contract");
                }
                Authenticate(Token("sql-owner"));
                await Command(client, $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest(gl11LimitedSubject,
                        new[] { Permissions.Read, Permissions.PeriodClose }, false,
                        new[] { journalRequest.BranchId }, null));
                var gl11BeforeDenied = await ReadOnlyCounts(connection, companyId);
                async Task DeniedGl11Close(string subject, string errorCode)
                {
                    Authenticate(Token(subject));
                    using var request = new HttpRequestMessage(HttpMethod.Post, gl11ClosePath);
                    request.Headers.Add("Idempotency-Key", Fixture.Key());
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(new ChangePeriodRequest(gl11CloseVersion,
                            "إقفال شركة غير مصرح به"), JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var response = await client.SendAsync(request);
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden &&
                        body.RootElement.GetProperty("code").GetString() == errorCode,
                        "GL11-T02 HTTPS close did not reject unauthorized actor: " + subject);
                }
                await DeniedGl11Close(gl11LimitedSubject, "all_branches_required");
                await DeniedGl11Close("gl11-http-no-membership", "permission_denied");
                Ensure(gl11BeforeDenied == await ReadOnlyCounts(connection, companyId),
                    "GL11-T02 denied HTTPS close changed journals, audit, receipts or outbox");
                await using (var gl11PeriodDb = new ErpDbContext(
                    new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options))
                {
                    var gl11Period = await gl11PeriodDb.Set<FiscalPeriod>().AsNoTracking()
                        .SingleAsync(x => x.CompanyId == companyId && x.Id == periodId);
                    Ensure(!gl11Period.IsClosed && gl11Period.Version == gl11CloseVersion,
                        "GL11-T02 denied HTTPS close modified the fiscal period");
                }
                Authenticate(Token("sql-approver"));
                Console.WriteLine("  HOST PASS GL11-T02 close POST denies branch-only/nonmember JWT, no SQL side effects, OpenAPI security");

                // GL11-T02/C01: a properly authorized HTTPS close must still
                // refuse the pending journal discovered by close-preview.
                // The rejected idempotency key remains reusable after a
                // separately authorized repair, not recorded as a close.
                var gl11PendingCounts = await ReadOnlyCounts(connection, companyId);
                var gl11PendingKey = Fixture.Key();
                using (var pendingClose = new HttpRequestMessage(HttpMethod.Post,
                    gl11ClosePath))
                {
                    pendingClose.Headers.Add("Idempotency-Key", gl11PendingKey);
                    pendingClose.Content = new StringContent(
                        JsonSerializer.Serialize(new ChangePeriodRequest(gl11CloseVersion,
                            "رفض إقفال فترة لها قيد معلق"), JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var gl11PendingDeniedResponse = await client.SendAsync(pendingClose);
                    using var gl11PendingErrorBody = JsonDocument.Parse(
                        await gl11PendingDeniedResponse.Content.ReadAsStringAsync());
                    Ensure(gl11PendingDeniedResponse.StatusCode == HttpStatusCode.Conflict &&
                        gl11PendingErrorBody.RootElement.GetProperty("code").GetString() == "unposted_journals",
                        "GL11-T02 authorized close bypassed unposted journal HTTP blocker");
                }
                Ensure(gl11PendingCounts == await ReadOnlyCounts(connection, companyId),
                    "GL11-T02 rejected pending journal HTTP close changed SQL state");
                await using (var gl11PendingDb = new ErpDbContext(
                    new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options))
                {
                    var saved = await gl11PendingDb.Set<FiscalPeriod>().AsNoTracking()
                        .SingleAsync(x => x.CompanyId == companyId && x.Id == periodId);
                    Ensure(!saved.IsClosed && saved.Version == gl11CloseVersion &&
                        !await gl11PendingDb.Set<IdempotencyRecord>().AnyAsync(x =>
                            x.CompanyId == companyId && x.Key == gl11PendingKey),
                        "GL11-T02 refused HTTP close mutated fiscal period or reserved receipt");
                }
                Console.WriteLine("  HOST PASS GL11-T02 authorized close POST returns 409 unposted_journals atomically without consuming Idempotency-Key");

                // GL11-A05: history endpoint is read-only and requires a tenant-wide
                // PeriodClose + Read + Audit permission set, not merely an OIDC token.
                var closeHistoryPath = $"/api/v1/companies/{companyId}/periods/{periodId}/execution-history";
                using (var response = await client.GetAsync(closeHistoryPath + "?offset=0&limit=1"))
                using (var gl11HistoryBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        gl11HistoryBody.RootElement.GetProperty("periodId").GetGuid() == periodId &&
                        gl11HistoryBody.RootElement.GetProperty("history").GetProperty("totalCount").GetInt32() == 0 &&
                        gl11HistoryBody.RootElement.GetProperty("history").GetProperty("items").GetArrayLength() == 0,
                        "GL11 HTTP execution history returned unintended event or wrong period");
                Authenticate(Token("gl11-audit-nonmember"));
                using (var gl11HistoryDenied = await client.GetAsync(closeHistoryPath))
                    Ensure(gl11HistoryDenied.StatusCode == HttpStatusCode.Forbidden,
                        "GL11 execution history leaked to an unrelated JWT subject");
                Authenticate(Token("sql-approver"));
                Console.WriteLine("  HOST PASS GL11-A05 scoped execution result/history over HTTPS/JWT/SQL");

                // GL12-A05: choose a truly closeable and otherwise unused
                // test period; never override the period-close safety guard.
                Guid gl12Period = Guid.Empty;
                Guid gl12PeriodVersion = Guid.Empty;
                using (var periodsResponse = await client.GetAsync(
                    $"/api/v1/companies/{companyId}/periods"))
                using (var periodsJson = JsonDocument.Parse(
                    await periodsResponse.Content.ReadAsStringAsync()))
                {
                    Ensure(periodsResponse.StatusCode == HttpStatusCode.OK,
                        "GL12 test could not enumerate fiscal periods");
                    foreach (var candidate in periodsJson.RootElement.EnumerateArray())
                    {
                        var candidateId = candidate.GetProperty("id").GetGuid();
                        if (candidateId == periodId || candidate.GetProperty("isClosed").GetBoolean())
                            continue;
                        using var preview = await client.GetAsync(
                            $"/api/v1/companies/{companyId}/periods/{candidateId}/close-preview?limit=1");
                        using var previewJson = JsonDocument.Parse(
                            await preview.Content.ReadAsStringAsync());
                        if (preview.StatusCode == HttpStatusCode.OK &&
                            previewJson.RootElement.GetProperty("canCloseGeneralLedger").GetBoolean())
                        {
                            gl12Period = candidateId;
                            gl12PeriodVersion = previewJson.RootElement
                                .GetProperty("expectedVersion").GetGuid();
                            break;
                        }
                    }
                }
                Ensure(gl12Period != Guid.Empty,
                    "No safely closeable disposable period available for GL12 HTTP acceptance");
                var gl12Path = $"/api/v1/companies/{companyId}/periods/{gl12Period}";
                var gl12Closed = await Command(client, gl12Path + "/close",
                    new ChangePeriodRequest(gl12PeriodVersion, "GL12 HTTPS إغلاق"));
                var gl12Reopened = await Command(client, gl12Path + "/reopen",
                    new ChangePeriodRequest(gl12Closed.Version, "GL12 HTTPS إعادة فتح"));
                Authenticate(Token("sql-owner"));
                await Command(client, $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest("gl12-read-audit-reopen",
                        new[] { Permissions.Read, Permissions.Audit, Permissions.PeriodReopen },
                        true, Array.Empty<Guid>(), null));
                await Command(client, $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest("gl12-read-audit-close",
                        new[] { Permissions.Read, Permissions.Audit, Permissions.PeriodClose },
                        true, Array.Empty<Guid>(), null));
                Authenticate(Token("gl12-read-audit-reopen"));
                using (var gl12Http = await client.GetAsync(gl12Path + "/reopen-history?offset=0&limit=1"))
                using (var gl12Json = JsonDocument.Parse(
                    await gl12Http.Content.ReadAsStringAsync()))
                {
                    var summary = gl12Json.RootElement;
                    var history = summary.GetProperty("history");
                    var item = history.GetProperty("items")[0];
                    Ensure(gl12Http.StatusCode == HttpStatusCode.OK &&
                        summary.GetProperty("periodId").GetGuid() == gl12Period &&
                        !summary.GetProperty("isClosed").GetBoolean() &&
                        summary.GetProperty("version").GetGuid() == gl12Reopened.Version &&
                        history.GetProperty("totalCount").GetInt32() == 1 &&
                        item.GetProperty("action").GetString() == "period.reopened" &&
                        item.GetProperty("actorId").GetString() == "sql-approver" &&
                        JsonDocument.Parse(item.GetProperty("details").GetString()!)
                            .RootElement.GetProperty("reason").GetString() ==
                                "GL12 HTTPS إعادة فتح",
                        "GL12 HTTP reopen-only event/result missing or mis-scoped");
                }
                using (var gl12Page = await client.GetAsync(gl12Path + "/reopen-history?offset=1&limit=1"))
                using (var gl12PageJson = JsonDocument.Parse(await gl12Page.Content.ReadAsStringAsync()))
                    Ensure(gl12Page.StatusCode == HttpStatusCode.OK &&
                        gl12PageJson.RootElement.GetProperty("history").GetProperty("totalCount").GetInt32() == 1 &&
                        gl12PageJson.RootElement.GetProperty("history").GetProperty("items").GetArrayLength() == 0,
                        "GL12 HTTP reopen audit paging duplicated a decision");
                using (var gl11Denied = await client.GetAsync(gl12Path + "/execution-history"))
                    Ensure(gl11Denied.StatusCode == HttpStatusCode.Forbidden,
                        "GL12-only reviewer gained unauthorized PeriodClose audit access");
                Authenticate(Token("gl12-read-audit-close"));
                using (var gl12Denied = await client.GetAsync(gl12Path + "/reopen-history"))
                    Ensure(gl12Denied.StatusCode == HttpStatusCode.Forbidden,
                        "GL11-only reviewer gained unauthorized PeriodReopen audit access");
                Authenticate(Token("gl12-not-member"));
                using (var gl12Outside = await client.GetAsync(gl12Path + "/reopen-history"))
                    Ensure(gl12Outside.StatusCode == HttpStatusCode.Forbidden,
                        "GL12 reopening history disclosed to an unrelated JWT");
                Authenticate(Token("sql-approver"));
                Console.WriteLine("  HOST PASS GL12-A05 reopen-only outcome/history, independent PeriodReopen rights and paging over HTTPS/JWT/SQL");
                Console.WriteLine("  HOST PASS SYS05 effective quote, history, direction, usage and audit; GL11 paginated close preview over HTTPS/JWT/SQL");

                // SYS05-GAP03: a quotation does not hold its rate; the next journal
                // must accept the latest effective reference at the document date.
                Authenticate(Token("sql-owner"));
                var sys05QuotedDate = Fixture.Date.AddDays(2);
                var sys05QuotePath = ratesPath + "/quote?currencyCode=NZD&date=" +
                    sys05QuotedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                Guid sys05PriorQuotedRate;
                using (var sys05BeforeRate = await client.GetAsync(sys05QuotePath))
                using (var sys05BeforeJson = JsonDocument.Parse(await sys05BeforeRate.Content.ReadAsStringAsync()))
                {
                    Ensure(sys05BeforeRate.StatusCode == HttpStatusCode.OK, "SYS05-GAP03 baseline quote failed");
                    sys05PriorQuotedRate = sys05BeforeJson.RootElement.GetProperty("exchangeRateId").GetGuid();
                }
                string sys05AuditSnapshot;
                using (var sys05BeforeAudit = await client.GetAsync(ratesPath + $"/{rate.ResourceId}/audit-events"))
                    sys05AuditSnapshot = await sys05BeforeAudit.Content.ReadAsStringAsync();
                using (var sys05RepeatedQuote = await client.GetAsync(sys05QuotePath))
                using (var sys05RepeatedJson = JsonDocument.Parse(await sys05RepeatedQuote.Content.ReadAsStringAsync()))
                    Ensure(sys05RepeatedQuote.StatusCode == HttpStatusCode.OK &&
                        sys05RepeatedJson.RootElement.GetProperty("exchangeRateId").GetGuid() == sys05PriorQuotedRate,
                        "SYS05-GAP03 identical quote was not stable before a newer rate");
                using (var sys05AfterAudit = await client.GetAsync(ratesPath + $"/{rate.ResourceId}/audit-events"))
                    Ensure(sys05AuditSnapshot == await sys05AfterAudit.Content.ReadAsStringAsync(),
                        "SYS05-GAP03 read-only quote modified the exchange-rate audit");

                var sys05NewEffective = await Command(client, ratesPath,
                    new CreateExchangeRateRequest("NZD", sys05QuotedDate, 1.75m, "SYS05 newer"));
                using (var sys05Refreshed = await client.GetAsync(sys05QuotePath))
                using (var sys05RefreshedJson = JsonDocument.Parse(await sys05Refreshed.Content.ReadAsStringAsync()))
                    Ensure(sys05Refreshed.StatusCode == HttpStatusCode.OK &&
                        sys05RefreshedJson.RootElement.GetProperty("exchangeRateId").GetGuid() == sys05NewEffective.ResourceId &&
                        sys05RefreshedJson.RootElement.GetProperty("rateToFunctional").GetString() == "1.75",
                        "SYS05-GAP03 previous quote wrongly reserved an outdated rate");

                Authenticate(Token("sql-clerk"));
                var sys05JournalPath = path + "?source=Manual";
                int sys05JournalsBefore;
                using (var sys05List = await client.GetAsync(sys05JournalPath))
                using (var sys05ListJson = JsonDocument.Parse(await sys05List.Content.ReadAsStringAsync()))
                {
                    Ensure(sys05List.StatusCode == HttpStatusCode.OK, "SYS05-GAP03 baseline journal list failed");
                    sys05JournalsBefore = sys05ListJson.RootElement.GetProperty("totalCount").GetInt32();
                }
                var sys05JournalRequest = journalRequest with {
                    DocumentDate = sys05QuotedDate, AccountingDate = sys05QuotedDate,
                    Lines = journalRequest.Lines.Select(x => x with {
                        CurrencyCode = "NZD", ExchangeRateId = sys05PriorQuotedRate }).ToArray()
                };
                using (var sys05StaleCommand = new HttpRequestMessage(HttpMethod.Post, path))
                {
                    sys05StaleCommand.Headers.Add("Idempotency-Key", Fixture.Key());
                    sys05StaleCommand.Content = new StringContent(
                        JsonSerializer.Serialize(sys05JournalRequest, JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var sys05Denied = await client.SendAsync(sys05StaleCommand);
                    using var sys05Problem = JsonDocument.Parse(await sys05Denied.Content.ReadAsStringAsync());
                    Ensure(sys05Denied.StatusCode == HttpStatusCode.UnprocessableEntity &&
                        sys05Problem.RootElement.GetProperty("code").GetString() == "stale_exchange_rate",
                        "SYS05-GAP03 stale quote was accepted at journal creation");
                }
                using (var sys05ListAfter = await client.GetAsync(sys05JournalPath))
                using (var sys05ListAfterJson = JsonDocument.Parse(await sys05ListAfter.Content.ReadAsStringAsync()))
                    Ensure(sys05ListAfter.StatusCode == HttpStatusCode.OK &&
                        sys05ListAfterJson.RootElement.GetProperty("totalCount").GetInt32() == sys05JournalsBefore,
                        "SYS05-GAP03 rejected old quote persisted a journal");
                var sys05AcceptedJournal = await Command(client, path,
                    sys05JournalRequest with {
                        Lines = sys05JournalRequest.Lines.Select(x => x with {
                            ExchangeRateId = sys05NewEffective.ResourceId }).ToArray()
                    });
                using (var sys05Persisted = await client.GetAsync(path + $"/{sys05AcceptedJournal.ResourceId}"))
                using (var sys05PersistedJson = JsonDocument.Parse(await sys05Persisted.Content.ReadAsStringAsync()))
                    Ensure(sys05Persisted.StatusCode == HttpStatusCode.OK &&
                        sys05PersistedJson.RootElement.GetProperty("lines").EnumerateArray().All(x =>
                            x.GetProperty("exchangeRateId").GetGuid() == sys05NewEffective.ResourceId &&
                            x.GetProperty("exchangeRate").GetString() == "1.75"),
                        "SYS05-GAP03 new rate not captured as journal snapshot");
                Console.WriteLine("  HOST PASS SYS05-GAP03 quote advisory, stale-rate rejection, fresh snapshot over HTTPS/JWT/SQL");

                Authenticate(Token("sql-owner"));
                var accountPath = $"/api/v1/companies/{companyId}/accounts";
                var card = await Command(client, accountPath, new CreateAccountRequest("HTTP-GL01", "حساب HTTP", AccountType.Asset,
                    null, true, false, false, null, NormalBalance.Credit, "HTTP contra asset", "ملاحظة"));
                using (var cardDetail = await client.GetAsync(accountPath + $"/{card.ResourceId}"))
                {
                    Ensure(cardDetail.StatusCode == HttpStatusCode.OK, "Account HTTP detail failed");
                    using var account = JsonDocument.Parse(await cardDetail.Content.ReadAsStringAsync());
                    Ensure(account.RootElement.GetProperty("revision").GetInt32() == 1 && account.RootElement.GetProperty("normalBalance").GetString() == "Credit" && account.RootElement.GetProperty("englishName").GetString() == "HTTP contra asset", "Account HTTP nature/labels differ");
                }
                var editCard = new EditAccountRequest(card.Version, "HTTP-GL01", "حساب معدل", AccountType.Asset, null, true, false, false,
                    null, NormalBalance.Credit, "Edited account", null, "تصحيح الوصف");
                var editKey = Fixture.Key();
                var editedCard = await Command(client, accountPath + $"/{card.ResourceId}", editCard, editKey, HttpMethod.Put);
                Ensure(await Command(client, accountPath + $"/{card.ResourceId}", editCard, editKey, HttpMethod.Put) == editedCard, "Account HTTP edit replay differs");
                using (var changes = await client.GetAsync(accountPath + $"/{card.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await changes.Content.ReadAsStringAsync()))
                    Ensure(changes.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 2, "Account HTTP audit differs");
                using (var usage = await client.GetAsync(accountPath + $"/{journalRequest.Lines[0].AccountId}/usage?limit=1"))
                using (var json = JsonDocument.Parse(await usage.Content.ReadAsStringAsync()))
                    Ensure(usage.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("hasUsageInSubtree").GetBoolean(), "Account HTTP usage differs");
                // GL01-02: production HTTPS/OIDC/JWT account-usage paging and tenant
                // authorization; reading does not add receipts, journals, audit or outbox.
                var accountUsageUrl = accountPath + $"/{journalRequest.Lines[0].AccountId}/usage";
                Ensure(specJson.Contains("/api/v1/companies/{companyId}/accounts/{id}/usage",
                    StringComparison.Ordinal), "GL01-02 OpenAPI account usage route missing");
                var accountUsageBefore = await ReadOnlyCounts(connection, companyId);
                using (var firstUsage = await client.GetAsync(accountUsageUrl + "?offset=0&limit=1"))
                using (var firstJson = JsonDocument.Parse(await firstUsage.Content.ReadAsStringAsync()))
                using (var nextUsage = await client.GetAsync(accountUsageUrl + "?offset=1&limit=1"))
                using (var nextJson = JsonDocument.Parse(await nextUsage.Content.ReadAsStringAsync()))
                {
                    var first = firstJson.RootElement;
                    var next = nextJson.RootElement;
                    Ensure(firstUsage.StatusCode == HttpStatusCode.OK &&
                        nextUsage.StatusCode == HttpStatusCode.OK &&
                        first.GetProperty("accountId").GetGuid() == journalRequest.Lines[0].AccountId &&
                        first.GetProperty("journalCount").GetInt32() > 0 &&
                        first.GetProperty("recurringTemplateCount").GetInt32() > 0 &&
                        first.GetProperty("hasUsageInSubtree").GetBoolean() &&
                        first.GetProperty("references").GetProperty("totalCount").GetInt32() >= 2 &&
                        first.GetProperty("references").GetProperty("totalCount").GetInt32() ==
                            next.GetProperty("references").GetProperty("totalCount").GetInt32() &&
                        first.GetProperty("references").GetProperty("items")[0].GetProperty("resourceId").GetGuid() !=
                            next.GetProperty("references").GetProperty("items")[0].GetProperty("resourceId").GetGuid(),
                        "GL01-02 HTTPS SQL-backed journal/template usage or page stability differs");
                }
                using (var missingAccountUsage = await client.GetAsync(accountPath + $"/{Guid.NewGuid()}/usage"))
                    Ensure(missingAccountUsage.StatusCode == HttpStatusCode.NotFound,
                        "GL01-02 account usage for a non-existent account was disclosed");
                Authenticate(Token("audit-no-membership"));
                using (var unauthorizedAccountUsage = await client.GetAsync(accountUsageUrl))
                    Ensure(unauthorizedAccountUsage.StatusCode == HttpStatusCode.Forbidden,
                        "GL01-02 unrelated JWT subject disclosed usage references");
                Authenticate(Token("sql-owner"));
                Ensure(accountUsageBefore == await ReadOnlyCounts(connection, companyId),
                    "GL01-02 GET usage or rejected JWT modified persisted finance/audit/outbox state");
                Console.WriteLine("  HOST PASS GL01-02 account usage paging, journal/template sources, JWT denial, OpenAPI and read-only SQL");
                foreach (var invalidBody in new[]
                {
                    "{\"code\":\"MISSING-NATURE\",\"name\":\"Missing nature\",\"type\":\"Asset\",\"parentId\":null,\"isPosting\":true,\"isControl\":false,\"requiresCostCenter\":false,\"currencyCode\":null}",
                    JsonSerializer.Serialize(new CreateAccountRequest("BAD-NATURE", "Bad nature", AccountType.Asset, null, true, false, false, null, NormalBalance.Debit), JsonDefaults.Options).Replace("\"Debit\"", "\"Unknown\"", StringComparison.Ordinal)
                })
                {
                    using var invalid = new HttpRequestMessage(HttpMethod.Post, accountPath);
                    invalid.Headers.Add("Idempotency-Key", Fixture.Key()); invalid.Content = new StringContent(invalidBody, Encoding.UTF8, "application/json");
                    using var result = await client.SendAsync(invalid); Ensure(result.StatusCode == HttpStatusCode.BadRequest, "Missing/invalid normal balance accepted");
                }
                var currenciesPath = $"/api/v1/companies/{companyId}/currencies";
                var currencyCard = await Command(client, currenciesPath, new CreateCurrencyRequest("GBP", 2, "جنيه إسترليني", "Pound", null, "£", "بنس"));
                var currencyEdit = new EditCurrencyRequest(currencyCard.Version, 3, "جنيه إسترليني", "Pound sterling", "اختبار", "£", "بنس", "تحديث");
                currencyCard = await Command(client, currenciesPath + $"/{currencyCard.ResourceId}", currencyEdit, method: HttpMethod.Put);
                currencyCard = await Command(client, currenciesPath + $"/{currencyCard.ResourceId}/status", new SetCurrencyStatusRequest(currencyCard.Version, false, "إيقاف"));
                using (var search = await client.GetAsync(currenciesPath + "/search?search=Pound&isActive=false"))
                using (var json = JsonDocument.Parse(await search.Content.ReadAsStringAsync()))
                    Ensure(search.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("items")[0].GetProperty("revision").GetInt32() == 3, "Currency HTTP search/metadata failed");
                using (var log = await client.GetAsync(currenciesPath + $"/{currencyCard.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await log.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 3, "Currency HTTP audit failed");
                // SYS04-FG04: the audit is system-issued, company-scoped,
                // chronologically ordered by the authoritative server clock.
                using (var audit = await client.GetAsync(currenciesPath + $"/{currencyCard.ResourceId}/audit-events"))
                using (var auditJson = JsonDocument.Parse(await audit.Content.ReadAsStringAsync()))
                {
                    var events = auditJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
                    Ensure(audit.StatusCode == HttpStatusCode.OK &&
                        auditJson.RootElement.GetProperty("totalCount").GetInt32() == 3 &&
                        events.Length == 3 && events.All(x =>
                            x.GetProperty("actorId").GetString() == "sql-owner" &&
                            x.GetProperty("companyId").GetGuid() == companyId &&
                            x.GetProperty("resourceId").GetGuid() == currencyCard.ResourceId),
                        "SYS04-FG04 currency audit actor/resource/tenant was not preserved");
                    var created = events.Single(x => x.GetProperty("action").GetString() == "currency.created")
                        .GetProperty("occurredAt").GetDateTimeOffset();
                    var edited = events.Single(x => x.GetProperty("action").GetString() == "currency.edited")
                        .GetProperty("occurredAt").GetDateTimeOffset();
                    var disabled = events.Single(x => x.GetProperty("action").GetString() == "currency.status-changed")
                        .GetProperty("occurredAt").GetDateTimeOffset();
                    Ensure(created != default && edited >= created && disabled >= edited,
                        "SYS04-FG04 currency audit timestamps were not chronological");
                }
                Console.WriteLine("  HOST PASS SYS04-FG04 currency audit actor, timestamps and tenant identity over HTTPS/JWT/SQL");

                // SYS04-FG03: display metadata and changed integer scale
                // survive authorized PUT and a disabled-card HTTPS GET.
                using (var currencyProfile = await client.GetAsync(currenciesPath + $"/{currencyCard.ResourceId}"))
                using (var currencyJson = JsonDocument.Parse(await currencyProfile.Content.ReadAsStringAsync()))
                    Ensure(currencyProfile.StatusCode == HttpStatusCode.OK &&
                        currencyJson.RootElement.GetProperty("scale").GetInt32() == 3 &&
                        currencyJson.RootElement.GetProperty("displaySymbol").GetString() == "£" &&
                        currencyJson.RootElement.GetProperty("fractionName").GetString() == "بنس" &&
                        currencyJson.RootElement.GetProperty("revision").GetInt32() == 3 &&
                        !currencyJson.RootElement.GetProperty("isActive").GetBoolean(),
                        "SYS04-FG03 currency scale/symbol/fraction did not persist over HTTPS/SQL");
                using (var invalidScale = new HttpRequestMessage(HttpMethod.Post, currenciesPath))
                {
                    invalidScale.Headers.Add("Idempotency-Key", Fixture.Key());
                    invalidScale.Content = new StringContent(JsonSerializer.Serialize(
                        new CreateCurrencyRequest("AUD", 7, "دولار أسترالي", DisplaySymbol: "A$"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(invalidScale);
                    using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                    Ensure(rejected.StatusCode == HttpStatusCode.UnprocessableEntity &&
                        error.RootElement.GetProperty("code").GetString() == "invalid_scale",
                        "SYS04-FG03 HTTP accepted out-of-bounds currency precision");
                }
                using (var rejectedSearch = await client.GetAsync(currenciesPath + "/search?search=AUD"))
                using (var rejectedJson = JsonDocument.Parse(await rejectedSearch.Content.ReadAsStringAsync()))
                    Ensure(rejectedSearch.StatusCode == HttpStatusCode.OK &&
                        rejectedJson.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "SYS04-FG03 invalid currency precision persisted a card");
                Console.WriteLine("  HOST PASS SYS04-FG03 integer precision and Unicode metadata over HTTPS/JWT/SQL");
                // SYS04-OP03: usage remains readable when disabled, and both
                // usage and audit must enforce tenant-level authorization.
                using (var usage = await client.GetAsync(currenciesPath + $"/{currencyCard.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await usage.Content.ReadAsStringAsync()))
                    Ensure(usage.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("currencyId").GetGuid() == currencyCard.ResourceId &&
                        !json.RootElement.GetProperty("isFunctionalCurrency").GetBoolean() &&
                        json.RootElement.GetProperty("accountCount").GetInt32() == 0,
                        "SYS04-OP03 currency usage lost disabled card or tenant identity");
                Authenticate(Token("sys04-unrelated-user"));
                using (var forbiddenUsage = await client.GetAsync(currenciesPath + $"/{currencyCard.ResourceId}/usage"))
                    Ensure(forbiddenUsage.StatusCode == HttpStatusCode.Forbidden,
                        "SYS04-OP03 unrelated actor read currency usage");
                using (var forbiddenAudit = await client.GetAsync(currenciesPath + $"/{currencyCard.ResourceId}/audit-events"))
                    Ensure(forbiddenAudit.StatusCode == HttpStatusCode.Forbidden,
                        "SYS04-OP03 unrelated actor read currency audit");
                Authenticate(Token("sql-owner"));
                Console.WriteLine("  HOST PASS SYS04-OP03 currency usage, audit, disabled history and forbidden access over HTTPS/JWT/SQL");

                // SYS04-AC02: an existing account reference freezes currency
                // precision even if no journal has been posted. The rejection
                // must not mutate the card version or its audit history.
                var sys04UsedCurrency = await Command(client, currenciesPath,
                    new CreateCurrencyRequest("CAD", 2, "دولار كندي", "Canadian dollar"));
                await Command(client, $"/api/v1/companies/{companyId}/accounts",
                    new CreateAccountRequest("SYS04-CAD-REF", "حساب كندي", AccountType.Asset,
                        null, true, false, false, "CAD", NormalBalance.Debit));
                using (var negativeEdit = new HttpRequestMessage(HttpMethod.Put,
                    currenciesPath + $"/{sys04UsedCurrency.ResourceId}"))
                {
                    negativeEdit.Headers.Add("Idempotency-Key", Fixture.Key());
                    negativeEdit.Content = new StringContent(JsonSerializer.Serialize(
                        new EditCurrencyRequest(sys04UsedCurrency.Version, 3, "دولار كندي",
                            "Canadian dollar", null, null, null, "محاولة تغيير دقة مستخدمة"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejectedPrecision = await client.SendAsync(negativeEdit);
                    using var error = JsonDocument.Parse(await rejectedPrecision.Content.ReadAsStringAsync());
                    Ensure(rejectedPrecision.StatusCode == HttpStatusCode.Conflict &&
                        error.RootElement.GetProperty("code").GetString() == "currency_precision_in_use",
                        "SYS04-AC02 referenced currency accepted retrospective precision change");
                }
                using (var detail = await client.GetAsync(currenciesPath + $"/{sys04UsedCurrency.ResourceId}"))
                using (var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                    Ensure(detail.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("scale").GetInt32() == 2 &&
                        json.RootElement.GetProperty("revision").GetInt32() == 1 &&
                        json.RootElement.GetProperty("version").GetGuid() == sys04UsedCurrency.Version,
                        "SYS04-AC02 rejected precision edit mutated currency state");
                using (var history = await client.GetAsync(currenciesPath + $"/{sys04UsedCurrency.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await history.Content.ReadAsStringAsync()))
                    Ensure(history.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 1,
                        "SYS04-AC02 rejected precision edit wrote currency audit");
                Console.WriteLine("  HOST PASS SYS04-AC02 referenced currency precision edit refused atomically over HTTPS/JWT/SQL");

                // SYS01-AC02: an existing posted journal forbids functional-currency
                // conversion on the real authenticated production HTTP + SQL path.
                var sys01Path = $"/api/v1/companies/{companyId}";
                Guid sys01Version;
                int sys01Revision;
                int sys01Scale;
                string sys01Code, sys01Name, sys01Country, sys01Functional;
                using (var response = await client.GetAsync(sys01Path))
                using (var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK, "SYS01-AC02 prerequisite company detail unavailable");
                    var c = detail.RootElement;
                    sys01Version = c.GetProperty("version").GetGuid();
                    sys01Revision = c.GetProperty("revision").GetInt32();
                    sys01Scale = c.GetProperty("currencyScale").GetInt32();
                    sys01Code = c.GetProperty("code").GetString()!;
                    sys01Name = c.GetProperty("name").GetString()!;
                    sys01Country = c.GetProperty("countryCode").GetString()!;
                    sys01Functional = c.GetProperty("functionalCurrency").GetString()!;
                }
                int sys01AuditBefore;
                using (var response = await client.GetAsync(sys01Path + "/profile-audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK, "SYS01-AC02 prerequisite audit unavailable");
                    sys01AuditBefore = json.RootElement.GetProperty("totalCount").GetInt32();
                }
                using (var request = new HttpRequestMessage(HttpMethod.Put, sys01Path))
                {
                    request.Headers.Add("Idempotency-Key", Fixture.Key());
                    request.Content = new StringContent(JsonSerializer.Serialize(new EditCompanyRequest(
                        sys01Version, sys01Code, sys01Name, sys01Country, "NOK", Fixture.Profile(),
                        "منع تحويل العملة بعد وجود قيود"), JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var response = await client.SendAsync(request);
                    Ensure(response.StatusCode == HttpStatusCode.Conflict &&
                        (await response.Content.ReadAsStringAsync()).Contains("functional_currency_in_use", StringComparison.Ordinal),
                        "SYS01-AC02 production HTTP accepted a historical functional-currency change");
                }
                using (var response = await client.GetAsync(sys01Path))
                using (var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    var c = detail.RootElement;
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        c.GetProperty("functionalCurrency").GetString() == sys01Functional &&
                        c.GetProperty("currencyScale").GetInt32() == sys01Scale &&
                        c.GetProperty("revision").GetInt32() == sys01Revision &&
                        c.GetProperty("version").GetGuid() == sys01Version,
                        "SYS01-AC02 rejected HTTP conversion mutated company in SQL");
                }
                using (var response = await client.GetAsync(sys01Path + "/profile-audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == sys01AuditBefore,
                        "SYS01-AC02 rejected HTTP conversion added audit");
                Console.WriteLine("  HOST PASS SYS01-AC02 functional-currency conversion denied atomically over HTTPS/JWT/SQL");


                var centersPath = $"/api/v1/companies/{companyId}/cost-centers";
                var rootCenter = await Command(client, centersPath, new CreateCostCenterRequest("HTTP-ROOT", "مركز عام", null));
                var childCenter = await Command(client, centersPath, new CreateCostCenterRequest("HTTP-CHILD", "مركز فرعي", journalRequest.BranchId, rootCenter.ResourceId, EnglishName: "HTTP center"));
                childCenter = await Command(client, centersPath + $"/{childCenter.ResourceId}",
                    new EditCostCenterRequest(childCenter.Version, "HTTP-CHILD", "مركز معدل", journalRequest.BranchId, rootCenter.ResourceId, null, "HTTP edited center", "ملاحظة", "تعديل"), method: HttpMethod.Put);
                using (var search = await client.GetAsync(centersPath + $"/search?parentId={rootCenter.ResourceId}&search=edited"))
                using (var json = JsonDocument.Parse(await search.Content.ReadAsStringAsync()))
                    Ensure(search.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("items")[0].GetProperty("revision").GetInt32() == 2, "Center HTTP search/metadata failed");
                using (var usage = await client.GetAsync(centersPath + $"/{rootCenter.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await usage.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("childCenterCount").GetInt32() == 1, "Center HTTP usage failed");
                Console.WriteLine("  HOST PASS SYS04/COST01 create, PUT edit, search, status, usage and audit over production HTTPS/JWT/SQL");
                Authenticate(Token("sql-clerk"));
                Console.WriteLine("  HOST PASS GL01 create/detail/PUT edit/replay, explicit nature, usage and audit over production HTTPS/JWT/SQL");
                var company = new CreateCompanyRequest(Guid.NewGuid(), "HTTPTEST", "شركة HTTP", "YE", "YER", 2, Fixture.Profile());
                using var denied = new HttpRequestMessage(HttpMethod.Post, "/api/v1/companies");
                denied.Headers.Add("Idempotency-Key", Fixture.Key()); denied.Headers.Add("X-Role", "erp.platform-admin");
                denied.Content = new StringContent(JsonSerializer.Serialize(company, JsonDefaults.Options), Encoding.UTF8, "application/json");
                using var deniedResult = await client.SendAsync(denied); Ensure(deniedResult.StatusCode == HttpStatusCode.Forbidden, "Header spoofed a role");
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                var companyReceipt = await Command(client, "/api/v1/companies", company);
                var companyPath = $"/api/v1/companies/{company.Id}";
                // SYS01-FG01: production HTTPS/JWT returns server identity and
                // Arabic name, and an already assigned company code is rejected.
                using(var detailsResponse=await client.GetAsync(companyPath))
                using(var detailsJson=JsonDocument.Parse(await detailsResponse.Content.ReadAsStringAsync()))
                {
                    var c=detailsJson.RootElement;
                    Ensure(detailsResponse.StatusCode==HttpStatusCode.OK &&
                        c.GetProperty("id").GetGuid()==company.Id &&
                        c.GetProperty("code").GetString()=="HTTPTEST" &&
                        c.GetProperty("name").GetString()=="شركة HTTP",
                        "SYS01-FG01 HTTPS identity/code/Arabic name mismatch");
                }
                using(var duplicateRequest=new HttpRequestMessage(HttpMethod.Post,"/api/v1/companies"))
                {
                    duplicateRequest.Headers.Add("Idempotency-Key",Fixture.Key());
                    duplicateRequest.Content=new StringContent(JsonSerializer.Serialize(
                        company with { Id=Guid.NewGuid() },JsonDefaults.Options),Encoding.UTF8,"application/json");
                    using var duplicate=await client.SendAsync(duplicateRequest);
                    Ensure(duplicate.StatusCode==HttpStatusCode.Conflict &&
                        (await duplicate.Content.ReadAsStringAsync()).Contains("company_exists",StringComparison.Ordinal),
                        "SYS01-FG01 HTTPS accepted a duplicate company code");
                }
                Console.WriteLine("  HOST PASS SYS01-FG01 identity/code/Arabic and duplicate rejection over HTTPS/JWT/SQL");
                // SYS01-OP01: search is scoped to visible companies, never
                // to all tenants. Positive code/Arabic queries and negative scope.
                using (var byCode = await client.GetAsync("/api/v1/companies?search=HTTPTEST"))
                using (var json = JsonDocument.Parse(await byCode.Content.ReadAsStringAsync()))
                    Ensure(byCode.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == company.Id),
                        "SYS01-OP01 authorized company code search omitted its tenant");
                using (var byArabic = await client.GetAsync("/api/v1/companies?search=" + Uri.EscapeDataString("شركة HTTP")))
                using (var json = JsonDocument.Parse(await byArabic.Content.ReadAsStringAsync()))
                    Ensure(byArabic.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == company.Id),
                        "SYS01-OP01 Arabic company search omitted its tenant");
                using (var absent = await client.GetAsync("/api/v1/companies?search=NOT-PRESENT-COMPANY"))
                using (var json = JsonDocument.Parse(await absent.Content.ReadAsStringAsync()))
                    Ensure(absent.StatusCode == HttpStatusCode.OK && json.RootElement.GetArrayLength() == 0,
                        "SYS01-OP01 unknown search leaked companies");
                Authenticate(Token("sql-clerk"));
                using (var deniedSearch = await client.GetAsync("/api/v1/companies?search=HTTPTEST"))
                using (var json = JsonDocument.Parse(await deniedSearch.Content.ReadAsStringAsync()))
                    Ensure(deniedSearch.StatusCode == HttpStatusCode.OK && json.RootElement.GetArrayLength() == 0,
                        "SYS01-OP01 another tenant's company is visible to a non-member");
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                Console.WriteLine("  HOST PASS SYS01-OP01 authorized creation, code/Arabic search, empty match and tenant isolation over HTTPS/JWT/SQL");

                var companyProfile = company.Profile with { CommercialRegistration = "00042", City = "مدينة معدلة", PostalCode = "00123",
                    EnglishName = "HTTP legal name", Notes = "ملاحظة HTTP", Street = "شارع HTTP",
                    BuildingNumber = "0007", AdditionalNumber = "0009",
                    TaxRegistration = "000456", District = "حي HTTP" };
                companyReceipt = await Command(client, companyPath, new EditCompanyRequest(companyReceipt.Version, company.Code, company.Name,
                    company.CountryCode, company.FunctionalCurrency, companyProfile, "استكمال البيانات"), method: HttpMethod.Put);
                using (var companyDetail = await client.GetAsync(companyPath))
                using (var json = JsonDocument.Parse(await companyDetail.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("id").GetGuid() == company.Id &&
                        json.RootElement.GetProperty("code").GetString() == company.Code &&
                        json.RootElement.GetProperty("name").GetString() == company.Name &&
                        json.RootElement.GetProperty("commercialRegistration").GetString() == "00042" &&
                        json.RootElement.GetProperty("revision").GetInt32() == 2 &&
                        json.RootElement.GetProperty("englishName").GetString() == "HTTP legal name" &&
                        json.RootElement.GetProperty("notes").GetString() == "ملاحظة HTTP" &&
                        json.RootElement.GetProperty("legalName").GetString() == companyProfile.LegalName &&
                        json.RootElement.GetProperty("street").GetString() == "شارع HTTP" &&
                        json.RootElement.GetProperty("buildingNumber").GetString() == "0007" &&
                        json.RootElement.GetProperty("postalCode").GetString() == "00123" &&
                        json.RootElement.GetProperty("additionalNumber").GetString() == "0009" &&
                        json.RootElement.GetProperty("taxRegistration").GetString() == "000456" &&
                        json.RootElement.GetProperty("city").GetString() == "مدينة معدلة" &&
                        json.RootElement.GetProperty("district").GetString() == "حي HTTP" &&
                        json.RootElement.GetProperty("fiscalYearStartsOn").GetString() == "2026-01-01" &&
                        json.RootElement.GetProperty("isProfileComplete").GetBoolean(), "SYS01-FG02 company HTTP profile differs");
                Console.WriteLine("  HOST PASS SYS01-FG05 address and zero-prefixed building/postal/additional numbers over HTTPS/JWT/SQL");
                Console.WriteLine("  HOST PASS SYS01-FG04 tax code, fiscal-year start, city and district over HTTPS/JWT/SQL");
                companyReceipt = await Command(client, companyPath + "/status", new SetCompanyStatusRequest(companyReceipt.Version, false, "إيقاف"));
                using (var blockedWrite = new HttpRequestMessage(HttpMethod.Post, companyPath + "/branches"))
                {
                    blockedWrite.Headers.Add("Idempotency-Key", Fixture.Key());
                    blockedWrite.Content = new StringContent(JsonSerializer.Serialize(new CreateBranchRequest("BLOCKED", "موقوف", "مدينة الاختبار", "عنوان الاختبار"), JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var result = await client.SendAsync(blockedWrite);
                    Ensure(result.StatusCode == HttpStatusCode.Conflict && (await result.Content.ReadAsStringAsync()).Contains("company_inactive", StringComparison.Ordinal), "Inactive company accepted HTTP write");
                }
                using (var log = await client.GetAsync(companyPath + "/profile-audit-events"))
                using (var json = JsonDocument.Parse(await log.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 3, "Company HTTP audit differs");
                // SYS01-FG06: prove system-owned audit fields on production HTTPS/JWT/SQL,
                // and forbid reading the profile audit with a non-member JWT.
                using (var auditResponse = await client.GetAsync(companyPath + "/profile-audit-events"))
                using (var auditJson = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync()))
                {
                    Ensure(auditResponse.StatusCode == HttpStatusCode.OK,
                        "SYS01-FG06 authorized company audit was not returned");
                    var events = auditJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
                    Ensure(events.Length == 3, "SYS01-FG06 expected exactly three profile audit events");
                    var created = events.Single(x => x.GetProperty("action").GetString() == "company.created");
                    var edited = events.Single(x => x.GetProperty("action").GetString() == "company.edited");
                    var disabled = events.Single(x => x.GetProperty("action").GetString() == "company.status-changed");
                    Ensure(events.All(x => x.GetProperty("actorId").GetString() == "runtime-admin" &&
                        x.GetProperty("resourceId").GetGuid() == company.Id &&
                        x.GetProperty("companyId").GetGuid() == company.Id),
                        "SYS01-FG06 caller identity or tenant scope was not preserved");
                    var createdAt = created.GetProperty("occurredAt").GetDateTimeOffset();
                    var editedAt = edited.GetProperty("occurredAt").GetDateTimeOffset();
                    var statusAt = disabled.GetProperty("occurredAt").GetDateTimeOffset();
                    Ensure(createdAt != default && editedAt >= createdAt && statusAt >= editedAt &&
                        companyReceipt.Version != Guid.Empty,
                        "SYS01-FG06 server audit timestamps or concurrency token invalid");
                }
                Authenticate(Token("sql-clerk"));
                using (var deniedAudit = await client.GetAsync(companyPath + "/profile-audit-events"))
                    Ensure(deniedAudit.StatusCode == HttpStatusCode.Forbidden,
                        "SYS01-FG06 unauthorized JWT could read company profile audit");
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                Console.WriteLine("  HOST PASS SYS01-FG06 actor, chronological audit, tenant scope and forbidden audit access over HTTPS/JWT/SQL");
                // SYS01-OP03: usage and audit remain readable for an inactive
                // tenant, but neither endpoint accepts an unrelated JWT.
                using (var usageResponse = await client.GetAsync(companyPath + "/usage"))
                using (var usageJson = JsonDocument.Parse(await usageResponse.Content.ReadAsStringAsync()))
                {
                    Ensure(usageResponse.StatusCode == HttpStatusCode.OK, "SYS01-OP03 inactive company usage unavailable");
                    var usage = usageJson.RootElement;
                    Ensure(usage.GetProperty("companyId").GetGuid() == company.Id &&
                        usage.GetProperty("branchCount").GetInt32() == 0 &&
                        usage.GetProperty("accountCount").GetInt32() == 0 &&
                        usage.GetProperty("currencyCount").GetInt32() == 1 &&
                        usage.GetProperty("fiscalYearCount").GetInt32() == 0 &&
                        usage.GetProperty("journalCount").GetInt32() == 0 &&
                        usage.GetProperty("recurringTemplateCount").GetInt32() == 0 &&
                        usage.GetProperty("exchangeRateCount").GetInt32() == 0 &&
                        usage.GetProperty("canChangeFunctionalCurrency").GetBoolean(),
                        "SYS01-OP03 tenant-scoped initial usage snapshot differs");
                }
                using (var auditResponse = await client.GetAsync(companyPath + "/profile-audit-events?offset=1&limit=1"))
                using (var auditJson = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync()))
                    Ensure(auditResponse.StatusCode == HttpStatusCode.OK &&
                        auditJson.RootElement.GetProperty("totalCount").GetInt32() == 3 &&
                        auditJson.RootElement.GetProperty("items").GetArrayLength() == 1,
                        "SYS01-OP03 audit pagination or count differs");
                Authenticate(Token("sql-clerk"));
                using (var deniedUsage = await client.GetAsync(companyPath + "/usage"))
                    Ensure(deniedUsage.StatusCode == HttpStatusCode.Forbidden,
                        "SYS01-OP03 unrelated JWT read another company's usage");
                using (var deniedAudit = await client.GetAsync(companyPath + "/profile-audit-events"))
                    Ensure(deniedAudit.StatusCode == HttpStatusCode.Forbidden,
                        "SYS01-OP03 unrelated JWT read another company's audit");
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                Console.WriteLine("  HOST PASS SYS01-OP03 inactive usage, audit pagination and unauthorized JWT isolation over HTTPS/SQL");
                await Command(client, companyPath + "/status", new SetCompanyStatusRequest(companyReceipt.Version, true, "تفعيل"));
                Console.WriteLine("  HOST PASS SYS01 required profile, PUT edit, integer revision, inactive-company write guard and reactivation");
                var branchesPath = companyPath + "/branches";
                var parentBranch = await Command(client, branchesPath, new CreateBranchRequest("PARENT", "أب", "مدينة", "عنوان"));
                var childBranch = await Command(client, branchesPath, new CreateBranchRequest("CHILD", "ابن", "مدينة", "عنوان", parentBranch.ResourceId));
                childBranch = await Command(client, branchesPath + $"/{childBranch.ResourceId}",
                    new EditBranchRequest(childBranch.Version, "CHILD", "ابن معدل", "مدينة", "عنوان 001", parentBranch.ResourceId, null, "HTTP child", "ملاحظة فرع", "تحديث"), method: HttpMethod.Put);
                // SYS02-OP01: both search and new reject a valid JWT from
                // a member of a different company, even before query filtering.
                Authenticate(Token("sql-clerk"));
                using (var deniedSearch = await client.GetAsync(branchesPath + "/search?search=PARENT"))
                    Ensure(deniedSearch.StatusCode == HttpStatusCode.Forbidden,
                        "SYS02-OP01 search exposed another company's branch to a valid JWT");
                using (var deniedCreate = new HttpRequestMessage(HttpMethod.Post, branchesPath))
                {
                    deniedCreate.Headers.Add("Idempotency-Key", Fixture.Key());
                    deniedCreate.Content = new StringContent(JsonSerializer.Serialize(
                        new CreateBranchRequest("FORGED", "غير مصرح", "مدينة", "عنوان"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(deniedCreate);
                    Ensure(rejected.StatusCode == HttpStatusCode.Forbidden,
                        "SYS02-OP01 cross-company JWT provisioned a branch");
                }
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                using (var searchAfterDeniedCreate = await client.GetAsync(branchesPath + "/search?search=FORGED"))
                using (var searchJson = JsonDocument.Parse(await searchAfterDeniedCreate.Content.ReadAsStringAsync()))
                    Ensure(searchAfterDeniedCreate.StatusCode == HttpStatusCode.OK &&
                        searchJson.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "SYS02-OP01 denied branch creation left an authorized-visible record");
                Console.WriteLine("  HOST PASS SYS02-OP01 tenant-scoped branch search/create and denied cross-company JWT");
                // SYS02-FG02: optional English name and notes round-trip,
                // with an active default and authorized same-company parent.
                using (var childProfile = await client.GetAsync(branchesPath + $"/{childBranch.ResourceId}"))
                using (var childJson = JsonDocument.Parse(await childProfile.Content.ReadAsStringAsync()))
                    Ensure(childProfile.StatusCode == HttpStatusCode.OK &&
                        childJson.RootElement.GetProperty("englishName").GetString() == "HTTP child" &&
                        childJson.RootElement.GetProperty("notes").GetString() == "ملاحظة فرع" &&
                        childJson.RootElement.GetProperty("parentId").GetGuid() == parentBranch.ResourceId &&
                        childJson.RootElement.GetProperty("isActive").GetBoolean() &&
                        childJson.RootElement.GetProperty("revision").GetInt32() == 2,
                        "SYS02-FG02 child branch optional fields or parent scope did not round-trip");
                // SYS02-FG01: the public API returns the generated IDs, company
                // reference and original Arabic name; duplicate codes are refused.
                Ensure(parentBranch.ResourceId != Guid.Empty && childBranch.ResourceId != Guid.Empty &&
                    parentBranch.ResourceId != childBranch.ResourceId,
                    "SYS02-FG01 public create did not generate independent branch IDs");
                using (var branchResponse = await client.GetAsync(branchesPath + $"/{parentBranch.ResourceId}"))
                using (var branchJson = JsonDocument.Parse(await branchResponse.Content.ReadAsStringAsync()))
                    Ensure(branchResponse.StatusCode == HttpStatusCode.OK &&
                        branchJson.RootElement.GetProperty("id").GetGuid() == parentBranch.ResourceId &&
                        branchJson.RootElement.GetProperty("companyId").GetGuid() == company.Id &&
                        branchJson.RootElement.GetProperty("code").GetString() == "PARENT" &&
                        branchJson.RootElement.GetProperty("name").GetString() == "أب",
                        "SYS02-FG01 public GET returned incorrect branch identity/company/code/name");
                using (var duplicate = new HttpRequestMessage(HttpMethod.Post, branchesPath))
                {
                    duplicate.Headers.Add("Idempotency-Key", Fixture.Key());
                    duplicate.Content = new StringContent(JsonSerializer.Serialize(
                        new CreateBranchRequest("PARENT", "رمز مكرر", "مدينة", "عنوان"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var result = await client.SendAsync(duplicate);
                    using var error = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
                    Ensure(result.StatusCode == HttpStatusCode.Conflict &&
                        error.RootElement.GetProperty("code").GetString() == "duplicate_code",
                        "SYS02-FG01 HTTP accepted duplicate branch code");
                }
                Console.WriteLine("  HOST PASS SYS02-FG01 generated ID, company reference, Arabic name and duplicate denial over HTTPS/JWT/SQL");
                // SYS02-AC02: reject a self-parent and an ancestor->descendant
                // cycle over the production HTTPS/JWT route without changing SQL state.
                async Task RejectBranchCycle(Guid branchId, Guid version, Guid invalidParent)
                {
                    using var edit = new HttpRequestMessage(HttpMethod.Put, branchesPath + $"/{branchId}");
                    edit.Headers.Add("Idempotency-Key", Fixture.Key());
                    edit.Content = new StringContent(JsonSerializer.Serialize(
                        new EditBranchRequest(version, branchId == parentBranch.ResourceId ? "PARENT" : "CHILD",
                            "دورة ممنوعة", "مدينة", "عنوان", invalidParent, null, null, null, "اختبار منع دورة"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(edit);
                    using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                    Ensure(rejected.StatusCode == HttpStatusCode.UnprocessableEntity &&
                        error.RootElement.GetProperty("code").GetString() == "branch_tree_cycle",
                        "SYS02-AC02 production HTTP accepted a branch cycle");
                }
                await RejectBranchCycle(parentBranch.ResourceId, parentBranch.Version, parentBranch.ResourceId);
                await RejectBranchCycle(parentBranch.ResourceId, parentBranch.Version, childBranch.ResourceId);
                using (var parentAfterRejection = await client.GetAsync(branchesPath + $"/{parentBranch.ResourceId}"))
                using (var parentJson = JsonDocument.Parse(await parentAfterRejection.Content.ReadAsStringAsync()))
                    Ensure(parentAfterRejection.StatusCode == HttpStatusCode.OK &&
                        parentJson.RootElement.GetProperty("version").GetGuid() == parentBranch.Version &&
                        parentJson.RootElement.GetProperty("parentId").ValueKind == JsonValueKind.Null,
                        "SYS02-AC02 rejected cycle mutated parent branch");
                Console.WriteLine("  HOST PASS SYS02-AC02 self-parent and ancestor cycle rejection over HTTPS/JWT");
                using (var response = await client.GetAsync(branchesPath + $"/search?parentId={parentBranch.ResourceId}&search=HTTP"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("items")[0].GetProperty("revision").GetInt32() == 2, "Branch HTTP search failed");
                using (var response = await client.GetAsync(branchesPath + $"/{parentBranch.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("childCount").GetInt32() == 1, "Branch HTTP usage failed");
                var childDisabled = await Command(client, branchesPath + $"/{childBranch.ResourceId}/status", new SetBranchStatusRequest(childBranch.Version, false, "إيقاف"));
                Ensure(childDisabled.Version != Guid.Empty && childDisabled.Version != childBranch.Version,
                    "SYS02-FG05 branch status did not advance the concurrency version");
                using (var disabledProfile = await client.GetAsync(branchesPath + $"/{childBranch.ResourceId}"))
                using (var disabledJson = JsonDocument.Parse(await disabledProfile.Content.ReadAsStringAsync()))
                    Ensure(disabledProfile.StatusCode == HttpStatusCode.OK &&
                        !disabledJson.RootElement.GetProperty("isActive").GetBoolean() &&
                        disabledJson.RootElement.GetProperty("revision").GetInt32() == 3 &&
                        disabledJson.RootElement.GetProperty("notes").GetString() == "ملاحظة فرع",
                        "SYS02-FG02 disabled state or optional notes were not persisted");
                using (var response = await client.GetAsync(branchesPath + $"/{childBranch.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 3,
                        "Branch HTTP audit failed");
                    var events = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
                    Ensure(events.Length == 3 && events.All(x =>
                        x.GetProperty("actorId").GetString() == "runtime-admin" &&
                        x.GetProperty("companyId").GetGuid() == company.Id &&
                        x.GetProperty("resourceId").GetGuid() == childBranch.ResourceId),
                        "SYS02-FG05 branch audit actor or tenant identity differs");
                    var createdAt = events.Single(x => x.GetProperty("action").GetString() == "branch.created")
                        .GetProperty("occurredAt").GetDateTimeOffset();
                    var editedAt = events.Single(x => x.GetProperty("action").GetString() == "branch.edited")
                        .GetProperty("occurredAt").GetDateTimeOffset();
                    var statusAt = events.Single(x => x.GetProperty("action").GetString() == "branch.status-changed")
                        .GetProperty("occurredAt").GetDateTimeOffset();
                    Ensure(createdAt != default && editedAt >= createdAt && statusAt >= editedAt,
                        "SYS02-FG05 system-owned branch create/last-modified timestamps differ");
                }
                Console.WriteLine("  HOST PASS SYS02-FG02/FG05 optional profile, status, numeric revision, audit actor and timestamps");
                // SYS02-OP02: the existing edit/status commands are authorized
                // independently; an unrelated tenant JWT must never mutate them.
                Authenticate(Token("sql-clerk"));
                using (var deniedEdit = new HttpRequestMessage(HttpMethod.Put,
                    branchesPath + $"/{childBranch.ResourceId}"))
                {
                    deniedEdit.Headers.Add("Idempotency-Key", Fixture.Key());
                    deniedEdit.Content = new StringContent(JsonSerializer.Serialize(
                        new EditBranchRequest(childDisabled.Version, "CHILD", "تزوير", "مدينة", "عنوان 001",
                            parentBranch.ResourceId, null, "HTTP child", "محاولة", "غير مصرح"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(deniedEdit);
                    Ensure(rejected.StatusCode == HttpStatusCode.Forbidden,
                        "SYS02-OP02 unrelated JWT edited another company's branch");
                }
                using (var deniedStatus = new HttpRequestMessage(HttpMethod.Post,
                    branchesPath + $"/{childBranch.ResourceId}/status"))
                {
                    deniedStatus.Headers.Add("Idempotency-Key", Fixture.Key());
                    deniedStatus.Content = new StringContent(JsonSerializer.Serialize(
                        new SetBranchStatusRequest(childDisabled.Version, true, "غير مصرح"),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(deniedStatus);
                    Ensure(rejected.StatusCode == HttpStatusCode.Forbidden,
                        "SYS02-OP02 unrelated JWT reactivated another company's branch");
                }
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                using (var unchanged = await client.GetAsync(branchesPath + $"/{childBranch.ResourceId}"))
                using (var unchangedJson = JsonDocument.Parse(await unchanged.Content.ReadAsStringAsync()))
                    Ensure(unchanged.StatusCode == HttpStatusCode.OK &&
                        unchangedJson.RootElement.GetProperty("version").GetGuid() == childDisabled.Version &&
                        unchangedJson.RootElement.GetProperty("revision").GetInt32() == 3 &&
                        !unchangedJson.RootElement.GetProperty("isActive").GetBoolean(),
                        "SYS02-OP02 denied edit/status changed branch revision or disabled state");
                Console.WriteLine("  HOST PASS SYS02-OP02 edit/disable and denied cross-company JWT mutations");
                // SYS02-OP03: usage and audit are tenant-scoped even with
                // active/disabled branches and a valid unrelated JWT.
                var sys02UsagePath = branchesPath + $"/{parentBranch.ResourceId}/usage";
                var sys02AuditPath = branchesPath + $"/{childBranch.ResourceId}/audit-events";
                using (var response = await client.GetAsync(sys02UsagePath))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("branchId").GetGuid() == parentBranch.ResourceId &&
                        json.RootElement.GetProperty("childCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("costCenterCount").GetInt32() == 0 &&
                        json.RootElement.GetProperty("journalCount").GetInt32() == 0 &&
                        json.RootElement.GetProperty("recurringTemplateCount").GetInt32() == 0 &&
                        !json.RootElement.GetProperty("hasSubtreeFinancialUsage").GetBoolean(),
                        "SYS02-OP03 production branch usage snapshot differs");
                using (var response = await client.GetAsync(sys02AuditPath + "?offset=1&limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 3 &&
                        json.RootElement.GetProperty("items").GetArrayLength() == 1,
                        "SYS02-OP03 production branch audit pagination differs");
                Authenticate(Token("sql-clerk"));
                using (var response = await client.GetAsync(sys02UsagePath))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden,
                        "SYS02-OP03 unrelated JWT read another tenant's branch usage");
                using (var response = await client.GetAsync(sys02AuditPath))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden,
                        "SYS02-OP03 unrelated JWT read another tenant's branch audit");
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                Console.WriteLine("  HOST PASS SYS02-OP03 usage, audit paging and unrelated JWT denial over HTTPS/SQL");
                Console.WriteLine("  HOST PASS SYS02 branch addresses, tree, edit, search, usage, status and audit over HTTPS/JWT/SQL");

                var calendarPath = companyPath + "/fiscal-years";
                var fiscalYear = await Command(client, calendarPath, new CreateFiscalYearRequest("2026", new(2026, 1, 1), new(2026, 12, 31),
                    new[] { new FiscalPeriodRequest("2026-H1", new(2026, 1, 1), new(2026, 6, 30)), new FiscalPeriodRequest("2026-H2", new(2026, 7, 1), new(2026, 12, 31)) }));
                using (var response = await client.GetAsync(calendarPath + $"/{fiscalYear.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("periods").GetArrayLength() == 2 &&
                        json.RootElement.GetProperty("state").GetString() == "Open" && json.RootElement.GetProperty("generalLedgerOnly").GetBoolean(), "Custom HTTP calendar failed");
                // SYS03-FG04 partial: server-supplied numeric first revision and
                // creator/time must survive the production HTTPS/JWT/SQL response.
                using (var auditFields = await client.GetAsync(calendarPath + $"/{fiscalYear.ResourceId}"))
                using (var auditFieldsJson = JsonDocument.Parse(await auditFields.Content.ReadAsStringAsync()))
                {
                    var year = auditFieldsJson.RootElement.GetProperty("year");
                    Ensure(auditFields.StatusCode == HttpStatusCode.OK &&
                        year.GetProperty("createdBy").GetString() == "runtime-admin" &&
                        year.GetProperty("createdAt").GetDateTimeOffset() != default &&
                        year.GetProperty("lastModifiedAt").GetDateTimeOffset() ==
                        year.GetProperty("createdAt").GetDateTimeOffset() &&
                        year.GetProperty("revision").GetInt32() == 1,
                        "SYS03-FG04 new fiscal-year audit fields missing from HTTPS JSON");
                }
                Console.WriteLine("  HOST PASS SYS03-FG04 server-owned year creation actor, UTC time and numeric revision");
                // SYS03-AC01: new periods are open, contiguous and carry
                // the exact posting calendar returned by the production API.
                using (var fiscalPositive = await client.GetAsync(calendarPath + $"/{fiscalYear.ResourceId}"))
                using (var positiveJson = JsonDocument.Parse(await fiscalPositive.Content.ReadAsStringAsync()))
                {
                    var periods = positiveJson.RootElement.GetProperty("periods").EnumerateArray().ToArray();
                    Ensure(fiscalPositive.StatusCode == HttpStatusCode.OK &&
                        positiveJson.RootElement.GetProperty("state").GetString() == "Open" &&
                        positiveJson.RootElement.GetProperty("generalLedgerOnly").GetBoolean() &&
                        periods.Length == 2 &&
                        periods[0].GetProperty("code").GetString() == "2026-H1" &&
                        periods[0].GetProperty("startsOn").GetString() == "2026-01-01" &&
                        periods[0].GetProperty("endsOn").GetString() == "2026-06-30" &&
                        !periods[0].GetProperty("isClosed").GetBoolean() &&
                        periods[1].GetProperty("code").GetString() == "2026-H2" &&
                        periods[1].GetProperty("startsOn").GetString() == "2026-07-01" &&
                        periods[1].GetProperty("endsOn").GetString() == "2026-12-31" &&
                        !periods[1].GetProperty("isClosed").GetBoolean(),
                        "SYS03-AC01 HTTPS calendar did not expose contiguous open periods");
                }
                Console.WriteLine("  HOST PASS SYS03-AC01 open contiguous fiscal periods over HTTPS/JWT/SQL");
                // SYS03-FG03: leading-zero period identifiers are text;
                // clients define dates/codes, but the server owns close state.
                var codedFiscal = await Command(client, calendarPath,
                    new CreateFiscalYearRequest("2034", new(2034, 1, 1), new(2034, 12, 31),
                        new[] {
                            new FiscalPeriodRequest("0001", new(2034, 1, 1), new(2034, 6, 30)),
                            new FiscalPeriodRequest("0002", new(2034, 7, 1), new(2034, 12, 31))
                        }));
                using (var codedResponse = await client.GetAsync(calendarPath + $"/{codedFiscal.ResourceId}"))
                using (var codedJson = JsonDocument.Parse(await codedResponse.Content.ReadAsStringAsync()))
                {
                    var periods = codedJson.RootElement.GetProperty("periods").EnumerateArray().ToArray();
                    Ensure(codedResponse.StatusCode == HttpStatusCode.OK && periods.Length == 2 &&
                        periods[0].GetProperty("code").GetString() == "0001" &&
                        periods[0].GetProperty("startsOn").GetString() == "2034-01-01" &&
                        periods[0].GetProperty("endsOn").GetString() == "2034-06-30" &&
                        !periods[0].GetProperty("isClosed").GetBoolean() &&
                        periods[1].GetProperty("code").GetString() == "0002" &&
                        periods[1].GetProperty("startsOn").GetString() == "2034-07-01" &&
                        periods[1].GetProperty("endsOn").GetString() == "2034-12-31" &&
                        !periods[1].GetProperty("isClosed").GetBoolean(),
                        "SYS03-FG03 HTTPS period identifiers/date fields or initial GL-only state differ");
                }
                Console.WriteLine("  HOST PASS SYS03-FG03 period codes with leading zeros and dates over HTTPS/JWT/SQL");
                // SYS03-FG02: fiscal date boundaries survive the public
                // HTTPS/SQL roundtrip and malformed ranges fail atomically.
                using (var fiscalDetail = await client.GetAsync(calendarPath + $"/{fiscalYear.ResourceId}"))
                using (var fiscalJson = JsonDocument.Parse(await fiscalDetail.Content.ReadAsStringAsync()))
                {
                    var year = fiscalJson.RootElement.GetProperty("year");
                    Ensure(fiscalDetail.StatusCode == HttpStatusCode.OK &&
                        year.GetProperty("companyId").GetGuid() == company.Id &&
                        year.GetProperty("startsOn").GetString() == "2026-01-01" &&
                        year.GetProperty("endsOn").GetString() == "2026-12-31",
                        "SYS03-FG02 fiscal date header was not preserved by HTTPS/SQL");
                }
                async Task RejectFiscalRange(string code, DateOnly start, DateOnly end)
                {
                    using var invalid = new HttpRequestMessage(HttpMethod.Post, calendarPath);
                    invalid.Headers.Add("Idempotency-Key", Fixture.Key());
                    invalid.Content = new StringContent(JsonSerializer.Serialize(
                        new CreateFiscalYearRequest(code, start, end),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(invalid);
                    using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                    Ensure(rejected.StatusCode == HttpStatusCode.UnprocessableEntity &&
                        error.RootElement.GetProperty("code").GetString() == "invalid_fiscal_year",
                        "SYS03-FG02 public API accepted reversed or excessively long fiscal dates");
                }
                await RejectFiscalRange("2030-BACK", new(2030, 12, 31), new(2030, 1, 1));
                await RejectFiscalRange("2030-LONG", new(2030, 1, 1), new(2031, 12, 31));
                using (var invalidSearch = await client.GetAsync(calendarPath + "/search?search=2030"))
                using (var invalidJson = JsonDocument.Parse(await invalidSearch.Content.ReadAsStringAsync()))
                    Ensure(invalidSearch.StatusCode == HttpStatusCode.OK &&
                        invalidJson.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "SYS03-FG02 rejected fiscal year persisted a partial row");
                Console.WriteLine("  HOST PASS SYS03-FG02 fiscal date roundtrip and negative ranges over HTTPS/JWT/SQL");
                using (var response = await client.GetAsync(calendarPath + "/search?search=2026"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "Calendar HTTP search failed");
                // SYS03-AC02: no gaps or overlaps in approved fiscal-year
                // coverage; re-opening closed periods remains a GL12 action.
                async Task RejectPeriodCoverage(CreateFiscalYearRequest invalidRequest)
                {
                    using var post = new HttpRequestMessage(HttpMethod.Post, calendarPath);
                    post.Headers.Add("Idempotency-Key", Fixture.Key());
                    post.Content = new StringContent(JsonSerializer.Serialize(
                        invalidRequest, JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(post);
                    using var error = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                    Ensure(rejected.StatusCode == HttpStatusCode.UnprocessableEntity &&
                        error.RootElement.GetProperty("code").GetString() == "invalid_period_coverage",
                        "SYS03-AC02 HTTP accepted an uncovered or overlapping fiscal period");
                }
                await RejectPeriodCoverage(new("2032-GAP", new(2032, 1, 1), new(2032, 12, 31), new[] {
                    new FiscalPeriodRequest("2032-P1", new(2032, 1, 1), new(2032, 6, 30)),
                    new FiscalPeriodRequest("2032-P2", new(2032, 7, 2), new(2032, 12, 31))
                }));
                await RejectPeriodCoverage(new("2033-OVERLAP", new(2033, 1, 1), new(2033, 12, 31), new[] {
                    new FiscalPeriodRequest("2033-P1", new(2033, 1, 1), new(2033, 6, 30)),
                    new FiscalPeriodRequest("2033-P2", new(2033, 6, 30), new(2033, 12, 31))
                }));
                foreach (var rejectedYear in new[] { "2032", "2033" })
                {
                    using var response = await client.GetAsync(calendarPath + "/search?search=" + rejectedYear);
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "SYS03-AC02 refused fiscal coverage created a partial year in SQL");
                }
                Console.WriteLine("  HOST PASS SYS03-AC02 fiscal gaps/overlaps refused atomically over HTTPS/JWT/SQL");
                // SYS03-OP01: a valid JWT belonging only to a different
                // company cannot search or provision this fiscal calendar.
                Authenticate(Token("sql-clerk"));
                using (var deniedCalendarSearch = await client.GetAsync(calendarPath + "/search?search=2026"))
                    Ensure(deniedCalendarSearch.StatusCode == HttpStatusCode.Forbidden,
                        "SYS03-OP01 unrelated JWT searched another company's fiscal years");
                using (var deniedCalendarCreate = new HttpRequestMessage(HttpMethod.Post, calendarPath))
                {
                    deniedCalendarCreate.Headers.Add("Idempotency-Key", Fixture.Key());
                    deniedCalendarCreate.Content = new StringContent(JsonSerializer.Serialize(
                        new CreateFiscalYearRequest("2030-FORGED", new(2030, 1, 1), new(2030, 12, 31)),
                        JsonDefaults.Options), Encoding.UTF8, "application/json");
                    using var rejected = await client.SendAsync(deniedCalendarCreate);
                    Ensure(rejected.StatusCode == HttpStatusCode.Forbidden,
                        "SYS03-OP01 unrelated JWT created another company's fiscal year");
                }
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                using (var deniedCalendarResult = await client.GetAsync(calendarPath + "/search?search=2030-FORGED"))
                using (var deniedCalendarJson = JsonDocument.Parse(await deniedCalendarResult.Content.ReadAsStringAsync()))
                    Ensure(deniedCalendarResult.StatusCode == HttpStatusCode.OK &&
                        deniedCalendarJson.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "SYS03-OP01 denied calendar creation persisted an unauthorized year");
                Console.WriteLine("  HOST PASS SYS03-OP01 tenant-scoped fiscal search/create and cross-company JWT denial");
                using (var response = await client.GetAsync(calendarPath + $"/{fiscalYear.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "Calendar HTTP audit failed");
                using (var response = await client.GetAsync(calendarPath + $"/{fiscalYear.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("journalCount").GetInt32() == 0, "Calendar HTTP usage failed");
                // SYS03-OP03: calendar usage and audit remain tenant-scoped and
                // deny a valid JWT without membership in the company.
                var sys03AuditPath = calendarPath + $"/{fiscalYear.ResourceId}/audit-events";
                var sys03UsagePath = calendarPath + $"/{fiscalYear.ResourceId}/usage";
                using (var response = await client.GetAsync(sys03AuditPath + "?offset=0&limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                {
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("totalCount").GetInt32() == 1 &&
                        json.RootElement.GetProperty("items").GetArrayLength() == 1,
                        "SYS03-OP03 calendar audit paging differs");
                    var audit = json.RootElement.GetProperty("items")[0];
                    Ensure(audit.GetProperty("companyId").GetGuid() == company.Id &&
                        audit.GetProperty("resourceId").GetGuid() == fiscalYear.ResourceId &&
                        audit.GetProperty("action").GetString() == "fiscal-year.created" &&
                        audit.GetProperty("actorId").GetString() == "runtime-admin",
                        "SYS03-OP03 audit leaked another tenant or actor");
                }
                using (var response = await client.GetAsync(sys03UsagePath))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        json.RootElement.GetProperty("fiscalYearId").GetGuid() == fiscalYear.ResourceId &&
                        json.RootElement.GetProperty("journalCount").GetInt32() == 0 &&
                        json.RootElement.GetProperty("unpostedJournalCount").GetInt32() == 0 &&
                        json.RootElement.GetProperty("recurringGenerationCount").GetInt32() == 0,
                        "SYS03-OP03 fresh calendar usage differs");
                Authenticate(Token("sql-clerk"));
                using (var response = await client.GetAsync(sys03AuditPath))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden,
                        "SYS03-OP03 unrelated JWT read fiscal audit");
                using (var response = await client.GetAsync(sys03UsagePath))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden,
                        "SYS03-OP03 unrelated JWT read fiscal usage");
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                Console.WriteLine("  HOST PASS SYS03-OP03 usage and audit tenant isolation over HTTPS/JWT/SQL");
                Console.WriteLine("  HOST PASS SYS03 explicit periods, calendar state, search, usage and audit over HTTPS/JWT/SQL");

                // SYS03-GAP04: closing a fiscal year is a GL12 period operation, never a delete
                // or an untracked activation toggle on the SYS03 master card.
                var fiscalDetailPath = calendarPath + $"/{fiscalYear.ResourceId}";
                using (var before = await client.GetAsync(fiscalDetailPath))
                using (var auditBefore = await client.GetAsync(fiscalDetailPath + "/audit-events"))
                {
                    Ensure(before.StatusCode == HttpStatusCode.OK && auditBefore.StatusCode == HttpStatusCode.OK,
                        "SYS03-GAP04 baseline calendar or audit not accessible");
                    var beforeBody = await before.Content.ReadAsStringAsync();
                    var auditBeforeBody = await auditBefore.Content.ReadAsStringAsync();

                    using var deniedDelete = await client.DeleteAsync(fiscalDetailPath);
                    Ensure(deniedDelete.StatusCode == HttpStatusCode.MethodNotAllowed,
                        "SYS03-GAP04 fiscal year could be deleted instead of closed");
                    using var disable = new HttpRequestMessage(HttpMethod.Post, fiscalDetailPath + "/status");
                    disable.Headers.Add("Idempotency-Key", Fixture.Key());
                    disable.Content = new StringContent("{\"isActive\":false}", Encoding.UTF8, "application/json");
                    using var deniedDisable = await client.SendAsync(disable);
                    Ensure(deniedDisable.StatusCode == HttpStatusCode.NotFound,
                        "SYS03-GAP04 fiscal year accepted an unsupported disable endpoint");

                    using var after = await client.GetAsync(fiscalDetailPath);
                    using var auditAfter = await client.GetAsync(fiscalDetailPath + "/audit-events");
                    Ensure(after.StatusCode == HttpStatusCode.OK && auditAfter.StatusCode == HttpStatusCode.OK &&
                        await after.Content.ReadAsStringAsync() == beforeBody &&
                        await auditAfter.Content.ReadAsStringAsync() == auditBeforeBody,
                        "SYS03-GAP04 rejected operations changed fiscal periods, state, or audit");
                }
                using (var openApi = JsonDocument.Parse(specJson))
                {
                    var routes = openApi.RootElement.GetProperty("paths");
                    Ensure(!routes.GetProperty("/api/v1/companies/{companyId}/fiscal-years/{id}").TryGetProperty("delete", out _) &&
                        !routes.TryGetProperty("/api/v1/companies/{companyId}/fiscal-years/{id}/status", out _),
                        "SYS03-GAP04 unsupported lifecycle endpoints appeared in OpenAPI");
                }
                Console.WriteLine("  HOST PASS SYS03-GAP04 no delete/disable bypass; unchanged calendar and audit over HTTPS/JWT/SQL");


                using var preflight = new HttpRequestMessage(HttpMethod.Options, path);
                preflight.Headers.Add("Origin", "https://permitted.example");
                preflight.Headers.Add("Access-Control-Request-Method", "POST");
                preflight.Headers.Add("Access-Control-Request-Headers", "authorization,idempotency-key,content-type");
                using var cors = await client.SendAsync(preflight);
                Ensure(cors.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed) && allowed.Single() == "https://permitted.example", "Allowed CORS origin failed");
                using var disallowed = new HttpRequestMessage(HttpMethod.Options, path);
                disallowed.Headers.Add("Origin", "https://untrusted.example"); disallowed.Headers.Add("Access-Control-Request-Method", "POST");
                using var blocked = await client.SendAsync(disallowed);
                Ensure(!blocked.Headers.Contains("Access-Control-Allow-Origin"), "Unlisted CORS origin accepted");
                Console.WriteLine("  HOST PASS HTTPS, OIDC discovery/JWKS, JWT validation, roles, strict JSON, SQL posting, OpenAPI and CORS");
                // GL02-GP01: exercise the production HTTPS + JWT route using an existing dated NZD quote.
                Authenticate(Token("sql-clerk"));
                var fxOpeningLines = journalRequest.Lines.Select(x => x with {
                    CurrencyCode = "NZD", ExchangeRateId = rate.ResourceId }).ToArray();
                var fxOpeningRequest = GeneralOpeningTests.Request(journalRequest) with {
                    CurrencyCode = "NZD", ExchangeRate = 1.25m, PreviousTrialBalanceReference = "TB-NZD-HTTP",
                    Lines = fxOpeningLines };
                var fxOpeningPath = $"/api/v1/companies/{companyId}/general-openings";
                var fxOpening = await Command(client, fxOpeningPath, fxOpeningRequest);
                using (var detail = await client.GetAsync(fxOpeningPath + $"/{fxOpening.ResourceId}"))
                using (var snapshot = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                    Ensure(detail.StatusCode == HttpStatusCode.OK &&
                        snapshot.RootElement.GetProperty("openingCurrencyCode").GetString() == "NZD" &&
                        snapshot.RootElement.GetProperty("openingExchangeRate").GetString() == "1.25" &&
                        snapshot.RootElement.GetProperty("lines").EnumerateArray().All(x =>
                            x.GetProperty("exchangeRateId").GetGuid() == rate.ResourceId &&
                            x.GetProperty("exchangeRate").GetString() == "1.25"),
                        "GL02 foreign opening HTTPS/SQL snapshot differs");
                fxOpening = await Command(client, path + $"/{fxOpening.ResourceId}/submit",
                    new TransitionRequest(fxOpening.Version));
                Authenticate(Token("sql-approver"));
                fxOpening = await Command(client, path + $"/{fxOpening.ResourceId}/approve",
                    new TransitionRequest(fxOpening.Version));
                fxOpening = await Command(client, path + $"/{fxOpening.ResourceId}/post",
                    new TransitionRequest(fxOpening.Version));
                Ensure(fxOpening.Status == "Posted", "GL02 foreign opening production post failed");
                Authenticate(Token("sql-clerk"));
                var fxReverse = await Command(client, path + $"/{fxOpening.ResourceId}/reverse",
                    new ReverseJournalRequest(fxOpening.Version, Fixture.Date, "عكس افتتاح أجنبي HTTP"));
                using (var inverse = await client.GetAsync(path + $"/{fxReverse.ResourceId}"))
                using (var history = JsonDocument.Parse(await inverse.Content.ReadAsStringAsync()))
                    Ensure(inverse.StatusCode == HttpStatusCode.OK &&
                        history.RootElement.GetProperty("lines").EnumerateArray().All(x =>
                            x.GetProperty("exchangeRateId").GetGuid() == rate.ResourceId &&
                            x.GetProperty("exchangeRate").GetString() == "1.25"),
                        "GL02 reversal unexpectedly repriced posted opening");
                Console.WriteLine("  HOST PASS GL02-GP01 dated FX opening and historical inverse over HTTPS/JWT/SQL");
                Authenticate(Token("sql-owner"));
                var numBranch=await Command(client,$"/api/v1/companies/{companyId}/branches",new CreateBranchRequest("NUM-HTTP","فرع الترقيم","مدينة","عنوان"));
                var numPath=$"/api/v1/companies/{companyId}/number-series";
                var numRequest=new CreateNumberSeriesRequest(NumberingDocumentKind.Journal,yearId,40,"HTTPNUM-",numBranch.ResourceId);
                var numKey=Fixture.Key();var numCard=await Command(client,numPath,numRequest,numKey);
                Ensure(numCard==await Command(client,numPath,numRequest,numKey),"HTTP numbering create replay differs");
                var numEdit=new EditNumberSeriesRequest(numCard.Version,42,"HTTPNUM-");
                numCard=await Command(client,numPath+$"/{numCard.ResourceId}",numEdit,method:HttpMethod.Put);
                using(var detail=await client.GetAsync(numPath+$"/{numCard.ResourceId}"))
                using(var json=JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                    Ensure(detail.StatusCode==HttpStatusCode.OK && json.RootElement.GetProperty("nextNumber").GetInt64()==42 &&
                        json.RootElement.GetProperty("revision").GetInt32()==2 && json.RootElement.GetProperty("createdBy").GetString()=="sql-owner",
                        "SYS10 HTTP configuration fields differ");
                var reserved=await Command(client,numPath+$"/{numCard.ResourceId}/reserve",new ReserveDocumentNumberRequest(numCard.Version,numBranch.ResourceId));
                Ensure(reserved.Number=="HTTPNUM-000042","SYS10 HTTP reserve failed");
                using(var detail=await client.GetAsync(numPath+$"/{numCard.ResourceId}"))
                using(var json=JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                    numCard=numCard with {Version=json.RootElement.GetProperty("version").GetGuid()};
                numCard=await Command(client,numPath+$"/{numCard.ResourceId}/status",new SetNumberSeriesStatusRequest(numCard.Version,false,"تعطيل مفحوص"));
                using(var usage=await client.GetAsync(numPath+$"/{numCard.ResourceId}/usage"))
                using(var json=JsonDocument.Parse(await usage.Content.ReadAsStringAsync()))
                    Ensure(usage.StatusCode==HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32()==1,"SYS10 HTTP immutable usage missing");
                using(var changes=await client.GetAsync(numPath+$"/{numCard.ResourceId}/audit-events"))
                using(var json=JsonDocument.Parse(await changes.Content.ReadAsStringAsync()))
                    Ensure(changes.StatusCode==HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32()==4,"SYS10 HTTP audit chain differs");
                using(var search=await client.GetAsync(numPath+"?search=HTTPNUM-&isActive=false"))
                using(var json=JsonDocument.Parse(await search.Content.ReadAsStringAsync()))
                    Ensure(search.StatusCode==HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32()==1,"SYS10 HTTP search/status filter differs");
                // SYS10-03: the same production JWT can read its branch series but not audit or allocate it.
                await Command(client,$"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest("sys10-http-scoped-reader",new[]{Permissions.Read},
                        false,new[]{numBranch.ResourceId},null));
                var foreignBranch=await Command(client,$"/api/v1/companies/{companyId}/branches",
                    new CreateBranchRequest("SYS10-HTTP-OTHER","فرع آخر","مدينة","عنوان"));
                var foreignSeries=await Command(client,numPath,
                    new CreateNumberSeriesRequest(NumberingDocumentKind.Journal,yearId,400,"SYS10-FOREIGN-",foreignBranch.ResourceId));
                Authenticate(Token("sys10-http-scoped-reader"));
                using(var ownResponse=await client.GetAsync(numPath+$"/{numCard.ResourceId}"))
                    Ensure(ownResponse.StatusCode==HttpStatusCode.OK,
                        "SYS10 scoped JWT could not read its own branch series");
                using(var otherResponse=await client.GetAsync(numPath+$"/{foreignSeries.ResourceId}"))
                    Ensure(otherResponse.StatusCode==HttpStatusCode.Forbidden,
                        "SYS10 scoped JWT read series from an unauthorized branch");
                using(var hidden=await client.GetAsync(numPath+"?limit=100"))
                using(var json=JsonDocument.Parse(await hidden.Content.ReadAsStringAsync()))
                    Ensure(hidden.StatusCode==HttpStatusCode.OK &&
                        json.RootElement.GetProperty("items").EnumerateArray().All(x=>
                            x.GetProperty("branchId").GetGuid()==numBranch.ResourceId),
                        "SYS10 search leaked other branch number-series cards");
                using(var auditDenied=await client.GetAsync(numPath+$"/{numCard.ResourceId}/audit-events"))
                    Ensure(auditDenied.StatusCode==HttpStatusCode.Forbidden,
                        "SYS10 scoped JWT read audit without Audit permission");
                using(var reserveRequest=new HttpRequestMessage(HttpMethod.Post,numPath+$"/{numCard.ResourceId}/reserve"))
                {
                    reserveRequest.Headers.Add("Idempotency-Key",Fixture.Key());
                    reserveRequest.Content=new StringContent(JsonSerializer.Serialize(new ReserveDocumentNumberRequest(
                        numCard.Version,numBranch.ResourceId),JsonDefaults.Options),Encoding.UTF8,"application/json");
                    using var reserveDenied=await client.SendAsync(reserveRequest);
                    Ensure(reserveDenied.StatusCode==HttpStatusCode.Forbidden,
                        "SYS10 read-only scoped JWT allocated a document number");
                }
                Authenticate(Token("sql-owner"));
                using(var finalResponse=await client.GetAsync(numPath+$"/{numCard.ResourceId}"))
                using(var json=JsonDocument.Parse(await finalResponse.Content.ReadAsStringAsync()))
                    Ensure(finalResponse.StatusCode==HttpStatusCode.OK &&
                        json.RootElement.GetProperty("nextNumber").GetInt64()==43,
                        "SYS10 denied HTTP allocation modified series counter");
                Console.WriteLine("  HOST PASS SYS10-03 branch isolation and read/audit/configure JWT separation over HTTPS/SQL");
                Console.WriteLine("  HOST PASS SYS10 create/edit/reserve/status/search/usage/audit with exact field types over HTTPS/JWT/SQL");
                var mapPath=$"/api/v1/companies/{companyId}/financial-mappings";
                var mapLines=new FinancialMappingLineRequest[]{new("ROOT","عنوان",FinancialDisplaySign.Normal),new("CASH","نقدية",FinancialDisplaySign.Normal,journalRequest.Lines[0].AccountId,"ROOT"),new("TOTAL","المجموع",FinancialDisplaySign.Normal,null,"ROOT","[CASH]")};
                var mapRequest=new CreateFinancialMappingRequest("HTTP-MAP",FinancialStatementKind.FinancialPosition,"V1",new(2026,1,1),mapLines);
                var mapKey=Fixture.Key();var mapCard=await Command(client,mapPath,mapRequest,mapKey);
                Ensure(mapCard==await Command(client,mapPath,mapRequest,mapKey),"GL13 HTTP create replay differs");
                using(var detail=await client.GetAsync(mapPath+$"/{mapCard.ResourceId}"))
                using(var json=JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
                    Ensure(detail.StatusCode==HttpStatusCode.OK && json.RootElement.GetProperty("branchId").ValueKind==JsonValueKind.Null && json.RootElement.GetProperty("revision").GetInt32()==1 && json.RootElement.GetProperty("createdBy").GetString()=="sql-owner" && json.RootElement.GetProperty("statementKind").GetString()=="FinancialPosition","GL13 HTTP source/audit fields differ");
                using(var forged=new HttpRequestMessage(HttpMethod.Post,mapPath))
                {
                    forged.Headers.Add("Idempotency-Key",Fixture.Key());var json=JsonSerializer.Serialize(mapRequest,JsonDefaults.Options);
                    forged.Content=new StringContent(json[..^1]+",\"revision\":99}",Encoding.UTF8,"application/json");
                    using var rejected=await client.SendAsync(forged);Ensure(rejected.StatusCode==HttpStatusCode.BadRequest,"GL13 HTTP audit spoof accepted");
                }
                var mapReview=await Command(client,mapPath+$"/{mapCard.ResourceId}/review",new ReviewFinancialMappingRequest(mapCard.Version,Fixture.Date));
                using(var review=await client.GetAsync(mapPath+$"/reviews/{mapReview.ResourceId}"))
                using(var json=JsonDocument.Parse(await review.Content.ReadAsStringAsync()))
                    Ensure(review.StatusCode==HttpStatusCode.OK && json.RootElement.GetProperty("mappingVersion").GetGuid()==mapCard.Version && json.RootElement.GetProperty("evaluation").GetProperty("issues").GetArrayLength()>0,"GL13 HTTP unclassified review issue/snapshot missing");
                mapCard=await Command(client,mapPath+$"/{mapCard.ResourceId}",new EditFinancialMappingRequest(mapCard.Version,"HTTP-MAP-EDIT",FinancialStatementKind.FinancialPosition,"V2",new(2026,1,1),mapLines.Reverse().ToArray()),method:HttpMethod.Put);
                mapCard=await Command(client,mapPath+$"/{mapCard.ResourceId}/status",new SetFinancialMappingStatusRequest(mapCard.Version,false,"تعطيل مفحوص"));
                using(var usage=await client.GetAsync(mapPath+$"/{mapCard.ResourceId}/usage"))
                using(var json=JsonDocument.Parse(await usage.Content.ReadAsStringAsync()))
                    Ensure(usage.StatusCode==HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32()==1,"GL13 HTTP snapshot usage missing");
                using(var audit=await client.GetAsync(mapPath+$"/{mapCard.ResourceId}/audit-events"))
                using(var json=JsonDocument.Parse(await audit.Content.ReadAsStringAsync()))
                    Ensure(audit.StatusCode==HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32()==4,"GL13 HTTP audit chain differs");
                using(var search=await client.GetAsync(mapPath+"?search=HTTP-MAP-EDIT&isActive=false&statementKind=FinancialPosition"))
                using(var json=JsonDocument.Parse(await search.Content.ReadAsStringAsync()))
                    Ensure(search.StatusCode==HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32()==1,"GL13 HTTP search filters differ");
                await Command(client,$"/api/v1/companies/{companyId}/memberships",new GrantMembershipRequest("http-map-reader",new[]{Permissions.Read},true,Array.Empty<Guid>(),null));
                Authenticate(Token("http-map-reader"));
                using(var denyRequest=new HttpRequestMessage(HttpMethod.Post,mapPath))
                {
                    denyRequest.Headers.Add("Idempotency-Key",Fixture.Key());denyRequest.Content=new StringContent(JsonSerializer.Serialize(mapRequest,JsonDefaults.Options),Encoding.UTF8,"application/json");
                    using var mapDenied=await client.SendAsync(denyRequest);Ensure(mapDenied.StatusCode==HttpStatusCode.Forbidden,"GL13 HTTP unauthorized configuration accepted");
                }
                Console.WriteLine("  HOST PASS GL13 create/edit/status/search/usage/audit/review, server field types, spoof rejection and unclassified errors over HTTPS/JWT/SQL");

                // SYS02-GAP02: a branch default cost center is master-data configuration,
                // never a silent value inserted into journal lines.
                Authenticate(Token("sql-owner"));
                var sysDefaultBranchPath = $"/api/v1/companies/{companyId}/branches";
                var sysDefaultBranch = await Command(client, sysDefaultBranchPath,
                    new CreateBranchRequest("SYS02-DEFAULT-HTTP", "فرع افتراضي", "مدينة", "عنوان"));
                var sysDefaultCenter = await Command(client, $"/api/v1/companies/{companyId}/cost-centers",
                    new CreateCostCenterRequest("SYS02-DEFAULT-CENTER", "مركز افتراضي", sysDefaultBranch.ResourceId));
                sysDefaultBranch = await Command(client, sysDefaultBranchPath + $"/{sysDefaultBranch.ResourceId}",
                    new EditBranchRequest(sysDefaultBranch.Version, "SYS02-DEFAULT-HTTP", "فرع افتراضي",
                        "مدينة", "عنوان", null, sysDefaultCenter.ResourceId, null, null, "تعيين مركز افتراضي"),
                    method: HttpMethod.Put);
                using (var branchDetail = await client.GetAsync(sysDefaultBranchPath + $"/{sysDefaultBranch.ResourceId}"))
                using (var branchJson = JsonDocument.Parse(await branchDetail.Content.ReadAsStringAsync()))
                    Ensure(branchDetail.StatusCode == HttpStatusCode.OK &&
                        branchJson.RootElement.GetProperty("defaultCostCenterId").GetGuid() == sysDefaultCenter.ResourceId,
                        "SYS02-GAP02 branch default center not persisted in SQL");

                await Command(client, $"/api/v1/companies/{companyId}/fiscal-years",
                    new CreateFiscalYearRequest("SYS02-2036", new(2036, 1, 1), new(2036, 12, 31)));
                var sysDefaultJournal = journalRequest with {
                    BranchId = sysDefaultBranch.ResourceId, DocumentDate = new(2036, 1, 15), AccountingDate = new(2036, 1, 15)
                };
                await Command(client, $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest("sys02-gap02-drafter", new[] { Permissions.Read, Permissions.Draft },
                        false, new[] { sysDefaultBranch.ResourceId }, null));
                Authenticate(Token("sys02-gap02-drafter"));
                var sysOrdinary = await Command(client, path, sysDefaultJournal);
                using (var journalDetail = await client.GetAsync(path + $"/{sysOrdinary.ResourceId}"))
                using (var journalJson = JsonDocument.Parse(await journalDetail.Content.ReadAsStringAsync()))
                    Ensure(journalDetail.StatusCode == HttpStatusCode.OK &&
                        journalJson.RootElement.GetProperty("lines").EnumerateArray().All(x =>
                            x.GetProperty("costCenterId").ValueKind == JsonValueKind.Null),
                        "SYS02-GAP02 default center was silently injected into an unassigned journal line");
                Authenticate(Token("sql-owner"));
                var sysRequiredAccount = await Command(client, $"/api/v1/companies/{companyId}/accounts",
                    new CreateAccountRequest("SYS02-REQUIRES-CC", "حساب يحتاج مركز تكلفة", AccountType.Expense,
                        null, true, false, true, null, NormalBalance.Debit));
                Authenticate(Token("sys02-gap02-drafter"));
                var sysMissingCenter = sysDefaultJournal with { Lines = sysDefaultJournal.Lines
                    .Select((line, index) => index == 0
                        ? line with { AccountId = sysRequiredAccount.ResourceId, CostCenterId = null } : line).ToArray() };
                using (var malformed = new HttpRequestMessage(HttpMethod.Post, path))
                {
                    malformed.Headers.Add("Idempotency-Key", Fixture.Key());
                    malformed.Content = new StringContent(JsonSerializer.Serialize(sysMissingCenter, JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var deniedCenter = await client.SendAsync(malformed);
                    using var error = JsonDocument.Parse(await deniedCenter.Content.ReadAsStringAsync());
                    Ensure((int)deniedCenter.StatusCode == 422 &&
                        error.RootElement.GetProperty("code").GetString() == "cost_center_required",
                        "SYS02-GAP02 required center was improperly substituted from branch default");
                }
                var sysExplicit = await Command(client, path,
                    sysMissingCenter with { Lines = sysMissingCenter.Lines.Select((line, index) =>
                        index == 0 ? line with { CostCenterId = sysDefaultCenter.ResourceId } : line).ToArray() });
                using (var journalDetail = await client.GetAsync(path + $"/{sysExplicit.ResourceId}"))
                using (var journalJson = JsonDocument.Parse(await journalDetail.Content.ReadAsStringAsync()))
                    Ensure(journalDetail.StatusCode == HttpStatusCode.OK &&
                        journalJson.RootElement.GetProperty("lines").EnumerateArray().Any(line =>
                            line.GetProperty("accountId").GetGuid() == sysRequiredAccount.ResourceId &&
                            line.GetProperty("costCenterId").ValueKind == JsonValueKind.String &&
                            line.GetProperty("costCenterId").GetGuid() == sysDefaultCenter.ResourceId),
                        "SYS02-GAP02 explicit journal cost center did not persist through SQL");
                Console.WriteLine("  HOST PASS SYS02-GAP02 branch default metadata does not inject journal cost center; required and explicit cases over HTTPS/JWT/SQL");

                // GL03-F10/F11/F12: keep the independent journal-header regression at the end
                // so it cannot affect accounting totals asserted by the other host scenarios.
                Authenticate(Token("sql-clerk"));
                await JournalHeaderFieldsHttpIntegration.Run(client, path, journalRequest);

                // GL03-T02: only the authorized subsidiary source may post to control accounts.
                Authenticate(Token("sql-owner"));
                var gl03Control = await Command(client, $"/api/v1/companies/{companyId}/accounts",
                    new CreateAccountRequest("GL03-HOST-CONTROL", "رقابة GL03", AccountType.Asset,
                        null, true, true, false, null, NormalBalance.Debit));
                Authenticate(Token("sql-clerk"));
                await JournalLineGuardHttpIntegration.Run(client, path, journalRequest,
                    gl03Control.ResourceId);

                // CASH01: production HTTPS/OIDC/JWT/SQL acceptance, including non-member
                // rejection and concurrency/replay, on a fresh dedicated posting account.
                Authenticate(Token("sql-owner"));
                var cashHostAccount = await Command(client, $"/api/v1/companies/{companyId}/accounts",
                    new CreateAccountRequest("CASH01-HOST", "حساب الصندوق التجريبي",
                        AccountType.Asset, null, true, false, false, null, NormalBalance.Debit));
                var cashHostCustodian = await Command(client, $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest("cash01-host-custodian", new[] { Permissions.Read },
                        false, new[] { journalRequest.BranchId }, null));
                var cashPath = $"/api/v1/companies/{companyId}/cash-boxes";
                var cashBody = new CreateCashBoxRequest("CASH01-HOST", "صندوق اختباري",
                    cashHostAccount.ResourceId, journalRequest.BranchId,
                    cashHostCustodian.ResourceId, "SAR", 100m, "Host Cash Box", "قبول");
                var cashHostKey = Fixture.Key();
                var cashHostReceipt = await Command(client, cashPath, cashBody, cashHostKey);
                Ensure(cashHostReceipt == await Command(client, cashPath, cashBody, cashHostKey),
                    "CASH01 production HTTPS replay returned a different receipt");
                using (var cashDetail = await client.GetAsync(cashPath + $"/{cashHostReceipt.ResourceId}"))
                using (var cashJson = JsonDocument.Parse(await cashDetail.Content.ReadAsStringAsync()))
                    Ensure(cashDetail.StatusCode == HttpStatusCode.OK &&
                        cashJson.RootElement.GetProperty("createdBy").GetString() == "sql-owner" &&
                        cashJson.RootElement.GetProperty("revision").GetInt32() == 1 &&
                        cashJson.RootElement.GetProperty("custodianMembershipId").GetGuid() ==
                            cashHostCustodian.ResourceId,
                        "CASH01 HTTPS/JWT card identity, revision or custodian mismatch");

                Authenticate(Token("cash01-nonmember"));
                using (var deniedCash = await client.GetAsync(cashPath + $"/{cashHostReceipt.ResourceId}"))
                    Ensure(deniedCash.StatusCode == HttpStatusCode.Forbidden,
                        "CASH01 non-member JWT read a tenant card");
                Authenticate(Token("sql-owner"));

                using (var duplicateCash = new HttpRequestMessage(HttpMethod.Post, cashPath))
                {
                    duplicateCash.Headers.Add("Idempotency-Key", Fixture.Key());
                    duplicateCash.Content = new StringContent(JsonSerializer.Serialize(
                        cashBody with { Code = "CASH01-HOST-SECOND" }, JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var deniedDuplicate = await client.SendAsync(duplicateCash);
                    using var error = JsonDocument.Parse(await deniedDuplicate.Content.ReadAsStringAsync());
                    Ensure(deniedDuplicate.StatusCode == HttpStatusCode.Conflict &&
                        error.RootElement.GetProperty("code").GetString() == "cashbox_account_already_linked",
                        "CASH01 duplicate posting-account dimension accepted on production HTTPS");
                }

                var cashEdit = new EditCashBoxRequest(cashHostReceipt.Version, "CASH01-UPDATED",
                    "صندوق معدل", cashBody.AccountId, cashBody.BranchId,
                    cashBody.CustodianMembershipId, "SAR", 10m, "Updated", "audit", "تحديث");
                var cashEdited = await Command(client, cashPath + $"/{cashHostReceipt.ResourceId}",
                    cashEdit, method: HttpMethod.Put);
                Ensure(cashEdited.Version != cashHostReceipt.Version,
                    "CASH01 production optimistic concurrency did not advance");
                using (var staleCash = new HttpRequestMessage(HttpMethod.Put,
                    cashPath + $"/{cashHostReceipt.ResourceId}"))
                {
                    staleCash.Headers.Add("Idempotency-Key", Fixture.Key());
                    staleCash.Content = new StringContent(JsonSerializer.Serialize(cashEdit, JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var deniedStale = await client.SendAsync(staleCash);
                    Ensure(deniedStale.StatusCode == HttpStatusCode.Conflict,
                        "CASH01 stale edit was not rejected by production HTTPS");
                }
                using (var cashAudit = await client.GetAsync(cashPath +
                    $"/{cashHostReceipt.ResourceId}/audit-events"))
                using (var cashAuditJson = JsonDocument.Parse(await cashAudit.Content.ReadAsStringAsync()))
                    Ensure(cashAudit.StatusCode == HttpStatusCode.OK &&
                        cashAuditJson.RootElement.GetProperty("totalCount").GetInt32() == 2,
                        "CASH01 production audit has duplicate or missing create/edit events");
                Console.WriteLine("  HOST PASS CASH01 SQL-backed HTTPS/OIDC/JWT create/read/edit/replay, account uniqueness, stale edit and audit");

                // GL04-01: start a REAL opted-in recurring draft worker
                // against this disposable SQL integration database, with
                // a dedicated member limited to one new branch. This is
                // never an activation of the VPS/production environment.
                Authenticate(Token("sql-owner"));
                var workerBranch = await Command(client, $"/api/v1/companies/{companyId}/branches",
                    new CreateBranchRequest("GL04-WORKER-CI", "فرع عامل الدوريات", "مدينة", "عنوان"));
                const string workerSubject = "gl04-recurring-worker-ci";
                await Command(client, $"/api/v1/companies/{companyId}/memberships",
                    new GrantMembershipRequest(workerSubject,
                        new[] { Permissions.RecurringGenerate, Permissions.Draft },
                        false, new[] { workerBranch.ResourceId }, null));
                Authenticate(Token("sql-clerk"));
                var workerTemplate = await Command(client, recurringPath, recurringRequest with
                {
                    BranchId = workerBranch.ResourceId,
                    Name = "GL04 worker isolated monthly occurrence",
                    GenerationMode = RecurringGenerationMode.Scheduled,
                    StartsOn = Fixture.Date,
                    EndsOn = Fixture.Date
                });
                using (var beforeHistory = await client.GetAsync(recurringPath + $"/{workerTemplate.ResourceId}/generations"))
                using (var beforeJson = JsonDocument.Parse(await beforeHistory.Content.ReadAsStringAsync()))
                    Ensure(beforeHistory.StatusCode == HttpStatusCode.OK &&
                        beforeJson.RootElement.GetProperty("totalCount").GetInt32() == 0,
                        "GL04-01 worker test template already has generated SQL occurrences");

                await using (var workerApi = ApiHost.Build(Array.Empty<string>(), b =>
                {
                    b.Logging.ClearProviders();
                    b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Erp"] = connection,
                        ["Authentication:Authority"] = issuer,
                        ["Authentication:Audience"] = audience,
                        ["RecurringScheduler:Enabled"] = "true",
                        ["RecurringScheduler:ActorSubject"] = workerSubject,
                        ["RecurringScheduler:IntervalMinutes"] = "1440"
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
                        RecurringJournalGeneration? generatedOccurrence = null;
                        JournalEntry? generatedDraft = null;
                        for (var attempt = 0; attempt < 100; attempt++)
                        {
                            await using var testScope = workerApi.Services.CreateAsyncScope();
                            var db = testScope.ServiceProvider.GetRequiredService<ErpDbContext>();
                            generatedOccurrence = await db.Set<RecurringJournalGeneration>().AsNoTracking()
                                .FirstOrDefaultAsync(x => x.CompanyId == companyId &&
                                    x.TemplateId == workerTemplate.ResourceId);
                            if (generatedOccurrence is not null)
                            {
                                generatedDraft = await db.Set<JournalEntry>().AsNoTracking()
                                    .SingleAsync(x => x.CompanyId == companyId &&
                                        x.Id == generatedOccurrence.JournalId);
                                break;
                            }
                            await Task.Delay(100);
                        }
                        Ensure(generatedOccurrence is not null && generatedDraft is not null &&
                            generatedDraft.State == JournalState.Draft &&
                            generatedDraft.Source == JournalSource.Recurring &&
                            generatedDraft.RecurringTemplateId == workerTemplate.ResourceId &&
                            generatedDraft.BranchId == workerBranch.ResourceId &&
                            generatedDraft.Lines.Sum(x => x.FunctionalDebit) ==
                                recurringRequest.Lines.Sum(x => x.Debit) &&
                            generatedDraft.Lines.Sum(x => x.FunctionalCredit) ==
                                recurringRequest.Lines.Sum(x => x.Credit),
                            "GL04-01 started opt-in recurring worker did not persist one unposted historical draft");
                    }
                    finally { await workerApi.StopAsync(); }
                }

                Authenticate(Token(workerSubject));
                using (var due = new HttpRequestMessage(HttpMethod.Post, recurringPath + "/run-due"))
                {
                    due.Content = new StringContent(JsonSerializer.Serialize(
                        new RunDueRecurringRequest(Fixture.Date, 10), JsonDefaults.Options),
                        Encoding.UTF8, "application/json");
                    using var response = await client.SendAsync(due);
                    using var workerDueJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Ensure(response.StatusCode == HttpStatusCode.OK &&
                        workerDueJson.RootElement.GetProperty("selectedCount").GetInt32() == 0 &&
                        workerDueJson.RootElement.GetProperty("generatedCount").GetInt32() == 0,
                        "GL04-01 restricted actor's re-scan duplicated a scheduler-created journal");
                }
                Authenticate(Token("sql-clerk"));
                using (var afterHistory = await client.GetAsync(recurringPath + $"/{workerTemplate.ResourceId}/generations"))
                using (var afterJson = JsonDocument.Parse(await afterHistory.Content.ReadAsStringAsync()))
                    Ensure(afterHistory.StatusCode == HttpStatusCode.OK &&
                        afterJson.RootElement.GetProperty("totalCount").GetInt32() == 1,
                        "GL04-01 real opted-in worker did not retain exactly one durable SQL occurrence");
                Console.WriteLine("  HOST PASS GL04-01 started opt-in recurring worker branch-scoped: one Draft, no auto-post or replay, isolated SQL");

            }
            finally { await api.StopAsync(); }
        }
        finally { await issuerApp.StopAsync(); }
    }

    private static HttpClientHandler PinnedTransport(X509Certificate2 expected) => new()
    {
        ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            certificate is not null && certificate.GetCertHashString(HashAlgorithmName.SHA256) == expected.GetCertHashString(HashAlgorithmName.SHA256)
    };
    private static async Task<CommandReceipt> Command(HttpClient client, string path, object body, string? key = null, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", key ?? Fixture.Key());
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonDefaults.Options), Encoding.UTF8, "application/json");
        using var result = await client.SendAsync(request);
        var json = await result.Content.ReadAsStringAsync();
        Ensure(result.StatusCode == HttpStatusCode.OK, "Production HTTP command failed: " + json);
        return JsonSerializer.Deserialize<CommandReceipt>(json, JsonDefaults.Options)!;
    }
    private static async Task<(int Journals, int Audits, int Receipts, int Outbox)> ReadOnlyCounts(string connection, Guid cid)
    {
        await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        return (
            await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid),
            await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid),
            await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid),
            await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid));
    }

    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
