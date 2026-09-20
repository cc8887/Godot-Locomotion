using Godot;
using GodotAls.Assets;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

// Actual Character/Motor/process groups. Each owner has an independent exchange,
// and all owners share the same immutable animation/source definition.
public partial class RefactoredFootDispatchSmoke : Node3D
{
    private AlsP3RuntimeContext _context = null!;
    private readonly List<AlsP3Character> _characters = [];
    private long[] _lastFrames = [], _events = [];
    private int _hz = 60, _count = 1, _tick, _phase, _pauseOwner, _resumeTick, _completedPauses;
    private AlsHarnessMode _mode;
    private AlsFrameInput _pausedInput;
    private AlsRefactoredFootRigState _pausedRig;
    private AlsBasedFootLockFrameState _pausedLocks;
    private ulong _pausedSerial;
    private Skeleton3D? _pausedSkeleton;
    private Transform3D[] _pausedBones = [];
    private long _pausedEvents, _pausedCommit;
    private long _prepareExcess, _queryExcess;
    private int _commitPhase, _commitOwner, _commitResumeTick;
    private AlsFrameInput _commitInput;
    private AlsP3SplitFootDiagnostics _commitStages;
    private long _commitEvents;
    private ulong _poseDigest = AlsResultDigest.OffsetBasis, _resultDigest = AlsResultDigest.OffsetBasis;
    private ulong _rootDigest = AlsResultDigest.OffsetBasis;
    private int _air, _crouch, _locked;
    private bool _done;
    private bool _captureGraph;
    private bool _overlayCycle;
    private bool _contactPlatform;
    private bool _contactStatic;
    private bool TestContacts => _contactPlatform || _contactStatic;
    private int _contactFrames;
    private int _clearanceFrames;
    private int _toeFrames;
    private bool[] _toeOwners = [];
    private bool[] _contactOwners = [];
    private int _armedFrames;
    private AlsProductionGraphCapture? _pausedGraph, _commitGraph;

    public override void _Ready()
    {
        try
        {
            Require(AlsP3FrameStages.SplitFeet, "Production split foot flag is required.");
            _captureGraph=OS.GetCmdlineUserArgs().Contains("--production-graph-capture");
            _overlayCycle=OS.GetCmdlineUserArgs().Contains("--overlay-cycle");
            _contactPlatform=OS.GetCmdlineUserArgs().Contains("--contact-platform");
            _contactStatic=OS.GetCmdlineUserArgs().Contains("--contact-static");
            Require(!_contactStatic || !_contactPlatform, "Select one contact surface fixture.");
            Require(!TestContacts || OS.GetCmdlineUserArgs().Contains("--foot-lock-final-contact"),
                "Contact coverage requires final-contact anchoring.");
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--hz=")) _hz = int.Parse(arg[5..]);
                if (arg.StartsWith("--characters=")) _count = int.Parse(arg[13..]);
                if (arg == "--parallel") _mode = AlsHarnessMode.Parallel;
            }
            Require(_hz is 30 or 60 or 120 && _count is 1 or 10, "Expected 30/60/120 Hz and 1/10 characters.");
            Engine.PhysicsTicksPerSecond = _hz;
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe;
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_cycle_locomotion_profile.json"), set);
            var settings = AlsLocomotionSettings.Load(Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));
            var motor = new AlsMotorSettings(.35f, settings.StandingHalfHeight * 2, settings.CrouchedHalfHeight * 2,
                settings.Standing, settings.Crouching, settings.InitialMaxAcceleration, settings.InitialMaxBrakingDeceleration,
                settings.Gravity, settings.JumpSpeed, 1, settings.VelocityAngleInterpolationStart, settings.VelocityAngleInterpolationEnd);
            _context = new(_mode, settings, motor, set, profile, System.Environment.CurrentManagedThreadId, true);
            _lastFrames = new long[_count]; _events = new long[_count];
            _contactOwners = new bool[_count];
            _toeOwners = new bool[_count];
            _context.AnimationEventCommitted += (identity, _) => _events[checked((int)identity.CharacterId)]++;
            PhysicsBody3D floor = _contactPlatform ? new AnimatableBody3D { SyncToPhysics = true } : new StaticBody3D();
            floor.Position = new(0, -.5f, 0); floor.CollisionLayer = floor.CollisionMask = 1;
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 1, 200) } });
            AddChild(floor);
            for (var i = 0; i < _count; i++)
            {
                var character = new AlsP3Character { Name = "Character" + i, Position = new(i * 5, motor.StandingHeight * .5f, 0) };
                AddChild(character); _characters.Add(character);
                character.Configure(_context, new AlsSlotHandle(checked((uint)i), 1), new Commands(_hz, _overlayCycle, i));
                character.SetActive(true);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _characters.Count != _count) return;
        try
        {
            _tick++;
            Require(_tick <= _hz * 6 + 30, "Dispatch did not recover before the timeout.");
            ObserveCommitHold();
            if (_phase == 1) CancelAtBoundary();
            else if (_phase == 2)
            {
                var paused = _characters[_pauseOwner];
                Require(!paused.SplitFootDiagnostics.Pending && paused.RuntimeCommittedFrameId == _pausedCommit &&
                    paused.FullMovementDiagnostics.RefactoredRig == _pausedRig && paused.FullMovementDiagnostics.RefactoredLocks == _pausedLocks &&
                    _pausedBones.SequenceEqual(CaptureBones(_pausedSkeleton!)) && _events[_pauseOwner] == _pausedEvents,
                    "Paused owner advanced its committed pose, history or callbacks.");
                Require(ReferenceEquals(paused.FullMovementDiagnostics.GraphCapture,_pausedGraph),
                    "Canceled candidate replaced the committed graph snapshot.");
                if (_tick == _resumeTick)
                {
                    paused.GetNode<Node>("FootPhysicsQuery").ProcessMode = ProcessModeEnum.Inherit;
                    paused.SetActive(true); _phase = 3;
                }
            }

            for (var i = 0; i < _count; i++)
            {
                var character = _characters[i];
                Require(!character.IsPoseFrozen && character.FailureDiagnosticCount == 0,
                    $"Dispatch failed owner={i} tick={_tick} published={character.PublishedFrameId} committed={character.RuntimeCommittedFrameId}.");
                if (i == _pauseOwner && _phase is 2 or 3 && character.RuntimeCommittedFrameId == _pausedCommit) continue;
                var frame = character.Diagnostics;
                if (frame.CommittedFrameId == _lastFrames[i]) continue;
                Require(frame.CommittedFrameId == _lastFrames[i] + 1, "Committed frame sequence skipped or duplicated.");
                var full = character.FullMovementDiagnostics; var split = character.SplitFootDiagnostics;
                if (full.RefactoredRig.LeftToePinned || full.RefactoredRig.RightToePinned)
                { _toeFrames++; _toeOwners[i] = true; }
                if (_contactStatic) Require(full.RefactoredLocks.Left.BaseIdentity == 0 && full.RefactoredLocks.Right.BaseIdentity == 0,
                    "Static contact must not manufacture an ALS movement base.");
                if (full.RefactoredRig.LeftClearanceCorrected || full.RefactoredRig.RightClearanceCorrected) _clearanceFrames++;
                if (full.RefactoredRig.Left.Location.ContactApplied && !full.RefactoredRig.LeftClearanceCorrected ||
                    full.RefactoredRig.Right.Location.ContactApplied && !full.RefactoredRig.RightClearanceCorrected)
                { _contactFrames++; _contactOwners[i] = true; }
                Require(full.Overlay == character.LatestMotorInput.Command.RequestedOverlay &&
                    full.Overlay == Commands.OverlayAt(frame.CommittedFrameId, _hz, _overlayCycle, i),
                    "Committed Overlay differs from the captured owner/frame selection.");
                if (full.Overlay != AlsOverlayKind.Default) _armedFrames++;
                if(_captureGraph)Require(full.GraphCapture is { } capture && capture.Identity==frame.Identity &&
                    capture.Row.ContainsKey("pose") && capture.Row.ContainsKey("curves"),
                    "Published graph snapshot is incomplete or belongs to another frame.");
                Require(frame.Identity == character.LatestMotorInput.Identity && frame.Identity == full.Identity &&
                    frame.Identity == full.FootPoseIdentity && frame.Identity == split.Request.Identity &&
                    split.Response.Queries == split.Request && !split.Pending && full.LockCurveProducersMatch,
                    "Pose/query/final curve identity differs across stages.");
                if (_phase == 3 && i == _pauseOwner)
                {
                    Require(character.LatestMotorInput.Equals(_pausedInput) && split.Request.RequestSerial > _pausedSerial,
                        "Resume must retry the interrupted input with a fresh query serial.");
                    _phase = 0; _completedPauses++;
                }
                _lastFrames[i] = frame.CommittedFrameId;
                _poseDigest = unchecked((_poseDigest ^ frame.FullPoseDigest) * 1099511628211UL);
                _rootDigest = unchecked((_rootDigest ^ frame.RootDigest) * 1099511628211UL);
                AlsResultDigest.Append(ref _resultDigest, frame.Result);
                if (_commitPhase == 3 && i == _commitOwner)
                {
                    Require(character.LatestMotorInput.Equals(_commitInput), "Main commit must consume the held input exactly once.");
                    _commitPhase = 4;
                }
                if (frame.Result.ResolvedLocomotionState == AlsLocomotionState.InAir) _air++;
                if (frame.Result.ActualStance == AlsStance.Crouching) _crouch++;
                if (frame.Result.LeftFootPose.LockAmount > .5f || frame.Result.RightFootPose.LockAmount > .5f) _locked++;
            }
            Require(_context.AffinityViolations == 0 && _context.AnimationEventHandlerFailures == 0, "Thread affinity/event dispatch violation.");
            if (_phase == 0 && _completedPauses < 2 && _tick == (_completedPauses == 0 ? _hz : TestContacts ? _hz * 11 / 2 : _hz * 3))
            {
                _pauseOwner = _completedPauses % _count;
                // Disable a real stage for one tick: no synthetic query or input.
                var split = _characters[_pauseOwner].SplitFootDiagnostics;
                _prepareExcess = split.Prepared - split.Resumed;
                _queryExcess = split.Queried - split.Resumed;
                _characters[_pauseOwner].GetNode<Node>("VisualWorker").ProcessMode = ProcessModeEnum.Disabled;
                if (_completedPauses == 0)
                    _characters[_pauseOwner].GetNode<Node>("FootPhysicsQuery").ProcessMode = ProcessModeEnum.Disabled;
                _phase = 1;
            }
            if (_tick == (TestContacts ? _hz * 23 / 4 : _hz * 4))
            {
                _commitOwner = 2 % _count;
                _characters[_commitOwner].GetNode<Node>("Commit").ProcessMode = ProcessModeEnum.Disabled;
                _commitPhase = 1;
            }
            if (_lastFrames.Any(f => f < _hz * 6)) return;
            Require(_completedPauses == 2 && _commitPhase == 4 && _air > 0 && _crouch > 0 && _locked > 0, "Incomplete movement/cancellation coverage.");
            Require(_mode != AlsHarnessMode.Parallel || _characters.All(c => c.WorkerObservedOffMainThread), "Parallel owners never used worker threads.");
            Require(!TestContacts || _contactFrames > _count * 10 && _contactOwners.All(p => p),
                "Not every character established a real final contact anchor.");
            if (TestContacts) GD.Print($"FINAL_CONTACT_DISPATCH_OK owners={_count} frames={_contactFrames} warm_cancel=true static={_contactStatic}");
            if (OS.GetCmdlineUserArgs().Contains("--foot-ground-clearance"))
            {
                Require(_clearanceFrames > 0, "Unplanted geometry correction was not exercised.");
                GD.Print($"UNPLANTED_CLEARANCE_DISPATCH_OK frames={_clearanceFrames} owners={_count}");
            }
            if (OS.GetCmdlineUserArgs().Contains("--foot-contact-toes"))
            {
                Require(_toeFrames > _count * 10 && _toeOwners.All(p => p), "Every owner must exercise actual toe contacts.");
                GD.Print($"TOE_CONTACT_DISPATCH_OK frames={_toeFrames} owners={_count}");
            }
            Require(!_overlayCycle || _armedFrames > 0 && _characters.All(c => c.FullMovementDiagnostics.Overlay == AlsOverlayKind.Default),
                "Overlay cycle did not equip and return to Default.");
            if (_overlayCycle) GD.Print($"OVERLAY_DISPATCH_OK armed_frames={_armedFrames} owners={_count} frame_selection_exact=true return_default=true");
            GD.Print($"REFACTORED_FOOT_DISPATCH_OK hz={_hz} characters={_count} mode={_mode} frames={_lastFrames.Sum()} " +
                $"cancellations={_completedPauses} commit_holds=1 air={_air} crouch={_crouch} locked={_locked} events={_events.Sum()} " +
                $"rays={_characters.Sum(c => c.SplitFootDiagnostics.Rays)} pose={_poseDigest:X16} root={_rootDigest:X16} result={_resultDigest:X16}");
            Stop(); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }

    private void ObserveCommitHold()
    {
        if (_commitPhase is not (1 or 2)) return;
        var character = _characters[_commitOwner];
        var stages = character.SplitFootDiagnostics;
        if (_commitPhase == 1)
        {
            Require(character.PublishedFrameId == character.RuntimeCommittedFrameId + 1 &&
                character.ResultPublishedFrameId == character.PublishedFrameId && !stages.Pending,
                "Main commit hold did not leave a completed worker result.");
            _commitInput = character.LatestMotorInput; _commitStages = stages;
            _commitGraph=character.FullMovementDiagnostics.GraphCapture;
            _commitEvents = _events[_commitOwner]; _commitResumeTick = _tick + 2; _commitPhase = 2;
            return;
        }
        Require(character.LatestMotorInput.Equals(_commitInput) && stages == _commitStages &&
            character.FullMovementDiagnostics.FootPoseIdentity == _commitInput.Identity && _events[_commitOwner] == _commitEvents,
            "Waiting for main commit advanced motor, animation, queries or callbacks.");
        Require(ReferenceEquals(character.FullMovementDiagnostics.GraphCapture,_commitGraph),
            "Waiting for main commit replaced the completed graph snapshot.");
        if (_tick == _commitResumeTick)
        {
            character.GetNode<Node>("Commit").ProcessMode = ProcessModeEnum.Inherit;
            _commitPhase = 3;
        }
    }

    private void CancelAtBoundary()
    {
        var character = _characters[_pauseOwner]; var split = character.SplitFootDiagnostics;
        Require(split.Pending && character.PublishedFrameId == character.RuntimeCommittedFrameId + 1,
            "The intended stage boundary did not leave an uncommitted frame.");
        Require(split.Prepared == split.Resumed + _prepareExcess + 1 && split.Queried == split.Resumed + _queryExcess + (_completedPauses == 0 ? 0 : 1),
            "Cancellation occurred on the wrong side of the physical query.");
        _pausedInput = character.LatestMotorInput; _pausedSerial = split.Request.RequestSerial;
        _pausedCommit = character.RuntimeCommittedFrameId;
        _pausedSkeleton = character.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().Single();
        _pausedBones = CaptureBones(_pausedSkeleton);
        _pausedRig = character.FullMovementDiagnostics.RefactoredRig; _pausedLocks = character.FullMovementDiagnostics.RefactoredLocks;
        if (TestContacts && _completedPauses == 1)
        {
            Require(_pausedRig.Left.Location.ContactApplied && !_pausedRig.LeftClearanceCorrected ||
                _pausedRig.Right.Location.ContactApplied && !_pausedRig.RightClearanceCorrected,
                "Late cancellation must interrupt an established final contact.");
            if (OS.GetCmdlineUserArgs().Contains("--foot-contact-toes"))
                Require(_pausedRig.LeftToePinned || _pausedRig.RightToePinned, "Late cancellation must interrupt a toe contact.");
        }
        _pausedGraph=character.FullMovementDiagnostics.GraphCapture;
        _pausedEvents = _events[_pauseOwner];
        character.SetActive(false);
        Require(!character.SplitFootDiagnostics.Pending && !character.Visible, "Deactivation retained a candidate or visible pose.");
        _resumeTick = _tick + 2; _phase = 2;
    }
    private sealed class Commands(int hz, bool overlayCycle, int owner) : IAlsLocomotionCommandSource
    {
        public static AlsOverlayKind OverlayAt(long frame, int hz, bool enabled, int owner) => !enabled || frame < hz || frame >= hz * 5
            ? AlsOverlayKind.Default : (frame < hz * 3) == (owner % 2 == 0) ? AlsOverlayKind.Rifle : AlsOverlayKind.Pistol2H;
        public AlsLocomotionCommand GetCommand(long frame) => AlsLocomotionCommand.CreateDefault() with
        {
            RequestedOverlay = OverlayAt(frame, hz, overlayCycle, owner),
            ViewYaw = overlayCycle ? .7f * MathF.Sin((float)frame / hz) : 0,
            AimYaw = overlayCycle ? .7f * MathF.Sin((float)frame / hz) : 0,
            RequestedRotationMode = overlayCycle ? AlsRotationMode.Aiming : AlsRotationMode.LookingDirection,
            MovementAxes = frame < hz / 4 || frame > hz * 4.5 ? default :
                frame < hz * 1.5 ? new(0, 1) : frame < hz * 2.5 ? new(-1, 0) : new(1, 0),
            RequestedGait = AlsGait.Running,
            RequestedStance = frame >= hz * 3 && frame < hz * 4 ? AlsStance.Crouching : AlsStance.Standing,
            JumpPressed = frame == hz / 2 ? (byte)1 : (byte)0,
        };
    }
    private void Stop()
    {
        _done = true; SetPhysicsProcess(false);
        foreach (var character in _characters)
        {
            character.SetActive(false); character.DisposeRuntime();
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static Transform3D[] CaptureBones(Skeleton3D skeleton) =>
        Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBonePose).ToArray();
    private void Fail(Exception error)
    { _done = true; SetPhysicsProcess(false); GD.PushError(error.ToString()); GetTree().Quit(1); }
}
