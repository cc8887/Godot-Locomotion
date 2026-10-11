namespace GodotAls.Core.Resources;

/// <summary>Pins one immutable version, including its dependency versions.</summary>
public sealed class ResourceLease<T> : IDisposable
{
    private ResourceManager? _owner;
    private readonly ResourceManager.Entry _entry;

    internal ResourceLease(ResourceManager owner, ResourceManager.Entry entry)
    {
        _owner = owner;
        _entry = entry;
    }

    public T Value => _owner is not null ? (T)_entry.Value : throw new ObjectDisposedException(nameof(ResourceLease<T>));
    public long Revision => _entry.Revision;
    public ResourceId Id => _entry.Id;

    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_entry);
}
