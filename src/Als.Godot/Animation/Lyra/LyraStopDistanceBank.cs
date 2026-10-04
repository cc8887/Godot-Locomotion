using System.Text.Json;
using GodotAls.Core.Curves;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraStopAsset(int Id, AlsAssetSyncSequence Sequence,
    float[] Times, float[] Values, ulong MarkerMask)
{
    public float Match(float distance) => AlsDistanceMatching.MatchToTarget(distance, Times, Values);
}

// Actual static FAnimCurveBufferAccess samples; no expected clocks or prediction.
internal sealed class LyraStopDistanceBank
{
    public string[] Paths { get; }
    public AlsAssetSyncSequence[] Sequences { get; }
    public AlsAssetSyncMarker[] Markers { get; }
    private readonly LyraStopAsset[] _assets;
    public LyraStopAsset Asset(int id) => _assets[id];
    public LyraStopAsset Asset(string path) => Asset(Array.IndexOf(Paths, path));
    public LyraStopDistanceBank(JsonElement data)
    {
        if (data.GetProperty("schemaVersion").GetInt32() != 1) throw new NotSupportedException("Stop distance schema.");
        var rows = data.GetProperty("assets").EnumerateObject().ToArray();
        Paths = rows.Select(r=>r.Name).ToArray();
        var symbols = rows.SelectMany(r=>r.Value.GetProperty("markers").EnumerateArray())
            .Select(m=>m.GetProperty("name").GetString()!).Distinct().OrderBy(n=>n,StringComparer.Ordinal).ToArray();
        if (symbols.Length >= 64) throw new NotSupportedException("Too many Stop marker symbols.");
        var markers = new List<AlsAssetSyncMarker>();
        Sequences = new AlsAssetSyncSequence[rows.Length]; _assets = new LyraStopAsset[rows.Length];
        for (var id=0; id<rows.Length; id++)
        {
            var row=rows[id].Value;
            if (row.GetProperty("path").GetString()!=Paths[id])
                throw new InvalidOperationException("Wrong Stop distance identity.");
            if (row.GetProperty("evaluation").GetString() is not ("UniformIndexable" or "RawRichCurve"))
                throw new NotSupportedException("Unknown Stop distance codec.");
            var points=row.GetProperty("samples").EnumerateArray().ToArray();
            var times=points.Select(p=>LyraStartDistanceBank.Float(p,"time")).ToArray();
            var values=points.Select(p=>LyraStartDistanceBank.Float(p,"value")).ToArray();
            if (points.Length<2 || times.Any(t=>!float.IsFinite(t)) || values.Any(v=>!float.IsFinite(v)) ||
                values[^1]-values[0]!=LyraStartDistanceBank.Float(row,"range") ||
                times.Zip(times.Skip(1)).Any(p=>p.First>=p.Second))
                throw new InvalidOperationException("Invalid Stop distance buffer.");
            // Original stop curves contain small descending tails. Preserve
            // the buffer and the native search behavior without repairing it.
            var ms=row.GetProperty("markers").EnumerateArray().ToArray();
            var sequence=new AlsAssetSyncSequence(id,LyraStartDistanceBank.Float(row,"length"),
                LyraStartDistanceBank.Float(row,"rateScale"),markers.Count,ms.Length);
            ulong mask=0;
            foreach (var m in ms)
            {
                var symbol=Array.IndexOf(symbols,m.GetProperty("name").GetString()!)+1;
                mask|=1UL<<symbol; markers.Add(new(symbol,m.GetProperty("time").GetSingle()));
            }
            Sequences[id]=sequence; _assets[id]=new(id,sequence,times,values,mask);
        }
        Markers=markers.ToArray();
    }
}
