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
            await using var api = ApiHost.Build(Array.Empty<string>(), b =>
            {
                b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Erp"] = connection, ["Authentication:Authority"] = issuer,
                    ["Authentication:Audience"] = audience, ["AllowedHosts"] = "127.0.0.1;localhost"
                });
                b.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
                b.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                    o => o.BackchannelHttpHandler = PinnedTransport(certificate));
            });
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
                var replay = await Due(client, path, journalRequest.AccountingDate);
                Ensure(replay.SelectedCount == 0 && replay.GeneratedCount == 0,
                    "Production due replay duplicated a reserved reversal");
                await Command(client, path + $"/{reversalId}/cancel", new TransitionRequest(reversalVersion, "إغلاق اختبار العكس المجدول"));
                var afterCancellation = await Due(client, path, journalRequest.AccountingDate);
                Ensure(afterCancellation.SelectedCount == 0 && afterCancellation.GeneratedCount == 0,
                    "Cancelled production reversal allowed scheduled regeneration");
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
