using System.Text.RegularExpressions;

namespace Ameen.Erp.Domain;

public enum ErrorKind { Validation, NotFound, Forbidden, Conflict }

public sealed class BusinessException : Exception
{
    public string Code { get; }
    public ErrorKind Kind { get; }
    public BusinessException(string code, string message, ErrorKind kind = ErrorKind.Validation) : base(message)
    {
        Code = code;
        Kind = kind;
    }
}

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

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public Guid Version { get; private set; } = Guid.NewGuid();
    public void Touch() => Version = Guid.NewGuid();
}

public abstract class CompanyEntity : Entity
{
    public Guid CompanyId { get; protected set; }
    protected CompanyEntity() { }
    protected CompanyEntity(Guid companyId)
    {
        Check.That(companyId != Guid.Empty, "company_required", "الشركة مطلوبة.");
        CompanyId = companyId;
    }
}

public static class Permissions
{
    public const string Read = "ledger.read";
    public const string Configure = "company.configure";
    public const string Members = "members.manage";
    public const string Draft = "journal.write";
    public const string Approve = "journal.approve";
    public const string Post = "journal.post";
    public const string Reverse = "journal.reverse";
    public const string PeriodClose = "period.close";
    public const string PeriodReopen = "period.reopen";
    public const string Audit = "audit.read";
    public static readonly string[] All = { Read, Configure, Members, Draft, Approve, Post, Reverse, PeriodClose, PeriodReopen, Audit };
}
