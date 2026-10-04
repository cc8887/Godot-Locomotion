namespace GodotAls.Animation.Lyra;

internal sealed class LyraPistolCatalog
{
    private LyraPistolCatalog(IReadOnlyDictionary<string, LyraClip> clips) => Clips = clips;

    public IReadOnlyDictionary<string, LyraClip> Clips { get; }

    public static LyraPistolCatalog Load() => new(LyraItemCatalog.Load("pistol", 63));
}
