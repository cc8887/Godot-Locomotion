using System.Text.Json;
using GodotAls.Core.Locomotion;
namespace GodotAls.Animation.Lyra;

internal sealed record LyraFootPlacementCandidate(bool Visited,float Alpha,AlsFootPlacementHistory Updated);
internal readonly struct LyraFootPlacementPoseView
{
    private readonly LyraFootPlacementNodeHost _owner;private readonly LyraFootPlacementCandidate _candidate;
    internal LyraFootPlacementPoseView(LyraFootPlacementNodeHost owner,LyraFootPlacementCandidate candidate){_owner=owner;_candidate=candidate;}
    public ReadOnlySpan<AlsPrecisePose> Pose=>_owner.Pose(_candidate);
    public ReadOnlySpan<LyraCurveSample> Curves=>_owner.Curves(_candidate);
    public ReadOnlySpan<LyraAttributeSample> Attributes=>_owner.Attributes(_candidate);
    public LyraRootMotionAttribute RootMotion=>_owner.RootMotion(_candidate);
}
internal sealed class LyraFootPlacementNodeHost
{
    private readonly LyraLogicalSourceBank _bank;
    private readonly AlsLyraFootPlacement _solver;
    private readonly AlsPrecisePose[] _pose=new AlsPrecisePose[81];
    private readonly LyraCurveSample[] _curves;private readonly LyraAttributeSample[] _attributes;
    private LyraRootMotionAttribute _root;private LyraFootPlacementCandidate? _pending;private bool _evaluated,_failed;
    private AlsFootPlacementHistory _prepared=AlsFootPlacementHistory.Default;
    public AlsFootPlacementHistory History{get;private set;}=AlsFootPlacementHistory.Default;
    public AlsFootPlacementHistory PreparedHistory{get{if(_pending is null||_failed)throw new InvalidOperationException("No FootPlacement candidate.");return _prepared;}}
    public LyraFootPlacementNodeHost(LyraLogicalSourceBank bank,LyraMainLayerGraphCatalog graphs,string profile)
    {
        _bank=bank;const string root="res://assets/generated/lyra_als/";
        using var doc=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"foot_placement_v2_policy.json"));var p=doc.RootElement;
        if(p.GetProperty("stage").GetString()!="OriginalFootPlacement"||p.GetProperty("skeleton").GetString()!="ALS81"||
            p.GetProperty("lockType").GetString()!="Unlocked"||p.GetProperty("manualSpeedFallback").GetSingle()!=60||!p.GetProperty("definedInitialStorage").GetBoolean())
            throw new NotSupportedException("Changed FootPlacement policy.");
        foreach(var dep in p.GetProperty("dependencies").EnumerateObject())
            if(dep.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+dep.Name)))throw new InvalidOperationException("Stale FootPlacement dependency.");
        var settings=graphs.Graph(profile,LyraLayerHook.FullBody_SkeletalControls).GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("index").GetInt32()==105).GetProperty("settings");
        if(!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(settings.GetRawText()),System.Text.Json.Nodes.JsonNode.Parse(p.GetProperty("profiles").GetProperty(profile).GetProperty("settings").GetRawText())))
            throw new NotSupportedException("Changed FootPlacement node.");
        int Bone(JsonElement v,string name)=>bank.Bone(v.GetProperty(name).GetProperty("boneName").GetString()!);
        var legs=settings.GetProperty("legDefinitions");
        _solver=new(bank.Parents,bank.Reference,Bone(settings,"pelvisBone"),Bone(settings,"iKFootRootBone"),
            Bone(legs[0],"fKFootBone"),Bone(legs[0],"iKFootBone"),Bone(legs[0],"ballBone"),Bone(legs[1],"fKFootBone"),Bone(legs[1],"iKFootBone"),Bone(legs[1],"ballBone"));
        _curves=new LyraCurveSample[bank.Curves.Names.Length];_attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
    }
    public LyraFootPlacementCandidate Prepare(bool visited,bool initialize,float alpha,float delta,short counter)
    {
        if(_pending is not null)throw new InvalidOperationException("FootPlacement is pending.");
        _prepared=AlsLyraFootPlacement.Update(History,visited,initialize,alpha,delta,counter);_evaluated=_failed=false;
        return _pending=new(visited,alpha,_prepared);
    }
    private void Validate(LyraFootPlacementCandidate c){if(!ReferenceEquals(c,_pending)||_failed)throw new InvalidOperationException("Foreign, stale or failed FootPlacement candidate.");}
    public LyraFootPlacementPoseView Evaluate(LyraFootPlacementCandidate c,in LyraLayerPoseInput input,in AlsFootCharacterInput character,IAlsFootGroundQuery ground)
    {
        Validate(c);_evaluated=false;
        try
        {
            if(!c.Visited||!ReferenceEquals(input.Layout,_bank)||input.Curves.Length!=_curves.Length||input.Attributes.Length!=_attributes.Length||
                input.Curves.Overlaps(_curves)||input.Attributes.Overlaps(_attributes))throw new ArgumentException("Invalid FootPlacement input owner or channels.");
            LyraPoseBuffers.Validate(input.Pose,_pose);_prepared=_solver.Evaluate(input.Pose,c.Alpha,character,ground,c.Updated,_pose);
            input.Curves.CopyTo(_curves);input.Attributes.CopyTo(_attributes);_root=input.RootMotion;_evaluated=true;return new(this,c);
        }
        catch{_failed=true;throw;}
    }
    public void ValidateCommit(LyraFootPlacementCandidate c,bool updateOnly=false)
    {Validate(c);if(c.Visited&&!updateOnly&&!_evaluated)throw new InvalidOperationException("FootPlacement needs its current pose.");}
    public void Commit(LyraFootPlacementCandidate c,bool updateOnly=false){ValidateCommit(c,updateOnly);History=_prepared;_pending=null;_evaluated=false;}
    public void Cancel(){_pending=null;_evaluated=_failed=false;_prepared=History;}
    private void Output(LyraFootPlacementCandidate c){Validate(c);if(!_evaluated)throw new InvalidOperationException("No current FootPlacement output.");}
    internal ReadOnlySpan<AlsPrecisePose> Pose(LyraFootPlacementCandidate c){Output(c);return _pose;}
    internal ReadOnlySpan<LyraCurveSample> Curves(LyraFootPlacementCandidate c){Output(c);return _curves;}
    internal ReadOnlySpan<LyraAttributeSample> Attributes(LyraFootPlacementCandidate c){Output(c);return _attributes;}
    internal LyraRootMotionAttribute RootMotion(LyraFootPlacementCandidate c){Output(c);return _root;}
}
