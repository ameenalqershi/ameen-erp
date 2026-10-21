using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private readonly IErpStore store;
    private readonly ITransactionRunner transactions;
    private readonly IClock clock;
    public ErpService(IErpStore store, ITransactionRunner transactions, IClock clock)
    { this.store = store; this.transactions = transactions; this.clock = clock; }

    private async Task<Company> Company(Guid id, CancellationToken ct) =>
        await store.GetCompany(id, ct) ?? throw new BusinessException("company_not_found", "الشركة غير موجودة.", ErrorKind.NotFound);

    private async Task<T> Required<T>(Guid companyId, Guid id, CancellationToken ct) where T : CompanyEntity =>
        await store.Get<T>(companyId, id, ct) ?? throw new BusinessException("reference_not_found", "السجل غير موجود ضمن الشركة المحددة.", ErrorKind.NotFound);

    private Task<CommandReceipt> Command(Guid companyId, Actor actor, string key, string operation, object body,
        Func<CancellationToken, Task> authorize, Func<CancellationToken, Task<CommandReceipt>> work, CancellationToken ct)
    {
        Check.That(!string.IsNullOrWhiteSpace(actor.Subject), "actor_required", "هوية المستخدم مطلوبة.", ErrorKind.Forbidden);
        Check.Text(actor.Subject, "هوية المستخدم", 200);
        key = Check.Text(key, "Idempotency-Key", 100);
        string data;
        try { data = JsonSerializer.Serialize(body, JsonDefaults.Options); }
        catch (JsonException) { throw new BusinessException("invalid_command_payload", "الطلب يحتوي قيمًا غير قابلة للتمثيل ضمن العقد."); }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
        return transactions.Run(new CommandIdentity(companyId, key, operation, actor.Subject, fingerprint), async token =>
        {
            await authorize(token);
            if (operation != "company.create" && !operation.StartsWith("company.edit:", StringComparison.Ordinal) && !operation.StartsWith("company.status:", StringComparison.Ordinal))
            {
                var company = await Company(companyId, token);
                Check.That(company.IsActive, "company_inactive", "الشركة معطلة؛ القراءة متاحة وإجراء العمليات يتطلب تفعيلها.", ErrorKind.Conflict);
                Check.That(company.IsProfileComplete, "company_profile_incomplete", "استكمل البيانات القانونية والعنوانية للشركة قبل إجراء العمليات.", ErrorKind.Conflict);
            }
        }, work, ct);
    }

    private CommandReceipt Receipt(Entity entity, string status, string? number = null) => new(entity.Id, entity.Version, status, number);
    private void Audit(Guid companyId, Actor actor, string action, Entity entity, object details)
        => store.Add(new AuditEvent(companyId, actor.Subject, action, entity.Id, clock.UtcNow, JsonSerializer.Serialize(details, JsonDefaults.Options)));

}
