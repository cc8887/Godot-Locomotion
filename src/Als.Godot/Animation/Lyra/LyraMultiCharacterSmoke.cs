using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Events;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

// Three same-transform pairs use one immutable source bank. Subjects cancel
// and retry while controls commit once; every pair follows a different input
// and equipment timeline. All pending frames coexist in the actual Jolt world.
public partial class LyraMultiCharacterSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private LyraMontageCatalog _catalog=null!;
    private readonly LyraSceneCharacter[] _actors=new LyraSceneCharacter[6];
    private LyraSceneCharacter _sentinel=null!;
    private string _sentinelState="",_report="";
    private int _hz=60,_frame,_retries,_rejects,_late,_switches,_replacements,_frames,_montageFrames;
    private int _retiredQueries;
    private int _montageCallbacks,_sourceCallbacks,_gameplaySignals,_meleeSignals,_reloadSignals;
    private int _contextSignals,_contextAudioSelections;
    private readonly List<string> _typedDeliveries=[];
    private bool _done;
    private readonly HashSet<uint> _identities=[];
    private readonly HashSet<int> _states=[];
    private readonly HashSet<string> _profiles=[];
    private readonly IncrementalHash _digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();
            string? Arg(string prefix)=>args.SingleOrDefault(a=>a.StartsWith(prefix,StringComparison.Ordinal))?[prefix.Length..];
            if(Arg("--lyra-multi-hz=") is {} hz&&(!int.TryParse(hz,out _hz)||_hz is not(30 or 60 or 120)))throw new ArgumentException("Invalid rate.");
            _report=Arg("--lyra-multi-report=")??throw new ArgumentException("New absolute report required.");
            Require(Path.IsPathFullyQualified(_report)&&!File.Exists(_report),"Report would overwrite evidence.");
            Engine.PhysicsTicksPerSecond=_hz;
            var floor=new StaticBody3D{CollisionLayer=5,CollisionMask=0,Position=new(0,-.15f,0)};
            floor.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(40,.3f,40)}});AddChild(floor);
            _resources=new(includeMontageActions:true);_catalog=new();
            for(int i=0;i<6;i++)_actors[i]=Create(i);
            _sentinel=new(this,_resources,_catalog,"IdleSentinel",new(10,.92f,10),"unarmed");Register(_sentinel);
            _sentinelState=State(_sentinel.Animation);ValidateOwners();
            Reject(()=>new LyraCharacterAnimation(_actors[0].Body,_resources,_catalog,"rifle",1,4));
        }
        catch(Exception e){Fail(e);}
    }
    private LyraSceneCharacter Create(int i)
    {
        var actor=new LyraSceneCharacter(this,_resources!,_catalog,$"Role{i}Generation{_replacements}",
            new((i/2-1)*5,.92f,0),new[]{"unarmed","pistol","rifle"}[i/2]);Register(actor);
        actor.GameplayEvents.GameplayEventReceived+=(tag,payload,magnitude)=>
        {
            var a=actor.Animation;
            Require(a.Binding.PublishedFrames==a.SourceNotifies.CommittedFrame+1&&a.MontageBank.CommittedIdentity.FrameId==a.SourceNotifies.CommittedFrame,
                "Gameplay signal preceded complete role publication.");
            Require(payload=="None"&&magnitude==0,"Default gameplay payload changed.");
            Require(a.GameplayNotifies.LastCommands.Any(c=>c.Event.Tag==tag),"Signal has no committed original notify.");
            _gameplaySignals++;if(tag=="GameplayEvent.MeleeHit")_meleeSignals++;else if(tag=="GameplayEvent.ReloadDone")_reloadSignals++;
            _typedDeliveries.Add("gameplay:"+tag);
        };
        actor.ContextEffects.Selected+=delivery=>
        {
            var a=actor.Animation;
            Require(a.Binding.PublishedFrames==a.SourceNotifies.CommittedFrame+1&&a.MontageBank.CommittedIdentity.FrameId==a.SourceNotifies.CommittedFrame,
                "ContextEffects preceded full role publication.");
            Require(a.ContextEffects.LastMessages.Contains(delivery.Message)&&delivery.Message.Contexts.IsEmpty&&delivery.Selection.Vfx.IsEmpty,
                "ContextEffects lost original provenance.");
            _contextSignals++;_contextAudioSelections+=delivery.Selection.Audio.Length;
            _typedDeliveries.Add("context:"+delivery.Message.Callback.Reference.Core.PolicyIndex);
        };
        return actor;
    }
    private void Register(LyraSceneCharacter actor)=>Require(_identities.Add(actor.Animation.CharacterId),"Reused a character lifetime ID.");
    private void ValidateOwners()
    {
        foreach(var actor in _actors)
        {
            var a=actor.Animation;Require(a.CharacterId>0&&a.SlotGeneration==1,"Invalid production role identity.");
            Require(ReferenceEquals(a.SourceBank,_resources!.Catalog.Bank),"Role duplicated the immutable source bank.");
            foreach(var hook in a.Host.Main.Layers.Contract.Functions.Keys)
                Require(ReferenceEquals(a.Host.Main.Layers.Call(hook).Instance,a.Host.Main.Layers),"Split grouped Layer owner.");
            foreach(var other in _actors.Where(o=>o!=actor))
            {
                var b=other.Animation;
                Require(a.CharacterId!=b.CharacterId&&!ReferenceEquals(a.Host,b.Host)&&!ReferenceEquals(a.MontageBank,b.MontageBank)&&
                    !ReferenceEquals(a.Host.Main.Layers,b.Host.Main.Layers)&&!ReferenceEquals(a.Binding.Model,b.Binding.Model)&&
                    !ReferenceEquals(a.Binding.Skeleton,b.Binding.Skeleton)&&!ReferenceEquals(a.SourceNotifies,b.SourceNotifies),"Mutable character owner was shared.");
            }
        }
    }
    private static string State(LyraCharacterAnimation a)=>JsonSerializer.Serialize(new{
        main=LyraMainLocomotionHostSmoke.Snapshot(a.Host.Main),a.Host.RigState,a.Host.InertiaState,
        a.Host.FinalRigDisableLegIK,a.Host.Main.Layers.SkeletalHistory,a.Host.Main.Layers.AimingNodes,
        a.Host.Main.Layers.AdditivesState,montages=a.MontageBank.Committed.ToArray(),a.Binding.PublishedFrames,
        a.SourceNotifies.RandomSeed,a.SourceNotifies.MontageRandomSeed,a.SourceNotifies.NextReference,a.SourceNotifies.NextInstance,a.SourceNotifies.States,a.SourceNotifies.Callbacks,a.SourceNotifies.CommittedFrame,
        gameplayFrame=a.GameplayNotifies.CommittedFrame,a.GameplayNotifies.DeliveredEvents,a.GameplayNotifies.MissingReceiverEvents,a.GameplayNotifies.LastCommands,
        contextFrame=a.ContextEffects.CommittedFrame,contextDelivered=a.ContextEffects.DeliveredMessages,a.ContextEffects.LastMessages});
    private void Reject(Action action)
    {try{action();}catch(InvalidOperationException){_rejects++;return;}throw new InvalidOperationException("Foreign character operation accepted.");}
    public override void _PhysicsProcess(double dt)
    {
        if(_done||_resources is null)return;
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Wrong physics rate.");float delta=(float)dt;
            if(_frame==_hz*6)
            {
                // Tear down a pair with live candidates, then recreate the
                // same ALS actors/profile; the surviving four retain history.
                var survivors=_actors.Skip(2).Select(a=>State(a.Animation)).ToArray();
                for(int i=0;i<2;i++)
                {
                    var old=_actors[i];var a=old.Animation;
                    var o=old.Move(default,delta);var pending=o.Prepare(a,delta);var call=a.Host.Main.Layers.Call(LyraLayerHook.FullBody_IdleState);
                    _retiredQueries+=a.PhysicsQueries;a.Dispose();Reject(()=>a.Commit(pending));Reject(()=>a.Prepare(o.Main,delta,o.Movement,o.GroundDistance,o.Hit,o.Point,o.Normal));
                    Reject(()=>a.Host.Main.Layers.Call(LyraLayerHook.FullBody_IdleState));
                    Reject(()=>a.Host.Main.Sources.Hosts.Ground.Scope.BeginMain(o.Main,delta));
                    Reject(()=>a.Rebind("pistol"));
                    old.Dispose();_replacements++;_actors[i]=Create(i);
                    Reject(()=>_actors[i].Animation.Host.Main.Layers.PrepareLeftHand(call,false,false,0));
                }
                Require(survivors.SequenceEqual(_actors.Skip(2).Select(a=>State(a.Animation))),"Destroying another role changed survivors.");ValidateOwners();
            }
            var candidates=new LyraCharacterAnimationCandidate[6];var observations=new LyraSceneObservation[6];
            var before=new string[6];
            for(int pair=0;pair<3;pair++)
            {
                double t=(double)_frame/_hz;
                int local=_frame-pair*_hz/5;
                if(local>=0&&new[]{_hz,_hz*21/10,_hz*3,_hz*43/10}.Contains(local))
                    for(int j=0;j<2;j++){var a=_actors[pair*2+j].Animation;a.Rebind(a.Profile switch{"unarmed"=>"pistol","pistol"=>"rifle",_=>"unarmed"});_switches++;}
                var direction=t>=.5&&t<1.6?new Vector2(0,-1):t>=2.5&&t<3.4?new Vector2(1,0):
                    t>=5.5&&t<6.8?new Vector2(0,-1):t>=6.8&&t<7.6?new Vector2(0,1):Vector2.Zero;
                var input=new LyraSceneMovement(direction,pair*.2f,t>=2&&t<2.5?.15f:0,t>=2.5&&t<3.7,
                    t>=2&&t<2.5,t>=.5&&t<.8,_frame==_hz*4+pair*_hz/5);
                for(int j=0;j<2;j++)
                {
                    int i=pair*2+j;var actor=_actors[i];before[i]=State(actor.Animation);
                    observations[i]=actor.Move(input,delta);candidates[i]=observations[i].Prepare(actor.Animation,delta);
                    Require(candidates[i].Identity==actor.Animation.NextIdentity,"Main/Montage role identity differs.");
                }
            }
            for(int i=0;i<6;i++)
            {
                var a=_actors[i].Animation;var foreign=candidates[(i+2)%6];
                Reject(()=>a.ValidateCommit(foreign));Reject(()=>a.Binding.ValidatePublish(foreign.Skin));
                Reject(()=>a.SourceNotifies.ValidateCommit(foreign.SourceNotifies));
                Reject(()=>a.Collision.ValidateFrame(foreign.Physics));
                var invocation=_actors[(i+2)%6].Animation.Host.Main.Layers.Call(LyraLayerHook.LeftHandPose_OverrideState);
                Reject(()=>a.Host.Main.Layers.PrepareLeftHand(invocation,false,false,0));
                a.Host.Main.SlotTraversal!.ValidateCommit(candidates[i].Identity);
                Reject(()=>a.Host.Main.SlotTraversal!.ValidateCommit(foreign.Identity));
                // Subject retry happens after the other five candidates exist.
                if(i%2==0)
                {
                    var first=candidates[i];
                    if(_frame%97==31)
                    {
                        var component=a.Binding.Component;var transform=component.Transform;
                        component.Reparent(_actors[i].Body,false);Reject(()=>a.ValidateCommit(first));
                        component.Reparent(a.Binding.Skeleton,false);component.Transform=transform;
                        Reject(()=>a.Commit(first));_late++;
                    }
                    a.Cancel();Require(State(a)==before[i],"Cancelled role published state.");Reject(()=>a.Commit(first));
                    Reject(()=>a.Host.Main.Prepare(observations[i].Main,delta,new(true,1,false),-1,
                        first.Main.Character.Component,AlsQuaternion.Identity,observations[i].Movement,observations[i].GroundDistance,
                        timeSinceFired:0,montageFrame:foreign.Main.Montage));
                    a.Cancel();Require(State(a)==before[i],"Foreign Montage frame changed committed history.");
                    candidates[i]=observations[i].Prepare(a,delta);
                    Compare(first.Output,candidates[i].Output,"Subject retry");_retries++;
                    Require(first.SourceNotifies.Windows.SequenceEqual(candidates[i].SourceNotifies.Windows)&&
                        first.SourceNotifies.Queued.SequenceEqual(candidates[i].SourceNotifies.Queued)&&
                        first.SourceNotifies.Callbacks.SequenceEqual(candidates[i].SourceNotifies.Callbacks)&&
                        first.SourceNotifies.RandomSeed==candidates[i].SourceNotifies.RandomSeed&&
                        first.SourceNotifies.MontageWindows.SequenceEqual(candidates[i].SourceNotifies.MontageWindows)&&
                        first.SourceNotifies.RelevantSlots==candidates[i].SourceNotifies.RelevantSlots&&
                        first.SourceNotifies.MontageRandomSeed==candidates[i].SourceNotifies.MontageRandomSeed,"Subject notify retry changed output.");
                    Require(first.GameplayNotifies.Commands.SequenceEqual(candidates[i].GameplayNotifies.Commands),"Subject gameplay retry changed output.");
                    Require(first.ContextEffects.Messages.SequenceEqual(candidates[i].ContextEffects.Messages),"Subject ContextEffects retry changed output.");
                }
                Require(_sentinelState==State(_sentinel.Animation),"Shared resources changed idle role.");
            }
            // A role-only authored action exercises the two upper-body tracks.
            int action=_catalog.Definitions.First(d=>d.AdditionalTracks.Any(t=>t.Slot.Id==1)&&
                _resources!.Catalog.Notifies.Asset(_catalog.Paths[d.ActionDefinitionId]).Count>0).ActionDefinitionId;
            if(_frame==_hz*5)for(int i=2;i<4;i++)Require(_actors[i].Animation.MontageBank.PlayAction(action,1),"Action was rejected.");
            int reload=_catalog.Paths.IndexOf("/Game/Weapons/Pistol/Animations/AM_MM_Pistol_Reload.AM_MM_Pistol_Reload");
            if(_frame==_hz*5)for(int i=4;i<6;i++)Require(_actors[i].Animation.MontageBank.PlayAction(reload,1),"Reload action was rejected.");
            for(int pair=0;pair<3;pair++)Compare(candidates[pair*2].Output,candidates[pair*2+1].Output,"Interleaved pair");
            foreach(int i in Enumerable.Range(0,6).Reverse())
            {
                var a=_actors[i].Animation;_typedDeliveries.Clear();a.Commit(candidates[i]);_frames++;
                var typed=candidates[i].SourceNotifies.Callbacks.Where(c=>!c.Named&&c.Kind==AlsAssetNotifyCallbackKind.Notify)
                    .Select(c=>(Callback:c,Kind:_resources.Catalog.Notifies.Event(c.Reference.Core.PolicyIndex).Kind))
                    .Where(c=>c.Kind==LyraAssetNotifyKind.ContextEffects||LyraGameplayNotifyConsumer.Translate(c.Kind).HasValue)
                    .Select(c=>c.Kind==LyraAssetNotifyKind.ContextEffects?"context:"+c.Callback.Reference.Core.PolicyIndex:
                        "gameplay:"+LyraGameplayNotifyConsumer.Translate(c.Kind)!.Value.Tag);
                Require(_typedDeliveries.SequenceEqual(typed),"Typed consumers reordered original callbacks.");
                _states.Add(a.Host.Main.Machine.State);_profiles.Add(a.Profile);
                Require(a.MontageBank.CommittedIdentity==candidates[i].Identity,"Montage committed another role identity.");
                Require(a.SourceNotifies.CommittedFrame==candidates[i].Identity.FrameId,"Source queue committed another role frame.");
                _montageCallbacks+=a.SourceNotifies.Callbacks.Count(c=>c.Reference.Playback.SourceKind==AlsAssetNotifySourceKind.Montage);
                _sourceCallbacks+=a.SourceNotifies.Callbacks.Count(c=>c.Reference.Playback.SourceKind==AlsAssetNotifySourceKind.AssetPlayer);
                _montageFrames+=a.MontageBank.Committed.Length>0?1:0;VerifySkin(a);
            }
            for(int pair=0;pair<3;pair++)Require(State(_actors[pair*2].Animation)==State(_actors[pair*2+1].Animation),"Committed pair histories differ.");
            // Fingerprint all channels, excluding role ID and world float noise.
            foreach(var c in candidates)_digest.AppendData(JsonSerializer.SerializeToUtf8Bytes(new{pose=c.Output.Pose.ToArray(),curves=c.Output.Curves.ToArray(),attributes=c.Output.Attributes.ToArray(),c.Output.RootMotion}));
            _frame++;if(_frame==_hz*8)Finish();
        }
        catch(Exception e){Fail(e);}
    }
    private static void Compare(LyraCompositionPoseBuffer a,LyraCompositionPoseBuffer b,string label)
    {Require(a.Pose.SequenceEqual(b.Pose)&&a.Curves.SequenceEqual(b.Curves)&&a.Attributes.SequenceEqual(b.Attributes)&&a.RootMotion==b.RootMotion,label+" differs.");}
    private void VerifySkin(LyraCharacterAnimation a)
    {
        for(int b=0;b<68;b++)
        {
            var pose=LyraAlsCharacterBinding.Convert(a.CommittedOutput.Pose[a.Binding.Bones[b]]);
            Require(a.Binding.Skeleton.GetBonePosePosition(b)==new Vector3(pose.Position.X,pose.Position.Y,pose.Position.Z)&&
                a.Binding.Skeleton.GetBonePoseRotation(b)==new Quaternion(pose.Rotation.X,pose.Rotation.Y,pose.Rotation.Z,pose.Rotation.W),"Foreign or wrong skin publication.");
        }
    }
    private void Finish()
    {
        Require(_switches==24&&_replacements==2&&_identities.Count==9&&_retries==_hz*8*3&&_late>0&&_montageFrames>0&&_montageCallbacks>0&&_sourceCallbacks>0&&_profiles.Count==3&&_states.Contains(2)&&_states.Contains(7)&&
            _gameplaySignals==4&&_meleeSignals==2&&_reloadSignals==2&&_actors.Sum(a=>a.Animation.GameplayNotifies.DeliveredEvents)==4&&
            _contextSignals>0&&_contextAudioSelections>0,"Incomplete multi-role coverage.");
        var result=new{hz=_hz,physicsFrames=_frame,roleFrames=_frames,activeRoles=6,createdLifetimes=_identities.Count,
            switches=_switches,replacements=_replacements,retries=_retries,rejected=_rejects,lateFailures=_late,
            montageFrames=_montageFrames,queries=_retiredQueries+_actors.Sum(a=>a.Animation.PhysicsQueries),
            montageNotifyCallbacks=_montageCallbacks,sourceNotifyCallbacks=_sourceCallbacks,montageNotifyQueue=true,
            gameplaySignals=_gameplaySignals,meleeSignals=_meleeSignals,reloadSignals=_reloadSignals,gameplayNotifyConsumer=true,
            contextEffectsSignals=_contextSignals,contextAudioSelections=_contextAudioSelections,contextEffectsConsumer=true,typedCallbackOrder=true,audioPlayback=false,
            states=_states.Order().ToArray(),profiles=_profiles.Order().ToArray(),samePoseAndHistory=true,
            actualGodotPhysics=true,nativeWholeMainParity=false,productionAccepted=false,digest=Convert.ToHexString(_digest.GetHashAndReset())};
        using var file=new FileStream(_report,FileMode.CreateNew);JsonSerializer.Serialize(file,result);
        GD.Print("LYRA_MULTI_CHARACTER_GODOT_OK "+JsonSerializer.Serialize(result));_done=true;GetTree().Quit();
    }
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private void Fail(Exception e){_done=true;GD.PushError("Lyra multi-character failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree()
    {foreach(var actor in _actors)actor?.Dispose();_sentinel?.Dispose();_resources?.Dispose();_digest.Dispose();}
}
