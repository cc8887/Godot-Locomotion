using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraWeaponMontageCatalog
{
    public static readonly AlsMontageSlot DefaultSlot=new(0);
    public string Kind {get;}
    public string[] Names {get;}
    public int[] Parents {get;}
    public AlsPrecisePose[] Reference {get;}
    public int[] PhysicalBones {get;}
    public ImmutableArray<string> Paths {get;}
    public ImmutableArray<AlsAuthoredMontageAsset> Definitions {get;}
    public LyraWeaponSequenceSampler[] Samplers {get;}
    public LyraWeaponMontageCatalog(LyraWeaponResources resources,string kind)
    {
        Kind=kind;var mesh=resources.Mesh(kind);var skeleton=mesh.GetProperty("metadata");
        Names=skeleton.GetProperty("logicalBoneNames").EnumerateArray().Select(p=>p.GetString()!).ToArray();
        Parents=skeleton.GetProperty("logicalParents").EnumerateArray().Select(p=>p.GetInt32()).ToArray();
        Reference=skeleton.GetProperty("referencePose").EnumerateArray().Select(LyraLogicalSourceBank.ParsePose).ToArray();
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/weapon_montage_v1_policy.json"));
        var policy=document.RootElement;
        if(policy.GetProperty("schemaVersion").GetInt32()!=1||policy.GetProperty("dependencies").GetProperty("weapon_resources/catalog.json").GetString()!=resources.Sha256)
            throw new InvalidOperationException("Stale original weapon graph/mesh reference.");
        var graph=policy.GetProperty("definitions").GetProperty(kind);var nodes=graph.GetProperty("nodes").EnumerateArray().ToArray();
        var root=nodes.Single(n=>n.GetProperty("type").GetString()=="/Script/Engine.AnimNode_Root");
        var slot=nodes.Single(n=>n.GetProperty("type").GetString()=="/Script/AnimGraphRuntime.AnimNode_Slot");
        var reference=nodes.Single(n=>n.GetProperty("type").GetString()=="/Script/AnimGraphRuntime.AnimNode_RefPose");
        if(nodes.Length!=3||root.GetProperty("source").GetInt32()!=slot.GetProperty("index").GetInt32()||
            slot.GetProperty("source").GetInt32()!=reference.GetProperty("index").GetInt32()||
            slot.GetProperty("name").GetString()!="DefaultSlot"||slot.GetProperty("alwaysUpdate").GetBoolean()||
            reference.GetProperty("refPoseType").GetInt32()!=0||graph.GetProperty("rootMotionMode").GetInt32()!=3)
            throw new NotSupportedException("Changed original weapon AnimBP graph.");
        PhysicalBones=graph.GetProperty("names").EnumerateArray().Select(p=>Array.IndexOf(Names,p.GetString())).ToArray();
        var meshParents=graph.GetProperty("parents").EnumerateArray().Select(p=>p.GetInt32()).ToArray();
        var meshReference=graph.GetProperty("reference").EnumerateArray().Select(LyraLogicalSourceBank.ParsePose).ToArray();
        if(PhysicalBones.Length!=7||PhysicalBones.Distinct().Count()!=7||PhysicalBones.Any(b=>b<0)||meshReference.Length!=7||meshParents.Length!=7)
            throw new InvalidOperationException("Invalid original weapon mesh layout.");
        for(int b=0;b<PhysicalBones.Length;b++)
        {
            if(Parents[PhysicalBones[b]]!=(meshParents[b]<0?-1:PhysicalBones[meshParents[b]]))throw new InvalidOperationException("Changed weapon mesh parent.");
            Reference[PhysicalBones[b]]=meshReference[b];
        }
        var metadata=resources.Catalog.GetProperty("montages").EnumerateArray().Where(m=>m.GetProperty("skeleton").GetString()==mesh.GetProperty("skeleton").GetString()).ToArray();
        if(metadata.Length!=2)throw new InvalidOperationException("Incomplete original weapon Montage bank.");
        var definitions=ImmutableArray.CreateBuilder<AlsAuthoredMontageAsset>();var paths=ImmutableArray.CreateBuilder<string>();Samplers=new LyraWeaponSequenceSampler[2];
        for(int i=0;i<metadata.Length;i++)
        {
            var m=metadata[i];var tracks=m.GetProperty("slots");var sections=m.GetProperty("sections");
            if(m.GetProperty("group").GetString()!="DefaultGroup"||tracks.GetArrayLength()!=1||tracks[0].GetProperty("name").GetString()!="DefaultSlot"||
                tracks[0].GetProperty("group").GetString()!="DefaultGroup"||sections.GetArrayLength()!=1||sections[0].GetProperty("time").GetSingle()!=0||
                sections[0].GetProperty("next").GetString()!="None"||m.GetProperty("rootMotion").GetBoolean()||m.GetProperty("legacyRootTranslation").GetBoolean()||
                m.GetProperty("legacyRootRotation").GetBoolean()||m.GetProperty("syncGroup").GetString()!="None"||m.GetProperty("blendModeIn").GetInt32()!=0||
                m.GetProperty("blendModeOut").GetInt32()!=0||m.GetProperty("blendInProfile").GetString()!=""||m.GetProperty("blendOutProfile").GetString()!=""||
                m.GetProperty("blendInCurve").GetString()!=""||m.GetProperty("blendOutCurve").GetString()!=""||m.GetProperty("notifies").GetProperty("events").GetArrayLength()!=0)
                throw new NotSupportedException("Changed original weapon Montage shape.");
            var segments=tracks[0].GetProperty("segments");if(segments.GetArrayLength()!=1)throw new NotSupportedException("Multiple weapon segments.");
            var segment=segments[0];var path=segment.GetProperty("animation").GetString()!;Samplers[i]=resources.CreateSampler(path,Reference);
            if(!Samplers[i].Names.SequenceEqual(Names)||!Samplers[i].Parents.SequenceEqual(Parents)||segment.GetProperty("start").GetSingle()!=0||
                segment.GetProperty("loops").GetInt32()!=1||segment.GetProperty("additiveType").GetInt32()!=0||
                Samplers[i].Resource.GetProperty("metadata").GetProperty("floatCurveNames").GetArrayLength()!=0||
                Samplers[i].Resource.GetProperty("metadata").GetProperty("animatedBoneAttributeCount").GetInt32()!=0)
                throw new NotSupportedException("Changed weapon source channels.");
            var lifecycle=new AlsActionLifecycleSettings(m.GetProperty("autoBlendOut").GetBoolean()?AlsActionLifecycleMode.MontageAutoBlendOut:AlsActionLifecycleMode.MontageHoldAtEnd,
                m.GetProperty("blendInTime").GetSingle(),(AlsActionBlendOption)m.GetProperty("blendInOption").GetInt32(),
                m.GetProperty("blendOutTime").GetSingle(),(AlsActionBlendOption)m.GetProperty("blendOutOption").GetInt32(),m.GetProperty("blendOutTriggerTime").GetSingle());
            definitions.Add(new(i,i,DefaultSlot,0,m.GetProperty("duration").GetSingle(),segment.GetProperty("clipStart").GetSingle(),segment.GetProperty("clipRate").GetSingle(),lifecycle,false,i)
                {RateScale=m.GetProperty("rateScale").GetSingle(),ClipEnd=segment.GetProperty("clipEnd").GetSingle()});
            paths.Add(m.GetProperty("path").GetString()!);
        }
        Definitions=definitions.ToImmutable();Paths=paths.ToImmutable();
    }
}

internal sealed class LyraWeaponMontageCandidate
{
    internal LyraWeaponMontageBank Owner {get;}
    public AlsFrameIdentity Identity {get;}
    internal ulong Serial {get;}
    private readonly AlsPrecisePose[] _pose;
    public ReadOnlySpan<AlsPrecisePose> Pose=>_pose;
    public ImmutableArray<AlsMontageEvaluation> Evaluations {get;}
    public AlsSlotWeights Weights {get;}
    internal LyraWeaponMontageCandidate(LyraWeaponMontageBank owner,AlsFrameIdentity id,ulong serial,AlsPrecisePose[] pose,
        ImmutableArray<AlsMontageEvaluation> evaluations,AlsSlotWeights weights)
    {Owner=owner;Identity=id;Serial=serial;_pose=pose;Evaluations=evaluations;Weights=weights;}
}

// Independent weapon AnimInstance. Its DefaultSlot and physical clocks never
// share the character's five-slot bank, ALS81 pose or Linked Layer epoch.
internal sealed class LyraWeaponMontageBank:IAlsMontagePoseSource,IDisposable
{
    private readonly AlsMontageRuntime _runtime;
    private readonly AlsMontageSlotPose _slot;
    private readonly uint _character,_generation;
    private LyraWeaponMontageCandidate? _candidate;
    private bool _retired;
    public LyraWeaponMontageCatalog Catalog {get;}
    public LyraWeaponMontageBank(LyraWeaponMontageCatalog catalog,uint character,uint generation)
    {
        if(generation==0)throw new ArgumentOutOfRangeException(nameof(generation));
        Catalog=catalog;_character=character;_generation=generation;_runtime=new([],catalog.Definitions.AsSpan());
        _slot=new(catalog.Reference,catalog.Parents,0,AlsMontageWeightNormalization.IndividualDivision);
    }
    public LyraWeaponMontageCandidate Prepare(AlsFrameIdentity identity,float delta,bool evaluate=true,
        ReadOnlySpan<AlsMontageActionRequest> beforeAdvance=default)
    {
        Live();if(_candidate is not null||identity.CharacterId!=_character||identity.SlotGeneration!=_generation)throw new InvalidOperationException("Foreign or unfinished weapon frame.");
        _runtime.BeginWithActionRequests(identity,delta,beforeAdvance);
        try
        {
            var pose=evaluate?new AlsPrecisePose[Catalog.Names.Length]:[];
            if(evaluate)_slot.Evaluate(_runtime.Frame,identity,LyraWeaponMontageCatalog.DefaultSlot,Catalog.Reference,[],pose,[],this);
            return _candidate=new(this,identity,_runtime.PreparationSerial,pose,_runtime.Evaluation.ToArray().ToImmutableArray(),_runtime.Frame.SlotWeights(LyraWeaponMontageCatalog.DefaultSlot));
        }
        catch{_runtime.Discard();throw;}
    }
    public void Play(LyraWeaponMontageCandidate candidate,string path,float rate,float start=0,bool stopGroup=true)
    {
        Validate(candidate);int asset=Catalog.Paths.IndexOf(path);
        if(asset<0||!_runtime.PlayAction(asset,rate,start,stopGroup))throw new InvalidOperationException("Unbound original weapon Montage.");
    }
    public void Stop(LyraWeaponMontageCandidate candidate,string path,float blend)
    {
        Validate(candidate);int asset=Catalog.Paths.IndexOf(path);if(asset<0)throw new InvalidOperationException("Foreign weapon Montage stop.");
        var instance=_runtime.Candidate.ToArray().LastOrDefault(i=>i.MontageId==asset&&i.OwnsActiveActionLookup);
        if(instance.InstanceId>0)_runtime.StopInstance(instance.InstanceId,blend,Catalog.Definitions[asset].Lifecycle.BlendOutOption);
    }
    public void Validate(LyraWeaponMontageCandidate candidate)
    {
        Live();if(!ReferenceEquals(candidate.Owner,this)||!ReferenceEquals(candidate,_candidate)||candidate.Serial!=_runtime.PreparationSerial)
            throw new InvalidOperationException("Retired, cancelled or foreign weapon candidate.");
        _runtime.ValidateCommit(candidate.Identity);
        if(!candidate.Evaluations.AsSpan().SequenceEqual(_runtime.Evaluation))throw new InvalidOperationException("Weapon request modified frozen pose data.");
    }
    public void Commit(LyraWeaponMontageCandidate candidate){Validate(candidate);_runtime.Commit(candidate.Identity);_candidate=null;}
    public void Cancel(){_runtime.Discard();_candidate=null;}
    public void Sample(in AlsMontageEvaluation entry,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
    {
        if(!curves.IsEmpty||entry.Slot!=LyraWeaponMontageCatalog.DefaultSlot||entry.AdditiveType!=0||(uint)entry.AnimationId>=Catalog.Samplers.Length)
            throw new InvalidOperationException("Foreign weapon source request.");
        Catalog.Samplers[entry.AnimationId].Sample(entry.Position,pose);
    }
    private void Live(){ObjectDisposedException.ThrowIf(_retired,this);}
    public void Dispose(){if(_retired)return;Cancel();_runtime.ClearForLifecycle();_retired=true;}
}
