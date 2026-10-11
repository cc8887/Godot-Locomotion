namespace GodotAls.Core.Resources;

/// <summary>Reads declare dependencies and pin their versions for the result.</summary>
public sealed class ResourceLoadContext
{
    private readonly ResourceManager _owner;
    internal readonly Dictionary<ResourceId, ResourceManager.Entry> Dependencies = [];
    internal long ByteCount;
    private bool _active = true;
    private readonly int _thread = Environment.CurrentManagedThreadId;

    internal ResourceLoadContext(ResourceManager owner) => _owner = owner;

    public T Read<T>(ResourceId id)
    {
        RequireActive();
        return _owner.ReadDependency<T>(this, id);
    }

    public void ReportBytes(long count)
    {
        RequireActive();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ByteCount = checked(ByteCount + count);
    }

    internal void Close() { _active = false; Dependencies.Clear(); }

    private void RequireActive()
    {
        if (!_active || _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("A synchronous resource context is only valid during its loader on the creating thread.");
    }
}
