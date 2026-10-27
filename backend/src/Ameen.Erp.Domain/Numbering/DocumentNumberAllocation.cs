namespace Ameen.Erp.Domain;
public sealed class DocumentNumberAllocation : CompanyEntity
{
    public Guid? JournalId { get; private set; }
    public Guid? InventoryOpeningId { get; private set; }
    public Guid? DocumentId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid SeriesVersion { get; private set; }
    public Guid BranchId { get; private set; }
    public NumberingDocumentKind DocumentKind { get; private set; }
    public long Sequence { get; private set; }
    public string Number { get; private set; } = "";
    public string ReservedBy { get; private set; } = "";
    public DateTimeOffset ReservedAt { get; private set; }
    public void BindDocument(Guid documentId)
    { Check.That(DocumentId is null && documentId!=Guid.Empty,"number_document_binding","حجز الرقم يرتبط بمستند واحد فقط.");DocumentId=documentId; if(DocumentKind==NumberingDocumentKind.Journal) JournalId=documentId; else InventoryOpeningId=documentId; }
    private DocumentNumberAllocation() { }
    public DocumentNumberAllocation(DocumentNumberSeries series,Guid branchId,Guid sourceVersion,long sequence,string number,string actor,DateTimeOffset now) : base(series.CompanyId)
    { SeriesId=series.Id; SeriesVersion=sourceVersion; BranchId=branchId; DocumentKind=series.DocumentKind; Sequence=sequence; Number=number; ReservedBy=actor; ReservedAt=now; }
}
