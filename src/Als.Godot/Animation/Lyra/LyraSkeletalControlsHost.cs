using System.Text.Json;
using System.Collections.Immutable;
using GodotAls.Core.Locomotion;
namespace GodotAls.Animation.Lyra;

internal sealed record LyraSkeletalControlsCandidate(LyraSkeletalUpdateCandidate Update,AlsLyraSkeletalHistory Updated);
internal readonly struct LyraSkeletalControlsPoseView
{
    private readonly LyraSkeletalControlsHost _owner;private readonly LyraSkeletalControlsCandidate _candidate;
    internal LyraSkeletalControlsPoseView(LyraSkeletalControlsHost owner,LyraSkeletalControlsCandidate candidate){_owner=owner;_candidate=candidate;}
    public ReadOnlySpan<AlsPrecisePose> Pose=>_owner.Pose(_candidate);
    public ReadOnlySpan<LyraCurveSample> Curves=>_owner.Curves(_candidate);
    public ReadOnlySpan<LyraAttributeSample> Attributes=>_owner.Attributes(_candidate);
    public LyraRootMotionAttribute RootMotion=>_owner.RootMotion(_candidate);
}

// Complete layer candidate: updates, Foot/Leg histories and full data channels
// share one owner. The enclosing ItemAnimLayers instance controls publication.
internal sealed class LyraSkeletalControlsHost
{
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraSkeletalControlUpdateHost _update;
    private readonly AlsLyraSkeletalControls _solver;
    private readonly AlsPrecisePose[] _pose=new AlsPrecisePose[81];
    private readonly LyraCurveSample[] _curves;private readonly LyraAttributeSample[] _attributes;
    private LyraRootMotionAttribute _root;private LyraSkeletalControlsCandidate? _pending;private bool _evaluated,_failed,_footDone;
    private AlsLyraSkeletalHistory _prepared=AlsLyraSkeletalHistory.Default;
    public AlsLyraSkeletalHistory History{get;private set;}=AlsLyraSkeletalHistory.Default;
    public LyraSkeletalUpdateState UpdateState=>_update.State;
    public double RightWeight=>_update.RightWeight;
    public double LeftWeight=>_update.LeftWeight;
    internal IReadOnlyDictionary<int,ImmutableArray<int>> BoneBindings=>_solver.BoneBindings;
    internal ReadOnlySpan<float> FootLengths=>_solver.FootLengths;
    public AlsLyraSkeletalHistory PreparedHistory{get{if(_pending is null||_failed)throw new InvalidOperationException("No SkeletalControls candidate.");return _prepared;}}
    public LyraSkeletalUpdateState PreparedUpdate(LyraSkeletalControlsCandidate c){Validate(c);return _update.PreparedState(c.Update);}
    public LyraSkeletalControlsHost(LyraLogicalSourceBank bank,LyraMainLayerGraphCatalog graphs,string profile,bool deferBoneCache=false)
    {
        _bank=bank;_update=new(graphs,profile);const string root="res://assets/generated/lyra_als/";
        using var doc=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"skeletal_controls_v1_policy.json"));var p=doc.RootElement;
        if(p.GetProperty("stage").GetString()!="OriginalSkeletalControls"||p.GetProperty("skeleton").GetString()!="ALS81"||
            !p.GetProperty("definedInitialStorage").GetBoolean()||p.GetProperty("production").GetBoolean())throw new NotSupportedException("Changed SkeletalControls policy.");
        foreach(var d in p.GetProperty("dependencies").EnumerateObject())if(d.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)))throw new InvalidOperationException("Stale SkeletalControls dependency.");
        var graph=graphs.Graph(profile,LyraLayerHook.FullBody_SkeletalControls);
        if(!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(graph.GetRawText()),System.Text.Json.Nodes.JsonNode.Parse(p.GetProperty("profiles").GetProperty(profile).GetProperty("settings").GetRawText())))throw new NotSupportedException("Changed SkeletalControls closure.");
        var fk=graphs.Defaults(profile).GetProperty("Hand FKWeight").GetProperty("value").GetSingle();
        if(fk!=p.GetProperty("profiles").GetProperty(profile).GetProperty("handFKWeight").GetSingle())throw new NotSupportedException("Changed HandFKWeight.");
        _solver=new(bank.Parents,bank.Reference,bank.Bone,fk,deferBoneCache);
        _curves=new LyraCurveSample[bank.Curves.Names.Length];_attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
    }
    internal bool InitializeSourceNode(int node)
    {
        if(_pending is not null)throw new InvalidOperationException("SkeletalControls initialization needs an idle layer.");
        if(!_update.InitializeSourceNode(node))return false;
        if(node==105)History=History with{Foot=History.Foot.ResetInterpolation()};
        _prepared=History;return true;
    }
    internal bool CacheSourceBones(int node)
    {
        if(_pending is not null)throw new InvalidOperationException("SkeletalControls CacheBones needs an idle layer.");
        return _solver.CacheBoneReferences(node);
    }
    public LyraSkeletalControlsCandidate Prepare(in LyraSkeletalUpdateInput input,LyraLinkedWorkerState? worker=null)
    {
        if(_pending is not null)throw new InvalidOperationException("SkeletalControls is pending.");
        try
        {
            var c=_update.Prepare(input,worker);var foot=AlsLyraFootPlacement.Update(History.Foot,input.Visited,input.Initialize,c.Updated.Alphas[5],input.Delta,input.Counter);
            _prepared=History with{Foot=foot};_evaluated=_failed=_footDone=false;return _pending=new(c,_prepared);
        }
        catch{Cancel();throw;}
    }
    private void Validate(LyraSkeletalControlsCandidate c)
    {if(!ReferenceEquals(c,_pending)||_failed)throw new InvalidOperationException("Foreign, stale or failed SkeletalControls candidate.");_update.Validate(c.Update);}
    public LyraSkeletalControlsPoseView Evaluate(LyraSkeletalControlsCandidate c,in LyraLayerPoseInput input,in AlsFootCharacterInput character,IAlsFootGroundQuery ground)
    {
        Validate(c);_evaluated=false;
        try
        {
            if(!c.Update.Input.Visited||!ReferenceEquals(input.Layout,_bank)||input.Curves.Length!=_curves.Length||input.Attributes.Length!=_attributes.Length||
                input.Curves.Overlaps(_curves)||input.Attributes.Overlaps(_attributes))throw new ArgumentException("Invalid SkeletalControls input owner/channels.");
            LyraPoseBuffers.Validate(input.Pose,_pose);
            _prepared=_solver.Evaluate(input.Pose,c.Update.Updated.Alphas.AsSpan(),character,ground,c.Updated,_pose);
            if(!_footDone&&c.Update.Updated.Alphas[5]>AlsPoseBlender.WeightThreshold){_update.CompleteFootEvaluation(c.Update);_footDone=true;}
            input.Curves.CopyTo(_curves);input.Attributes.CopyTo(_attributes);_root=input.RootMotion;_evaluated=true;return new(this,c);
        }
        catch{_failed=true;throw;}
    }
    public void ValidateCommit(LyraSkeletalControlsCandidate c,bool updateOnly=false)
    {
        Validate(c);if(c.Update.Input.Visited&&!updateOnly&&!_evaluated)throw new InvalidOperationException("SkeletalControls needs its current pose.");
        var u=_update.PreparedState(c.Update);var f=_prepared.Foot;
        if(u.FootDelta!=f.Delta||u.FootFirst!=f.First||u.FootCounter!=f.Counter)throw new InvalidOperationException("SkeletalControls Foot history is inconsistent.");
    }
    public void Commit(LyraSkeletalControlsCandidate c,bool updateOnly=false)
    {ValidateCommit(c,updateOnly);_update.Commit(c.Update);History=_prepared;_pending=null;_evaluated=false;}
    public void Cancel(){_update.Cancel();_pending=null;_evaluated=_failed=_footDone=false;_prepared=History;}
    private void Output(LyraSkeletalControlsCandidate c){Validate(c);if(!_evaluated)throw new InvalidOperationException("No current SkeletalControls output.");}
    internal ReadOnlySpan<AlsPrecisePose> Pose(LyraSkeletalControlsCandidate c){Output(c);return _pose;}
    internal ReadOnlySpan<LyraCurveSample> Curves(LyraSkeletalControlsCandidate c){Output(c);return _curves;}
    internal ReadOnlySpan<LyraAttributeSample> Attributes(LyraSkeletalControlsCandidate c){Output(c);return _attributes;}
    internal LyraRootMotionAttribute RootMotion(LyraSkeletalControlsCandidate c){Output(c);return _root;}
}
