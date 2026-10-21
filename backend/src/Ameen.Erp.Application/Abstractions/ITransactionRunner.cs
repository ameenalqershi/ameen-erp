using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public interface ITransactionRunner
{
    Task<CommandReceipt> Run(CommandIdentity identity, Func<CancellationToken, Task> authorize,
        Func<CancellationToken, Task<CommandReceipt>> work, CancellationToken ct);
}
