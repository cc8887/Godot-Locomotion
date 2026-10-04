using System.Collections.Immutable;
using Godot;
using GodotAls.Core.Animation;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraDefaultLayerRoutesSmoke:Node
{
    public override void _Ready()
    {
        try{Run();GetTree().Quit();}
        catch(Exception error){GD.PushError("Default layer routes failed: "+error);GetTree().Quit(1);}
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Reject(Action action)
    {
        try{action();}catch(InvalidOperationException){return;}catch(ArgumentException){return;}
        throw new InvalidOperationException("Invalid call-site operation was accepted.");
    }
    private static LyraLinkedLayerContracts Layout(string name)
    {
        var c=LyraLinkedLayerContracts.Load();if(name=="single")return c;
        return c.WithFunctionGroups(Enum.GetValues<LyraLayerHook>().ToDictionary(h=>h,h=>name=="per-call"?"None":
            h is LyraLayerHook.FullBody_SkeletalControls or LyraLayerHook.LeftHandPose_OverrideState?"Controls":
            h is LyraLayerHook.FullBody_Aiming or LyraLayerHook.FullBodyAdditives?name=="mixed"?"None":"Aim":"Body"));
    }
    private void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        int self=0,unbound=0,external=0,rejected=0,phaseCalls=0,phaseRoots=0,phaseInputs=0,phaseRejected=0;
        foreach(var profile in new[]{"unarmed","pistol","rifle"})
        foreach(var layout in new[]{"single","three-groups","mixed","per-call"})
        {
            var contracts=Layout(layout);var host=new LyraMainLocomotionHost(resources,profile,contracts:contracts);
            var bindings=contracts.CreateBindings();bindings.Commit(bindings.PrepareLink(resources.LayerGraphs.ClassPath(profile)));
            Require(bindings.Targets.SequenceEqual(contracts.CallSites.Select(c=>host.LayerBinding(c.Node))),"Production and public binding policy differ.");
            var originalInstances=host.LayerInstances.ToArray();
            foreach(var site in contracts.CallSites)
            {
                var call=host.LayerCalls.Call(site.Node);var instance=host.Layer(site.Hook);
                Require(ReferenceEquals(instance,host.LayerCalls.External(call)),"Production instance lookup bypassed its call-site target.");
                var updates=0;var evaluations=0;var upstream=0;
                Action[] inputs=Enumerable.Range(0,call.PoseInputs).Select<int,Action>(_=>()=>++upstream).ToArray();
                Action<LyraCompositionPoseBuffer>[] poses=Enumerable.Range(0,call.PoseInputs).Select<int,Action<LyraCompositionPoseBuffer>>(_=>_=>++upstream).ToArray();
                var output=new LyraCompositionPoseBuffer(bank);
                var phaseUpstream=0;var phaseOwners=0;
                Action[] phaseArguments=Enumerable.Range(0,call.PoseInputs).Select<int,Action>(_=>()=>++phaseUpstream).ToArray();
                void ExternalPhase(LyraItemLayerGraphInstance target,LyraLayerHook hook)
                {Require(ReferenceEquals(target,instance)&&hook==site.Hook,"Wrong initialization/cache owner.");++phaseOwners;}
                host.LayerCalls.Initialize(call,phaseArguments,ExternalPhase);
                host.LayerCalls.CacheBones(call,phaseArguments,ExternalPhase);
                host.LayerCalls.InitializeSubGraph(call,ExternalPhase);
                host.LayerCalls.CacheBonesSubGraph(call,ExternalPhase);
                Require(phaseUpstream==2*call.PoseInputs&&phaseOwners==4,"External initialization/cache traversal differs.");
                phaseCalls+=4;phaseRoots+=phaseOwners;phaseInputs+=phaseUpstream;
                host.LayerCalls.Update(call,inputs,(target,hook)=>{Require(ReferenceEquals(target,instance)&&hook==site.Hook,"Wrong external update owner.");++updates;});
                host.LayerCalls.Evaluate(call,poses,output,false,(target,hook,destination)=>
                {Require(ReferenceEquals(target,instance)&&hook==site.Hook&&ReferenceEquals(destination,output),"Wrong external evaluation owner.");++evaluations;});
                Require(updates==1&&evaluations==1&&upstream==0,"External root call eagerly traversed its input.");++external;
            }
            var unlink=bindings.PrepareUnlink(resources.LayerGraphs.ClassPath(profile));bindings.Commit(unlink);
            Require(bindings.Targets.All(t=>t.Kind==AlsLinkedLayerTargetKind.Self),"Ordinary Unlink did not restore Main defaults.");
            var selfRoutes=new LyraLinkedLayerCallRoutes(resources.LayerGraphs,contracts,bindings.Targets,host.MainState,
                new Dictionary<long,LyraItemLayerGraphInstance>());
            var emptyTargets=bindings.Targets.Select(t=>new AlsLinkedLayerTarget(AlsLinkedLayerTargetKind.Unbound,null,"",0,false,false)).ToImmutableArray();
            var emptyRoutes=new LyraLinkedLayerCallRoutes(resources.LayerGraphs,contracts,emptyTargets,host.MainState,
                new Dictionary<long,LyraItemLayerGraphInstance>());
            foreach(var isSelf in new[]{true,false})foreach(var additive in new[]{false,true})foreach(var site in contracts.CallSites)
            {
                var routes=isSelf?selfRoutes:emptyRoutes;var call=routes.Call(site.Node);
                var output=new LyraCompositionPoseBuffer(bank);var input=new LyraCompositionPoseBuffer(bank);
                bank.Reference.CopyTo(input.Pose);
                if(additive)Array.Fill(input.Pose,new AlsPrecisePose(default,AlsQuaternion.Identity,default));
                input.Pose[0]=input.Pose[0] with{Position=new(11,-11,5.5)};
                for(var n=0;n<input.Curves.Length;n++)input.Curves[n]=new(n+.25f,true,2);
                for(var n=0;n<input.Attributes.Length;n++)input.Attributes[n]=new(n+33,true);
                input.RootMotion=new(new(new(11,-11,5.5),AlsQuaternion.Identity,AlsDoubleVector.One),true);
                Array.Fill(output.Pose,new AlsPrecisePose(new(902,902,902),AlsQuaternion.Identity,AlsDoubleVector.One));
                Array.Fill(output.Curves,new LyraCurveSample(-7,true,1));Array.Fill(output.Attributes,new LyraAttributeSample(-21,true));
                output.RootMotion=new(new(new(-7,3,1),AlsQuaternion.Identity,AlsDoubleVector.One),true);
                var oldRoot=output.RootMotion;var updates=0;var evaluations=0;
                var phaseUpstream=0;
                Action[] phaseArguments=Enumerable.Range(0,call.PoseInputs).Select<int,Action>(_=>()=>++phaseUpstream).ToArray();
                void UnexpectedPhase(LyraItemLayerGraphInstance _,LyraLayerHook __)=>throw new InvalidOperationException("Default/empty phase created an external owner.");
                routes.Initialize(call,phaseArguments,UnexpectedPhase);routes.CacheBones(call,phaseArguments,UnexpectedPhase);
                routes.InitializeSubGraph(call,UnexpectedPhase);routes.CacheBonesSubGraph(call,UnexpectedPhase);
                Require(phaseUpstream==2*call.PoseInputs,"Default/empty Initialize/CacheBones pruned an incoming pose.");
                phaseCalls+=4;phaseInputs+=phaseUpstream;
                Action[] inputs=Enumerable.Range(0,call.PoseInputs).Select<int,Action>(_=>()=>++updates).ToArray();
                Action<LyraCompositionPoseBuffer>[] poses=Enumerable.Range(0,call.PoseInputs).Select<int,Action<LyraCompositionPoseBuffer>>(_=>o=>{++evaluations;o.Copy(input.Input);}).ToArray();
                routes.Update(call,inputs,(_,_)=>throw new InvalidOperationException("Default/empty call created an external update."));
                routes.Evaluate(call,poses,output,additive,(_,_,_)=>throw new InvalidOperationException("Default/empty call created an external evaluation."));
                var forward=!isSelf&&call.PoseInputs>0;
                Require(updates==(forward?1:0)&&evaluations==(forward?1:0),"Default or empty-target traversal differs.");
                if(forward)
                {
                    Require(output.Pose.SequenceEqual(input.Pose)&&output.Curves.SequenceEqual(input.Curves)&&output.Attributes.SequenceEqual(input.Attributes)&&output.RootMotion==input.RootMotion,"Fallback dropped input pose data.");
                }
                else
                {
                    for(var n=0;n<81;n++)Require(output.Pose[n]==(additive?new AlsPrecisePose(default,AlsQuaternion.Identity,default):bank.Reference[n]),"Default pose reset differs.");
                    Require(output.Curves.All(c=>c==new LyraCurveSample(-7,true,1))&&output.Attributes.All(a=>a==new LyraAttributeSample(-21,true))&&output.RootMotion==oldRoot,"Pose reset discarded preexisting curve or attribute data.");
                }
                if(isSelf)++self;else ++unbound;
            }
            Require(originalInstances.SequenceEqual(host.LayerInstances)&&!host.MainState.Update.HasPending,"Default routes fabricated graph owners or pending Main state.");
            var first=contracts.CallSites[0];var oldCall=host.LayerCalls.Call(first.Node);var outputCheck=new LyraCompositionPoseBuffer(bank);
            Reject(()=>selfRoutes.External(oldCall));++rejected;
            var selfCall=selfRoutes.Call(first.Node);
            Reject(()=>selfRoutes.Initialize(selfCall,new Action[selfCall.PoseInputs+1],(_,_)=>{}));++phaseRejected;
            Reject(()=>selfRoutes.CacheBones(selfCall,new Action[selfCall.PoseInputs+1],(_,_)=>{}));++phaseRejected;
            Reject(()=>selfRoutes.InitializeSubGraph(oldCall,(_,_)=>{}));++phaseRejected;
            Reject(()=>selfRoutes.CacheBonesSubGraph(oldCall,(_,_)=>{}));++phaseRejected;
            Reject(()=>selfRoutes.Update(selfCall,new Action[selfCall.PoseInputs+1],(_,_)=>{}));++rejected;
            Reject(()=>selfRoutes.Evaluate(selfCall,new Action<LyraCompositionPoseBuffer>[selfCall.PoseInputs+1],outputCheck,false,(_,_,_)=>{}));++rejected;
            selfRoutes.Retire();Reject(()=>selfRoutes.Call(first.Node));++rejected;
            host.RetireLayers();Reject(()=>host.LayerCalls.External(oldCall));++rejected;
            emptyRoutes.Retire();
        }
        Require(self==336&&unbound==336&&external==168&&rejected==60,"Incomplete route coverage.");
        Require(phaseCalls==3360&&phaseRoots==672&&phaseInputs==360&&phaseRejected==48,"Incomplete initialization/cache route coverage.");
        GD.Print($"LYRA_LAYER_PHASE_ROUTES_OK profiles=3 layouts=4 calls={phaseCalls} externalRoots={phaseRoots} inputs={phaseInputs} rejected={phaseRejected} fullMainInitialize=false");
        GD.Print($"LYRA_DEFAULT_LAYER_ROUTES_OK profiles=3 layouts=4 bones=81 self={self} unbound={unbound} external={external} rejected={rejected} fullMainUnlink=false");
    }
}
