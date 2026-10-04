using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Montage-only AnimInstance extraction uses its selected physical instance,
// first track and actual advance range; graph weights/attributes do not drive it.
internal sealed class LyraMontageMovementReader:IDisposable,IAlsMotionWarpingRootSource
{
    private readonly LyraCompressedRootBank _compressed;
    private readonly AlsRawRootMotionIntervalSampler[] _roots;
    private readonly int[] _firstSources;
    private bool _disposed;
    public bool UseControllerYaw {get;}
    public LyraMontageMovementReader(LyraLogicalSourceBank bank,LyraMontageCatalog catalog)
    {
        const string root="res://assets/generated/lyra_als/";
        using var actorDocument=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"root_movement_v1_actor_policy.json"));var actorPolicy=actorDocument.RootElement;
        UseControllerYaw=actorPolicy.GetProperty("useControllerRotationYaw").GetBoolean();
        if(actorPolicy.GetProperty("schemaVersion").GetInt32()!=1||actorPolicy.GetProperty("pawnClass").GetString()!="/ShooterCore/Game/B_Hero_ShooterMannequin.B_Hero_ShooterMannequin_C"||
            !UseControllerYaw||actorPolicy.GetProperty("allowPhysicsRotationDuringAnimRootMotion").GetBoolean())
            throw new NotSupportedException("Changed original Shooter root/control rotation policy.");
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"root_movement_v1_policy.json"));var policy=document.RootElement;
        foreach(var d in policy.GetProperty("dependencies").EnumerateObject())
            if(d.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)))throw new InvalidOperationException("Stale Montage movement policy.");
        if(policy.GetProperty("schemaVersion").GetInt32()!=1||policy.GetProperty("mode").GetInt32()!=3||
            !policy.GetProperty("firstTrackOnly").GetBoolean()||!policy.GetProperty("montageWeightIgnored").GetBoolean()||
            policy.GetProperty("providerAttributeDrivesCapsule").GetBoolean())throw new NotSupportedException("Unsupported Lyra root movement mode.");
        _firstSources=catalog.Definitions.Select(a=>a.AnimationId).ToArray();
        var bindings=catalog.BindSources(bank);var refs=new Dictionary<int,(AlsPrecisePose Reference,bool Normalized)>();
        var rows=policy.GetProperty("references").EnumerateArray().ToArray();if(rows.Length!=catalog.Definitions.Length)throw new InvalidOperationException("Incomplete root movement closure.");
        for(int i=0;i<rows.Length;i++)
        {
            var row=rows[i];var asset=catalog.Definitions[i];
            if(row.GetProperty("montage").GetString()!=catalog.Paths[i]||row.GetProperty("sequence").GetString()!=catalog.SequencePaths[asset.AnimationId]||
                row.GetProperty("enabled").GetBoolean()!=asset.RootMotionEnabled)throw new InvalidOperationException("Foreign Montage root source.");
            var value=(LyraLogicalSourceBank.ParsePose(row.GetProperty("reference")),row.GetProperty("normalizedScale").GetBoolean());
            if(refs.TryGetValue(asset.AnimationId,out var old)&&old!=value)throw new InvalidOperationException("Ambiguous first-track root reference.");
            refs[asset.AnimationId]=value;
        }
        using var data=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"montage_sampling_v1_roots.json"));_compressed=LyraCompressedRootBank.Load(data.RootElement,bank);
        _roots=bindings.Select((s,i)=>_compressed.CreateSampler(s,refs.TryGetValue(i,out var reference)?reference.Reference:bank.Reference[0],
            refs.TryGetValue(i,out reference)?reference.Normalized:bank.Get(s).NormalizedRootMotionScale)).ToArray();
    }
    public LyraRootMotionAttribute Read(in AlsMontageRootMotionRange range)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!range.HasMotion)return default;
        if((uint)range.AnimationId>=_roots.Length||range.StartSeconds==range.EndSeconds)throw new ArgumentException("Invalid physical root movement range.");
        return new(_roots[range.AnimationId].ExtractRange(range.StartSeconds,range.EndSeconds) with{Scale=AlsDoubleVector.One},true);
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_compressed.Dispose();}
    public static AlsMotionWarpingContext WarpContext(AlsMontageRuntime bank,float delta)
    {
        var owner=bank.CandidateRootMotionInstance;
        foreach(var instance in bank.Candidate)
            if(instance.InstanceId==owner)return new(instance.ActionDefinitionId,instance.DeltaTimeRecord.PreviousPosition,
                instance.Position,instance.Blend.CurrentWeight,instance.EffectivePlayRate,delta);
        return new(-1,0,0,0,1,delta);
    }
    public AlsPrecisePose ExtractRange(int montageAsset,float previous,float current)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if((uint)montageAsset>=_firstSources.Length)throw new ArgumentOutOfRangeException(nameof(montageAsset));
        return _roots[_firstSources[montageAsset]].ExtractRange(previous,current) with{Scale=AlsDoubleVector.One};
    }
}
