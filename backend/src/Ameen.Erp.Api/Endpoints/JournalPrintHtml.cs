using System.Globalization;
using System.Net;
using System.Text;
using Ameen.Erp.Application;

namespace Ameen.Erp.Api;

internal static class JournalPrintHtml
{
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "—");
    private static string N(decimal amount) => amount.ToString("0.######", CultureInfo.InvariantCulture);
    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string Render(JournalPrintDocument record)
    {
        var reverse = record.Source == "Reversal";
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>");
        html.Append(reverse ? "طباعة قيد عكسي " : "طباعة قيد اليومية ");
        html.Append(H(record.Number)).Append("</title><style>");
        html.Append("body{font-family:system-ui,Tahoma,Arial,sans-serif;margin:2rem;color:#18212c}");
        html.Append("header{border-bottom:2px solid #444;padding-bottom:1rem}");
        html.Append("dl{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:.7rem}");
        html.Append("dt{color:#555;font-size:.8rem}dd{margin:0;font-weight:600}");
        html.Append("table{width:100%;border-collapse:collapse;margin-top:1.2rem}");
        html.Append("th,td{padding:.45rem;border:1px solid #ccc;text-align:right}");
        html.Append("th{background:#f2f2f2}.num{text-align:left;direction:ltr}");
        html.Append("tfoot{font-weight:bold}footer{margin-top:1rem;color:#555}");
        html.Append("@media print{body{margin:8mm}thead{display:table-header-group}tr{break-inside:avoid}}");
        html.Append("</style></head><body><header><h1>");
        html.Append(reverse ? "قيد عكسي مرتبط" : "قيد يومية");
        html.Append("</h1><p>").Append(H(record.CompanyName)).Append(" — ")
            .Append(H(record.BranchName)).Append("</p></header><dl>");

        static void Field(StringBuilder sb, string label, string value)
        {
            sb.Append("<div><dt>").Append(label).Append("</dt><dd>")
                .Append(WebUtility.HtmlEncode(value)).Append("</dd></div>");
        }

        Field(html, "رقم المستند", record.Number);
        Field(html, "المصدر", record.Source);
        Field(html, "الحالة", record.State);
        Field(html, "تاريخ الوثيقة", D(record.DocumentDate));
        Field(html, "تاريخ الأثر", D(record.AccountingDate));
        Field(html, "العملة الوظيفية", record.FunctionalCurrency);
        Field(html, "المرجع الخارجي", record.ExternalReference ?? "—");
        Field(html, "المنشئ", record.CreatedBy);
        Field(html, "الإصدار", record.Revision.ToString(CultureInfo.InvariantCulture));
        Field(html, "آخر تعديل", record.LastModifiedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        if (record.PostedAt is { } posted)
            Field(html, "وقت الترحيل", posted.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        if (reverse)
        {
            Field(html, "رقم القيد الأصلي", record.OriginalJournalNumber ?? "—");
            Field(html, "سبب العكس", record.ReversalReason ?? "—");
        }
        html.Append("</dl><section><h2>البيان</h2><p>").Append(H(record.Description)).Append("</p></section>");
        html.Append("<table><thead><tr><th>م</th><th>الحساب</th><th>اسم الحساب</th>");
        html.Append("<th>مركز التكلفة</th><th>بيان السطر</th><th>العملة</th><th>سعر السطر</th>");
        html.Append("<th>مدين</th><th>دائن</th><th>مدين وظيفي</th><th>دائن وظيفي</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var line in record.Lines)
        {
            html.Append("<tr><td>").Append(line.Position).Append("</td><td>").Append(H(line.AccountCode))
                .Append("</td><td>").Append(H(line.AccountName))
                .Append("</td><td>").Append(H(line.CostCenterCode))
                .Append("</td><td>").Append(H(line.Description))
                .Append("</td><td>").Append(H(line.CurrencyCode))
                .Append("</td><td class=\"num\">").Append(N(line.ExchangeRate))
                .Append("</td><td class=\"num\">").Append(N(line.Debit))
                .Append("</td><td class=\"num\">").Append(N(line.Credit))
                .Append("</td><td class=\"num\">").Append(N(line.FunctionalDebit))
                .Append("</td><td class=\"num\">").Append(N(line.FunctionalCredit))
                .Append("</td></tr>");
        }
        html.Append("</tbody><tfoot><tr><th colspan=\"7\">الإجمالي</th>");
        html.Append("<td class=\"num\">").Append(record.TotalDebit is { } d ? N(d) : "—")
            .Append("</td><td class=\"num\">").Append(record.TotalCredit is { } c ? N(c) : "—")
            .Append("</td><td class=\"num\">").Append(N(record.FunctionalDebit))
            .Append("</td><td class=\"num\">").Append(N(record.FunctionalCredit))
            .Append("</td></tr></tfoot></table>");
        html.Append("<footer>عرض للطباعة فقط. أسعار الصرف على مستوى السطر، دون استنتاج سعر لرأس القيد.");
        if (record.OriginalTotalsCurrency is null)
            html.Append(" تتضمن السطور عملات مختلفة؛ لا يُجمع المدين والدائن الأصليان عبر العملات.");
        else
            html.Append(" إجماليات المبالغ الأصلية بعملة ").Append(H(record.OriginalTotalsCurrency)).Append(".");
        html.Append("</footer>");
        html.Append("</body></html>");
        return html.ToString();
    }
}
