using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class JournalService
{
    public async Task<JournalProcessingDurableResult> DurableBatch(Guid cid, Actor actor,
        string batchKey, CancellationToken ct)
    {
        batchKey = Check.Text(batchKey, "مفتاح الدفعة", 80);
        await Authorize(cid, actor, Permissions.Read, null, ct);
        var batch = (await store.Find<JournalProcessingBatch>(cid, x => x.BatchKey == batchKey, ct)).SingleOrDefault();
        Check.That(batch is not null && batch.ActorId == actor.Subject,
            "processing_batch_not_found", "الدفعة غير موجودة ضمن صلاحيات المستخدم.", ErrorKind.NotFound);
        var stored = batch!.Rows();
        foreach (var item in stored)
            await AuthorizeJournal(cid, item.JournalId, actor, Permissions.Read, ct);
        var rows = stored.Select(x => new JournalProcessingRow(x.JournalId, x.Succeeded,
            x.RejectionRecorded,
            x.ReceiptId is Guid rid && x.ReceiptVersion is Guid version && x.ReceiptStatus is string status
                ? new CommandReceipt(rid, version, status, x.ReceiptNumber) : null,
            x.ErrorCode)).ToArray();
        var succeeded = rows.Count(x => x.Succeeded);
        return new(batch.Id, batch.Version, batch.Revision, batch.Action, batch.ActorId, batch.CreatedAt, batch.LastModifiedAt,
            batch.FinishedAt, rows.Length == batch.ItemCount && succeeded == rows.Length,
            succeeded, rows.Count(x => !x.Succeeded), rows);
    }
}
