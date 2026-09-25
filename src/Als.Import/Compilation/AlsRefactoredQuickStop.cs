using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredQuickStopInput(bool VelocityDirection,bool Crouching,bool HasInput,
    double InputYaw,double TargetYaw,double ActorYaw);

/// <summary>Original StopQuick notification and Parent settings. Dispatch is a
/// main-thread candidate operation, after worker PostUpdate, using the shared queue.</summary>
public sealed class AlsRefactoredQuickStop
{
    private readonly AlsRefactoredStandingResources _standing;
    private readonly AlsSequenceMontageAsset[] _assets=new AlsSequenceMontageAsset[4];
    private readonly string[] _paths=new string[4];
    public string CatalogDigest=>_standing.CatalogDigest;
    public ReadOnlySpan<AlsSequenceMontageAsset> Assets=>_assets;
    public float BlendIn {get;}
    public float BlendOut {get;}
    public float StartTime {get;}
    public float MinRate {get;}
    public float MaxRate {get;}
    public string SourcePath(int id)
    {
        var index=Array.FindIndex(_assets,a=>a.AnimationId==id);
        if(index<0)throw new ArgumentException("Unknown QuickStop animation.");
        return _paths[index];
    }
    public AlsRefactoredQuickStop(string json,AlsRefactoredAnimationCatalog catalog,AlsRefactoredStandingResources standing,
        AlsRefactoredRestMontages rest,IReadOnlyDictionary<string,int> ids)
    {
        if(catalog.IndexDigest!=standing.CatalogDigest||catalog.IndexDigest!=rest.Settings.CatalogDigest)throw new ArgumentException("Foreign QuickStop catalog.");
        _standing=standing;using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        Expect(root,new{schemaVersion=1,source=AlsRefactoredMovementSettings.Source,animationClass="/ALS/ALS/Character/AB_Als.AB_Als_C"});
        if(!string.Equals(root.GetProperty("catalogSha256").GetString(),CatalogDigest,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Foreign QuickStop settings.");
        BlendIn=Number(root.GetProperty("blendIn"));BlendOut=Number(root.GetProperty("blendOut"));StartTime=Number(root.GetProperty("startTime"));
        var rates=root.GetProperty("playRate");if(rates.GetArrayLength()!=2)throw new ArgumentException("Invalid QuickStop rate range.");
        MinRate=Number(rates[0]);MaxRate=Number(rates[1]);if(MinRate<=0||MaxRate<MinRate)throw new ArgumentException("Invalid QuickStop rate range.");
        var native=catalog.Read(AlsRefactoredMovementSettings.Source).GetProperty("nativeText").GetString()!;
        var settings=Regex.Match(native,@"(?m)^   Transitions=([^\r\n]+)").Value;
        if(ids.Count!=4||ids.Values.Any(id=>id<0)||ids.Values.Distinct().Count()!=4)throw new ArgumentException("Invalid QuickStop resource IDs.");
        for(var i=0;i<4;i++)
        {
            var stance=i<2?"standing":"crouching";var side=i%2==0?"left":"right";
            var path=root.GetProperty("sequences").GetProperty(stance+"_"+side).GetString()!;
            var field=(i<2?"Standing":"Crouching")+(i%2==0?"Left":"Right")+"Sequence";
            if(!ids.TryGetValue(path,out var id)||!settings.Contains(field+"=\"/Script/Engine.AnimSequence'"+path+"'\"",StringComparison.Ordinal))throw new ArgumentException("Wrong QuickStop sequence binding.");
            var source=catalog.Read(path);var evaluation=source.GetProperty("evaluation");
            if(source.GetProperty("raw").GetProperty("skeletonSource").GetString()!="/ALS/ALS/Character/SK_Als.SK_Als"||evaluation.GetProperty("enableRootMotion").GetBoolean()||
                evaluation.GetProperty("additiveType").GetString()!="AAT_RotationOffsetMeshSpace")throw new ArgumentException("Unsupported QuickStop sequence policy.");
            var scale=Regex.Match(source.GetProperty("nativeText").GetString()!,@"(?m)^   RateScale=([^\r\n]+)");
            if(scale.Success&&float.Parse(scale.Groups[1].Value,CultureInfo.InvariantCulture)!=1)throw new ArgumentException("Unsupported QuickStop sequence rate.");
            var duration=evaluation.GetProperty("sequencePlayLength").GetSingle();if(duration<=StartTime)throw new ArgumentException("QuickStop start exceeds sequence.");
            _paths[i]=path;_assets[i]=new(id,AlsMontageSlot.Transition,rest.HostGroupId,duration,2);
        }
        var blueprint=AlsRefactoredRotatePlayers.Blueprint(false);var text=catalog.Read(blueprint).GetProperty("nativeText").GetString()!;
        var graph=new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(text,blueprint,blueprint+":EventGraph"),true);
        var entry=graph.Nodes.Single(n=>n.Kind=="K2Node_Event"&&n.Body.Contains("MemberName=\"AnimNotify_StopQuick\"",StringComparison.Ordinal));
        var call=graph.Nodes.Single(n=>n.Kind=="K2Node_CallFunction"&&n.Member=="PlayQuickStopAnimation");graph.Function(call,"PlayQuickStopAnimation","ALS.AlsAnimationInstance");
        graph.Links(call,"execute",(entry,"then"));var execute=call.Pins.Values.Single(p=>p.Name=="execute");
        if(entry.Pins.Values.Single(p=>p.Name=="then").Links!=call.Name+" "+execute.Id+","||call.Pins.Values.Single(p=>p.Name=="then").Links!="")throw new ArgumentException("QuickStop dispatch differs.");
        var (parent,pin)=graph.Follow(call,"self");graph.Self(parent);
        if(parent.Member!="GetParent"||pin.Name!="ReturnValue"||standing.Edges[4].StartNotify!=1)throw new ArgumentException("QuickStop receiver/notify differs.");
    }
    private static float Number(JsonElement element)
    {var value=element.GetSingle();if(!float.IsFinite(value)||value<0)throw new ArgumentException("Invalid QuickStop parameter.");return value;}
    public AlsSequenceMontageCommand Command(in AlsRefactoredQuickStopInput input)
    {
        if(!double.IsFinite(input.InputYaw)||!double.IsFinite(input.TargetYaw)||!double.IsFinite(input.ActorYaw))throw new ArgumentException("Invalid QuickStop yaw.");
        var left=true;var rate=MinRate;
        if(input.VelocityDirection)
        {
            var angle=(float)((input.HasInput?input.InputYaw:input.TargetYaw)-input.ActorYaw);
            if(!float.IsFinite(angle))throw new ArgumentException("QuickStop yaw difference exceeds float range.");
            while(angle>180){var next=angle-360;if(next==angle)throw new ArgumentException("QuickStop yaw cannot unwind.");angle=next;}
            while(angle< -180){var next=angle+360;if(next==angle)throw new ArgumentException("QuickStop yaw cannot unwind.");angle=next;}
            if(angle>175)angle-=360;
            left=angle<=0;rate=MinRate+(MaxRate-MinRate)*(MathF.Abs(angle)/180);
        }
        var asset=_assets[(input.Crouching?2:0)+(left?0:1)];return new(asset.AnimationId,asset.Slot,rate,StartTime,BlendIn,BlendOut);
    }
    public int Dispatch(AlsTransitionQueueRuntime queue,AlsMontageRuntime bank,in AlsFrameIdentity identity,
        AlsRefactoredStandingRuntime machine,in AlsRefactoredQuickStopInput input)
    {
        queue.ValidateBank(bank,identity);bank.ValidateCommit(identity);machine.ValidateCommit(identity.FrameId);
        if(!ReferenceEquals(machine.Resources,_standing))throw new ArgumentException("Foreign QuickStop machine.");
        var update=machine.Candidate;var count=0;
        for(var i=0;i<update.EventCount;i++)
        {
            var e=update.GetEvent(i);
            if(e.Kind!=AlsGroundedEventKind.TransitionStarted||e.SourceIndex!=4||e.NotifyIndex!=1)throw new ArgumentException("Unimplemented Standing notification.");
            count++;
        }
        if(count==0)return 0;var command=Command(input);var expected=_assets.Single(a=>a.AnimationId==command.AnimationId);
        if(!bank.TryGetSequenceAsset(expected.AnimationId,expected.Slot,out var actual)||actual!=expected)throw new ArgumentException("QuickStop resource missing from bank.");
        for(var i=0;i<count;i++)queue.PlayImmediate(command,"",false);
        return count;
    }
}
