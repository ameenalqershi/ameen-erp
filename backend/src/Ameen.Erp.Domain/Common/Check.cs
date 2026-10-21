using System.Text.RegularExpressions;

namespace Ameen.Erp.Domain;

public static class Check
{
    public static void That(bool condition, string code, string message, ErrorKind kind = ErrorKind.Validation)
    {
        if (!condition) throw new BusinessException(code, message, kind);
    }

    public static string Text(string? value, string field, int max = 200)
    {
        var result = value?.Trim() ?? "";
        That(result.Length > 0 && result.Length <= max, "invalid_text", $"{field}: يجب إدخال نص من 1 إلى {max} حرفًا.");
        That(!result.Any(char.IsControl), "invalid_text", $"{field}: النص يحتوي محارف تحكم.");
        return result;
    }

    public static string Code(string? value, string field, int max = 32)
    {
        var result = Text(value, field, max).ToUpperInvariant();
        That(Regex.IsMatch(result, @"^[A-Z0-9][A-Z0-9._-]*$"), "invalid_code", $"{field}: رمز لاتيني أو رقمي صالح مطلوب.");
        return result;
    }

    public static string Currency(string? value)
    {
        var result = Code(value, "العملة", 3);
        That(Regex.IsMatch(result, "^[A-Z]{3}$"), "invalid_currency", "رمز العملة يتكون من ثلاثة أحرف.");
        return result;
    }

    public static void Version(Entity entity, Guid expected)
    {
        That(expected != Guid.Empty && entity.Version == expected, "version_conflict",
            "تغير السجل منذ قراءته؛ أعد تحميله قبل تنفيذ العملية.", ErrorKind.Conflict);
    }

    public static void Amount(decimal amount, int scale = 6, bool positive = false)
    {
        That(amount >= 0 && (!positive || amount > 0) && amount < 100_000_000_000_000m &&
             decimal.Round(amount, scale) == amount, "invalid_amount", "المبلغ خارج الحدود أو يتجاوز دقة العملة.");
    }
}
