namespace GodotAls.Core.Resources;

public interface IResourceProvider
{
    ReadOnlyMemory<byte> Read(ResourceId id);
}

public interface IResourceLoader<T>
{
    T Load(ResourceLoadContext context, ReadOnlyMemory<byte> content);
}

public sealed class DelegateResourceProvider(Func<ResourceId, ReadOnlyMemory<byte>> read) : IResourceProvider
{
    private readonly Func<ResourceId, ReadOnlyMemory<byte>> _read = read ?? throw new ArgumentNullException(nameof(read));
    public ReadOnlyMemory<byte> Read(ResourceId id) => _read(id);
}

public sealed class FileResourceProvider(Func<ResourceId, string> locate) : IResourceProvider
{
    private readonly Func<ResourceId, string> _locate = locate ?? throw new ArgumentNullException(nameof(locate));
    public ReadOnlyMemory<byte> Read(ResourceId id) => File.ReadAllBytes(_locate(id));
}
