namespace GodotAls.Animation.Lyra;

internal sealed class LyraRifleCatalog
{
    private LyraRifleCatalog(IReadOnlyDictionary<string, LyraClip> clips) => Clips = clips;

    public IReadOnlyDictionary<string, LyraClip> Clips { get; }

    public static LyraRifleCatalog Load() => new(LyraItemCatalog.Load("rifle", 64));
}
