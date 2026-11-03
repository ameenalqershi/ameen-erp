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
                var replay = await Command(client, path, journalRequest, requestKey);
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
                        json.RootElement.GetProperty("lastModifiedAt").GetDateTimeOffset() >= json.RootElement.GetProperty("createdAt").GetDateTimeOffset() &&
                        !json.RootElement.GetProperty("allSucceeded").GetBoolean() &&
                        json.RootElement.GetProperty("rows").GetArrayLength() == 2,
                        "GL06 durable processing batch not retrievable over HTTPS/JWT/SQL");
                using (var response = await client.GetAsync(path + $"/{receipt.ResourceId}/processing-history"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP processing history duplicated");
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

                Authenticate(Token("sql-clerk"));
                var generalOpening = await Command(client, $"/api/v1/companies/{companyId}/general-openings", GeneralOpeningTests.Request(journalRequest));
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
                using (var response = await client.GetAsync(openingsPath + $"/{opening.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("state").GetString() == "Posted" && json.RootElement.GetProperty("journalId").GetGuid() != Guid.Empty, "HTTP posted source missing journal");
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/stock-movements?warehouseId={openingWarehouse.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("totalCount").GetInt32() == 1, "HTTP stock movement missing");
                Authenticate(Token("sql-owner"));
                Console.WriteLine("  HOST PASS INV06 configure/preview/post/linked journal/stock movement over HTTPS/JWT/SQL");
                Authenticate(Token("sql-clerk"));
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
                using (var response = await client.GetAsync(auditPath + "?from=2026-12-31&to=2026-01-01"))
                    Ensure(response.StatusCode == HttpStatusCode.UnprocessableEntity, "HTTP reversed audit dates accepted");
                using (var response = await client.GetAsync(auditPath + "?from=bad-date"))
                    Ensure(response.StatusCode == HttpStatusCode.UnprocessableEntity, "HTTP malformed audit date accepted");
                Authenticate(Token("audit-no-membership"));
                using (var response = await client.GetAsync(auditPath + $"/{eventId}"))
                    Ensure(response.StatusCode == HttpStatusCode.Forbidden, "HTTP audit detail exposed without permission");
                Authenticate(Token("sql-owner"));
                Console.WriteLine("  HOST PASS SYS14 audit filters, event detail, invalid dates and authorization over HTTPS/JWT/SQL");
                var ratesPath = $"/api/v1/companies/{companyId}/exchange-rates";
                await Command(client, $"/api/v1/companies/{companyId}/currencies", new CreateCurrencyRequest("NZD", 2, "دولار نيوزيلندي"));
                var rate = await Command(client, ratesPath, new CreateExchangeRateRequest("NZD", Fixture.Date, 1.25m, "HTTP source"));
                await Command(client, ratesPath, new CreateExchangeRateRequest("NZD", Fixture.Date.AddDays(1), 1.5m, "HTTP future"));
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
                Authenticate(Token("sql-approver"));
                using (var response = await client.GetAsync($"/api/v1/companies/{companyId}/periods/{periodId}/close-preview?limit=1"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && !json.RootElement.GetProperty("canCloseGeneralLedger").GetBoolean() &&
                        json.RootElement.GetProperty("generalLedgerOnly").GetBoolean() && json.RootElement.GetProperty("issues").GetProperty("items").GetArrayLength() == 1,
                        "SQL/HTTP close preview did not expose pending blockers");
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
                var companyProfile = company.Profile with { CommercialRegistration = "00042", City = "مدينة معدلة", PostalCode = "00123" };
                companyReceipt = await Command(client, companyPath, new EditCompanyRequest(companyReceipt.Version, company.Code, company.Name,
                    company.CountryCode, company.FunctionalCurrency, companyProfile, "استكمال البيانات"), method: HttpMethod.Put);
                using (var companyDetail = await client.GetAsync(companyPath))
                using (var json = JsonDocument.Parse(await companyDetail.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("commercialRegistration").GetString() == "00042" &&
                        json.RootElement.GetProperty("revision").GetInt32() == 2 && json.RootElement.GetProperty("isProfileComplete").GetBoolean(), "Company HTTP profile differs");
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
                await Command(client, companyPath + "/status", new SetCompanyStatusRequest(companyReceipt.Version, true, "تفعيل"));
                Console.WriteLine("  HOST PASS SYS01 required profile, PUT edit, integer revision, inactive-company write guard and reactivation");
                var branchesPath = companyPath + "/branches";
                var parentBranch = await Command(client, branchesPath, new CreateBranchRequest("PARENT", "أب", "مدينة", "عنوان"));
                var childBranch = await Command(client, branchesPath, new CreateBranchRequest("CHILD", "ابن", "مدينة", "عنوان", parentBranch.ResourceId));
                childBranch = await Command(client, branchesPath + $"/{childBranch.ResourceId}",
                    new EditBranchRequest(childBranch.Version, "CHILD", "ابن معدل", "مدينة", "عنوان 001", parentBranch.ResourceId, null, "HTTP child", null, "تحديث"), method: HttpMethod.Put);
                using (var response = await client.GetAsync(branchesPath + $"/search?parentId={parentBranch.ResourceId}&search=HTTP"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("items")[0].GetProperty("revision").GetInt32() == 2, "Branch HTTP search failed");
                using (var response = await client.GetAsync(branchesPath + $"/{parentBranch.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("childCount").GetInt32() == 1, "Branch HTTP usage failed");
                await Command(client, branchesPath + $"/{childBranch.ResourceId}/status", new SetBranchStatusRequest(childBranch.Version, false, "إيقاف"));
                using (var response = await client.GetAsync(branchesPath + $"/{childBranch.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 3, "Branch HTTP audit failed");
                Console.WriteLine("  HOST PASS SYS02 branch addresses, tree, edit, search, usage, status and audit over HTTPS/JWT/SQL");

                var calendarPath = companyPath + "/fiscal-years";
                var fiscalYear = await Command(client, calendarPath, new CreateFiscalYearRequest("2026", new(2026, 1, 1), new(2026, 12, 31),
                    new[] { new FiscalPeriodRequest("2026-H1", new(2026, 1, 1), new(2026, 6, 30)), new FiscalPeriodRequest("2026-H2", new(2026, 7, 1), new(2026, 12, 31)) }));
                using (var response = await client.GetAsync(calendarPath + $"/{fiscalYear.ResourceId}"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("periods").GetArrayLength() == 2 &&
                        json.RootElement.GetProperty("state").GetString() == "Open" && json.RootElement.GetProperty("generalLedgerOnly").GetBoolean(), "Custom HTTP calendar failed");
                using (var response = await client.GetAsync(calendarPath + "/search?search=2026"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "Calendar HTTP search failed");
                using (var response = await client.GetAsync(calendarPath + $"/{fiscalYear.ResourceId}/audit-events"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("totalCount").GetInt32() == 1, "Calendar HTTP audit failed");
                using (var response = await client.GetAsync(calendarPath + $"/{fiscalYear.ResourceId}/usage"))
                using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    Ensure(json.RootElement.GetProperty("journalCount").GetInt32() == 0, "Calendar HTTP usage failed");
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
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
