using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Resource layout and original Main node policy; all numerical history is Core.
internal sealed class LyraMainInertialization
{
    private readonly LyraLogicalSourceBank _bank;
    private readonly AlsPoseDataInertialization _runtime;
    internal AlsPoseDataInertialization Runtime => _runtime;
    public bool Active=>_runtime.Active;
    public float Elapsed=>_runtime.Elapsed;
    public float Duration=>_runtime.Duration;
    public float Deficit=>_runtime.Deficit;
    public int HistoryCount=>_runtime.HistoryCount;
    public float PendingDelta=>_runtime.PendingDelta;
    public float PendingRequest=>_runtime.PendingRequest;
    public LyraMainInertialization(LyraLogicalSourceBank bank)
    {
        _bank=bank;_runtime=new(bank.Reference.Length,bank.Curves.Names.Length);
        var graph=LyraMainLayerGraphCatalog.Load().MainGraph.GetProperty("nodes");
        var node=graph.EnumerateArray().Single(n=>n.GetProperty("index").GetInt32()==75);
        var settings=node.GetProperty("settings");
        if(node.GetProperty("type").GetString()!="/Script/Engine.AnimNode_Inertialization"||
            node.GetProperty("links")[0].GetProperty("index").GetInt32()!=84||
            settings.GetProperty("defaultBlendProfile").GetString()!=""||settings.GetProperty("filteredCurves").GetArrayLength()!=0||
            settings.GetProperty("filteredBones").GetArrayLength()!=0||settings.GetProperty("bResetOnBecomingRelevant").GetBoolean()||
            !settings.GetProperty("bForwardRequestsThroughSkippedCachedPoseNodes").GetBoolean()||settings.GetProperty("tag").GetString()!="None")
            throw new NotSupportedException("Changed original Main inertia policy.");
    }
    public void Reset()=>_runtime.Reset();
    public void CopyFrom(LyraMainInertialization source)
    {
        if(!ReferenceEquals(source._bank,_bank))throw new InvalidOperationException("Foreign Main inertia layout.");
        _runtime.CopyFrom(source._runtime);
    }
    public void Update(float delta)=>_runtime.Update(delta);
    public void Request(float duration)=>_runtime.Request(duration);
    public void Evaluate(in LyraLayerPoseInput input,in AlsPrecisePose component,long actorParent,float teleportDistance,LyraCompositionPoseBuffer output)
    {
        output.Validate(input);
        if(!ReferenceEquals(input.Layout,_bank))throw new ArgumentException("Invalid Main inertia layout.");
        _runtime.Evaluate(input.Pose,input.Curves,new(input.RootMotion.Value,input.RootMotion.Present),
            component,actorParent,teleportDistance,output.Pose,output.Curves,out var root);
        input.Attributes.CopyTo(output.Attributes);output.RootMotion=new(root.Value,root.Present);
    }
}
