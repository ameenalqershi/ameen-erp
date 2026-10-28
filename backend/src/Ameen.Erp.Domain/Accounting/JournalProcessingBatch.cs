using System.Text.Json;
namespace Ameen.Erp.Domain;
public sealed class JournalProcessingBatch : CompanyEntity
{
    public string BatchKey { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string ActorId { get; private set; } = "";
    public string RequestHash { get; private set; } = "";
    public int ItemCount { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string RowsJson { get; private set; } = "[]";
    private JournalProcessingBatch() { }
    public JournalProcessingBatch(Guid companyId, string batchKey, string action, string actor, string hash, int itemCount, DateTimeOffset now) : base(companyId)
    {
        BatchKey = Check.Text(batchKey, "مفتاح الدفعة", 80); Action = Check.Text(action, "الإجراء", 20);
        ActorId = Check.Text(actor, "منفذ الدفعة", 200); RequestHash = Check.Text(hash, "بصمة الدفعة", 64);
        Check.That(itemCount is >= 1 and <= 100, "invalid_processing_count", "عدد عناصر الدفعة غير صالح.");
        ItemCount = itemCount; CreatedAt = LastModifiedAt = now;
    }
    public JournalProcessingStoredRow[] Rows() => JsonSerializer.Deserialize<JournalProcessingStoredRow[]>(RowsJson) ?? [];
    public void Record(int index, JournalProcessingStoredRow result, DateTimeOffset now)
    {
        var rows = Rows();
        Check.That(index >= 0 && index <= rows.Length && index < ItemCount, "processing_batch_order",
            "ترتيب تسجيل نتائج الدفعة غير صالح.", ErrorKind.Conflict);
        if (index == rows.Length) rows = rows.Append(result).ToArray();
        else
        {
            Check.That(!rows[index].Succeeded && !rows[index].RejectionRecorded, "processing_result_final",
                "نتيجة المستند النهائية محفوظة ولا تقبل الاستبدال.", ErrorKind.Conflict);
            rows[index] = result;
        }
        Check.That(Revision < int.MaxValue && now >= CreatedAt, "processing_batch_revision", "إصدار الدفعة غير صالح.");
        RowsJson = JsonSerializer.Serialize(rows); Revision++; LastModifiedAt = now;
        FinishedAt = rows.Length == ItemCount ? now : null; Touch();
    }
}
public sealed record JournalProcessingStoredRow(Guid JournalId, bool Succeeded, bool RejectionRecorded,
    Guid? ReceiptId, Guid? ReceiptVersion, string? ReceiptStatus, string? ReceiptNumber, string? ErrorCode);
