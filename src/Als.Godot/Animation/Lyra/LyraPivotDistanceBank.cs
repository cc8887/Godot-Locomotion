using System.Text.Json;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// Reuse the two native codec adapters on the same immutable sequence buffers.
internal sealed class LyraPivotDistanceBank
{
    private readonly LyraStartDistanceBank _advance;
    private readonly LyraPivotAsset[] _assets;
    public string[] Paths => _advance.Paths;
    public AlsAssetSyncSequence[] Sequences => _advance.Sequences;
    public AlsAssetSyncMarker[] Markers => _advance.Markers;
    public LyraStartPolicy Policy(string profile) => _advance.Policy(profile);
    public LyraPivotAsset Asset(int id) => _assets[id];
    public LyraPivotAsset Asset(string path) => Asset(Array.IndexOf(Paths,path));
    public LyraPivotDistanceBank(JsonElement data)
    {
        _advance=new(data); var match=new LyraStopDistanceBank(data);
        if (!_advance.Paths.SequenceEqual(match.Paths) || !_advance.Sequences.SequenceEqual(match.Sequences) ||
            !_advance.Markers.SequenceEqual(match.Markers))
            throw new InvalidOperationException("Pivot match/advance resource layouts differ.");
        _assets=Enumerable.Range(0,Paths.Length).Select(i=>new LyraPivotAsset(_advance.Asset(i),match.Asset(i))).ToArray();
    }
}
