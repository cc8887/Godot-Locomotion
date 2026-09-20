using Godot;
using GodotAls.Animation;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;
using NMatrix = System.Numerics.Matrix4x4;

namespace GodotAls.Locomotion;

// Real animation graph and real current-frame rays; motor and ragdoll observations
// are controlled. The ordinary demo's process-group dispatcher is a later caller
// of this same explicit pause/resume boundary, not a second foot implementation.
public partial class RefactoredLayeredFootSmoke : Node3D, IAlsGroundedFrameRuntimeSink
{
    private readonly AlsLayeredAnimationFrameRuntime[] _owners = new AlsLayeredAnimationFrameRuntime[2];
    private readonly AlsAnimationLibraryBuildResult[] _libraries = new AlsAnimationLibraryBuildResult[2];
    private readonly AlsLocomotionGraphBuildResult[] _graphs = new AlsLocomotionGraphBuildResult[2];
    private AlsRefactoredFootQueryGather _gather = null!;
    private AlsMovementGraphDefinition _definition = null!;
    private AlsRawAnimationSkeletonDefinition _skeleton = null!;
    private AlsStandingMovementSettings _movementSettings;
    private AlsRefactoredGroundPrediction _prediction = null!;
    private AlsRefactoredPoseCurveReader _reader = null!;
    private AlsRefactoredAnimationFeedback _feedback;
    private AlsLocalPose[] _lastPose = [];
    private AlsNamedPoseSnapshot? _snapshot;
    private string _meshName = "";
    private int _hz = 60, _tick, _frame, _queries, _hidden, _mixed, _retries, _foreign, _changed, _poseChanged, _locked, _worker, _blockIndex, _pelvis;
    private AlsLocomotionState _previousState;
    private ulong _predictionSerial;
    public override void _Ready()
    {
        try
        {
            Require(OS.GetCmdlineUserArgs().Contains("--refactored-pose-curves"), "Full Refactored curve producers must be enabled.");
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--als-hz=")) _hz = int.Parse(arg[9..]);
            Require(_hz is 30 or 60 or 120, "Unsupported physics frequency."); Engine.PhysicsTicksPerSecond = _hz;
            var floor = new StaticBody3D { Position = new(0, -.25f, 0), CollisionLayer = 1 };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(20, .2f, 20) } }); AddChild(floor);
            _gather = new(this, 1, []);
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
            var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, profile);
            _definition = AlsMovementGraphDefinition.Load(set, profile, pose).WithSharedRootSources(set);
            _skeleton = _definition.OverlayRawSources.GetSkeleton(pose.SkeletonId);
            _meshName = set.SkeletalMeshes[_definition.MannequinMeshId].Name;
            _movementSettings = AlsLocomotionInputCompiler.Compile(Read("v4_locomotion_inputs.json")).Movement;
            _prediction = AlsRefactoredGroundPredictionCompiler.Compile(Read("refactored_ground_prediction_inputs.json")).Model;
            for (var i = 0; i < 2; i++)
            {
                var library = _libraries[i] = AlsAnimationLibraryBuilder.BuildP5a(set, _definition.Binding);
                library.UseMovementSources(set, _definition.RawSources); AddChild(library.Root);
                _graphs[i] = AlsLocomotionGraphBuilder.Build(library, profile, pose, set, _definition.Binding);
                _owners[i] = new(_definition, library, _graphs[i].StandingCycle!, set, pose, 11, 2,
                    enableFootIk: true, enableBasedFootLock: true, enableRefactoredFeet: true);
            }
            _reader = new(_owners[0].CurveNames); _blockIndex = _owners[0].CurveNames.IndexOf("GroundPredictionBlock");
            _lastPose = _skeleton.ReferencePose.ToArray();
            _pelvis = Array.FindIndex(_skeleton.LogicalBoneNames.ToArray(), n => n.Equals("pelvis", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_tick++ == 0) return;
        try
        {
            _frame++; RunFrame();
            if (_frame < _hz * 5) return;
            Require(_queries > 0 && _changed > 0 && _poseChanged > 0 && _locked > 0 && _hidden > 0 && _mixed > 0 && _foreign > 0 && _worker != 0,
                $"Missing full foot coverage queries={_queries} changed={_changed} locked={_locked} hidden={_hidden} mixed={_mixed} foreign={_foreign} worker={_worker}.");
            GD.Print($"REFACTORED_LAYERED_FOOT_OK hz={_hz} frames={_frame} owners=2 rays={_queries} pelvis_state={_changed} changed_final_pelvis={_poseChanged} locked={_locked} hidden={_hidden} mixed={_mixed} retries={_retries} stale_responses={_foreign} order=spine_feet_hands_root queries=main halves=worker pose_curves_locks_sync_events=identical commit=atomic motor=controlled demo_dispatch=pending");
            SetPhysicsProcess(false); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private void RunFrame()
    {
        var id = new AlsFrameIdentity(_frame, 11, 2); var dt = 1f / _hz; var seconds = (_frame - 1) * dt;
        var state = seconds is >=2.2f and <3.2f ? AlsLocomotionState.Ragdoll :
            seconds is >=1.5f and <1.9f ? AlsLocomotionState.InAir : AlsLocomotionState.Grounded;
        if (_previousState == AlsLocomotionState.Ragdoll && state != _previousState)
        {
            var physical = new AlsLocalPose[_skeleton.PhysicalBoneCount];
            for (var bone = 0; bone < physical.Length; bone++) physical[bone] = _lastPose[_skeleton.PhysicalToLogical[bone]];
            _snapshot = new(id, _definition.RagdollPose.SnapshotName, _meshName, _skeleton.RawBoneNames, physical);
        }
        var moving = seconds is >=.3f and <1.2f || seconds is >=3.8f and <4.3f;
        var velocity = new NVector3(moving ? MathF.Sin(seconds * 3) * 2 : 0, state == AlsLocomotionState.InAir ? -3 : 0, moving ? -3 : 0);
        var component = new AlsLocalPose(new(.1f * MathF.Sin(seconds), 0, 0), AlsFootIkCoordinates.FbxToGodotRotation, NVector3.One);
        var predictionQuery = _prediction.Prepare(new(id, new(0, 0, 100), new(0, 0, velocity.Y * 100), 1, 35, 90, .7f, _feedback.GroundPredictionBlock), ++_predictionSerial);
        var input = AlsFrameInput.CreateDefault(id, dt) with
        {
            ActualVelocity = velocity, ActualAcceleration = moving ? new(2, 0, -3) : default, MaxAcceleration = 8, MaxBrakingDeceleration = 16,
            InputDirection = moving ? new(.2f, 0, -.8f) : default,
            Floor = new((byte)(state == AlsLocomotionState.InAir ? 0 : 1), NVector3.UnitY, -1, NMatrix.Identity, default),
            LandPrediction = new() { Queried = 1, BlockingHit = 1, Walkable = 1, Time = .3f },
            RefactoredGroundPrediction = new(1, _feedback, new(predictionQuery, true, false, .3f, new(0, 0, 1))),
            FootIk = new(1, _owners[0].CommittedIdentity, component, AlsLocalPose.Identity, AlsLocalPose.Identity,
                default, System.Numerics.Quaternion.Identity, velocity, dt)
        };
        input = input with { Command = input.Command with { AimYaw = .4f * MathF.Sin(seconds), AimPitch = .3f * MathF.Cos(seconds),
            MovementAxes = moving ? new(.2f, .8f) : default } };
        var result = AlsFrameResult.CreateDefault(id); result.ResolvedLocomotionState = state;
        result.ActualStance = seconds >=3.5f ? AlsStance.Crouching : AlsStance.Standing;
        result.ActualGait = AlsGait.Running; result.ActualRotationMode = AlsRotationMode.LookingDirection;
        result.BlendCoordinates = new(velocity.X, -velocity.Z); result.PlayRate = result.Stride = 1;
        var movement = AlsStandingMovementInputModel.Evaluate(id, velocity, moving ? 1 : 0, _movementSettings);
        var rules = new AlsGroundedRuleInput(movement.ShouldMove, false, false, result.ActualStance, true, false, 0, 1)
            { HasMovementInput = moving, Speed = movement.Speed, FeetCrossing = 1 };
        var ground = new AlsGroundedFrameInputs(dt, new(1.75f, 3.75f, 6.5f), 1, 1, 1, default, default,
            AlsSlotWeights.Passthrough, new(0, 0), new(0, 0), new((short)_frame, (ulong)_frame));
        var observation = new AlsRagdollFrameObservation(id, new(120, 40, 350), _snapshot);
        var oldRig = _owners[1].CommittedRefactoredRig; var oldLocks = _owners[1].CommittedRefactoredLocks;
        var oldId = _owners[1].CommittedIdentity; var oldCurves = _owners[1].CommittedCurves.ToArray();
        var singleQuery = Prepare(0); var workerQuery = OnWorker(() => Prepare(1));
        var beforePelvis = _owners[0].NormalVisited ? _owners[0].PreFootPose[_pelvis].Position : default;
        Require(singleQuery == workerQuery with { RequestSerial = singleQuery.RequestSerial }, "Worker current-pose rays differ.");
        var singleHit = Gather(singleQuery); var workerHit = Gather(workerQuery);
        Require(singleHit == workerHit with { Queries = singleHit.Queries }, "Owner physics observations differ.");
        // A query boundary is not a commit boundary, even if all source clocks
        // and most of the animation graph have already been evaluated.
        ExpectInvalid(() => _owners[1].ValidateCommit(id));
        _owners[0].ResumeFootQueries(singleHit);
        if (_frame % 37 == 0)
        {
            OnWorker(() => { _owners[1].Discard(); return 0; });
            CheckOld(); _ = OnWorker(() => Prepare(1));
            try { OnWorker(() => { _owners[1].ResumeFootQueries(workerHit); return 0; }); throw new Exception("Canceled response accepted."); }
            catch (ArgumentException) { _foreign++; }
            CheckOld(); workerQuery = OnWorker(() => Prepare(1)); workerHit = Gather(workerQuery);
        }
        OnWorker(() => { _owners[1].ResumeFootQueries(workerHit); return 0; });
        var owner = _owners[1]; var expectedPose = _owners[0].Pose.ToArray(); var expectedCurves = _owners[0].Curves.ToArray();
        if (owner.NormalVisited && !owner.RagdollVisited && NVector3.Distance(beforePelvis, expectedPose[_pelvis].Position) > 1e-5f) _poseChanged++;
        Require(owner.Pose.SequenceEqual(expectedPose) && owner.Curves.SequenceEqual(expectedCurves) &&
            StandingCycleSmoke.SameSync(owner.Base.Sources, _owners[0].Base.Sources), "Split graph pose/curves/source clocks differ.");
        Require(owner.Base.SourceEvents.Count == _owners[0].Base.SourceEvents.Count, "Split graph source event count differs.");
        for (var i = 0; i < owner.Base.SourceEvents.Count; i++) Require(owner.Base.SourceEvents[i] == _owners[0].Base.SourceEvents[i], "Split graph event differs.");
        OnWorker(() => { owner.Discard(); return 0; }); CheckOld();
        workerQuery = OnWorker(() => Prepare(1)); workerHit = Gather(workerQuery);
        OnWorker(() => { owner.ResumeFootQueries(workerHit); return 0; });
        Require(owner.Pose.SequenceEqual(expectedPose) && owner.Curves.SequenceEqual(expectedCurves), "Late cancellation changed retried pose."); _retries++;
        _owners[0].Commit(id); OnWorker(() => { owner.Commit(id); return 0; });
        Require(owner.CommittedRefactoredRig == _owners[0].CommittedRefactoredRig && owner.CommittedRefactoredLocks == _owners[0].CommittedRefactoredLocks,
            "Split graph committed different rig/lock histories.");
        if (owner.CommittedRefactoredRig.PelvisOffset != 0) _changed++;
        if (owner.CommittedRefactoredLocks.Left.Amount > 0 || owner.CommittedRefactoredLocks.Right.Amount > 0) _locked++;
        if (!owner.NormalVisited) _hidden++;
        if (owner.NormalVisited && owner.RagdollVisited) _mixed++;
        _feedback = new(_reader.Read(id, expectedCurves), expectedCurves[_blockIndex].Present ? expectedCurves[_blockIndex].Value : 0);
        _lastPose = expectedPose; _previousState = state;

        AlsFootRigQueries Prepare(int index)
        {
            var target = _owners[index];
            target.PrepareFromFrame(input, result, movement, rules, ground, new(id, 1, dt), this, component, 0, 0,
                seconds <3.5f ? AlsOverlayKind.Rifle : AlsOverlayKind.Barrel, ragdollObservation: observation);
            return target.PrepareFootQueries(component, 0, 0);
        }
        void CheckOld() => Require(_owners[1].CommittedIdentity == oldId && _owners[1].CommittedRefactoredRig == oldRig &&
            _owners[1].CommittedRefactoredLocks == oldLocks && _owners[1].CommittedCurves.SequenceEqual(oldCurves), "Canceled foot candidate leaked history.");
    }
    private T OnWorker<T>(Func<T> action) => Task.Run(() =>
    {
        Require(!GodotThread.IsMainThread(), "Animation half ran on the main thread."); _worker++; return action();
    }).GetAwaiter().GetResult();
    private AlsFootRigObservations Gather(AlsFootRigQueries query)
    { var hit = _gather.Gather(query); _queries += _gather.LastQueryCount; return hit; }
    private static void ExpectInvalid(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Incomplete foot frame committed."); }
    public void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context)
    { weights.Validate(); } // BaseLayer owns real stop/turn Montage evaluation; this is its update notification.
    public void RefreshSourceBones(int cache) { }
    public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) => throw new InvalidOperationException("Escaped inertia owner.");
    public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) => throw new InvalidOperationException("Unknown skipped updates.");
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { GD.PushError(error.ToString()); SetPhysicsProcess(false); GetTree().Quit(1); }
    public override void _ExitTree()
    {
        foreach (var owner in _owners) owner?.Dispose();
        foreach (var graph in _graphs) graph?.Dispose();
        foreach (var library in _libraries) library?.Dispose();
        _gather?.Dispose();
    }
}
