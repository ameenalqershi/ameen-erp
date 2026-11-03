using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// GL03-T02 production HTTPS/JWT adapter. Executes inside the isolated SQL test host.
public static class JournalLineGuardHttpIntegration
{
    public static async Task Run(HttpClient client, string path, CreateJournalRequest example,
        Guid controlAccountId)
    {
        var negativeCases = new (string Label, decimal Debit, decimal Credit, string Error)[]
        {
            ("dual-positive", 50m, 50m, "invalid_journal_side"),
            ("zero-sides", 0m, 0m, "invalid_journal_side"),
            ("negative", -1m, 0m, "invalid_amount")
        };
        foreach (var sample in negativeCases)
        {
            var reference = "GL03-T02-" + sample.Label + "-" + Fixture.Key();
            var request = example with
            {
                ExternalReference = reference,
                Lines = example.Lines.Select((x, i) => i == 0
                    ? x with { Debit = sample.Debit, Credit = sample.Credit } : x).ToArray()
            };
            await Denied(client, path, request, sample.Error);
            await NoJournal(client, path, reference);
        }

        var controlReference = "GL03-T02-control-" + Fixture.Key();
        var control = example with
        {
            ExternalReference = controlReference,
            Lines = example.Lines.Select((x, i) => i == 0
                ? x with { AccountId = controlAccountId } : x).ToArray()
        };
        await Denied(client, path, control, "control_account_protected");
        await NoJournal(client, path, controlReference);

        // The prohibition is specific to invalid sides / unauthorized control posting;
        // a legitimate balanced manual journal still creates an ordinary draft.
        var good = example with { ExternalReference = "GL03-T02-good-" + Fixture.Key() };
        using var create = new HttpRequestMessage(HttpMethod.Post, path);
        create.Headers.Add("Idempotency-Key", Fixture.Key());
        create.Content = Json(good);
        using var posted = await client.SendAsync(create);
        Ensure(posted.StatusCode == HttpStatusCode.OK,
            "Valid manual journal failed HTTP create after rejected cases");
        using var receipt = JsonDocument.Parse(await posted.Content.ReadAsStringAsync());
        var id = receipt.RootElement.GetProperty("resourceId").GetGuid();
        using var get = await client.GetAsync(path + "/" + id);
        using var detail = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Ensure(get.StatusCode == HttpStatusCode.OK &&
            detail.RootElement.GetProperty("state").GetString() == nameof(JournalState.Draft) &&
            detail.RootElement.GetProperty("lines").EnumerateArray().All(line =>
            {
                var debit = decimal.Parse(line.GetProperty("debit").GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture);
                var credit = decimal.Parse(line.GetProperty("credit").GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture);
                return (debit > 0 && credit == 0) || (credit > 0 && debit == 0);
            }), "GL03-T02 accepted journal contains invalid amounts");
        Console.WriteLine("  HOST PASS GL03-T02 dual-positive, zero, negative and direct control rejected over HTTPS/JWT/SQL while valid draft remains enabled");
    }

    private static async Task Denied(HttpClient client, string path, CreateJournalRequest request,
        string expectedCode)
    {
        using var command = new HttpRequestMessage(HttpMethod.Post, path);
        command.Headers.Add("Idempotency-Key", Fixture.Key());
        command.Content = Json(request);
        using var response = await client.SendAsync(command);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Ensure((int)response.StatusCode == 422 &&
            error.RootElement.GetProperty("code").GetString() == expectedCode,
            "Expected GL03-T02 error " + expectedCode);
    }

    private static async Task NoJournal(HttpClient client, string path, string reference)
    {
        using var response = await client.GetAsync(path + "?source=Manual&search=" +
            Uri.EscapeDataString(reference));
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Ensure(response.StatusCode == HttpStatusCode.OK &&
            result.RootElement.GetProperty("totalCount").GetInt32() == 0,
            "Rejected GL03-T02 HTTP request leaked a journal");
    }
    private static StringContent Json(object value) => new(
        JsonSerializer.Serialize(value, JsonDefaults.Options), Encoding.UTF8, "application/json");
    private static void Ensure(bool condition, string error)
    {
        if (!condition) throw new Exception(error);
    }
}
