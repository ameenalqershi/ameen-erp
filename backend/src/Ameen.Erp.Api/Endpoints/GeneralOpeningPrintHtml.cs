using System.Globalization;
using System.Net;
using System.Text;
using Ameen.Erp.Application;

namespace Ameen.Erp.Api;

// Static, script-free RTL print view; every user-controlled value is HTML-encoded.
internal static class GeneralOpeningPrintHtml
{
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "—");
    private static string Money(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string Render(GeneralOpeningPrintDocument document)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<title>افتتاح عام ").Append(H(document.Number)).Append("</title>");
        html.Append("<style>body{font-family:system-ui,Tahoma,Arial,sans-serif;margin:2rem;color:#18212c}");
        html.Append("h1{margin-bottom:.2rem}header{border-bottom:2px solid #444;padding-bottom:1rem}");
        html.Append("dl{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:.7rem}");
        html.Append("dt{font-size:.8rem;color:#555}dd{margin:0;font-weight:600}");
        html.Append("table{width:100%;border-collapse:collapse;margin-top:1.2rem}");
        html.Append("th,td{border:1px solid #ccc;padding:.45rem;text-align:right}");
        html.Append("th{background:#f2f2f2}td.num{text-align:left;direction:ltr}");
        html.Append("tfoot{font-weight:700}footer{margin-top:1rem;color:#555}");
        html.Append("@media print{body{margin:8mm}thead{display:table-header-group}tr{break-inside:avoid}}");
        html.Append("</style></head><body><header><h1>قيد الأرصدة الافتتاحية العامة</h1>");
        html.Append("<p>").Append(H(document.CompanyName)).Append(" — ")
            .Append(H(document.BranchName)).Append("</p></header><dl>");
        static void Field(StringBuilder sb, string label, string value)
        {
            sb.Append("<div><dt>").Append(label).Append("</dt><dd>")
                .Append(WebUtility.HtmlEncode(value)).Append("</dd></div>");
        }
        Field(html, "رقم المستند", document.Number);
        Field(html, "الحالة", document.State);
        Field(html, "تاريخ الوثيقة", Date(document.DocumentDate));
        Field(html, "تاريخ الأثر", Date(document.AccountingDate));
        Field(html, "بداية التشغيل", Date(document.OperationStartsOn));
        Field(html, "مرجع الميزان السابق", document.PreviousTrialBalanceReference);
        Field(html, "العملة", document.OpeningCurrency);
        Field(html, "سعر الصرف التاريخي", Money(document.OpeningExchangeRate));
        Field(html, "العملة الوظيفية", document.FunctionalCurrency);
        Field(html, "المرجع الخارجي", document.ExternalReference ?? "—");
        Field(html, "أنشأه", document.CreatedBy);
        Field(html, "الإصدار", document.Revision.ToString(CultureInfo.InvariantCulture));
        html.Append("</dl><section><h2>البيان</h2><p>").Append(H(document.Description)).Append("</p></section>");
        html.Append("<table><thead><tr><th>م</th><th>الحساب</th><th>اسم الحساب</th>");
        html.Append("<th>مركز التكلفة</th><th>بيان السطر</th><th>العملة</th><th>السعر</th>");
        html.Append("<th>مدين</th><th>دائن</th><th>مدين وظيفي</th><th>دائن وظيفي</th></tr></thead><tbody>");
        foreach (var line in document.Lines)
        {
            html.Append("<tr><td>").Append(line.Position).Append("</td><td>").Append(H(line.AccountCode))
                .Append("</td><td>").Append(H(line.AccountName)).Append("</td><td>")
                .Append(H(line.CostCenterCode)).Append("</td><td>").Append(H(line.Description))
                .Append("</td><td>").Append(H(line.CurrencyCode)).Append("</td><td class=\"num\">")
                .Append(Money(line.ExchangeRate)).Append("</td><td class=\"num\">")
                .Append(Money(line.Debit)).Append("</td><td class=\"num\">")
                .Append(Money(line.Credit)).Append("</td><td class=\"num\">")
                .Append(Money(line.FunctionalDebit)).Append("</td><td class=\"num\">")
                .Append(Money(line.FunctionalCredit)).Append("</td></tr>");
        }
        html.Append("</tbody><tfoot><tr><th colspan=\"7\">الإجمالي</th><td class=\"num\">")
            .Append(Money(document.TotalDebit)).Append("</td><td class=\"num\">")
            .Append(Money(document.TotalCredit)).Append("</td><td class=\"num\">")
            .Append(Money(document.FunctionalDebit)).Append("</td><td class=\"num\">")
            .Append(Money(document.FunctionalCredit)).Append("</td></tr></tfoot></table>");
        html.Append("<footer>عرض للطباعة فقط — لا يغيّر القيد أو أرصدة الأستاذ.</footer></body></html>");
        return html.ToString();
    }
}
