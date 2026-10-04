using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraLegIkCandidate(bool Visited,float Alpha,bool Recache);
internal readonly struct LyraLegIkPoseView
{
    private readonly LyraLegIkNodeHost _owner;
    private readonly LyraLegIkCandidate _candidate;
    internal LyraLegIkPoseView(LyraLegIkNodeHost owner,LyraLegIkCandidate candidate){_owner=owner;_candidate=candidate;}
    public ReadOnlySpan<AlsPrecisePose> Pose=>_owner.Pose(_candidate);
    public ReadOnlySpan<LyraCurveSample> Curves=>_owner.Curves(_candidate);
    public ReadOnlySpan<LyraAttributeSample> Attributes=>_owner.Attributes(_candidate);
    public LyraRootMotionAttribute RootMotion=>_owner.RootMotion(_candidate);
}

// Per-occurrence LegIK Evaluate history. No animation clocks, source ticks or
// skeleton publication. The enclosing graph supplies its actual resolved alpha.
internal sealed class LyraLegIkNodeHost
{
    private readonly LyraLogicalSourceBank _bank;
    private readonly AlsLegIkController _controller;
    private readonly AlsPrecisePose[] _pose=new AlsPrecisePose[81];
    private readonly LyraCurveSample[] _curves;
    private readonly LyraAttributeSample[] _attributes;
    private LyraRootMotionAttribute _root;
    private LyraLegIkCandidate? _pending;
    private bool _evaluated,_failed;
    private ImmutableArray<AlsLegIkBendHistory> _prepared;
    public ImmutableArray<AlsLegIkBendHistory> History {get;private set;}=[default,default];
    public ImmutableArray<AlsLegIkBendHistory> PreparedHistory
    {get{if(_pending is null||!_evaluated||_failed)throw new InvalidOperationException("LegIK has no current evaluation history.");return _prepared;}}
    public LyraLegIkNodeHost(LyraLogicalSourceBank bank,LyraMainLayerGraphCatalog graphs,string profile)
    {
        _bank=bank;const string root="res://assets/generated/lyra_als/";
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"leg_ik_v1_policy.json"));var policy=document.RootElement;
        if(policy.GetProperty("schemaVersion").GetInt32()!=1||policy.GetProperty("stage").GetString()!="OriginalLegIK"||
            policy.GetProperty("skeleton").GetString()!="ALS81"||!policy.GetProperty("defaultTwoBoneSolver").GetBoolean()||
            policy.GetProperty("forceAlwaysSolve").GetBoolean()||!policy.GetProperty("twistCurveAbsent").GetBoolean())throw new NotSupportedException("Changed LegIK policy.");
        foreach(var dep in policy.GetProperty("dependencies").EnumerateObject())
            if(dep.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+dep.Name)))throw new InvalidOperationException("Stale LegIK policy.");
        var settings=graphs.Graph(profile,LyraLayerHook.FullBody_SkeletalControls).GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("index").GetInt32()==107).GetProperty("settings");
        var authored=policy.GetProperty("profiles").GetProperty(profile).GetProperty("settings");
        if(!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(settings.GetRawText()),System.Text.Json.Nodes.JsonNode.Parse(authored.GetRawText()))||settings.GetProperty("reachPrecision").GetSingle()!=.01f||
            settings.GetProperty("softPercentLength").GetSingle()!=1||settings.GetProperty("softAlpha").GetSingle()!=1||
            settings.GetProperty("alphaCurveName").GetString()!="DisableLegIK")throw new NotSupportedException("Changed original LegIK node.");
        var definitions=settings.GetProperty("legsDefinition").EnumerateArray().Select(l=>
        {
            if(l.GetProperty("numBonesInLimb").GetInt32()!=2||l.GetProperty("bEnableRotationLimit").GetBoolean()||
                !l.GetProperty("bEnableKneeTwistCorrection").GetBoolean()||l.GetProperty("footBoneForwardAxis").GetString()!="Y"||
                l.GetProperty("hingeRotationAxis").GetString()!="Z"||l.GetProperty("twistOffsetCurveName").GetString()!="None")throw new NotSupportedException("Unsupported LegIK limb.");
            return new AlsLegIkDefinition(bank.Bone(l.GetProperty("fKFootBone").GetProperty("boneName").GetString()!),
                bank.Bone(l.GetProperty("iKFootBone").GetProperty("boneName").GetString()!));
        }).ToArray();
        if(definitions.Length!=2)throw new NotSupportedException("Changed LegIK limb count.");
        _controller=new(bank.Parents,definitions,settings.GetProperty("reachPrecision").GetSingle());
        _curves=new LyraCurveSample[bank.Curves.Names.Length];_attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
    }
    public LyraLegIkCandidate Prepare(bool visited,float alpha,bool recache=false)
    {
        if(_pending is not null)throw new InvalidOperationException("LegIK frame is pending.");
        if(!float.IsFinite(alpha)||alpha is <0 or >1)throw new ArgumentException("Invalid LegIK alpha.");
        _evaluated=_failed=false;_prepared=History;
        // Original CacheBones retains the bend directions by FK foot identity.
        return _pending=new(visited,alpha,recache);
    }
    private void Validate(LyraLegIkCandidate c)
    {if(!ReferenceEquals(c,_pending)||_failed)throw new InvalidOperationException("Foreign, stale or failed LegIK frame.");}
    public LyraLegIkPoseView Evaluate(LyraLegIkCandidate c,in LyraLayerPoseInput input)
    {
        Validate(c);_evaluated=false;
        try
        {
            if(!c.Visited||!ReferenceEquals(input.Layout,_bank)||input.Curves.Length!=_curves.Length||input.Attributes.Length!=_attributes.Length||
                input.Curves.Overlaps(_curves)||input.Attributes.Overlaps(_attributes))throw new ArgumentException("Foreign or aliased LegIK input.");
            LyraPoseBuffers.Validate(input.Pose,_pose);
            _prepared=_controller.Evaluate(input.Pose,c.Alpha,History,_pose);
            input.Curves.CopyTo(_curves);input.Attributes.CopyTo(_attributes);_root=input.RootMotion;
            _evaluated=true;return new(this,c);
        }
        catch{_failed=true;throw;}
    }
    public void ValidateCommit(LyraLegIkCandidate c,bool updateOnly=false)
    {Validate(c);if(c.Visited&&!updateOnly&&!_evaluated)throw new InvalidOperationException("LegIK needs its current pose.");}
    public void Commit(LyraLegIkCandidate c,bool updateOnly=false)
    {ValidateCommit(c,updateOnly);if(_evaluated)History=_prepared;_pending=null;_evaluated=false;}
    public void Cancel(){_pending=null;_evaluated=_failed=false;_prepared=History;}
    private void Output(LyraLegIkCandidate c){Validate(c);if(!_evaluated)throw new InvalidOperationException("LegIK has no current output.");}
    internal ReadOnlySpan<AlsPrecisePose> Pose(LyraLegIkCandidate c){Output(c);return _pose;}
    internal ReadOnlySpan<LyraCurveSample> Curves(LyraLegIkCandidate c){Output(c);return _curves;}
    internal ReadOnlySpan<LyraAttributeSample> Attributes(LyraLegIkCandidate c){Output(c);return _attributes;}
    internal LyraRootMotionAttribute RootMotion(LyraLegIkCandidate c){Output(c);return _root;}
}
