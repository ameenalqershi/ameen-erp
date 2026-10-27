using System.Globalization;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class JournalService
{
    private async Task<(long From,long To,string[]? Included,string[] Excluded)> ResolveNumberRange(Guid cid,FiscalPeriod period,string text,Guid[] branches,CancellationToken ct)
    {
        Check.That(text.Length is >0 and <=200,"invalid_journal_number_range","نطاق أرقام القيود غير صالح.");
        var parts=text.Split("..",StringSplitOptions.None).Select(x=>x.Trim()).ToArray();
        Check.That(parts.Length is 1 or 2 && parts.All(x=>x.Length>0),"invalid_journal_number_range","رقم واحد أو رقمان مفصولان بـ .. مطلوبان.");
        var allocations=await store.Find<DocumentNumberAllocation>(cid,x=>x.DocumentKind==NumberingDocumentKind.Journal && parts.Contains(x.Number),ct);
        if(allocations.Any())
        {
            Check.That(parts.All(x=>allocations.Any(a=>a.Number==x)),"invalid_journal_number_range","حدود النطاق يجب أن تخص السلسلة نفسها.");
            var first=allocations.Single(x=>x.Number==parts[0]);var last=allocations.Single(x=>x.Number==parts[^1]);
            var series=await Required<DocumentNumberSeries>(cid,first.SeriesId,ct);
            Check.That(series.FiscalYearId==period.FiscalYearId && first.SeriesId==last.SeriesId && branches.Contains(first.BranchId) && branches.Contains(last.BranchId) && first.Sequence<=last.Sequence,
                "invalid_journal_number_range","نطاق السلسلة أو السنة أو الفرع غير متوافق.");
            var rows=await store.Find<DocumentNumberAllocation>(cid,x=>x.SeriesId==first.SeriesId && x.Sequence>=first.Sequence && x.Sequence<=last.Sequence && branches.Contains(x.BranchId),ct);
            return(first.Sequence,last.Sequence,rows.Select(x=>x.Number).ToArray(),Array.Empty<string>());
        }
        var year=await Required<FiscalYear>(cid,period.FiscalYearId,ct);var prefix=$"JV-{year.Code}-";
        long Parse(string input)
        {
            if(input.StartsWith("JV-",StringComparison.Ordinal))
            {Check.That(input.StartsWith(prefix,StringComparison.Ordinal),"invalid_journal_number_range","رقم القيد لا يخص السنة المختارة.");input=input[prefix.Length..];}
            Check.That(long.TryParse(input,NumberStyles.None,CultureInfo.InvariantCulture,out var n) && n>0,"invalid_journal_number_range","رقم قيد غير صالح.");return n;
        }
        var from=Parse(parts[0]);var to=parts.Length==2?Parse(parts[1]):from;
        Check.That(from<=to,"invalid_journal_number_range","بداية نطاق القيود بعد نهايته.");
        var configured=await store.Find<DocumentNumberAllocation>(cid,x=>x.DocumentKind==NumberingDocumentKind.Journal,ct);
        return(from,to,null,configured.Select(x=>x.Number).ToArray());
    }
}
