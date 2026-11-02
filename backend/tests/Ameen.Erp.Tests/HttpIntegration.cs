using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ameen.Erp.Api;
using Ameen.Erp.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ameen.Erp.Tests;

// Test-only authentication over a loopback test host. Does not validate an OIDC provider or JWT signature.
public sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers["Authorization"].ToString();
        if (!header.StartsWith("Test ", StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
        var subject = header[5..];
        var identity = new ClaimsIdentity(new[] { new Claim("sub", subject) }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

public static class HttpIntegration
{
    public static async Task Run()
    {
        var f = new Fixture(); await f.Initialize();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IErpStore>(f.Store);
        builder.Services.AddSingleton<ITransactionRunner>(f.Store);
        builder.Services.AddSingleton<IClock, FixedClock>();
        builder.Services.AddErpApplication();
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o => JsonDefaults.Configure(o.SerializerOptions));
        builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.Use(ApiSupport.ErrorMiddleware); app.UseAuthentication(); app.UseAuthorization(); app.MapErp();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
            var path = $"/api/v1/companies/{f.CompanyId}/journals";
            using var anonymous = await client.GetAsync(path);
            Ensure(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Anonymous route did not return 401");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", f.Clerk.Subject);
            using var missingKey = await Send(client, path, f.Request());
            await Problem(missingKey, 422, "idempotency_key_required");

            var key = Fixture.Key();
            using var created = await Send(client, path, f.Request(), key);
            Ensure(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
            var draft = JsonSerializer.Deserialize<CommandReceipt>(await created.Content.ReadAsStringAsync(), JsonDefaults.Options)!;
            using var replayed = await Send(client, path, f.Request(), key);
            Ensure(JsonSerializer.Deserialize<CommandReceipt>(await replayed.Content.ReadAsStringAsync(), JsonDefaults.Options) == draft, "HTTP replay differs");

            using var conflict = await Send(client, path, f.Request(200, 200), key);
            await Problem(conflict, 409, "idempotency_conflict");

            using var submitted = await Send(client, path + $"/{draft.ResourceId}/submit", new TransitionRequest(draft.Version), Fixture.Key());
            Ensure(submitted.StatusCode == HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());
            var review = JsonSerializer.Deserialize<CommandReceipt>(await submitted.Content.ReadAsStringAsync(), JsonDefaults.Options)!;
            using var selfApprove = await Send(client, path + $"/{draft.ResourceId}/approve", new TransitionRequest(review.Version), Fixture.Key());
            await Problem(selfApprove, 403, "self_approval_forbidden");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", f.Approver.Subject);
            using var approved = await Send(client, path + $"/{draft.ResourceId}/approve", new TransitionRequest(review.Version), Fixture.Key());
            Ensure(approved.StatusCode == HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
            var ready = JsonSerializer.Deserialize<CommandReceipt>(await approved.Content.ReadAsStringAsync(), JsonDefaults.Options)!;
            using var posted = await Send(client, path + $"/{draft.ResourceId}/post", new TransitionRequest(ready.Version), Fixture.Key());
            Ensure(posted.StatusCode == HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

            using var report = await client.GetAsync($"/api/v1/companies/{f.CompanyId}/reports/trial-balance?from=2026-01-01&to=2026-12-31");
            Ensure(report.StatusCode == HttpStatusCode.OK, await report.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
            Ensure(json.RootElement.GetProperty("totalClosingDebit").GetString() == "100", "Decimal HTTP contract differs");
            Ensure(json.RootElement.GetProperty("from").GetString() == "2026-01-01", "Date HTTP contract differs");

            await f.Post(await f.Draft(f.Request() with { AutomaticReversal = true, ReversalDate = Fixture.Date }));
            using (var anonymousRun = await Send(client, path + "/reversals/run-due", new RunDueReversalsRequest(Fixture.Date)))
                Ensure(anonymousRun.StatusCode == HttpStatusCode.Unauthorized, "Anonymous scheduled reversal passed");
            using (var run = await Send(client, path + "/reversals/run-due", new RunDueReversalsRequest(Fixture.Date)))
            {
                Ensure(run.StatusCode == HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
                using var result = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
                Ensure(result.RootElement.GetProperty("generatedCount").GetInt32() == 1 &&
                    result.RootElement.GetProperty("rows")[0].GetProperty("succeeded").GetBoolean(), "HTTP scheduled reversal failed");
            }
            using (var replay = await Send(client, path + "/reversals/run-due", new RunDueReversalsRequest(Fixture.Date)))
            using (var result = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()))
                Ensure(replay.StatusCode == HttpStatusCode.OK && result.RootElement.GetProperty("selectedCount").GetInt32() == 0,
                    "HTTP scheduled reversal replay duplicated work");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "unrelated-user");
            using var forbidden = await client.GetAsync(path);
            await Problem(forbidden, 403, "permission_denied");
            using var forbiddenRun = await Send(client, path + "/reversals/run-due", new RunDueReversalsRequest(Fixture.Date));
            await Problem(forbiddenRun, 403, "permission_denied");
        }
        finally { await app.StopAsync(); }
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, string? key = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        message.Content = new StringContent(JsonSerializer.Serialize(body, JsonDefaults.Options), Encoding.UTF8, "application/json");
        if (key is not null) message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }
    private static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        Ensure((int)response.StatusCode == status, await response.Content.ReadAsStringAsync());
        Ensure(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Wrong error content type");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Ensure(json.RootElement.GetProperty("code").GetString() == code, "Wrong problem code");
        Ensure(!string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()), "Missing trace id");
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
