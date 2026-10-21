using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public abstract partial class ApplicationService
{
    protected static void Paging(int offset, int limit)
    {
        Check.That(offset >= 0 && offset <= 1_000_000 && limit is >= 1 and <= 200,
            "invalid_pagination", "حجم الصفحة من 1 إلى 200 وإزاحة صالحة مطلوبان.");
    }
}
