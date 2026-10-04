using System.Text.Json;
using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Events;

namespace GodotAls.Locomotion;

// The live Main host owns state selection and source time. This scene gathers
// CharacterBody movement and input, then publishes one complete ALS skin pose.
public partial class LyraLocomotionDemo : Node3D
{
    [Export] public string Profile { get; set; } = "unarmed";
    private CharacterBody3D _body = null!;
    private CollisionShape3D _shape = null!;
    private ShapeCast3D _clearance = null!;
    private RayCast3D _floor = null!;
    private AlsOrbitCamera _camera = null!;
    private LyraLocomotionResources? _resources;
    private LyraCharacterAnimation? _animation;
    private LyraSceneMovementService? _movement;
    private LyraTerrainCourse? _terrain;
    private Label _hud = null!;
    private bool _crouching, _wantsCrouching, _done, _smoke, _retry;
    private int _hz = 60, _frame, _retries, _lateFailures, _air, _crouch, _aim;
    private double _maxWorldPosition, _maxSkinPosition, _maxSkinRotation;
    private readonly HashSet<int> _states = [];
    private readonly HashSet<string> _captures = [];
    private readonly List<object> _captureFrames=[];
    private bool _captureSubscribed;
    private string? _report, _captureDirectory;
    private bool _rebindSmoke;
    private bool _weaponSmoke;
    private bool _emoteSmoke;
    private bool _directionDiagnostic;
    private bool _directionSteady;
    private readonly List<object> _directionFrames = [];
    private bool _rebindFeedback;
    private bool _leftSettings;
    private bool _montageEvents;
    private int _montagePublishedFrames,_montagePreparedFrames,_montageCancelledFrames;
    private int _montagePendingRejected,_montageForeignRejected,_montageLateRejected,_montageRetiredRejected;
    private int _leftEnabledFrames,_leftDisabledFrames,_leftPendingRejected,_leftRetiredRejected,_leftSameClass;
    private int _rigCurveCarry;
    private int _switches,_sameClass,_pendingRebindRejected;
    private readonly List<object> _bindings=[];
    private readonly List<LyraItemLayerGraphInstance> _retired=[];
    private readonly List<LyraSceneCharacter> _companions=[];
    private readonly List<Vector3> _companionOrigins=[];
    private int _companionFrames;
    private int _notifyCallbacks,_notifySourceFrames,_notifyMontageCallbacks;
    private int _contextMessages,_contextContacts;
    private static string NotifyState(LyraCharacterAnimation a)=>JsonSerializer.Serialize(new
        {a.SourceNotifies.RandomSeed,a.SourceNotifies.MontageRandomSeed,a.SourceNotifies.NextReference,a.SourceNotifies.NextInstance,
            a.SourceNotifies.States,a.SourceNotifies.Callbacks,a.SourceNotifies.CommittedFrame});
    internal LyraCharacterAnimation Animation => _animation ?? throw new InvalidOperationException("Lyra Main is not ready.");

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            bool terrainSmoke = args.Contains("--lyra-terrain-smoke");
            _leftSettings=args.Contains("--lyra-main-left-settings");
            _montageEvents=args.Contains("--lyra-main-montage-events");
            string? Arg(string prefix) => args.SingleOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
            Profile = (Arg("--lyra-profile=") ?? Profile).ToLowerInvariant();
            if (Profile is not ("unarmed" or "pistol" or "rifle")) throw new ArgumentException("Unknown complete Lyra provider.");
            _smoke = args.Contains("--lyra-main-smoke"); _retry = args.Contains("--lyra-main-retry");
            _directionDiagnostic = args.Contains("--lyra-direction-diagnostic");
            _directionSteady = args.Contains("--lyra-direction-steady");
            if (_directionSteady && !_directionDiagnostic) throw new ArgumentException("Steady direction fixture requires its diagnostic trace.");
            if (_directionDiagnostic && !_smoke) throw new ArgumentException("Direction diagnostic requires Main smoke mode.");
            _rebindSmoke=args.Contains("--lyra-main-rebind");
            _rebindFeedback=args.Contains("--lyra-main-rebind-feedback");
            if(_rebindFeedback&&!_rebindSmoke)throw new ArgumentException("Rebind feedback fixture requires rebind mode.");
            if(_rebindSmoke&&!_smoke)throw new ArgumentException("Rebind fixture requires smoke mode.");
            if(_leftSettings&&(!_smoke||!_retry||!_rebindSmoke))
                throw new ArgumentException("Left-hand settings fixture requires smoke, retry and rebind modes.");
            if(_montageEvents&&(!_smoke||!_retry||!_rebindSmoke))
                throw new ArgumentException("Montage phase fixture requires smoke, retry and rebind modes.");
            if (Arg("--lyra-main-hz=") is { } hz)
            {
                if (!_smoke || !int.TryParse(hz, out _hz) || _hz is not (30 or 60 or 120)) throw new ArgumentException("Invalid Lyra smoke physics rate.");
                Engine.PhysicsTicksPerSecond = _hz;
            }
            _report = Arg("--lyra-main-report="); _captureDirectory = Arg("--lyra-main-capture=");
            if (_smoke && (_report is null || !Path.IsPathFullyQualified(_report) || File.Exists(_report)))
                throw new ArgumentException("Smoke requires a new absolute report path.");
            if (_captureDirectory is not null)
            {
                if (!_smoke || DisplayServer.GetName() == "headless" || !Path.IsPathFullyQualified(_captureDirectory) || Directory.Exists(_captureDirectory))
                    throw new ArgumentException("Capture requires a rendered run and a new absolute directory.");
                Directory.CreateDirectory(_captureDirectory);
            }
            _body = GetNode<CharacterBody3D>("Character"); _shape = GetNode<CollisionShape3D>("Character/CollisionShape3D");
            if (_directionSteady)
            {
                // The longer straight run must remain on the diagnostic floor.
                // Extend only this fixture; ordinary scenes keep their terrain.
                GetNode<CollisionShape3D>("Floor/CollisionShape3D").Shape = new BoxShape3D { Size = new(200, .3f, 200) };
                GetNode<MeshInstance3D>("Floor/MeshInstance3D").Mesh = new BoxMesh { Size = new(200, .3f, 200) };
            }
            _floor = GetNode<RayCast3D>("Character/GroundProbe"); _clearance = GetNode<ShapeCast3D>("Character/StandClearance");
            _shape.Shape = (Shape3D)_shape.Shape.Duplicate();
            _clearance.AddException(_body); _floor.AddException(_body);
            _camera = GetNode<AlsOrbitCamera>("OrbitCamera"); _camera.Configure(_body);
            _resources = new(includeMontageActions: true);
            _weaponSmoke=OS.GetCmdlineUserArgs().Contains("--lyra-main-weapons");
            _emoteSmoke=args.Contains("--lyra-main-emote");
            if(_emoteSmoke&&!_smoke)throw new ArgumentException("Emote input fixture requires smoke mode.");
            var catalog=new LyraMontageCatalog();
            _body.AddChild(new LyraGameplayEventComponent{Name="GameplayEvents"});
            _body.AddChild(new LyraContextEffectComponent{Name="ContextEffects"});
            _animation = new(_body, _resources, catalog, Profile, 1, 4);
            if(!InputMap.HasAction("lyra_emote"))
            {InputMap.AddAction("lyra_emote");InputMap.ActionAddEvent("lyra_emote",new InputEventKey{PhysicalKeycode=Key.E});}
            _movement=new(_body,Animation,_shape,_floor,_clearance);
            if (!_smoke || terrainSmoke)
            {
                if (terrainSmoke && (_smoke || Profile != "rifle")) throw new ArgumentException("Terrain run uses Rifle and its own smoke mode.");
                int terrainHz = int.Parse(Arg("--lyra-terrain-hz=") ?? "60");
                _terrain = new(this, _resources, catalog, terrainSmoke, Arg("--lyra-terrain-report="), Arg("--lyra-terrain-capture="), terrainHz);
                if (terrainSmoke) { _hz = terrainHz; _retry = true; }
            }
            int characters=1;
            if(Arg("--lyra-characters=") is {} count&&(!int.TryParse(count,out characters)||characters is <1 or >10))
                throw new ArgumentException("Lyra scene supports 1..10 characters.");
            for(int i=1;i<characters;i++)
            {
                var position=new Vector3((i%5-2)*3,.92f,3+(i/5)*4);
                _companionOrigins.Add(position);
                _companions.Add(new(this,_resources,catalog,$"Companion{i}",position,new[]{"unarmed","pistol","rifle"}[i%3]));
            }
            if(_captureDirectory is not null&&_companions.Count>0)
            {
                var overview=new Camera3D{Name="MultiCharacterCapture",Position=new(0,14,20),Fov=60,Current=true};
                AddChild(overview);overview.LookAt(Vector3.Zero);
            }
            var hud = new CanvasLayer(); _hud = new Label { Position = new(16, 16) }; hud.AddChild(_hud); AddChild(hud);
            UpdateHud();
            if(_captureDirectory is not null){RenderingServer.FramePostDraw+=CaptureRenderedFrame;_captureSubscribed=true;}
        }
        catch (Exception e) { Fail(e); }
    }
    public override void _PhysicsProcess(double dt)
    {
        if (_done || _animation is null) return;
        try
        {
            if (_smoke)
            {
                Require(Math.Abs(dt - 1d / _hz) < 1e-9, "Actual physics delta differs.");
                DriveSmokeInput();
            }
            if (_terrain?.Smoke == true) _terrain.DriveInput(_body, _frame);
            float delta = (float)dt;
            if(Input.IsActionJustPressed("switch_item_layer"))SwitchProfile(NextProfile(Animation.Profile));
            bool leftEnabled=_frame>=_hz&&_frame<_hz*3||_frame>=_hz*5&&_frame<_hz*7;
            if(_leftSettings)SetLeftSettings(Animation,leftEnabled);
            if (Input.IsActionJustPressed("crouch_toggle")) _wantsCrouching=!_wantsCrouching;
            var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
            bool ads = Input.IsActionPressed("aim");
            bool fire=Input.IsActionJustPressed("lyra_fire");
            if(fire)Animation.RequestWeaponAction(false);
            if(Input.IsActionJustPressed("lyra_reload"))Animation.RequestWeaponAction(true);
            if(Input.IsActionJustPressed("lyra_emote"))Animation.RequestEmote(_crouching);
            if(Animation.Emote.UncrouchRequested)_wantsCrouching=false;
            var observed=_movement!.Move(new(input,_camera.Yaw,_camera.Pitch,_wantsCrouching,ads,
                Input.IsActionPressed("walk"),Input.IsActionJustPressed("jump"),fire),delta);
            _crouching=_movement.Crouching;
            bool onFloor = _movement!.Grounded;
            float? disable=_rebindFeedback&&_frame>=_hz*3-1&&_frame<=_hz*3?1:null;
            LyraCharacterAnimationCandidate Prepare() => Animation.Prepare(observed.Main,delta,observed.Movement,
                observed.GroundDistance,observed.Hit,observed.Point,observed.Normal,disable);
            var before = Animation.Observation; var rigBefore = Animation.Host.RigState; int published = Animation.Binding.PublishedFrames;
            var notifyBefore=NotifyState(Animation);
            var candidate = Prepare();
            if(_montageEvents)CheckPreparedMontages(candidate.Identity.FrameId);
            if(_leftSettings)
            {Reject(()=>Animation.SetLeftHandPoseOverrideEnabled(!leftEnabled));_leftPendingRejected++;}
            if(_rebindFeedback&&_frame==_hz*3+1)
            {
                Require(Animation.Host.FinalRigDisableLegIK==1&&Animation.Host.Main.Layers.CommittedCurve("DisableLegIK")==0&&
                    candidate.Main.Rig!.Update.Updated.Blend.Target==0,"Rebind lost Main's previous nonzero Rig curve.");_rigCurveCarry++;
            }
            if(_rebindSmoke&&_frame%113==7)
            {Reject(()=>Animation.Rebind(NextProfile(Animation.Profile)));_pendingRebindRejected++;}
            if(_rebindSmoke)foreach(var retired in _retired)retired.Sources.Cancel();
            if (_retry)
            {
                var first = candidate.Output.Pose.ToArray(); var curves = candidate.Output.Curves.ToArray();
                var attributes = candidate.Output.Attributes.ToArray(); var root = candidate.Output.RootMotion;
                if (_frame % 97 == 31)
                {
                    var original = Animation.Binding.Model.Position; Animation.Binding.Model.Position += new Vector3(.02f, 0, 0);
                    Reject(() => Animation.ValidateCommit(candidate)); Animation.Binding.Model.Position = original;
                    Reject(() => Animation.Commit(candidate)); _lateFailures++;
                }
                Animation.Cancel(); Require(Animation.Observation == before && Animation.Host.RigState == rigBefore &&
                    Animation.Binding.PublishedFrames == published&&NotifyState(Animation)==notifyBefore, "Cancelled animation published state or skin.");
                Reject(() => Animation.Commit(candidate));
                if(_montageEvents){CheckDispatchedMontages(Animation,false);_montageCancelledFrames++;}
                if(_leftSettings)Require(Animation.Host.Main.LayerInstances.All(i=>i.LeftHandPoseOverrideEnabled==leftEnabled),
                    "Animation cancellation changed an authored linked setting.");
                var notifyCandidate=candidate.SourceNotifies;
                candidate = Prepare(); Require(first.SequenceEqual(candidate.Output.Pose) && curves.SequenceEqual(candidate.Output.Curves) &&
                    attributes.SequenceEqual(candidate.Output.Attributes) && root == candidate.Output.RootMotion, "Live character retry differs."); _retries++;
                if(_montageEvents)CheckPreparedMontages(candidate.Identity.FrameId);
                Require(notifyCandidate.Queued.SequenceEqual(candidate.SourceNotifies.Queued)&&notifyCandidate.Callbacks.SequenceEqual(candidate.SourceNotifies.Callbacks)&&
                    notifyCandidate.RandomSeed==candidate.SourceNotifies.RandomSeed&&
                    notifyCandidate.MontageWindows.SequenceEqual(candidate.SourceNotifies.MontageWindows)&&
                    notifyCandidate.RelevantSlots==candidate.SourceNotifies.RelevantSlots&&
                    notifyCandidate.MontageRandomSeed==candidate.SourceNotifies.MontageRandomSeed,"Live notify retry differs.");
            }
            Animation.Commit(candidate);
            if (_directionDiagnostic)
            {
                var observedDirection = Animation.Observation;
                var cycle = Animation.Host.Main.Sources.Hosts.Ground.Cycle;
                var warp = cycle.OrientationState;
                var root = Animation.CommittedOutput.Pose[0];
                var facing = new AlsDoubleVector(1, 0, 0).Rotate(root.Rotation);
                var componentRotation = LyraGodotRigCollision.NativeTransform(Animation.Binding.Component.GlobalTransform).Rotation;
                var componentForward = new AlsDoubleVector(1, 0, 0).Rotate(componentRotation);
                var actorRotation = LyraGodotRigCollision.NativeTransform(_body.GlobalTransform).Rotation;
                var relativeForward = new AlsDoubleVector(1, 0, 0).Rotate((actorRotation.Conjugate() * componentRotation).Normalized());
                _directionFrames.Add(new { frame = _frame, profile = Animation.Profile,
                    state = Animation.Host.Main.Machine.State, direction = observedDirection.DirectionNoOffset,
                    angle = observedDirection.DirectionAngle, rootYaw = observedDirection.RootYaw,
                    velocityX = observedDirection.LocalVelocity.X, velocityY = observedDirection.LocalVelocity.Y,
                    asset = cycle.Cycle.AssetId, time = cycle.Cycle.Time, warpAngle = warp.Angle,
                    warpDirectionX = warp.Direction.X, warpDirectionY = warp.Direction.Y,
                    counter = warp.CounterTarget, rootFacingX = facing.X, rootFacingY = facing.Y,
                    componentForwardX = componentForward.X, componentForwardY = componentForward.Y,
                    relativeForwardX = relativeForward.X, relativeForwardY = relativeForward.Y });
            }
            if(_montageEvents)CheckDispatchedMontages(Animation,true);
            if(_leftSettings)CountLeftSettings(Animation,leftEnabled);
            CountNotifies(Animation);
            for(int i=0;i<_companions.Count;i++)
            {
                var companion=_companions[i];double t=_companionFrames*dt+i*.31;
                var companionTarget=_companionOrigins[i]+new Vector3((float)Math.Sin(t)*1.5f,0,(float)Math.Cos(t)*1.5f);
                var offset=companionTarget-companion.Body.Position;var companionDirection=new Vector2(offset.X,offset.Z).LimitLength();
                if(_companionFrames>0&&_companionFrames%(Engine.PhysicsTicksPerSecond*2)==0)
                    companion.Animation.Rebind(NextProfile(companion.Animation.Profile));
                if(_leftSettings)SetLeftSettings(companion.Animation,leftEnabled);
                if(_weaponSmoke&&_companionFrames==_hz/10)companion.Animation.RequestWeaponAction(false);
                if(_weaponSmoke&&_companionFrames==_hz/5)companion.Animation.RequestWeaponAction(true);
                if(_weaponSmoke&&_companionFrames==_hz*6)companion.Animation.RequestWeaponAction(true);
                companion.Advance(new(companionDirection,0,.08f,t%8>=2.5&&t%8<3.7,t%8>=2&&t%8<2.5,false,
                    _companionFrames==Engine.PhysicsTicksPerSecond*4,_weaponSmoke&&_companionFrames==_hz/10),delta);
                if(_montageEvents)CheckDispatchedMontages(companion.Animation,true);
                CountNotifies(companion.Animation);
                if(_leftSettings)CountLeftSettings(companion.Animation,leftEnabled);
            }
            _companionFrames++;
            _terrain?.Advance(Animation, _body, _movement!.Settings, delta, _frame);
            if (_terrain?.Done == true) _done = true;
            if (_smoke) {VerifySkin();foreach(var companion in _companions)VerifySkin(companion.Animation);}
            _states.Add(Animation.Host.Main.Machine.State); _air += !onFloor ? 1 : 0; _crouch += _crouching ? 1 : 0; _aim += ads ? 1 : 0;
            _frame++;
            if (_smoke && _frame >= _hz * 8) Finish();
        }
        catch (Exception e) { Fail(e); }
    }
    private void DriveSmokeInput()
    {
        double t = (double)_frame / _hz;
        foreach (var a in new[] { "move_left", "move_right", "move_forward", "move_back", "walk", "aim", "jump", "crouch_toggle", "switch_item_layer", "lyra_fire", "lyra_reload" }) Input.ActionRelease(a);
        if(_emoteSmoke){Input.ActionRelease("lyra_emote");if(_frame==_hz*3/2)Input.ActionPress("lyra_emote");}
        void Press(string action, bool value) { if (value) Input.ActionPress(action); }
        if (_directionSteady)
        {
            Press("move_forward", t >= .2 && t < 3 || t >= 5 && t < 6.7);
            Press("move_right", t >= 3 && t < 3.8);
            Press("move_back", t >= 6.7 && t < 7.5);
            Press("aim", t >= 4.5 && t < 5);
            Press("walk", t >= 5 && t < 5.4);
            Press("crouch_toggle", _frame == _hz * 5 || _frame == _hz * 57 / 10);
            Press("jump", _frame == _hz * 4);
            return;
        }
        Press("move_forward", t >= .5 && t < 1.5 || t >= 2.5 && t < 3.2 || t >= 5.8 && t < 6.7);
        Press("move_right", t >= 1.5 && t < 2);
        Press("move_back", t >= 6.7 && t < 7.5);
        Press("walk", t >= .5 && t < .8); Press("aim", t >= 2 && t < 2.5 || t >= 5.5 && t < 6);
        Press("crouch_toggle", _frame == _hz * 5 / 2 || _frame == _hz * 37 / 10);
        Press("jump", _frame == _hz * 4);
        if(_weaponSmoke)
        {
            Press("lyra_fire",_frame==_hz/10||_frame==_hz*4);
            Press("lyra_reload",_frame==_hz/5||_frame==_hz*5);
        }
        if (_frame == _hz * 2) _camera.ApplyMouseMotion(new(350, 0));
        if(_rebindSmoke)
        {
            Press("switch_item_layer",new[]{_hz,_hz*21/10,_hz*3,_hz*43/10,_hz*64/10,_hz*69/10}.Contains(_frame));
            if(new[]{_hz*4/5,_hz*6/5,_hz*23/10,_hz*31/10,_hz*46/10,_hz*71/10}.Contains(_frame))SwitchProfile(Animation.Profile);
        }
    }
    private static string NextProfile(string profile)=>profile switch{"unarmed"=>"pistol","pistol"=>"rifle",_=>"unarmed"};
    private void UpdateHud()=>_hud.Text=$"Lyra · {Animation.Profile} · {(_terrain?.Smoke == true ? 2 : 1)+_companions.Count}角色\nWASD 移动 · Ctrl 蹲伏 · Space 跳跃 · 右键瞄准 · 左键开火 · R 换弹 · E 表情动作 · Q 切换装备 · Esc 鼠标" +
        (_terrain is not null ? "\n前方：25 cm 台阶 → 斜坡 → 落差 · 左侧：60 cm 阻挡" : "");
    private void SwitchProfile(string profile)
    {
        var host=Animation.Host;var main=host.Main;var old=main.Layers;
        var oldSettings=main.LayerInstances.Select(i=>i.LeftHandPoseOverrideEnabled).ToArray();
        var oldLeft=main.Layer(LyraLayerHook.LeftHandPose_OverrideState);var oldInstances=main.LayerInstances.ToArray();
        var call=oldLeft.Call(LyraLayerHook.LeftHandPose_OverrideState);
        var observation=Animation.Observation;var tail=main.Sources.Tail;var machine=main.Machine;
        var state=machine.State;var elapsed=machine.Elapsed;var stack=StackState(machine.Stack);
        var graph=main.Sources.Hosts.Ground.Scope.GraphState;var lean=main.Sources.Hosts.Ground.Scope.LeanStates;
        var turn=main.Sources.MainTurnYaw;var feedback=main.Sources.MainFeedback;
        var groups=main.SyncGroups.ToArray();var players=main.SyncPlayers.ToArray();var samples=main.SyncSamples.ToArray();var write=main.WriteIndex;
        var rig=host.RigState;var rigCurve=host.FinalRigDisableLegIK;var inertia=JsonSerializer.Serialize(host.InertiaState);
        var skin=Animation.Binding.Skeleton;var model=Animation.Binding.Model;var published=Animation.Binding.PublishedFrames;
        var notifyState=NotifyState(Animation);
        bool changed=Animation.Rebind(profile);
        Require(ReferenceEquals(host,Animation.Host)&&ReferenceEquals(main,host.Main)&&ReferenceEquals(machine,main.Machine)&&
            Animation.Observation==observation&&main.Sources.Tail==tail&&machine.State==state&&machine.Elapsed==elapsed&&StackState(machine.Stack)==stack&&
            main.Sources.Hosts.Ground.Scope.GraphState==graph&&main.Sources.Hosts.Ground.Scope.LeanStates.SequenceEqual(lean)&&
            main.Sources.MainTurnYaw==turn&&main.Sources.MainFeedback==feedback&&groups.SequenceEqual(main.SyncGroups.ToArray())&&
            players.SequenceEqual(main.SyncPlayers.ToArray())&&samples.SequenceEqual(main.SyncSamples.ToArray())&&write==main.WriteIndex&&
            host.RigState==rig&&host.FinalRigDisableLegIK==rigCurve&&JsonSerializer.Serialize(host.InertiaState)==inertia&&
            ReferenceEquals(model,Animation.Binding.Model)&&ReferenceEquals(skin,Animation.Binding.Skeleton)&&Animation.Binding.PublishedFrames==published&&NotifyState(Animation)==notifyState,
            "Equipment rebind changed Main, Sync, Rig, inertia or model history.");
        if(changed)
        {
            Require(!ReferenceEquals(old,main.Layers)&&main.Layers.Epoch==old.Epoch+1,"Changed class reused its retired instance.");
            main.ValidateLayerRoutes();
            Reject(()=>oldLeft.Call(call.Hook));Reject(()=>main.Layer(call.Hook).PrepareLeftHand(call,false,false,0));
            Reject(()=>old.Sources.Hosts.Ground.Scope.BeginMain(default,0));
            if(_montageEvents)foreach(var retired in oldInstances)
            {Reject(()=>retired.DispatchQueuedMontageEvents(main,_frame-1));_montageRetiredRejected++;}
            if(_leftSettings)
            {
                foreach(var retired in oldInstances)
                {Reject(()=>retired.SetLeftHandPoseOverrideEnabled(main,true));_leftRetiredRejected++;}
                Require(main.LayerInstances.All(i=>!i.LeftHandPoseOverrideEnabled),"New linked class did not initialize its original default.");
            }
            if(_rebindSmoke)_retired.AddRange(oldInstances);_switches++;
        }
        else{Require(ReferenceEquals(old,main.Layers),"Same-class rebind replaced its instance.");_sameClass++;}
        if(_leftSettings&&!changed)
        {Require(oldSettings.SequenceEqual(main.LayerInstances.Select(i=>i.LeftHandPoseOverrideEnabled)),"Same-class rebind changed linked settings.");_leftSameClass++;}
        _bindings.Add(new{frame=_frame,profile=Animation.Profile,epoch=Animation.LayerEpoch,changed,state,elapsed,air=!observation.Ground,crouch=observation.Crouching,ads=observation.Ads});
        UpdateHud();
    }
    private static string StackState(AlsTransitionStackState stack)=>JsonSerializer.Serialize(new{
        stack.CurrentState,stack.Count,stack.Latest,entries=Enumerable.Range(0,stack.Count).Select(i=>stack.GetTransition(i)).ToArray()});
    private void CheckPreparedMontages(long frame)
    {
        Require(Animation.MontageBank.MontageEventsQueued&&!Animation.MontageBank.CommittedMontageEventsQueued,
            "Main queue phase did not enter its unpublished update.");
        foreach(var instance in Animation.Host.Main.LayerInstances)
        {
            Require(instance.PreparedMontageEventsQueued&&!instance.MontageEventsQueued,
                "Linked queue phase must update even without root visibility, without publishing.");
            Reject(()=>instance.DispatchQueuedMontageEvents(Animation.Host.Main,frame));_montagePendingRejected++;
        }
        Reject(()=>Animation.MontageBank.DispatchQueuedMontageEvents());_montagePendingRejected++;
        _montagePreparedFrames++;
    }
    private void CheckDispatchedMontages(LyraCharacterAnimation owner,bool count)
    {
        Require(!owner.MontageBank.MontageEventsQueued&&!owner.MontageBank.CommittedMontageEventsQueued&&
            owner.Host.Main.LayerInstances.All(i=>!i.MontageEventsQueued&&!i.PreparedMontageEventsQueued),
            "Published or cancelled role retained a queued montage phase.");
        if(!count)return;
        foreach(var instance in owner.Host.Main.LayerInstances)
        {
            Reject(()=>instance.DispatchQueuedMontageEvents(new object(),owner.NextIdentity.FrameId-1));_montageForeignRejected++;
            Reject(()=>instance.DispatchQueuedMontageEvents(owner.Host.Main,owner.NextIdentity.FrameId-2));_montageLateRejected++;
        }
        _montagePublishedFrames++;
    }
    private static void SetLeftSettings(LyraCharacterAnimation owner,bool enabled)
    {
        var main=owner.Host.Main;
        foreach(var instance in main.LayerInstances)
            Reject(()=>instance.SetLeftHandPoseOverrideEnabled(new object(),!enabled));
        owner.SetLeftHandPoseOverrideEnabled(enabled);
        Require(main.LayerInstances.All(i=>i.LeftHandPoseOverrideEnabled==enabled),"Linked settings were not applied to every actual instance.");
    }
    private void CountLeftSettings(LyraCharacterAnimation owner,bool enabled)
    {
        var instance=owner.Host.Main.Layer(LyraLayerHook.LeftHandPose_OverrideState);
        Require(instance.LeftHandPoseOverrideEnabled==enabled,"Committed linked setting differs from authored input.");
        if(instance.LeftHandWeight>0)_leftEnabledFrames++;
        else _leftDisabledFrames++;
        Require(enabled||instance.LeftHandWeight==0,"Disabled left-hand override kept an active weight.");
    }
    private void CountNotifies(LyraCharacterAnimation owner)
    {
        Require(owner.SourceNotifies.CommittedFrame==_frame,"Source queue lost the role physics frame.");
        _notifySourceFrames++;
        _notifyCallbacks+=owner.SourceNotifies.Callbacks.Count(c=>c.Reference.Playback.SourceKind==AlsAssetNotifySourceKind.AssetPlayer);
        _notifyMontageCallbacks+=owner.SourceNotifies.Callbacks.Count(c=>c.Reference.Playback.SourceKind==AlsAssetNotifySourceKind.Montage);
        Require(owner.ContextEffects.CommittedFrame==_frame,"ContextEffects lost the role frame.");
        Require(owner.WeaponNotifies.CommittedFrame==_frame&&
            (owner.Weapons.First is not {} gun||gun.LastTick==_frame),"Weapon component lost its prerequisite role frame.");
        Require(owner.CapsuleMoves==_frame+1&&owner.LastMovement.Identity.FrameId==_frame,"Capsule and animation did not share one physical tick.");
        _contextMessages+=owner.ContextEffects.LastMessages.Length;_contextContacts+=owner.ContextEffects.LastMessages.Count(m=>m.Hit.Hit);
    }
    private void VerifySkin(LyraCharacterAnimation? animation=null)
    {
        var owner=animation??Animation;var binding = owner.Binding; var pose = owner.CommittedOutput.Pose;
        var component = LyraGodotRigCollision.NativeTransform(binding.Component.GlobalTransform);
        var global = new AlsPrecisePose[81];
        var bank = _resources!.Catalog.Bank;
        for (int b = 0; b < 81; b++) global[b] = bank.Parents[b] < 0 ? pose[b] : AlsPrecisePose.Compose(pose[b], global[bank.Parents[b]]);
        for (int b = 0; b < 68; b++)
        {
            var expected = LyraAlsCharacterBinding.Convert(pose[binding.Bones[b]]);
            var position = binding.Skeleton.GetBonePosePosition(b);
            var rotation = binding.Skeleton.GetBonePoseRotation(b);
            _maxSkinPosition = Math.Max(_maxSkinPosition, position.DistanceTo(new(expected.Position.X, expected.Position.Y, expected.Position.Z)));
            _maxSkinRotation = Math.Max(_maxSkinRotation, Math.Max(Math.Abs(rotation.X - expected.Rotation.X), Math.Max(Math.Abs(rotation.Y - expected.Rotation.Y),
                Math.Max(Math.Abs(rotation.Z - expected.Rotation.Z), Math.Abs(rotation.W - expected.Rotation.W)))));
            var world = binding.Skeleton.ToGlobal(binding.Skeleton.GetBoneGlobalPose(b).Origin);
            var wanted = LyraGodotRigCollision.Position((component.Scale * global[binding.Bones[b]].Position).Rotate(component.Rotation) + component.Position);
            _maxWorldPosition = Math.Max(_maxWorldPosition, world.DistanceTo(wanted));
        }
        Require(_maxSkinPosition == 0 && _maxSkinRotation <= 2e-7 && _maxWorldPosition <= 1e-4,
            $"Model/Rig coordinate mismatch skin={_maxSkinPosition:R} q={_maxSkinRotation:R} world={_maxWorldPosition:R}");
    }
    private void CaptureRenderedFrame()
    {
        if (_captureDirectory is null || _animation is null || _done) return;
        try
        {
            var samples = _directionSteady
                ? new[] { ("forward", 2.5), ("change-early", 3.02), ("change-mid", 3.08), ("change-late", 3.2), ("right", 3.5), ("right-end", 3.78), ("stop", 3.95) }
                : _directionDiagnostic
                ? new[] { ("forward", 1.2), ("change-early", 1.52), ("change-mid", 1.58), ("change-late", 1.7), ("right", 1.9), ("right-end", 1.98), ("stop", 2.15) }
                : new[] { ("standing", .3), ("movement", 1.2), ("aim", 2.3), ("crouching", 3.1), ("jump", 4.35), ("landing", 5.5), ("reverse", 7.2) };
            foreach (var (name, seconds) in samples)
                if (_frame >= _hz * seconds && _captures.Add(name))
                {
                    using var image = GetViewport().GetTexture().GetImage();
                    Require(image.SavePng(Path.Combine(_captureDirectory, name + ".png")) == Error.Ok, "Failed to save rendered frame.");
                    _captureFrames.Add(new{name,physicsFrame=_frame,physicsTick=Engine.GetPhysicsFrames(),drawnFrame=Engine.GetFramesDrawn(),
                        profile=Animation.Profile,layerEpoch=Animation.LayerEpoch,state=Animation.Host.Main.Machine.State});
                }
        }
        catch (Exception e) { Fail(e); }
    }
    private void Finish()
    {
        Require(_air > 0 && _crouch > 0 && _aim > 0 && Animation.PhysicsQueries > 0 && _states.Contains(0) && _states.Contains(2), "Incomplete live character coverage.");
        Require(!_retry || _retries == _frame && _lateFailures > 0, "Incomplete live retry coverage.");
        Require(_notifySourceFrames==_frame*(1+_companions.Count)&&_notifyCallbacks>0,"Ordinary source queue did not publish callbacks.");
        Require(!_weaponSmoke||Animation.WeaponNotifies.Played+_companions.Sum(c=>c.Animation.WeaponNotifies.Played)>0,
            "Ordinary Fire/Reload did not reach weapon components.");
        Require(_captureDirectory is null || _captures.Count == 7, "Missing actual rendered captures.");
        if(_captureDirectory is not null)
        {
            Require(_captureFrames.Count==7,"Missing post-draw capture identity.");
            using var frames=new FileStream(Path.Combine(_captureDirectory,"frames.json"),FileMode.CreateNew);
            JsonSerializer.Serialize(frames,new{phase="FramePostDraw",frames=_captureFrames});
        }
        Require(_companions.All(c=>c.Animation.Binding.PublishedFrames==_frame&&c.Animation.PhysicsQueries>0&&
            ReferenceEquals(c.Animation.SourceBank,_resources!.Catalog.Bank))&&
            _companions.Select(c=>c.Animation.CharacterId).Append(Animation.CharacterId).Distinct().Count()==1+_companions.Count,
            "Incomplete shared-resource companion publication/identity.");
        if(_rebindSmoke)
        {
            Require(_switches==6&&_sameClass==6&&_pendingRebindRejected>0,"Incomplete production rebind coverage.");
            Require(_bindings.OfType<object>().Count()==12,"Missing actual binding sequence.");
            Require(!_rebindFeedback||_rigCurveCarry==1,"Missing nonzero Main Rig feedback carry.");
        }
        var report = new { profile = Profile, hz = _hz, frames = _frame, published = Animation.Binding.PublishedFrames,
            skinBones = 68, logicalBones = 81, finalRig = Animation.Host.HasFinalFootPlant, actualCharacterMovement = true,
            actualGodotPhysics = true, retries = _retries, lateFailures = _lateFailures, queries = Animation.PhysicsQueries,
            sharedShooterMovement=true,characterMotorSha256=_movement!.Settings.Sha256,
            air = _air, crouch = _crouch, aim = _aim, states = _states.Order().ToArray(),
            maxSkinPositionM = _maxSkinPosition, maxSkinQuaternion = _maxSkinRotation, maxWorldPositionM = _maxWorldPosition,
            captures = _captures.Count,sourceNotifyFrames=_notifySourceFrames,sourceNotifyCallbacks=_notifyCallbacks,
            sourceNotifyQueue=true,montageNotifyQueue=true,montageNotifyCallbacks=_notifyMontageCallbacks,
            gameplayNotifyConsumer=true,contextEffectsConsumer=true,contextAudioPlayback=false,weaponNotifyConsumer=true,motionWarpingConsumer=false,
            weaponPublications=Animation.Weapons.Publications,weaponPlayed=Animation.WeaponNotifies.Played,weaponMissing=Animation.WeaponNotifies.Missing,
            capsuleMoves=Animation.CapsuleMoves,rootCapsuleMoves=Animation.RootCapsuleMoves,montageOnlyRootMovement=true,
            contextMessages=_contextMessages,contextContacts=_contextContacts,
            typedNotifyConsumers=false,nativeWholeMainParity = false, productionAccepted = false };
        object saved=_rebindSmoke?new{model=report,switches=_switches,sameClassReuse=_sameClass,pendingRebindRejected=_pendingRebindRejected,rigCurveCarry=_rigCurveCarry,bindings=_bindings}:report;
        if (_directionDiagnostic) saved = new { model = saved, directionChanges = _directionFrames };
        if(_emoteSmoke)
        {
            Require(Animation.Emote.Activations==1&&Animation.Emote.Ends==1&&Animation.Emote.MovementClears==1&&!Animation.Emote.Active,"Ordinary E input did not reach original movement cancellation.");
            saved=new{model=saved,emote=new{Animation.Emote.Activations,Animation.Emote.Ends,Animation.Emote.MovementClears}};
        }
        if(_companions.Count>0)saved=new{player=saved,characters=1+_companions.Count,companions=_companions.Select(c=>new{
            id=c.Animation.CharacterId,profile=c.Animation.Profile,epoch=c.Animation.LayerEpoch,
            published=c.Animation.Binding.PublishedFrames,queries=c.Animation.PhysicsQueries,
            weaponPublications=c.Animation.Weapons.Publications,weaponPlayed=c.Animation.WeaponNotifies.Played,weaponMissing=c.Animation.WeaponNotifies.Missing,
            capsuleMoves=c.Animation.CapsuleMoves,rootCapsuleMoves=c.Animation.RootCapsuleMoves}).ToArray()};
        if(_leftSettings)
        {
            Require(_leftEnabledFrames>0&&_leftDisabledFrames>0&&_leftEnabledFrames+_leftDisabledFrames==_frame*(1+_companions.Count)&&
                _leftPendingRejected==_frame&&_leftRetiredRejected==_switches*Animation.Host.Main.LayerInstances.Count&&_leftSameClass==6,
                "Incomplete live left-hand settings coverage.");
            var settings=new{enabledFrames=_leftEnabledFrames,disabledFrames=_leftDisabledFrames,pendingRejected=_leftPendingRejected,
                retiredRejected=_leftRetiredRejected,sameClassPreserved=_leftSameClass,nullSequence=true};
            saved=new{model=saved,leftSettings=settings};
            GD.Print("LYRA_LINKED_LEFT_SETTINGS_DEMO_OK "+JsonSerializer.Serialize(settings));
        }
        if(_montageEvents)
        {
            int owners=Animation.Host.Main.LayerInstances.Count;
            Require(_montagePublishedFrames==_frame*(1+_companions.Count)&&_montagePreparedFrames==_frame*2&&
                _montageCancelledFrames==_frame&&_montagePendingRejected==_frame*2*(owners+1)&&
                _montageForeignRejected==_montagePublishedFrames*owners&&_montageLateRejected==_montageForeignRejected&&
                _montageRetiredRejected==_switches*owners,"Incomplete live montage queue lifecycle checks.");
            var phases=new{publishedFrames=_montagePublishedFrames,preparedFrames=_montagePreparedFrames,cancelledFrames=_montageCancelledFrames,
                pendingRejected=_montagePendingRejected,foreignRejected=_montageForeignRejected,lateRejected=_montageLateRejected,
                retiredRejected=_montageRetiredRejected,linkedBanksEmpty=true,fullMontageEventDispatch=false};
            saved=new{model=saved,montagePhases=phases};
            GD.Print("LYRA_LINKED_MONTAGE_PHASE_DEMO_OK "+JsonSerializer.Serialize(phases));
        }
        using var stream = new FileStream(_report!, FileMode.CreateNew); JsonSerializer.Serialize(stream, saved);
        GD.Print((_companions.Count>0?"LYRA_MAIN_MULTI_DEMO_GODOT_OK ":_rebindSmoke?"LYRA_MAIN_REBIND_GODOT_OK ":"LYRA_MAIN_MODEL_GODOT_OK ") + JsonSerializer.Serialize(saved)); _done = true; GetTree().Quit();
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Stale live character candidate accepted."); }
    private void Fail(Exception e) { _done = true; GD.PushError("Lyra Main character failed: " + e); GetTree().Quit(1); }
    public override void _ExitTree() { if(_captureSubscribed)RenderingServer.FramePostDraw-=CaptureRenderedFrame;_terrain?.Dispose();foreach(var companion in _companions)companion.Dispose();_animation?.Dispose();_movement?.Dispose(); _resources?.Dispose(); }
}
