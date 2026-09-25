using System.Globalization;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Original Standing state-entry calls. These are worker queue writes,
/// not main-thread StopQuick notifications; the host retains their traversal order.</summary>
public sealed class AlsRefactoredStandingActions
{
    private readonly AlsRefactoredStandingResources _standing;
    private readonly AlsRefactoredStopResources _stop;
    private readonly AlsSequenceMontageAsset[] _assets;
    private readonly AlsSequenceMontageCommand[] _commands;
    public ReadOnlySpan<AlsSequenceMontageAsset> Assets=>_assets;
    public string CatalogDigest=>_standing.CatalogDigest;
    public string SourcePath(int animationId)
    {
        var side=Array.FindIndex(_assets,a=>a.AnimationId==animationId);if(side<0)throw new ArgumentException("Unknown Stop animation ID.");
        var name=side==0?"Left":"Right";return $"/ALS/ALS/Animations/Transitions/A_Als_Stop_{name}.A_Als_Stop_{name}";
    }
    public AlsRefactoredStandingActions(AlsRefactoredAnimationCatalog catalog,AlsRefactoredStandingResources standing,
        AlsRefactoredStopResources stop,AlsRefactoredRestMontages rest,int leftId,int rightId)
    {
        if(catalog.IndexDigest!=standing.CatalogDigest || catalog.IndexDigest!=stop.CatalogDigest || catalog.IndexDigest!=rest.Settings.CatalogDigest ||
            leftId<0 || rightId<0 || leftId==rightId || rest.Assets.ToArray().Any(a=>a.AnimationId==leftId||a.AnimationId==rightId))
            throw new ArgumentException("Foreign Standing action resources/IDs.");
        _standing=standing;_stop=stop;_assets=new AlsSequenceMontageAsset[2];_commands=new AlsSequenceMontageCommand[2];
        var blueprint=AlsRefactoredRotatePlayers.Blueprint(false);var text=catalog.Read(blueprint).GetProperty("nativeText").GetString()!;
        for(var side=0;side<2;side++)
        {
            var name=side==0?"Left":"Right";var sequence=$"/ALS/ALS/Animations/Transitions/A_Als_Stop_{name}.A_Als_Stop_{name}";
            ValidateFunction(AlsNativeNestedGraph.Extract(text,blueprint,blueprint+":PlayStop"+name+"TransitionAnimation"),"PlayStop"+name+"TransitionAnimation",sequence);
            var source=catalog.Read(sequence);var evaluation=source.GetProperty("evaluation");
            Require(source.GetProperty("raw").GetProperty("skeletonSource").GetString()=="/ALS/ALS/Character/SK_Als.SK_Als" &&
                !evaluation.GetProperty("enableRootMotion").GetBoolean() && evaluation.GetProperty("additiveType").GetString()=="AAT_RotationOffsetMeshSpace",
                "Unsupported Stop sequence policy.");
            var rate=Regex.Match(source.GetProperty("nativeText").GetString()!,@"(?m)^   RateScale=([^\r\n]+)");
            Require(!rate.Success||float.Parse(rate.Groups[1].Value,CultureInfo.InvariantCulture)==1,"Unsupported Stop sequence rate.");
            var duration=evaluation.GetProperty("sequencePlayLength").GetSingle();Require(duration>.4f,"Stop sequence cannot reach its start time.");
            var id=side==0?leftId:rightId;
            _assets[side]=new(id,AlsMontageSlot.Transition,rest.HostGroupId,duration,2);
            _commands[side]=new(id,AlsMontageSlot.Transition,1.5f,.4f,.2f,.2f);
        }
        ValidateFunction(AlsNativeNestedGraph.Extract(text,blueprint,blueprint+":StopTransitionAndTurnInPlaceAnimations"),"StopTransitionAndTurnInPlaceAnimations",null);
    }
    internal static void ValidateFunction(string text,string function,string? sequence)
    {
        var graph=new Graph(text,true);var entry=graph.Nodes.Single(n=>n.Kind=="K2Node_FunctionEntry");
        Require(entry.Body.Contains("MemberName=\""+function+"\"",StringComparison.Ordinal),"Wrong Standing action entry.");
        var member=sequence is null?"StopTransitionAndTurnInPlaceAnimations":"PlayTransitionAnimation";
        var call=graph.Nodes.Single(n=>n.Kind=="K2Node_CallFunction"&&n.Member==member);graph.Function(call,member,"ALS.AlsAnimationInstance");
        graph.Links(call,"execute",(entry,"then"));
        var then=entry.Pins.Values.Single(p=>p.Name=="then"&&p.Output);
        var target=call.Pins.Values.Single(p=>p.Name=="execute");
        Require(then.Links==call.Name+" "+target.Id+",","Unexpected Standing action continuation.");
        Require(call.Pins.Values.Single(p=>p.Name=="then").Links=="","Unexpected Standing action tail.");
        var (parent,pin)=graph.Follow(call,"self");graph.Self(parent);
        Require(parent.Kind=="K2Node_CallFunction"&&parent.Member=="GetParent"&&pin.Name=="ReturnValue","Wrong Standing action receiver.");
        float Number(string key)=>float.Parse(graph.Literal(call,key),CultureInfo.InvariantCulture);
        if(sequence is null)Require(Number("BlendOutDuration")==-1,"Original stop duration differs.");
        else
        {
            var asset=call.Pins.Values.Single(p=>p.Name=="Sequence");
            var line=Regex.Matches(call.Body,@"CustomProperties Pin [^\r\n]+").Single(m=>m.Value.Contains("PinId="+asset.Id+",",StringComparison.Ordinal)).Value;
            Require(!asset.Output&&asset.Links==""&&Regex.Match(line,"(?:^|,)DefaultObject=\"([^\"]*)\"").Groups[1].Value==sequence,"Wrong Stop animation.");
            Require(Number("BlendInDuration")==.2f&&Number("BlendOutDuration")==.2f&&Number("PlayRate")==1.5f&&Number("StartTime")==.4f&&
                graph.Literal(call,"bFromStandingIdleOnly")=="false","Original Stop playback parameters differ.");
        }
        Require(graph.Nodes.All(n=>ReferenceEquals(n,entry)||ReferenceEquals(n,call)||ReferenceEquals(n,parent)||n.Kind=="EdGraphNode_Comment"&&n.Pins.Count==0),"Unconsumed Standing action graph.");
    }
    public bool QueueStopState(AlsTransitionQueueRuntime queue,AlsMontageRuntime bank,in AlsFrameIdentity identity,AlsRefactoredStopRuntime machine)
    {
        queue.ValidateBank(bank,identity);machine.ValidateCommit(identity.FrameId);
        if(!ReferenceEquals(machine.Resources,_stop))throw new ArgumentException("Foreign Stop callback owner.");
        if(machine.StateCallback is not {} callback)return false;
        var side=callback.State is 1 or 3?0:1;
        Require(callback.Entry&&callback.Function==_stop.States[callback.State].EntryFunction,"Wrong Stop callback.");
        var expected=_assets[side];
        if(!bank.TryGetSequenceAsset(expected.AnimationId,expected.Slot,out var actual)||actual!=expected)throw new ArgumentException("Stop animation missing from shared bank.");
        return queue.QueuePlay(_commands[side],"Als.Stance.Standing",false);
    }
    public int QueueStanding(AlsTransitionQueueRuntime queue,in AlsFrameIdentity identity,AlsRefactoredStandingRuntime machine)
    {
        queue.ValidateCommit(identity);machine.ValidateCommit(identity.FrameId);
        if(!ReferenceEquals(machine.Resources,_standing))throw new ArgumentException("Foreign Standing callback owner.");
        foreach(var callback in machine.StateCallbacks)
            Require((callback.Entry&&callback.State==1||!callback.Entry&&callback.State==0)&&callback.Function=="StopTransitionAndTurnInPlaceAnimations","Unimplemented Standing action callback.");
        foreach(var callback in machine.StateCallbacks)queue.QueueStop();
        return machine.StateCallbacks.Length;
    }
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}
