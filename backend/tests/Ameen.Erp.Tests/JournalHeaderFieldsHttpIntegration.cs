using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// Runs inside the existing production HTTPS/JWT/SQL test host as a SQL clerk.
// Uses the shared tenant-scoped journal API; does not grant additional roles.
public static class JournalHeaderFieldsHttpIntegration
{
    public static async Task Run(HttpClient client, string path, CreateJournalRequest example)
    {
        const string originalDescription = "بيان القيد يدعم اللغة العربية";
        const string originalReference = "00004905-GL";
        var create = example with { Description = originalDescription, ExternalReference = originalReference };
        var created = await Send(client, path, create);
        using (var detail = await client.GetAsync(path + $"/{created.ResourceId}"))
        using (var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
            Ensure(detail.StatusCode == HttpStatusCode.OK &&
                json.RootElement.GetProperty("description").GetString() == originalDescription &&
                json.RootElement.GetProperty("externalReference").GetString() == originalReference &&
                json.RootElement.GetProperty("state").GetString() == nameof(JournalState.Draft) &&
                json.RootElement.GetProperty("revision").GetInt32() == 1,
                "GL03 fields or server state changed across HTTPS/JWT/SQL boundary");

        using (var found = await client.GetAsync(path + "?source=Manual&search=" + Uri.EscapeDataString(originalReference)))
        using (var json = JsonDocument.Parse(await found.Content.ReadAsStringAsync()))
            Ensure(found.StatusCode == HttpStatusCode.OK &&
                json.RootElement.GetProperty("items").EnumerateArray()
                    .Any(x => x.GetProperty("id").GetGuid() == created.ResourceId),
                "GL03 leading-zero external reference not searchable over HTTPS/JWT/SQL");

        using (var invalid = new HttpRequestMessage(HttpMethod.Post, path))
        {
            invalid.Headers.Add("Idempotency-Key", Fixture.Key());
            invalid.Content = new StringContent(JsonSerializer.Serialize(
                create with { ExternalReference = new string('Z', 101) }, JsonDefaults.Options),
                Encoding.UTF8, "application/json");
            using var denied = await client.SendAsync(invalid);
            using var error = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Ensure((int)denied.StatusCode == 422 &&
                error.RootElement.GetProperty("code").GetString() == "invalid_text",
                "GL03 API failed to reject overlong external reference");
        }

        using (var forgedState = new HttpRequestMessage(HttpMethod.Post, path))
        {
            forgedState.Headers.Add("Idempotency-Key", Fixture.Key());
            var body = JsonSerializer.Serialize(create, JsonDefaults.Options);
            forgedState.Content = new StringContent(body[..^1] + ",\"state\":\"Posted\"}",
                Encoding.UTF8, "application/json");
            using var result = await client.SendAsync(forgedState);
            Ensure(result.StatusCode == HttpStatusCode.BadRequest,
                "GL03 API accepted client-forged system journal state");
        }

        const string editedReference = "00000037";
        var edit = new EditJournalRequest(created.Version, "تعديل بيان معتمد",
            create.Lines, new DraftJournalHeaderRequest(example.BranchId,
                example.DocumentDate, example.AccountingDate, editedReference));
        var editKey = Fixture.Key();
        var changed = await Send(client, path + $"/{created.ResourceId}", edit, editKey, HttpMethod.Put);
        var replay = await Send(client, path + $"/{created.ResourceId}", edit, editKey, HttpMethod.Put);
        Ensure(changed == replay, "GL03 HTTP edit idempotency changed result");
        using (var detail = await client.GetAsync(path + $"/{created.ResourceId}"))
        using (var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()))
            Ensure(detail.StatusCode == HttpStatusCode.OK &&
                json.RootElement.GetProperty("description").GetString() == "تعديل بيان معتمد" &&
                json.RootElement.GetProperty("externalReference").GetString() == editedReference &&
                json.RootElement.GetProperty("state").GetString() == nameof(JournalState.Draft) &&
                json.RootElement.GetProperty("revision").GetInt32() == 2,
                "GL03 HTTP edit failed to preserve reference or system state");
        Console.WriteLine("  HOST PASS GL03-FG03 Unicode, zero-preserving external reference, input rejection and state over HTTPS/JWT/SQL");
    }

    private static async Task<CommandReceipt> Send(HttpClient client, string path, object payload,
        string? key = null, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", key ?? Fixture.Key());
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonDefaults.Options),
            Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Ensure(response.StatusCode == HttpStatusCode.OK, "GL03 HTTP command failed: " + body);
        return JsonSerializer.Deserialize<CommandReceipt>(body, JsonDefaults.Options)!;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
