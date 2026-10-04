using System.Collections.Immutable;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraSkeletalFeedback(float Right,float Left,float Retarget,float Leg,float Weapon)
{
    public bool Finite=>float.IsFinite(Right)&&float.IsFinite(Left)&&float.IsFinite(Retarget)&&float.IsFinite(Leg)&&float.IsFinite(Weapon);
}
internal readonly record struct LyraSkeletalControlStorage(AlsLinearBoolBlend Bool,bool ClampInitialized,float ClampValue);
internal sealed record LyraSkeletalUpdateState(AlsLinearBoolBlend Root,AlsLinearBoolBlend Foot,
    float FootDelta,bool FootFirst,short FootCounter,ImmutableArray<float> Alphas,
    ImmutableArray<LyraSkeletalControlStorage> Controls=default);
internal readonly record struct LyraSkeletalUpdateInput(float Delta,short Counter,bool Visited,bool Initialize,
    bool EnableControlRig,bool UseFootPlacement,LyraSkeletalFeedback Feedback);
internal sealed record LyraSkeletalUpdateCandidate(LyraSkeletalUpdateInput Input,LyraSkeletalUpdateState Updated,
    double RightWeight,double LeftWeight);

// Graph updates only. The eventual FootPlacement solver must signal its actual
// successful Evaluate boundary; this host does not calculate a foot pose.
internal sealed class LyraSkeletalControlUpdateHost
{
    private readonly bool _disableHand;
    private static readonly int[] Nodes=[103,102,104,110,109,105,107,106];
    private LyraSkeletalUpdateCandidate? _pending;
    private bool _footEvaluated;
    public LyraSkeletalUpdateState State{get;private set;}=new(AlsLinearBoolBlend.Default,AlsLinearBoolBlend.Default,0,true,-1,[0,0,0,0,0,0,0,0],
        Enumerable.Repeat(new LyraSkeletalControlStorage(AlsLinearBoolBlend.Default,false,0),8).ToImmutableArray());
    public double RightWeight{get;private set;}=1;
    public double LeftWeight{get;private set;}=1;
    public LyraSkeletalControlUpdateHost(LyraMainLayerGraphCatalog graphs,string profile)
    {
        var defaults=graphs.Defaults(profile);var disable=defaults.GetProperty("DisableHandIK");
        if(disable.GetProperty("type").GetString()!="bool")throw new NotSupportedException("Changed hand IK disable type.");
        _disableHand=disable.GetProperty("value").GetBoolean();
        var graph=graphs.Graph(profile,LyraLayerHook.FullBody_SkeletalControls);
        if(graph.GetProperty("root").GetInt32()!=113||graph.GetProperty("nodes").GetArrayLength()!=12)
            throw new NotSupportedException("Changed SkeletalControls closure.");
        using var doc=System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/skeletal_update_v1_policy.json"));
        var policy=doc.RootElement;
        if(policy.GetProperty("stage").GetString()!="OriginalSkeletalControlUpdate"||policy.GetProperty("boolBlendTime").GetSingle()!=.2f||
            !System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(graph.GetRawText()),
                System.Text.Json.Nodes.JsonNode.Parse(policy.GetProperty("profiles").GetProperty(profile).GetProperty("settings").GetRawText())))
            throw new NotSupportedException("Changed original SkeletalControls policy.");
        foreach(var dep in policy.GetProperty("dependencies").EnumerateObject())
            if(dep.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+dep.Name)))
                throw new InvalidOperationException("Stale SkeletalControls policy.");
    }
    private static LyraSkeletalUpdateState InitializeNode(LyraSkeletalUpdateState state,int index)
    {
        var control=state.Controls[index];state=state with{Controls=state.Controls.SetItem(index,control with{Bool=control.Bool.Reinitialize(),ClampInitialized=false})};
        return index switch{2=>state with{Root=state.Root.Reinitialize()},5=>state with{Foot=state.Foot.Reinitialize(),FootFirst=true},_=>state};
    }
    internal bool InitializeSourceNode(int node)
    {
        var index=Array.IndexOf(Nodes,node);if(index<0)return false;
        if(_pending is not null)throw new InvalidOperationException("SkeletalControls initialization needs an idle layer.");
        State=InitializeNode(State,index);return true;
    }
    public LyraSkeletalUpdateCandidate Prepare(in LyraSkeletalUpdateInput input,LyraLinkedWorkerState? worker=null)
    {
        if(_pending is not null)throw new InvalidOperationException("SkeletalControls update is pending.");
        if(!float.IsFinite(input.Delta)||input.Delta<0||input.Counter==-1&&input.Visited||!input.Feedback.Finite)
            throw new ArgumentException("Invalid SkeletalControls update input.");
        var state=State;var feedback=input.Feedback;double hand=_disableHand?0:1;
        var right=worker?.RightHand??Math.Clamp(hand-(double)feedback.Right,0d,1d);
        var left=worker?.LeftHand??Math.Clamp(hand-(double)feedback.Left,0d,1d);
        if(input.Initialize)for(var index=0;index<8;index++)state=InitializeNode(state,index);
        if(input.Visited)
        {
            var root=state.Root.Step(input.EnableControlRig,input.Delta);
            var foot=state.Foot.Step(input.UseFootPlacement&&feedback.Leg<=0,input.Delta);
            state=state with{Root=root,Foot=foot,Alphas=[Math.Clamp(1f-feedback.Retarget,0,1),1,root.Value,
                (float)right,(float)left,foot.Value,Math.Clamp(1f-feedback.Leg,0,1),Math.Clamp(feedback.Weapon*100f,0,1)]};
            var controls=state.Controls.ToBuilder();
            for(var index=0;index<8;index++)controls[index]=index switch
            {2=>controls[index] with{Bool=root},5=>controls[index] with{Bool=foot},_=>controls[index] with{ClampInitialized=true}};
            state=state with{Controls=controls.ToImmutable()};
            if(foot.Value>AlsPoseBlender.WeightThreshold)
            {
                var next=unchecked((short)(state.FootCounter+1));if(next==-1)next=0;
                var stale=!state.FootFirst&&state.FootCounter!=-1&&state.FootCounter!=input.Counter&&next!=input.Counter;
                state=state with{FootFirst=stale||state.FootFirst,FootCounter=input.Counter,FootDelta=state.FootDelta+input.Delta};
            }
        }
        _footEvaluated=false;return _pending=new(input,state,right,left);
    }
    public void Validate(LyraSkeletalUpdateCandidate candidate)
    {if(!ReferenceEquals(candidate,_pending))throw new InvalidOperationException("Foreign or stale SkeletalControls update.");}
    public void CompleteFootEvaluation(LyraSkeletalUpdateCandidate candidate)
    {
        Validate(candidate);
        if(_footEvaluated||!candidate.Input.Visited||candidate.Updated.Alphas[5]<=AlsPoseBlender.WeightThreshold)
            throw new InvalidOperationException("No pending FootPlacement evaluation.");
        _footEvaluated=true;
    }
    public LyraSkeletalUpdateState PreparedState(LyraSkeletalUpdateCandidate candidate)
    {Validate(candidate);return _footEvaluated?candidate.Updated with{FootDelta=0,FootFirst=false}:candidate.Updated;}
    public void Commit(LyraSkeletalUpdateCandidate candidate)
    {State=PreparedState(candidate);RightWeight=candidate.RightWeight;LeftWeight=candidate.LeftWeight;_pending=null;_footEvaluated=false;}
    public void Cancel(){_pending=null;_footEvaluated=false;}
}
