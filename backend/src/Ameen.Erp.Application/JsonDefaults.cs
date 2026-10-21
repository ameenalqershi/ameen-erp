using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ameen.Erp.Application;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options); return options;
    }
    public static void Configure(JsonSerializerOptions options)
    {
        options.MaxDepth = 32;
        options.RespectRequiredConstructorParameters = true;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.Converters.Add(new DateOnlyJsonConverter());
        options.Converters.Add(new DecimalStringJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    }
}

public sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && DateOnly.TryParseExact(reader.GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) return value;
        throw new JsonException("Date must use yyyy-MM-dd.");
    }
    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}

public sealed class DecimalStringJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        string? input = null;
        if (reader.TokenType == JsonTokenType.String) input = reader.GetString();
        else if (reader.TokenType == JsonTokenType.Number)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            input = document.RootElement.GetRawText();
        }
        if (input is not null && decimal.TryParse(input,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            && Canonical(input) == Canonical(value.ToString("0.############################", CultureInfo.InvariantCulture)))
            return value;
        // Decimal.TryParse alone can silently round oversized input. Exact round-trip is mandatory.
        throw new JsonException("Use an exactly representable decimal without thousands separators or exponents.");
    }
    private static string Canonical(string value)
    {
        var negative = value.StartsWith('-');
        var parts = value.TrimStart('+', '-').Split('.');
        var whole = parts[0].TrimStart('0'); if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        return (negative && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
    }
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
}
