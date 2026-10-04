using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal enum LyraAssetNotifyKind { ContextEffects, FootPlantLeft, FootPlantRight, TransitionToLocomotion,
    PlaySound, PlayWeaponMontage, Reload, Melee, MotionWarping, EmoteSound, Named }
internal sealed record LyraAssetNotify(int Index,int Asset,int LocalIndex,string Name,string ObjectPath,
    string ClassPath,LyraAssetNotifyKind Kind,float Duration,JsonElement Payload);
internal readonly record struct LyraNotifyAsset(int Index,string Path,float Length,int Offset,int Count);

// Shared authored definitions. No player clocks, random state or lifecycle.
// Original and target notify objects remain different identities; target
// aliases reuse the same actual target object's policy range.
internal sealed class LyraNotifyCatalog
{
    private const string Root="res://assets/generated/lyra_als/";
    private readonly AlsAssetNotifyDefinition[] _definitions;
    private readonly AlsAssetNotifyPolicy[] _policies;
    private readonly LyraAssetNotify[] _events;
    private readonly LyraNotifyAsset[] _sequences;
    private readonly Dictionary<string,LyraNotifyAsset> _assets;
    private readonly Dictionary<string,AlsBlendSpaceNotifyMode> _modes;
    public string Sha256 {get;}
    public ReadOnlySpan<AlsAssetNotifyDefinition> Definitions=>_definitions;
    public ReadOnlySpan<AlsAssetNotifyPolicy> Policies=>_policies;
    public ImmutableArray<LyraNotifyAsset> Assets {get;}
    public LyraAssetNotify Event(int index)=>(uint)index<_events.Length?_events[index]:throw new ArgumentOutOfRangeException(nameof(index));
    public LyraNotifyAsset Asset(string path)=>_assets.TryGetValue(path,out var a)?a:throw new InvalidOperationException("Unbound notify asset: "+path);
    public LyraNotifyAsset Sequence(int index)=>(uint)index<_sequences.Length?_sequences[index]:throw new ArgumentOutOfRangeException(nameof(index));
    public AlsBlendSpaceNotifyMode Mode(string path)=>_modes.TryGetValue(path,out var m)?m:throw new InvalidOperationException("Unbound BlendSpace notify mode: "+path);
    public LyraNotifyCatalog(LyraLocomotionResourceCatalog resources)
    {
        var bytes=Godot.FileAccess.GetFileAsBytes(Root+"notify_contract_v1.json");Sha256=LyraLogicalSourceBank.Sha(bytes);
        using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;
        using var modes=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"notify_source_modes_v1.json"));
        if(root.GetProperty("schemaVersion").GetInt32()!=1||modes.RootElement.GetProperty("schemaVersion").GetInt32()!=1)
            throw new InvalidOperationException("Unsupported Lyra notify contract.");
        foreach(var data in new[]{root,modes.RootElement})foreach(var dep in data.GetProperty("dependencies").EnumerateObject())
            if(LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name))!=dep.Value.GetString())
                throw new InvalidOperationException("Stale notify dependency: "+dep.Name);
        _modes=modes.RootElement.GetProperty("spaces").EnumerateArray().ToDictionary(
            r=>r.GetProperty("source").GetString()!,r=>(AlsBlendSpaceNotifyMode)r.GetProperty("notifyMode").GetByte(),StringComparer.Ordinal);
        if(_modes.Count!=4||_modes.Values.Any(m=>!Enum.IsDefined(m)))throw new InvalidOperationException("Incomplete notify BlendSpace policies.");
        var rows=root.GetProperty("assets").EnumerateArray().Concat(root.GetProperty("targets").EnumerateArray())
            .OrderBy(r=>r.GetProperty("source").GetString(),StringComparer.Ordinal).ToArray();
        var names=rows.SelectMany(r=>r.GetProperty("events").EnumerateArray()).Select(e=>e.GetProperty("name").GetString()!)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select((s,i)=>(s,i)).ToDictionary(v=>v.s,v=>v.i,StringComparer.Ordinal);
        var objects=new Dictionary<string,int>(StringComparer.Ordinal);var definitions=new List<AlsAssetNotifyDefinition>();
        var policies=new List<AlsAssetNotifyPolicy>();var events=new List<LyraAssetNotify>();_assets=new(StringComparer.Ordinal);
        int ObjectId(string path){if(path=="")return -1;if(!objects.TryGetValue(path,out var i)){i=objects.Count;objects.Add(path,i);}return i;}
        foreach(var (r,index) in rows.Select((r,i)=>(r,i)))
        {
            var path=r.GetProperty("source").GetString()!;var length=r.GetProperty("length").GetSingle();var offset=definitions.Count;
            if(!float.IsFinite(length)||length<=0)throw new InvalidOperationException("Invalid notify asset length.");
            foreach(var e in r.GetProperty("events").EnumerateArray())
            {
                var local=e.GetProperty("index").GetInt32();var id=definitions.Count;var name=e.GetProperty("name").GetString()!;
                var notify=e.GetProperty("notifyObject").GetString()!;var state=e.GetProperty("stateObject").GetString()!;
                var cls=(state!=""?e.GetProperty("notifyStateClass"):e.GetProperty("notifyClass")).GetString()!;
                var obj=state!=""?state:notify;var flags=e.GetProperty("stateBehaviorFlags").GetByte();
                var time=e.GetProperty("triggerTime").GetSingle();var end=e.GetProperty("endTriggerTime").GetSingle();
                var duration=e.GetProperty("duration").GetSingle();var tick=(AlsTimelineTickMode)e.GetProperty("tickMode").GetByte();
                if(local!=id-offset||!float.IsFinite(time)||!float.IsFinite(end)||!float.IsFinite(duration)||duration<0||
                    notify!=""&&state!=""||obj!=""&&!obj.StartsWith(path+":",StringComparison.Ordinal)||flags>1||
                    tick!=AlsTimelineTickMode.Queued||e.GetProperty("branchingPoint").GetBoolean())
                    throw new NotSupportedException("Changed original Lyra notify event: "+path);
                var kind=Kind(cls,name);
                policies.Add(new(id,index,e.GetProperty("track").GetInt32(),ObjectId(notify),ObjectId(state),names[name],
                    e.GetProperty("triggerWeightThreshold").GetSingle(),e.GetProperty("triggerChance").GetSingle(),
                    (AlsAssetNotifyFilterType)e.GetProperty("filterType").GetByte(),e.GetProperty("filterLod").GetInt32(),tick,
                    e.GetProperty("canBeFilteredViaRequest").GetBoolean(),e.GetProperty("triggerOnDedicatedServer").GetBoolean(),
                    e.GetProperty("triggerOnFollower").GetBoolean(),flags));
                definitions.Add(new(id,time,end));events.Add(new(id,index,local,name,obj,cls,kind,duration,e.GetProperty("payload").Clone()));
            }
            _assets.Add(path,new(index,path,length,offset,definitions.Count-offset));
        }
        if(_assets.Count!=644||events.Count!=2975||objects.Count!=2969)throw new InvalidOperationException("Incomplete original/target notify identities.");
        _definitions=definitions.ToArray();_policies=policies.ToArray();_events=events.ToArray();Assets=_assets.Values.ToImmutableArray();
        _sequences=new LyraNotifyAsset[resources.Sequences.Length];
        for(var i=0;i<_sequences.Length;i++)
        {
            var path=i<resources.LeanBase?resources.Paths[i]:i<resources.RecoveryBase?
                resources.Bank.Get(new[]{"main_lean_center","main_lean_left","main_lean_right"}[i-resources.LeanBase]).Data.Identity.AssetPath:resources.Path(i);
            var asset=Asset(path);
            if(asset.Length!=resources.Sequences[i].DurationSeconds)throw new InvalidOperationException("Foreign notify sequence duration: "+path);
            _sequences[i]=asset;
        }
    }
    private static LyraAssetNotifyKind Kind(string cls,string name)=>cls switch
    {
        "/Script/LyraGame.AnimNotify_LyraContextEffects"=>LyraAssetNotifyKind.ContextEffects,
        "/Game/Effects/AnimationNotifies/AN_FootPlant_Left.AN_FootPlant_Left_C"=>LyraAssetNotifyKind.FootPlantLeft,
        "/Game/Effects/AnimationNotifies/AN_FootPlant_Right.AN_FootPlant_Right_C"=>LyraAssetNotifyKind.FootPlantRight,
        "/Game/Characters/Heroes/Mannequin/Animations/AnimNotifies/TransitionToLocomotion.TransitionToLocomotion_C"=>LyraAssetNotifyKind.TransitionToLocomotion,
        "/Script/Engine.AnimNotify_PlaySound"=>LyraAssetNotifyKind.PlaySound,
        "/Game/Characters/Heroes/Mannequin/Animations/AnimNotifies/AN_PlayWeaponMontage.AN_PlayWeaponMontage_C"=>LyraAssetNotifyKind.PlayWeaponMontage,
        "/Game/Characters/Heroes/Abilities/AN_Reload.AN_Reload_C"=>LyraAssetNotifyKind.Reload,
        "/Game/Characters/Heroes/Abilities/AN_Melee.AN_Melee_C"=>LyraAssetNotifyKind.Melee,
        "/Script/MotionWarping.AnimNotifyState_MotionWarping"=>LyraAssetNotifyKind.MotionWarping,
        "/Game/Audio/Sounds/Emotes/ANS_EmoteSound.ANS_EmoteSound_C"=>LyraAssetNotifyKind.EmoteSound,
        "" when name is "SaveAttack" or "ResetCombo"=>LyraAssetNotifyKind.Named,
        _=>throw new NotSupportedException("Unbound Lyra notify class: "+cls)
    };
}
