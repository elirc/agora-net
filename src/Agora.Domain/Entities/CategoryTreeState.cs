namespace Agora.Domain.Entities;

/// <summary>One revision for topology shared by every category parent writer.</summary>
public class CategoryTreeState
{
    public int Id { get; private set; } = 1;
    public long Version { get; private set; }
    public void Advance() => Version = checked(Version + 1);
}
