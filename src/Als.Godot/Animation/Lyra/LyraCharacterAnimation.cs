using Godot;
using System.Collections.Immutable;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraCharacterAnimationCandidate(LyraMainPoseCandidate Main, AlsFrameIdentity Identity,
    LyraGodotRigCollision.Frame Physics, LyraAlsSkinCandidate Skin, LyraCompositionPoseBuffer Output,
    LyraNotifyQueueCandidate SourceNotifies,LyraGameplayNotifyCandidate GameplayNotifies,LyraContextEffectsCandidate ContextEffects,
    LyraWeaponNotifyCandidate WeaponNotifies,LyraNamedNotifyCandidate NamedNotifies,float Delta);
internal sealed record LyraCharacterMovementCandidate(LyraCharacterAnimation Owner,AlsFrameIdentity Identity,ulong Tick,
    float Delta,AlsMontageRootMotionRange Range,LyraRootMotionAttribute Motion,AlsMotionWarpingContext WarpContext);

// Live character boundary: complete Main and five-slot physical bank,
// explicit ALS Rig reference, real scene queries, one final skin publication.
internal sealed class LyraCharacterAnimation : IDisposable
{
    private static readonly HashSet<ulong> LiveActors=[];
    private static long _nextCharacter;
    private readonly ulong _actorId;
    private bool _disposed;
    private bool _dispatching;
    private readonly List<AlsMontageActionRequest> _actionRequests=[];
    private readonly List<AlsMontageStopRequest> _stopRequests=[];
    private readonly List<long> _postMovementStops=[];
    internal LyraCharacterMovementDelegate MovementUpdated {get;}
    internal LyraEmoteAbility Emote {get;}
    private readonly LyraMontageMovementReader _movementReader;
    private readonly LyraRootMovementMotor? _motor;
    internal LyraCharacterMotionWarping? MotionWarping {get;private set;}
    private LyraCharacterMovementCandidate? _movement;
    private bool _movementApplied;
    private bool _movementBankPrepared;
    private AlsMontageEvent[]? _physicalImmediateMontageEvents;
    internal LyraPhysicalMovementResult LastMovement=>_motor?.Last??default;
    internal int CapsuleMoves=>_motor?.Moves??0;
    internal int RootCapsuleMoves=>_motor?.RootMoves??0;
    internal LyraMontageMovementReader MovementReader=>_movementReader;
    internal LyraWeaponEquipment Weapons {get;}
    internal LyraWeaponNotifyConsumer WeaponNotifies {get;}
    internal LyraNamedNotifyConsumer NamedNotifies {get;}
    public uint CharacterId {get;}
    private void WarpRequestBoundary()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||_dispatching||_pending is not null||_movement is not null||_actor is not CharacterBody3D||
            !GodotObject.IsInstanceValid(_actor)||!_actor.IsInsideTree()||_actor.IsQueuedForDeletion())
            throw new InvalidOperationException("MotionWarping changes require the live idle character physics boundary.");
    }
    internal void EnableMotionWarping()
    {WarpRequestBoundary();MotionWarping??=new((CharacterBody3D)_actor,Binding.Component,_resources.MotionWarping(_montageCatalog),_movementReader,CharacterId,SlotGeneration);}
    internal void RequestWarpTarget(AlsMotionWarpingTarget target,Node3D? follow=null)
    {WarpRequestBoundary();(MotionWarping??throw new InvalidOperationException("MotionWarping component is not attached.")).Set(target,follow);}
    internal void RequestRemoveWarpTarget(string name)
    {WarpRequestBoundary();(MotionWarping??throw new InvalidOperationException("MotionWarping component is not attached.")).Remove(name);}
    internal void RequestDisableWarpModifiers()
    {WarpRequestBoundary();(MotionWarping??throw new InvalidOperationException("MotionWarping component is not attached.")).DisableExisting();}
    public uint SlotGeneration=>1;
    public AlsFrameIdentity NextIdentity=>new(_frame,CharacterId,SlotGeneration);
    internal AlsMontageRuntime MontageBank=>_montages;
    internal LyraGodotRigCollision Collision=>_collision;
    internal LyraLogicalSourceBank SourceBank=>_resources.Catalog.Bank;
    internal LyraNotifyQueueRuntime SourceNotifies=>_sourceNotifies;
    internal event Action<LyraNotifyCallback>? MontageNotifyStateEnd;
    internal event Action<LyraNotifyCallback>? NotifyStateCallback;
    internal ImmutableArray<LyraNotifyActiveState> ActiveNotifyStates=>_sourceNotifies.States;
    internal void ClearActiveNotifyStates()
    {if(!_dispatching)throw new InvalidOperationException("State mutation requires live role dispatch.");_sourceNotifies.ClearActiveStates();}
    internal LyraGameplayNotifyConsumer GameplayNotifies {get;}
    internal LyraContextEffectsConsumer ContextEffects {get;}
    private readonly LyraNotifyQueueRuntime _sourceNotifies;
    private readonly LyraSourceNotifyBinding _notifyBinding;
    private readonly LyraMontageCatalog _montageCatalog;
    private LyraMontageNotifyBinding? _montageNotifyBinding;
    private sealed class InactiveFootPlacement : IAlsFootGroundQuery
    {
        public AlsFootGroundHit Sweep(int leg, in AlsFootTraceQuery query) =>
            throw new InvalidOperationException("This character uses final FootPlant; unexpected provider FootPlacement query.");
    }
    private readonly LyraLocomotionResources _resources;
    private readonly Node3D _actor;
    private readonly long _epoch;
    private readonly LyraGodotRigCollision _collision;
    private readonly AlsMontageRuntime _montages;
    private readonly IAlsFootGroundQuery _ground = new InactiveFootPlacement();
    private LyraCharacterAnimationCandidate? _pending;
    private long _frame;
    private bool _initialized;
    private bool _failed;
    public LyraMainPoseHost Host { get; }
    public LyraAlsCharacterBinding Binding { get; }
    public LyraMainObservationState Observation => Host.Main.MainState.Update.State;
    public string Profile => Host.Main.Profile;
    public long LayerEpoch => Host.Main.LayerEpoch;
    public bool LayersLinked=>Host.Main.IsLinked;
    public bool Unlink()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||_pending is not null||_movement is not null||
            !GodotObject.IsInstanceValid(_actor)||!_actor.IsInsideTree()||_actor.IsQueuedForDeletion())
            throw new InvalidOperationException("Unlink requires the live idle character physics boundary.");
        if(!Host.Unlink(checked(LayerEpoch+1)))return false;
        NamedNotifies.Unlink();return true;
    }
    public void SetLeftHandPoseOverrideEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||_dispatching||_pending is not null||_movement is not null||
            !GodotObject.IsInstanceValid(_actor)||!_actor.IsInsideTree()||_actor.IsQueuedForDeletion())
            throw new InvalidOperationException("Linked settings changes require the live idle character physics boundary.");
        Host.Main.SetLeftHandPoseOverrideEnabled(enabled);
    }
    public bool Rebind(string profile)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        // Committed notify callbacks may replace equipment. Prepare remains
        // prohibited during dispatch; no graph or physics candidate is alive.
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||_pending is not null||_movement is not null)
            throw new InvalidOperationException("Equipment changes require the idle main physics boundary.");
        profile=profile.ToLowerInvariant();
        if(profile is not("unarmed" or "pistol" or "rifle"))throw new ArgumentException("Unknown complete equipment provider.");
        if(profile==Profile&&LayersLinked)return false;
        var nextEpoch=checked(LayerEpoch+1);
        int playerBase=checked((int)(nextEpoch*2048));
        _=checked(playerBase*32+8191);
        bool equipmentChanged=profile!=Profile;
        var replacement=equipmentChanged?Weapons.CreateReplacement(profile):null;
        try
        {
            bool changed=Host.Rebind(_resources,profile,playerBase,nextEpoch);
            if(changed){if(equipmentChanged)Weapons.Replace(replacement);NamedNotifies.Rebind(Host.Main.Layers);}else replacement?.Dispose();
            return changed;
        }
        catch{replacement?.Dispose();throw;}
    }
    public bool RequestWeaponAction(bool reload)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||_pending is not null||_movement is not null)
            throw new InvalidOperationException("Action requests require the idle main physics boundary.");
        if(Profile=="unarmed")return false;
        var title=Profile=="pistol"?"Pistol":"Rifle";var name=$"AM_MM_{title}_{(reload?"Reload":"Fire")}";
        var path=$"/Game/Weapons/{title}/Animations/{name}.{name}";int asset=_montageCatalog.Paths.IndexOf(path);
        if(asset<0)throw new InvalidOperationException("Missing original character weapon action.");
        _actionRequests.Add(new(asset,1));return true;
    }
    internal void RequestMontageAction(int asset,float rate=1,float start=0)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||_pending is not null||_movement is not null||
            (uint)asset>=_montageCatalog.Definitions.Length||!float.IsFinite(rate)||!float.IsFinite(start)||start<0)
            throw new ArgumentException("Invalid idle character Montage request.");
        _actionRequests.Add(new(asset,rate,start));
    }
    internal void RequestMontageStop(int asset,float seconds)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||_pending is not null||_movement is not null||
            (uint)asset>=_montageCatalog.Definitions.Length||!float.IsFinite(seconds)||seconds<0)
            throw new ArgumentException("Invalid idle character Montage stop.");
        _stopRequests.Add(new(asset,seconds));
    }
    internal bool RequestEmote(bool crouched)
    {WarpRequestBoundary();return Emote.Activate(crouched);}
    internal void CancelEmote()
    {WarpRequestBoundary();Emote.CancelAbility();}
    private void StopEmoteAfterMovement(int asset)
    {
        var instance=_montages.ActiveActionInstance(asset);
        if(instance==0)return; // ACharacter::StopAnimMontage skips already stopped assets.
        _postMovementStops.Add(instance);
        _montages.StopInstance(instance,Emote.BlendOut,_montageCatalog.Definitions[asset].Lifecycle.BlendOutOption);
    }
    internal LyraCharacterMovementCandidate PrepareMovement(float delta)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(_motor is null||!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||_dispatching||_pending is not null||_movement is not null||
            !float.IsFinite(delta)||delta<=0)throw new InvalidOperationException("Movement requires the idle character physics boundary.");
        try
        {
            _montages.BeginWithActionRequests(NextIdentity,delta,_actionRequests.ToArray(),_stopRequests.ToArray(),Emote.BindMontageCallbacks);
            _movementBankPrepared=true;
            var range=_montages.RootMotionRange;var motion=_movementReader.Read(range);
            return _movement=new(this,NextIdentity,Engine.GetPhysicsFrames(),delta,range,motion,LyraMontageMovementReader.WarpContext(_montages,delta));
        }
        catch{Cancel();throw;}
    }
    internal LyraPhysicalMovementResult MoveCapsule(LyraCharacterMovementCandidate candidate,Vector3 velocity,bool falling,Vector3? finalVelocity=null,
        LyraCharacterFloorProbe? floor=null,float capsuleHalfHeight=0,LyraCharacterGroundMovement? groundMovement=null,
        LyraCharacterAirMovement? airMovement=null,LyraCharacterFallingInput? fallingInput=null)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!ReferenceEquals(candidate,_movement)||candidate.Owner!=this||candidate.Identity!=NextIdentity||candidate.Tick!=Engine.GetPhysicsFrames()||_movementApplied)
            throw new InvalidOperationException("Foreign, stale or consumed character root movement.");
        try
        {
            var oldLocation=LyraGodotRigCollision.NativePosition(_actor.GlobalPosition);
            var oldVelocity=LyraGodotRigCollision.NativePosition(((CharacterBody3D)_actor).Velocity);
            var emoteInstance=Emote.CaptureInstance(_montages);
            var immediate=_montages.ImmediateMontageEvents;
            var warp=MotionWarping?.Prepare(candidate.Identity,candidate.Motion,candidate.WarpContext);
            var motion=warp is null?candidate.Motion:new LyraRootMotionAttribute(warp.Core.Warped,candidate.Motion.Present);
            var prepared=_motor!.Prepare(candidate.Identity,candidate.Delta,candidate.Range,motion);
            if(warp is not null)MotionWarping!.ValidateCommit(warp);
            var result=_motor.Step(prepared,velocity,falling,finalVelocity,floor,capsuleHalfHeight,groundMovement,airMovement,fallingInput);_movementApplied=true;
            if(warp is not null)MotionWarping!.Commit(warp);
            Emote.ObserveInstance(emoteInstance);
            _montages.DeliverImmediateMontageEvents();
            _physicalImmediateMontageEvents=immediate;
            MovementUpdated.Broadcast(new(candidate.Identity,candidate.Delta,oldLocation,oldVelocity,_movementCrouched));
            return result;
        }
        catch{_motor?.Cancel();MotionWarping?.Cancel();throw;}
    }
    public int PhysicsQueries => _collision.QueryCount;
    private bool _movementCrouched;
    internal void SetMovementCrouched(bool crouched)
    {if(_movementApplied||_pending is not null)throw new InvalidOperationException("Stance belongs before physical movement.");_movementCrouched=crouched;}
    public LyraCompositionPoseBuffer CommittedOutput { get; }

    public LyraCharacterAnimation(Node3D actor, LyraLocomotionResources resources, LyraMontageCatalog catalog,
        string profile, long epoch, uint traversableMask,bool linkInitially=true)
    {
        if(!GodotThread.IsMainThread()||!actor.IsInsideTree()||actor.IsQueuedForDeletion())
            throw new InvalidOperationException("Character animation requires its live main-thread actor.");
        _actorId=actor.GetInstanceId();
        if(!LiveActors.Add(_actorId))throw new InvalidOperationException("Actor already owns a Lyra animation publisher.");
        _actor = actor; _resources = resources; _epoch = epoch;
        try
        {
            CharacterId=checked((uint)Interlocked.Increment(ref _nextCharacter));
            MovementUpdated=new(CharacterId,SlotGeneration);
            Binding = new(actor, resources.Catalog.Bank);
            _failed = false;
            _montages = catalog.CreateRuntime();
            _montageCatalog = catalog;
            var emotePolicy=new LyraEmotePolicy(catalog);
            Emote=new(emotePolicy.Asset,emotePolicy.BlendOut,MovementUpdated,
                request=>_actionRequests.Add(request),request=>_stopRequests.Add(request),StopEmoteAfterMovement);
            _movementReader=resources.Movement(catalog);
            if(actor is CharacterBody3D body)_motor=new(body,Binding.Component,CharacterId,SlotGeneration);
            Host = new(resources, profile, epoch: epoch, montageRuntime: _montages, montageCatalog: catalog,
                enableFinalFootPlant: true, rigReference: LyraFootPlantRigReference.AlsCompactReference,
                characterId:CharacterId,slotGeneration:SlotGeneration,initialMovement:LyraCharacterMovementSettings.Default.Snapshot(
                    actor is CharacterBody3D initialBody?LyraGodotRigCollision.NativePosition(initialBody.Velocity):AlsDoubleVector.Zero),linkInitially:linkInitially);
            _notifyBinding=new(Host.Main,resources.Catalog.Notifies);
            _sourceNotifies=new(resources.Catalog.Notifies,CharacterId,SlotGeneration);
            _sourceNotifies.AttachMontageTermination(_montages,catalog,
                ()=>!_disposed&&GodotObject.IsInstanceValid(_actor)&&_actor.IsInsideTree()&&!_actor.IsQueuedForDeletion(),
                callback=>MontageNotifyStateEnd?.Invoke(callback));
            _=new LyraNotifyDispatchPolicy();
            NamedNotifies=new(actor,resources.Catalog.Notifies,_sourceNotifies,Host.Main.IsLinked?Host.Main.Layers:null);
            GameplayNotifies=new(actor,resources.Catalog.Notifies,_sourceNotifies);
            ContextEffects=new(actor,resources.Catalog.Notifies,resources.Catalog.ContextEffects,resources.Catalog.Bank,_sourceNotifies);
            Weapons=new(actor,CharacterId);Weapons.Replace(Weapons.CreateReplacement(profile));
            WeaponNotifies=new(actor,resources.Catalog.Notifies,catalog,_sourceNotifies,Weapons);
            _collision = new(Binding.Component, actor, epoch, 2, traversableMask);
            CommittedOutput = new(resources.Catalog.Bank);
        }
        catch { if(NamedNotifies is not null)NamedNotifies.Retire();if(Weapons is not null)Weapons.Dispose();if(Host is not null)Host.Dispose();if(Binding is not null)Binding.Dispose();LiveActors.Remove(_actorId);throw; }
    }
    public LyraCharacterAnimationCandidate Prepare(in LyraMainUpdateInput input, float delta,
        in AlsStopMovementSnapshot movement, double groundDistance, bool floorBlocking,
        AlsDoubleVector floorPoint, AlsDoubleVector floorNormal,float? diagnosticDisableLegIK=null)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if (_pending is not null||_dispatching) throw new InvalidOperationException("Character animation frame is pending or dispatching.");
        var identity = NextIdentity;
        try
        {
            var physics = _collision.Capture(_epoch, _frame + 1);
            if(_movement is not null)
            {
                if(!_movementApplied||_movement.Identity!=identity||_movement.Tick!=Engine.GetPhysicsFrames()||_movement.Delta!=delta)
                    throw new InvalidOperationException("Animation must reuse its consumed physical Montage tick.");
                // Animation cancellation discards only its candidate bank. Replay
                // that same tick and require the already-consumed range to agree.
                if(!_movementBankPrepared)
                {
                    _montages.BeginWithActionRequests(identity,delta,_actionRequests.ToArray(),_stopRequests.ToArray(),Emote.BindMontageCallbacks);_movementBankPrepared=true;
                    if(_montages.RootMotionRange!=_movement.Range||_movementReader.Read(_movement.Range)!=_movement.Motion||
                        LyraMontageMovementReader.WarpContext(_montages,delta)!=_movement.WarpContext)
                        throw new InvalidOperationException("Animation retry changed consumed root movement.");
                    _montages.AcknowledgeImmediateMontageEvents(_physicalImmediateMontageEvents??throw new InvalidOperationException("Consumed movement lacks its Montage event receipt."));
                    foreach(var stop in _postMovementStops)
                        if(!_montages.StopInstance(stop,Emote.BlendOut,_montageCatalog.Definitions[Emote.Asset].Lifecycle.BlendOutOption))
                            throw new InvalidOperationException("Animation retry lost its post-movement Montage stop.");
                }
            }
            else _montages.BeginWithActionRequests(identity, delta,_actionRequests.ToArray(),_stopRequests.ToArray(),Emote.BindMontageCallbacks);
            var character = new AlsFootCharacterInput(physics.Component, input.Observation.Ground,
                floorBlocking, floorPoint, floorNormal, input.Observation.Velocity);
            var actorRotation = LyraGodotRigCollision.NativeTransform(_actor.GlobalTransform).Rotation;
            var relative = (actorRotation.Conjugate() * physics.Component.Rotation).Normalized();
            var fired = input.Observation.Firing ? 0 : Observation.TimeSinceFired + delta;
            var main = Host.Prepare(input with { MontagePlaying = _montages.IsAnyMontagePlaying },
                delta, new(true, 1, !_initialized), character, relative, movement, groundDistance, fired,
                new(false, false), pivotNotify:_sourceNotifies.WasTransitionActiveInMainSourceState(
                    LyraNotifyDispatchPolicy.MainMachine,LyraNotifyDispatchPolicy.PivotState),montageFrame: _montages.Frame);
            _montageNotifyBinding??=new(_resources.Catalog.Notifies,_montageCatalog,_montages,Host.Main.SlotTraversal
                ??throw new InvalidOperationException("Main Montage traversal is absent."));
            var montageNotifies=_montageNotifyBinding.Capture(identity);
            var notifies=_sourceNotifies.Prepare(_notifyBinding.Capture(main),_notifyBinding,delta,
                montage:montageNotifies,montageBinding:_montageNotifyBinding);
            var gameplay=GameplayNotifies.Prepare(notifies);
            var weapon=WeaponNotifies.Prepare(notifies);
            var named=NamedNotifies.Prepare(notifies);
            var view = Host.Evaluate(main, _ground, physics);
            var output = new LyraCompositionPoseBuffer(_resources.Catalog.Bank);
            output.Copy(new(_resources.Catalog.Bank, view.Pose, view.Curves, view.Attributes, view.RootMotion));
            var skin = Binding.Stage(_frame + 1, view.Pose);
            var context=ContextEffects.Prepare(notifies,view.Pose,physics,()=>_collision.ValidateFrame(physics));
            if(diagnosticDisableLegIK is {} disable)
            {
                var curves=view.Curves.ToArray();curves[_resources.Catalog.Bank.Curves.Index("DisableLegIK")]=new(disable,true);
                Host.StageFinalFeedback(main,diagnosticCurves:curves);
            }
            else Host.StageFinalFeedback(main);
            return _pending = new(main, identity, physics, skin, output,notifies,gameplay,context,weapon,named,delta);
        }
        catch { Cancel(); throw; }
    }
    public void ValidateCommit(LyraCharacterAnimationCandidate candidate)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if (_failed || !ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Foreign or failed character animation candidate.");
        try
        {
            Host.ValidateCommit(candidate.Main); _montages.ValidateCommit(candidate.Identity);
            _sourceNotifies.ValidateCommit(candidate.SourceNotifies);
            GameplayNotifies.ValidateCommit(candidate.GameplayNotifies);
            ContextEffects.ValidateCommit(candidate.ContextEffects);
            WeaponNotifies.ValidateCommit(candidate.WeaponNotifies);
            NamedNotifies.ValidateCommit(candidate.NamedNotifies);
            Binding.ValidatePublish(candidate.Skin);
            _collision.ValidateFrame(candidate.Physics);
        }
        catch { _failed = true; throw; }
    }
    public void Commit(LyraCharacterAnimationCandidate candidate)
    {
        ValidateCommit(candidate);
        // Consume captured ranges while Main is prepared; gameplay signals follow
        // the complete role publication and never run from Prepare or Evaluate.
        _sourceNotifies.Commit(candidate.SourceNotifies,deferDispatch:true);
        Host.Commit(candidate.Main); _montages.Commit(candidate.Identity);
        Binding.Publish(candidate.Skin); CommittedOutput.Copy(candidate.Output.Input);
        _initialized = true; _frame++; _pending = null;_actionRequests.Clear();_stopRequests.Clear();_postMovementStops.Clear();_movement=null;_movementApplied=false;_movementBankPrepared=false;_physicalImmediateMontageEvents=null;_dispatching=true;
        try
        {
            bool ContinueDispatch()
            {
                if(!_disposed&&GodotObject.IsInstanceValid(_actor)&&_actor.IsInsideTree()&&!_actor.IsQueuedForDeletion())return true;
                GameplayNotifies.AbortCommitted();ContextEffects.AbortCommitted();WeaponNotifies.AbortCommitted();NamedNotifies.AbortCommitted();return false;
            }
            Host.Main.DispatchLinkedMontageEvents(candidate.Identity.FrameId);
            if(!ContinueDispatch())return;
            GameplayNotifies.BeginCommitted(candidate.GameplayNotifies);ContextEffects.BeginCommitted(candidate.ContextEffects);
            WeaponNotifies.BeginCommitted(candidate.WeaponNotifies);
            NamedNotifies.BeginCommitted(candidate.NamedNotifies);
            int gameplay=0,context=0,weapon=0,named=0;
            _sourceNotifies.DispatchCommitted(callback=>
            {
                if(callback.Kind!=AlsAssetNotifyCallbackKind.Notify)
                {NotifyStateCallback?.Invoke(callback);return;}
                if(named<candidate.NamedNotifies.Commands.Length&&candidate.NamedNotifies.Commands[named]==callback)
                    NamedNotifies.DispatchAt(candidate.NamedNotifies,named++);
                if(!ContinueDispatch())return;
                if(gameplay<candidate.GameplayNotifies.Commands.Length&&LyraNotifyQueueRuntime.SameOccurrence(candidate.GameplayNotifies.Commands[gameplay].Callback,callback))
                    GameplayNotifies.DispatchAt(candidate.GameplayNotifies,gameplay++,callback);
                if(!ContinueDispatch())return;
                if(context<candidate.ContextEffects.Messages.Length&&LyraNotifyQueueRuntime.SameOccurrence(candidate.ContextEffects.Messages[context].Callback,callback))
                    ContextEffects.DispatchAt(candidate.ContextEffects,context++,callback);
                if(!ContinueDispatch())return;
                if(weapon<candidate.WeaponNotifies.Commands.Length&&LyraNotifyQueueRuntime.SameOccurrence(candidate.WeaponNotifies.Commands[weapon].Callback,callback))
                    WeaponNotifies.DispatchAt(candidate.WeaponNotifies,weapon++,callback);
            },ContinueDispatch);
            if(!ContinueDispatch())return;
            GameplayNotifies.EndCommitted(candidate.GameplayNotifies);ContextEffects.EndCommitted(candidate.ContextEffects);
            WeaponNotifies.EndCommitted(candidate.WeaponNotifies);
            NamedNotifies.EndCommitted(candidate.NamedNotifies);
            _montages.DispatchMontageEventCallbacks();
            if(!_disposed&&GodotObject.IsInstanceValid(_actor)&&!_actor.IsQueuedForDeletion())
                Weapons.Tick(candidate.Identity.FrameId,candidate.Delta,SourceBank,CommittedOutput.Pose,
                    LyraGodotRigCollision.NativeTransform(Binding.Component.GlobalTransform));
        }
        finally{_dispatching=false;}
    }
    public void Cancel()
    {
        GameplayNotifies.Cancel(); ContextEffects.Cancel(); WeaponNotifies.Cancel();NamedNotifies.Cancel(); _sourceNotifies.Cancel(); Binding.Cancel(); Host.Cancel(); _montages.Discard(); _pending = null; _failed = false;
        _motor?.Cancel();MotionWarping?.Cancel();_movementBankPrepared=false;if(!_movementApplied){_movement=null;_physicalImmediateMontageEvents=null;}
    }
    public void Dispose()
    {
        if(_disposed)return;
        if(!GodotThread.IsMainThread())throw new InvalidOperationException("Character disposal belongs to the main thread.");
        Cancel();Emote.Retire();MovementUpdated.Retire();_movement=null;_movementApplied=false;GameplayNotifies.Retire();ContextEffects.Retire();WeaponNotifies.Retire();NamedNotifies.Retire();Weapons.Dispose();_sourceNotifies.Retire();_disposed=true;_collision.Dispose();Host.Dispose();Binding.Dispose();LiveActors.Remove(_actorId);
    }
}
