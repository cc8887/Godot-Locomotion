using System.Text.Json;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// One immutable sequence/marker/root address space for all ten original
// locomotion entry points. Captured poses are not loaded by this resource.
internal sealed class LyraLocomotionResourceCatalog : IDisposable
{
    private const string Root="res://assets/generated/lyra_als/";
    private readonly JsonDocument _document;
    private readonly Dictionary<string,int> _ids;
    public LyraLogicalSourceBank Bank {get;}
    private LyraNotifyCatalog? _notifies;
    public LyraNotifyCatalog Notifies=>_notifies??=new(this);
    private LyraContextEffectsCatalog? _contextEffects;
    public LyraContextEffectsCatalog ContextEffects=>_contextEffects??=new();
    public string[] Paths {get;}
    public string[] Slots {get;}
    public ulong[] Masks {get;}
    public AlsAssetSyncSequence[] Sequences {get;}
    public AlsAssetSyncMarker[] Markers {get;}
    public int LeanBase {get;}
    public int RecoveryBase {get;}
    public int AimBase {get;}
    private readonly string[] _recoveryPaths;
    private readonly string[] _aimPaths=[];
    public int AimId(string profile,int sample)=>sample is >=0 and <15?AimBase+(profile switch{"unarmed"=>0,"pistol"=>15,"rifle"=>30,_=>throw new ArgumentException("Unknown AimOffset profile.")})+sample:throw new ArgumentException("Invalid AimOffset sample.");
    public int RecoveryId(string profile)=>RecoveryBase+(profile switch { "unarmed"=>0,"pistol"=>1,"rifle"=>2,_=>throw new ArgumentException("Unknown recovery profile.") });
    public LyraCompressedRootBank Roots {get;}
    public string Sha256 {get;}
    public JsonElement Provider(string profile)=>_document.RootElement.GetProperty("providers").GetProperty(profile);
    public int Id(string path)=>_ids.TryGetValue(path,out var id)?id:throw new InvalidOperationException("Unbound locomotion asset: "+path);
    public string Path(int id)=>id<0?"":id>=AimBase?_aimPaths[id-AimBase]:id>=RecoveryBase?_recoveryPaths[id-RecoveryBase]:Paths[id];
    public LyraLocomotionResourceCatalog(bool includeMontageActions=false)
    {
        Bank=LyraLogicalSourceBank.Load(includeMainLean:true,includeLocomotionExtras:true,includeMontageActions:includeMontageActions);
        var bytes=Godot.FileAccess.GetFileAsBytes(Root+"locomotion_resources.json");Sha256=LyraLogicalSourceBank.Sha(bytes);_document=JsonDocument.Parse(bytes);
        var data=_document.RootElement;
        if(data.GetProperty("schemaVersion").GetInt32()!=1 || !data.GetProperty("groups").EnumerateArray().Select(r=>r.GetString()).SequenceEqual(new[]{"Locomotion","Stop","Test"}))
            throw new InvalidOperationException("Changed locomotion resource contract.");
        foreach(var dependency in data.GetProperty("dependencies").EnumerateObject())
            if(dependency.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dependency.Name)))
                throw new InvalidOperationException("Stale locomotion resource dependency: "+dependency.Name);
        var assets=data.GetProperty("assets").EnumerateArray().ToArray();Paths=assets.Select(r=>r.GetProperty("path").GetString()!).ToArray();
        _ids=Paths.Select((p,i)=>(p,i)).ToDictionary(p=>p.p,p=>p.i,StringComparer.Ordinal);
        var sourceSlots=Bank.Slots.ToDictionary(s=>Bank.Get(s).Data.Identity.AssetPath,s=>s,StringComparer.Ordinal);
        Slots=Paths.Select(p=>sourceSlots[p]).ToArray();
        var symbols=assets.SelectMany(r=>r.GetProperty("markers").EnumerateArray()).Select(m=>m.GetProperty("name").GetString()!).Distinct().Order(StringComparer.Ordinal).ToArray();
        if(symbols.Length>=64)throw new NotSupportedException("Locomotion marker capacity.");
        Masks=new ulong[assets.Length+6+45];var sequences=new List<AlsAssetSyncSequence>();var markers=new List<AlsAssetSyncMarker>();
        for(var id=0;id<assets.Length;id++)
        {
            var row=assets[id];var ms=row.GetProperty("markers").EnumerateArray().ToArray();
            sequences.Add(new(id,row.GetProperty("length").GetSingle(),row.GetProperty("rateScale").GetSingle(),markers.Count,ms.Length));
            foreach(var marker in ms)
            {var symbol=Array.IndexOf(symbols,marker.GetProperty("name").GetString()!)+1;Masks[id]|=1UL<<symbol;markers.Add(new(symbol,marker.GetProperty("time").GetSingle()));}
        }
        LeanBase=sequences.Count;
        foreach(var slot in new[]{"main_lean_center","main_lean_left","main_lean_right"})
        {var d=Bank.Get(slot).Data;sequences.Add(new(d.Identity.AnimationId,(float)d.PlayLength,1,0,0));}
        RecoveryBase=sequences.Count;
        using var playback=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"locomotion_extras/playback.json"));
        if(playback.RootElement.GetProperty("schemaVersion").GetInt32()!=1 || playback.RootElement.GetProperty("catalogSha256").GetString()!=
            LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"locomotion_extras/catalog.json")))throw new InvalidOperationException("Stale recovery playback catalog.");
        _recoveryPaths=new string[3];
        foreach(var (profile,n) in new[]{"unarmed","pistol","rifle"}.Select((p,n)=>(p,n)))
        {
            var slot=profile+"_jump_recovery_additive";var d=Bank.Get(slot);
            var row=playback.RootElement.GetProperty("entries").EnumerateArray().Single(r=>r.GetProperty("slot").GetString()==slot).GetProperty("targetSync");
            if(!d.IsAdditive || d.MeshSpaceAdditive || row.GetProperty("markers").GetArrayLength()!=0 || row.GetProperty("length").GetSingle()!=(float)d.Data.PlayLength)
                throw new NotSupportedException("Changed ALS81 recovery binding.");
            _recoveryPaths[n]=d.Data.Identity.AssetPath;_ids.Add(_recoveryPaths[n],RecoveryBase+n);
            sequences.Add(new(RecoveryBase+n,row.GetProperty("length").GetSingle(),row.GetProperty("rateScale").GetSingle(),markers.Count,0));
        }
        AimBase=sequences.Count;
        // Append a stable suffix; never renumber the existing absolute, Lean
        // and Recovery sources or rewrite their hashed native fixtures.
        if(Godot.FileAccess.FileExists(Root+"aiming_layer_v1_policy.json"))
        {
            using var aim=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"aiming_layer_v1_policy.json"));
            if(aim.RootElement.GetProperty("schemaVersion").GetInt32()!=1 || aim.RootElement.GetProperty("stage").GetString()!="OriginalFullBody_Aiming")
                throw new InvalidOperationException("Wrong Aiming resource policy.");
            foreach(var dep in aim.RootElement.GetProperty("dependencies").EnumerateObject())
                if(dep.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name)))throw new InvalidOperationException("Stale Aiming resource dependency.");
            var paths=new List<string>();
            foreach(var profile in new[]{"unarmed","pistol","rifle"})
            {
                var rows=aim.RootElement.GetProperty("spaces").EnumerateArray().Single(r=>r.GetProperty("profile").GetString()==profile).GetProperty("samples");
                if(rows.GetArrayLength()!=15)throw new InvalidOperationException("Incomplete AimOffset resource closure.");
                for(var sample=0;sample<15;sample++)
                {
                    var r=rows[sample];var d=Bank.Get($"aim_{profile}_{sample}");var sync=r.GetProperty("sync");
                    if(!d.IsAdditive || !d.MeshSpaceAdditive || d.EnableRootMotion || r.GetProperty("slot").GetString()!=d.Slot ||
                        r.GetProperty("source").GetString()!=d.Source || r.GetProperty("target").GetString()!=d.Data.Identity.AssetPath ||
                        sync.GetProperty("length").GetSingle()!=(float)d.Data.PlayLength || sync.GetProperty("markers").GetArrayLength()!=0)
                        throw new InvalidOperationException("Changed AimOffset sample binding.");
                    var id=AimBase+paths.Count;paths.Add(d.Data.Identity.AssetPath);_ids.Add(d.Data.Identity.AssetPath,id);
                    sequences.Add(new(100000+id,sync.GetProperty("length").GetSingle(),sync.GetProperty("rateScale").GetSingle(),markers.Count,0));
                }
            }
            _aimPaths=paths.ToArray();
        }
        Sequences=sequences.ToArray();Markers=markers.ToArray();Roots=LyraCompressedRootBank.Load(data.GetProperty("compressedRoots"),Bank);
    }
    public void Dispose(){Roots.Dispose();_document.Dispose();}
}
