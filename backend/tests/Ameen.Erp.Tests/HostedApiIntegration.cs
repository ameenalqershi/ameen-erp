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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            await using var api = ApiHost.Build(Array.Empty<string>(), b =>
            {
                b.Logging.ClearProviders();
                b.Logging.AddSimpleConsole(); b.Logging.SetMinimumLevel(LogLevel.Error);
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Erp"] = connection, ["Authentication:Authority"] = issuer,
                    ["Authentication:Audience"] = audience, ["AllowedHosts"] = "127.0.0.1;localhost",
                    ["Cors:Origins:0"] = "https://permitted.example"
                });
                b.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
                b.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                    o => o.BackchannelHttpHandler = PinnedTransport(certificate));
            });
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
                }

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
                var receipt = await Command(client, path, journalRequest, requestKey);
                var replay = await Command(client, path, journalRequest, requestKey);
                Ensure(receipt == replay, "Production HTTP/SQL replay differs");
                receipt = await Command(client, path + $"/{receipt.ResourceId}/submit", new TransitionRequest(receipt.Version));
                Authenticate(Token("sql-approver"));
                receipt = await Command(client, path + $"/{receipt.ResourceId}/approve", new TransitionRequest(receipt.Version));
                receipt = await Command(client, path + $"/{receipt.ResourceId}/post", new TransitionRequest(receipt.Version));
                Ensure(receipt.Status == "Posted", "Production HTTP posting failed");
                using var hierarchy = await client.GetAsync($"/api/v1/companies/{companyId}/reports/trial-balance?from=2026-01-01&to=2026-12-31&accountLevel=1&showZero=false");
                Ensure(hierarchy.StatusCode == HttpStatusCode.OK, "Production report filters failed");
                using (var report = JsonDocument.Parse(await hierarchy.Content.ReadAsStringAsync()))
                {
                    Ensure(report.RootElement.GetProperty("accountLevel").GetInt32() == 1 && !report.RootElement.GetProperty("showZero").GetBoolean(), "Report query binding differs");
                }

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
                var company = new CreateCompanyRequest(Guid.NewGuid(), "HTTPTEST", "شركة HTTP", "YE", "YER", 2);
                using var denied = new HttpRequestMessage(HttpMethod.Post, "/api/v1/companies");
                denied.Headers.Add("Idempotency-Key", Fixture.Key()); denied.Headers.Add("X-Role", "erp.platform-admin");
                denied.Content = new StringContent(JsonSerializer.Serialize(company, JsonDefaults.Options), Encoding.UTF8, "application/json");
                using var deniedResult = await client.SendAsync(denied); Ensure(deniedResult.StatusCode == HttpStatusCode.Forbidden, "Header spoofed a role");
                Authenticate(Token("runtime-admin", "erp.platform-admin"));
                await Command(client, "/api/v1/companies", company);

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
    private static async Task<CommandReceipt> Command(HttpClient client, string path, object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", key ?? Fixture.Key());
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonDefaults.Options), Encoding.UTF8, "application/json");
        using var result = await client.SendAsync(request);
        var json = await result.Content.ReadAsStringAsync();
        Ensure(result.StatusCode == HttpStatusCode.OK, "Production HTTP command failed: " + json);
        return JsonSerializer.Deserialize<CommandReceipt>(json, JsonDefaults.Options)!;
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
