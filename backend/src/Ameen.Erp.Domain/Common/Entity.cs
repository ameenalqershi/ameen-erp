namespace Ameen.Erp.Domain;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public Guid Version { get; private set; } = Guid.NewGuid();
    public void Touch() => Version = Guid.NewGuid();
}
