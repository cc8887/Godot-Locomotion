using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

public sealed record AlsRefactoredSyncAsset(int AssetId,int SequenceIndex,ulong MarkerMask);
public sealed class AlsRefactoredSyncBlendSpace
{
    private readonly int[] _sequences;
    private readonly float[] _rates;
    public int AssetId { get; }
    public ulong MarkerMask { get; }
    public bool LegacyLength { get; }
    public bool AllowMarkers { get; }
    public AlsBlendSpaceNotifyMode NotifyMode { get; }
    public ReadOnlySpan<int> SequenceIndices=>_sequences;
    internal AlsRefactoredSyncBlendSpace(int id,ulong mask,bool legacy,bool allow,AlsBlendSpaceNotifyMode notify,int[] sequences,float[] rates)
    {AssetId=id;MarkerMask=mask;LegacyLength=legacy;AllowMarkers=allow;NotifyMode=notify;_sequences=sequences;_rates=rates;}
    public int BindSamples(ReadOnlySpan<AlsAimGridVertex> weights,int sampleIdBase,Span<AlsAssetSyncSample> output)
    {
        if(sampleIdBase<0||sampleIdBase>int.MaxValue-_sequences.Length||weights.IsEmpty||output.Length<weights.Length)
            throw new ArgumentException("Invalid Sync sample output layout.");
        var total=0f;
        for(var i=0;i<weights.Length;i++)
        {
            var w=weights[i];
            if((uint)w.Sample>=(uint)_sequences.Length||!float.IsFinite(w.Weight)||w.Weight<=0||w.Weight>1)
                throw new ArgumentException("Invalid Sync sample weight.");
            for(var j=0;j<i;j++)if(weights[j].Sample==w.Sample)throw new ArgumentException("Duplicate Sync sample.");
            total+=w.Weight;
        }
        if(MathF.Abs(total-1)>1e-5f)throw new ArgumentException("Unnormalized Sync sample weights.");
        for(var i=0;i<weights.Length;i++)
        {
            var w=weights[i];output[i]=new(sampleIdBase+w.Sample,_sequences[w.Sample],w.Weight,_rates[w.Sample],_rates[w.Sample]);
        }
        return weights.Length;
    }
}

/// <summary>One frozen original source identity/marker table. Sequence IDs precede
/// BlendSpace IDs, both sorted by asset path; they are not legacy V4 IDs or player IDs.</summary>
public sealed class AlsRefactoredSyncBank
{
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly AlsAssetSyncMarker[] _markers;
    private readonly string[] _symbols;
    public string CatalogDigest { get; }
    public IReadOnlyDictionary<string,AlsRefactoredSyncAsset> Assets { get; }
    public IReadOnlyDictionary<string,AlsRefactoredSyncBlendSpace> BlendSpaces { get; }
    public ReadOnlySpan<AlsAssetSyncSequence> Sequences=>_sequences;
    public ReadOnlySpan<AlsAssetSyncMarker> Markers=>_markers;
    public ReadOnlySpan<string> Symbols=>_symbols;
    public AlsRefactoredSyncBank(string json,AlsRefactoredAnimationCatalog catalog)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32()==1&&root.GetProperty("catalogSha256").GetString()!.Equals(catalog.IndexDigest,StringComparison.OrdinalIgnoreCase),"Foreign Sync catalog.");
        CatalogDigest=catalog.IndexDigest;
        var rows=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var row in root.GetProperty("assets").EnumerateArray())Require(rows.TryAdd(Text(row,"source"),row),"Duplicate Sync resource.");
        Require(rows.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(catalog.Assets.Values.Where(a=>a.Class is "AnimSequence" or "BlendSpace" or "BlendSpace1D").Select(a=>a.Source)),"Incomplete original Sync resource closure.");
        var sequencePaths=rows.Keys.Where(p=>catalog.Assets[p].Class=="AnimSequence").Order(StringComparer.Ordinal).ToArray();
        var names=sequencePaths.SelectMany(p=>rows[p].GetProperty("markers").EnumerateArray().Select(m=>Text(m,"name"))).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        Require(names.Length<=63&&names.All(n=>!string.IsNullOrWhiteSpace(n)&&!n.Equals("None",StringComparison.OrdinalIgnoreCase)),"Invalid Sync marker symbols.");
        _symbols=["",..names];var symbols=names.Select((n,i)=>(n,i)).ToDictionary(p=>p.n,p=>p.i+1,StringComparer.OrdinalIgnoreCase);
        var assets=new Dictionary<string,AlsRefactoredSyncAsset>(StringComparer.Ordinal);var sequences=new List<AlsAssetSyncSequence>();var markers=new List<AlsAssetSyncMarker>();
        ulong Mask(IEnumerable<string> values)
        {
            ulong mask=0;
            foreach(var name in values){Require(symbols.TryGetValue(name,out var id),"Unknown native Sync marker.");mask|=1UL<<id;}
            return mask;
        }
        foreach(var path in sequencePaths)
        {
            var row=rows[path];var source=catalog.Read(path);var length=Number(row,"length");var rate=Number(row,"rateScale");
            Require(length==source.GetProperty("evaluation").GetProperty("sequencePlayLength").GetSingle()&&length>0&&rate>0,"Unsupported original Sync duration/rate.");
            var native=Text(source,"nativeText").Replace("\r","");
            var rateText=Regex.Match(native,@"(?m)^   RateScale=([-0-9.]+)$");
            var authoredRate=rateText.Success?float.Parse(rateText.Groups[1].Value,CultureInfo.InvariantCulture):1;
            Require(MathF.Abs(rate-authoredRate)<=.00000051f,"Source rate differs from native asset.");
            var authored=Regex.Matches(native,@"(?m)^   AuthoredSyncMarkers\((\d+)\)=([^\n]+)$");
            var track=row.GetProperty("markers").EnumerateArray().ToArray();Require(track.Length==authored.Count,"Original marker count differs.");
            var start=markers.Count;var last=-1f;ulong mask=0;
            for(var i=0;i<track.Length;i++)
            {
                var marker=track[i];var time=Number(marker,"time");var name=Text(marker,"name");
                Require(marker.GetProperty("index").GetInt32()==i&&time>=last&&time>=0&&time<=length&&marker.GetProperty("track").GetInt32()>=0,"Invalid ordered marker track.");
                var text=authored[i].Groups[2].Value;var nt=Regex.Match(text,@"(?:\(|,)Time=([-0-9.]+)");var tr=Regex.Match(text,@"(?:\(|,)TrackIndex=(\d+)");
                var nativeTime=nt.Success?float.Parse(nt.Groups[1].Value,CultureInfo.InvariantCulture):0;
                Require(int.Parse(authored[i].Groups[1].Value,CultureInfo.InvariantCulture)==i&&text.Contains("MarkerName=\""+name+"\"",StringComparison.Ordinal)&&
                    MathF.Abs(nativeTime-time)<=.00000051f&&(tr.Success?int.Parse(tr.Groups[1].Value,CultureInfo.InvariantCulture):0)==marker.GetProperty("track").GetInt32(),"Marker differs from native asset text.");
                var symbol=symbols[name];markers.Add(new(symbol,time));mask|=1UL<<symbol;last=time;
            }
            Require(mask==Mask(row.GetProperty("markerNames").EnumerateArray().Select(v=>v.GetString()!)),"Native sequence marker-name cache differs.");
            var id=sequences.Count;sequences.Add(new(id,length,rate,start,markers.Count-start));assets.Add(path,new(id,id,mask));
        }
        var blends=new Dictionary<string,AlsRefactoredSyncBlendSpace>(StringComparer.Ordinal);
        foreach(var path in rows.Keys.Except(sequencePaths,StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var row=rows[path];var source=catalog.Read(path);var sampleRows=row.GetProperty("samples").EnumerateArray().ToArray();
            var authored=source.GetProperty("samples");Require(sampleRows.Length==authored.GetArrayLength()&&sampleRows.Length>0,"Invalid Sync blend sample closure.");
            var indices=new int[sampleRows.Length];var rates=new float[indices.Length];ulong union=0;
            var mask=Mask(row.GetProperty("markerNames").EnumerateArray().Select(v=>v.GetString()!));
            for(var i=0;i<indices.Length;i++)
            {
                var sample=sampleRows[i];var sequence=Text(sample,"sequence");
                Require(sequence==authored[i].GetProperty("sequence").GetString()&&!sample.GetProperty("mirror").GetBoolean()&&!sample.GetProperty("singleFrame").GetBoolean(),"Unsupported Sync sample policy.");
                var binding=assets[sequence];indices[i]=binding.SequenceIndex;rates[i]=Number(sample,"rateScale");
                Require(rates[i]==1&&(binding.MarkerMask==0||binding.MarkerMask==mask),"Unsupported original sample rate or marker pattern.");union|=binding.MarkerMask;
            }
            Require(union==mask&&!row.GetProperty("matchPhases").GetBoolean()&&!row.GetProperty("legacyLength").GetBoolean()&&
                row.GetProperty("allowMarkers").GetBoolean(),"Unsupported original Sync length/phase/marker policy.");
            var notify=row.GetProperty("notifyMode").GetInt32();Require(notify==1,"Unsupported original notify policy.");
            var id=assets.Count;var allow=row.GetProperty("allowMarkers").GetBoolean();
            blends.Add(path,new(id,mask,row.GetProperty("legacyLength").GetBoolean(),allow,(AlsBlendSpaceNotifyMode)notify,indices,rates));assets.Add(path,new(id,-1,mask));
        }
        _sequences=sequences.ToArray();_markers=markers.ToArray();Assets=new ReadOnlyDictionary<string,AlsRefactoredSyncAsset>(assets);
        BlendSpaces=new ReadOnlyDictionary<string,AlsRefactoredSyncBlendSpace>(blends);
    }
    private static string Text(JsonElement value,string field)=>value.GetProperty(field).GetString()??throw new ArgumentException("Missing Sync text.");
    private static float Number(JsonElement value,string field)
    {var result=value.GetProperty(field).GetSingle();Require(float.IsFinite(result),"Nonfinite Sync metadata.");return result;}
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}
