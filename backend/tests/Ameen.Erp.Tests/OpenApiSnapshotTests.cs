using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Ameen.Erp.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ameen.Erp.Tests;

public static class OpenApiSnapshotTests
{
    // Uses the real production endpoint map and schema configuration; authentication is test-only.
    // No database or external identity provider is needed to export the contract.
    public static async Task Run()
    {
        await using var app = ApiHost.Build([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Erp"] = "Server=localhost;Database=SchemaOnly;Integrated Security=true;TrustServerCertificate=true",
                ["Authentication:Authority"] = "https://schema-only.invalid", ["Authentication:Audience"] = "schema-only"
            });
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthentication>("SchemaTest", _ => { });
            builder.Services.PostConfigure<AuthenticationOptions>(options =>
            { options.DefaultAuthenticateScheme = "SchemaTest"; options.DefaultChallengeScheme = "SchemaTest"; });
        });
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var anonymous = await client.GetAsync("/openapi/v1.json");
            if (anonymous.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("Schema must require authentication");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "schema-reader");
            using var response = await client.GetAsync("/openapi/v1.json"); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
            foreach (var name in new[] { "EditJournalRequest", "EditGeneralOpeningRequest" })
            {
                var schema = schemas.GetProperty(name);
                if (!schema.GetProperty("properties").TryGetProperty("header", out _) ||
                    schema.GetProperty("required").EnumerateArray().Any(x => x.GetString() == "header"))
                    throw new Exception("Draft header must be optional for legacy clients");
            }
            foreach (var name in new[] { "JournalEntry", "JournalSummary" })
            {
                var properties = schemas.GetProperty(name).GetProperty("properties");
                var revisionType = properties.GetProperty("revision").GetProperty("type");
                var integer = revisionType.ValueKind == JsonValueKind.String ? revisionType.GetString() == "integer" :
                    revisionType.EnumerateArray().Any(x => x.GetString() == "integer");
                if (!integer ||
                    properties.GetProperty("lastModifiedAt").GetProperty("format").GetString() != "date-time" ||
                    !properties.TryGetProperty("automaticReversal", out _) || !properties.TryGetProperty("automaticReversalDate", out _))
                    throw new Exception("GL03 audit/scheduled reversal fields must be exposed with stable types");
            }
            var create = schemas.GetProperty("CreateJournalRequest");
            if (create.GetProperty("required").EnumerateArray().Any(x => x.GetString() is "automaticReversal" or "reversalDate"))
                throw new Exception("Legacy journal create clients must remain compatible");
            var edit = schemas.GetProperty("EditJournalRequest");
            if (edit.GetProperty("required").EnumerateArray().Any(x => x.GetString() is "automaticReversal" or "reversalDate"))
                throw new Exception("Legacy journal edit clients must remain compatible");
            var artifacts = Environment.GetEnvironmentVariable("AMEEN_TEST_ARTIFACTS");
            foreach (var name in new[] { "CreateRecurringJournalRequest", "EditRecurringJournalRequest" })
            {
                var schema = schemas.GetProperty(name);
                var required = schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
                if (required.Contains("endsOn") || !required.Contains("lines") || !required.Contains("startsOn"))
                    throw new Exception("GL04 end date must be optional while start date and lines remain required");
            }
            var runDue = json.RootElement.GetProperty("paths").GetProperty("/api/v1/companies/{companyId}/recurring-journals/run-due").GetProperty("post");
            if (runDue.GetProperty("parameters").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "Idempotency-Key"))
                throw new Exception("Scheduled scan uses per-occurrence command keys and must not require a batch key");
            var impact = json.RootElement.GetProperty("paths").GetProperty("/api/v1/companies/{companyId}/journals/{id}/impact-preview").GetProperty("get");
            if (impact.GetProperty("security").GetArrayLength() == 0 ||
                !schemas.GetProperty("JournalImpactPreview").GetProperty("properties").TryGetProperty("lines", out _) ||
                !schemas.GetProperty("JournalImpactPreview").GetProperty("properties").TryGetProperty("validationErrorCode", out _))
                throw new Exception("GL03-A05 impact preview must have an authorized read-only contract");
            var reversalDue = json.RootElement.GetProperty("paths").GetProperty("/api/v1/companies/{companyId}/journals/reversals/run-due").GetProperty("post");
            if (reversalDue.GetProperty("security").GetArrayLength() == 0 ||
                reversalDue.GetProperty("parameters").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "Idempotency-Key"))
                throw new Exception("Scheduled reversal scan must be authorized without a batch idempotency key");
            if (!string.IsNullOrWhiteSpace(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                await File.WriteAllTextAsync(Path.Combine(artifacts, "openapi-v1.json"),
                    JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            }
        }
        finally { await app.StopAsync(); }
    }
}
