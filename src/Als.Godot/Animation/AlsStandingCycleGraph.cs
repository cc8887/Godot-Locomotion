using System.Runtime.CompilerServices;
using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Core.Animation;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

[InlineArray(26)]
internal struct AlsCycleClipTimes { private float _element; }

[InlineArray(6)]
internal struct AlsCycleStateWeights { private float _element; }

[InlineArray(AlsCycleSyncFrame.PlayerCapacity)]
internal struct AlsCyclePlayerTimes { private float _element; }
[InlineArray(AlsCycleSyncFrame.PlayerCapacity)]
internal struct AlsCyclePlayerHistory { private AlsAssetPlayerHistory _element; }
[InlineArray(AlsCycleSyncFrame.SampleCapacity)]
internal struct AlsCycleSampleHistory { private AlsAssetSampleHistory _element; }
[InlineArray(AlsCycleSyncFrame.SampleCapacity)]
internal struct AlsCycleNotifyTicks { private AlsP5SourceNotifyTick _element; }
[InlineArray(AlsCycleSyncFrame.PlayerCapacity)] internal struct AlsCycleSourceEpochs { private long _element; }
[InlineArray(AlsCycleSyncFrame.GroupCapacity)] internal struct AlsCycleSyncGroups { private AlsAssetSyncBatchGroupHistory _element; }

// Shared per-character source transaction, indexed by formal player identity. Not a second P5 clock.
internal struct AlsCycleSyncFrame
{
    public const int PlayerCapacity = 224;
    public const int SampleCapacity = 258;
    public const int GroupCapacity = 10;
    public bool Initialized;
    public AlsLocomotionSourceStamp BindingStamp;
    public ulong BindingDigest;
    public ulong LayoutDigest;
    public AlsCyclePlayerTimes Times;
    public AlsCyclePlayerHistory Players;
    public AlsCycleSampleHistory Samples;
    public int PlayerCount;
    public int SampleCount;
    public AlsAssetSyncGroupHistory Group;
    public AlsCycleSyncGroups Groups;
    public int GroupCount;
    public AlsCycleSourceEpochs Epochs;
    public AlsCyclePlayerTimes CachedWeights;
    public AlsCycleNotifyTicks NotifyTicks;
    public int NotifyTickCount;
}

[InlineArray(6)] internal struct AlsStandingBlendFilters { private AlsWalkRunFilterState _element; }
[InlineArray(6)] internal struct AlsStandingBlendInputs { private System.Numerics.Vector2 _element; }

internal record struct AlsStandingCycleFrame(AlsStandingCycleState State, float SprintWeight, AlsCycleClipTimes Times,
    AlsWalkRunFilterState Filter, System.Numerics.Vector4 CornerWeights,
    AlsTransitionStackState Transitions = default, bool DirectionInitialized = false,
    AlsCycleStateWeights BodyStateWeights = default, AlsCycleStateWeights LegStateWeights = default,
    AlsCycleSyncFrame Sync = default, AlsStandingMovementInput Movement = default,
    AlsBinaryBlendState SprintBlend = default, float SprintMask = 0, AlsCycleCachedUpdates CachedUpdates = default,
    AlsCycleDetailFrame Detail = default, AlsStandingCycleLifetime Lifetime = default,
    bool InitializeOuterCycleSources = false, byte DirectionInitializedStates = 0,
    AlsStandingDirectionInputState DirectionInputs = default, System.Numerics.Vector4 YawInputs = default,
    AlsCycleClipTimes SourceSeconds = default, bool HasSourceSeconds = false, AlsStandingCycleTailState Tail = default,
    AlsSprintInputUpdate SprintInput = default, AlsStandingBlendFilters SourceFilters = default);

internal sealed class AlsStandingCycleGraph
{
    private readonly int[] _ids;
    private readonly float[] _lengths;
    private readonly Dictionary<string, int>[] _curveIds;
    private readonly AlsCyclePoseSampler _poseSampler;
    private readonly StringName _poseSeek;
    private readonly AlsCurveSampler _walkStride;
    private readonly AlsCurveSampler _runStride;
    private readonly AlsCurveSampler _changeDirection;
    private readonly AlsLocomotionSourcePlayerBinding[] _players;
    private readonly AlsP5CoreRuntimeBindingSnapshot _sourceSnapshot;
    private readonly AlsLocomotionSourceSampleBinding[] _samples;
    private readonly int[] _poseOffsets;
    private readonly int[] _sampleByCorner;
    private readonly int[] _poseBySample;
    private readonly int _syncGroupId;
    private readonly System.Numerics.Vector2 _filterWindows;
    private readonly Func<float, float> _sampleTransitionCurve;
    private readonly int _maxTransitionsPerFrame;
    private readonly bool _skipFirstUpdateTransition;
    private readonly AlsStandingMovementSettings _movementSettings;
    private readonly AlsStandingSprintProfile _sprintProfile;
    private readonly AlsLocomotionSourcePlayerBinding _idleSource;
    private readonly AlsCycleDetailGraph _detail;
    private readonly AlsDirectionFeedbackDefinition _directionFeedback;
    private readonly AlsYawOffset _yawOffsets;
    private readonly AlsStandingCycleTail _tail;
    private readonly AlsSprintInputSource _sprintInput;
    internal AlsStandingDirectionCacheProfile DirectionCacheProfile { get; }

    public AnimationNodeBlendTree Root { get; }

    public AlsStandingCycleGraph(AlsAnimationLibraryBuildResult library, AlsLocomotionAnimationProfile profile,
        AlsAnimationSetDefinition set, string prefix, List<IDisposable> owned, AlsP5CoreRuntimeBindingSnapshot sourceBindings,
        AlsPoseAnimationProfile? poseProfile = null, AlsRefactoredPoseCurveWrite[]? refactoredMovementCurves = null)
    {
        ArgumentNullException.ThrowIfNull(sourceBindings);
        var graphBinding = sourceBindings.CreateGraphBuildView();
        if (sourceBindings.Version != GodotAls.Core.Animation.AlsP5RuntimeBindings.SourceGraphVersion ||
            sourceBindings.AnimationSetDefinitionDigest != set.DefinitionDigest || graphBinding.SkeletonId != profile.SkeletonId ||
            graphBinding.StandingIdleAnimationId != profile.StandingIdleAnimationId ||
            !graphBinding.StandingWalkRun.SequenceEqual(profile.StandingWalkRun) ||
            !graphBinding.StandingSamples.SequenceEqual(profile.StandingSamples.Select(s => new AlsP5GraphSample(s.AnimationId, s.X, s.Y, s.RateScale)).ToArray()))
            throw new InvalidOperationException("Cycle graph and P5 source snapshot disagree.");
        _sourceSnapshot = sourceBindings;
        DirectionCacheProfile = AlsStandingDirectionCacheCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_locomotion_source_graph.json"),
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_pose_cache_graph.json"));
        _yawOffsets = AlsYawOffsetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_yaw_inputs.json"));
        _sprintProfile = sourceBindings.StandingSprint;
        _movementSettings = AlsLocomotionInputCompiler.Compile(Godot.FileAccess.GetFileAsString(
            "res://assets/config/v4_locomotion_inputs.json")).Movement;
        _directionFeedback = AlsDirectionFeedbackCompiler.Compile(Godot.FileAccess.GetFileAsString(
            "res://assets/config/v4_locomotion_inputs.json"), Godot.FileAccess.GetFileAsString(
            "res://assets/config/v4_locomotion_source_graph.json")).Runtime;
        _ids = new int[26];
        _ids[0] = profile.StandingIdleAnimationId;
        for (var direction = 0; direction < 6; direction++)
        {
            var source = profile.StandingWalkRun[direction];
            var offset = 1 + direction * 4;
            _ids[offset] = source.WalkPoseId;
            _ids[offset + 1] = source.WalkId;
            _ids[offset + 2] = source.RunPoseId;
            _ids[offset + 3] = source.RunId;
        }
        _ids[25] = profile.StandingSamples.MaxBy(value => value.Y)!.AnimationId;
        _lengths = _ids.Select(id => set.Animations[id].PlayLength).ToArray();
        _curveIds = _ids.Select(id => set.Animations[id].Curves.Where(c => c.Provenance == AlsCurveProvenance.SourceCurve)
            .ToDictionary(c => c.SourceName, c => c.CurveId, StringComparer.OrdinalIgnoreCase)).ToArray();
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_locomotion_curves.json"));
        var curves = document.RootElement.GetProperty("curves").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!);
        _walkStride = ReadCurve(curves["StrideBlend_N_Walk"]);
        _runStride = ReadCurve(curves["StrideBlend_N_Run"]);
        _changeDirection = ReadCurve(curves["ChangeDirection"]);
        _sampleTransitionCurve = progress => Sample(_changeDirection, progress);
        using var directionSettings = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_direction_state_machine.json"));
        var machine = directionSettings.RootElement;
        if (machine.GetProperty("schemaVersion").GetInt32() != 1 ||
            !machine.GetProperty("sourceGraph").GetString()!.EndsWith(":(N) CycleBlending.AnimGraphNode_StateMachine_1.(N) Directional States", StringComparison.Ordinal) ||
            !machine.GetProperty("reinitializeOnBecomingRelevant").GetBoolean())
            throw new InvalidOperationException("Unsupported direction state machine settings.");
        _maxTransitionsPerFrame = machine.GetProperty("maxTransitionsPerFrame").GetInt32();
        _skipFirstUpdateTransition = machine.GetProperty("skipFirstUpdateTransition").GetBoolean();
        if (_maxTransitionsPerFrame is < 1 or > 32) throw new InvalidOperationException("Invalid direction transition limit.");
        using var sampling = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_walkrun_sampling.json"));
        if (sampling.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported WalkRun sampling schema.");
        var spaces = sampling.RootElement.GetProperty("assets");
        if (spaces.GetArrayLength() != 6) throw new InvalidOperationException("Expected all six native WalkRun spaces.");
        string[] directions = ["F", "B", "FL", "BL", "FR", "BR"];
        for (var i = 0; i < 6; i++)
        {
            var space = spaces[i];
            if (space.GetProperty("name").GetString() != $"ALS_N_WalkRun_{directions[i]}" ||
                !space.GetProperty("grid").GetBoolean() || space.GetProperty("sampleWeightSpeed").GetSingle() != 0)
                throw new InvalidOperationException("WalkRun sampling topology differs from the native grid.");
            var axes = space.GetProperty("axes");
            if (axes.GetArrayLength() != 2 || axes[0].GetProperty("mode").GetString() != "Cubic" ||
                axes[1].GetProperty("mode").GetString() != "Cubic")
                throw new InvalidOperationException("Unsupported WalkRun axis filter.");
            var windows = new System.Numerics.Vector2(axes[0].GetProperty("seconds").GetSingle(), axes[1].GetProperty("seconds").GetSingle());
            if (!float.IsFinite(windows.X) || !float.IsFinite(windows.Y) || windows.X <= 0 || windows.Y <= 0 ||
                windows.X > 1 || windows.Y > 1 || i > 0 && windows != _filterWindows)
                throw new InvalidOperationException("WalkRun bindings require identical bounded native filter windows.");
            _filterWindows = windows;
            var sampleNames = space.GetProperty("sampleNames");
            int[] corners = [2, 1, 4, 3];
            if (sampleNames.GetArrayLength() != 4) throw new InvalidOperationException("Expected four native WalkRun samples.");
            for (var j = 0; j < 4; j++)
                if (sampleNames[j].GetString() != set.Animations[_ids[i * 4 + corners[j]]].Name)
                    throw new InvalidOperationException("WalkRun sampling profile and animation closure disagree.");
        }

        var sources = sourceBindings.CreateCoreView().Sources;
        _samples = sources.Samples.ToArray();
        _idleSource = sources.Players.ToArray().Single(p => p.Domain == AlsLocomotionSourceDomain.Standing &&
            p.Kind == AlsLocomotionSourceKind.TeleportEvaluator);
        if (_idleSource.SampleCount != 1 || _samples[_idleSource.SampleStart].AnimationId != _ids[0])
            throw new InvalidOperationException("Standing Idle differs from its native evaluator binding.");
        _players = sources.Players.ToArray().Where(p => p.Domain == AlsLocomotionSourceDomain.Cycle &&
            (p.InputX == AlsSourceAxisInput.StrideBlend || p.Kind == AlsLocomotionSourceKind.Sequence &&
                _samples[p.SampleStart].AnimationId == _ids[25])).ToArray();
        if (_players.Length != 7 || _players.Count(p => p.Kind == AlsLocomotionSourceKind.BlendSpace) != 6 ||
            _players.Sum(p => p.SampleCount) != 25 || _players.Any(p => p.PlayRateInput != AlsSourceRateInput.StandingPlayRate))
            throw new InvalidOperationException("Cycle timing requires six WalkRun sources and Sprint F.");
        _syncGroupId = _players[0].SyncGroupId;
        _poseOffsets = new int[7]; _sampleByCorner = new int[28];
        _poseBySample = Enumerable.Repeat(-1, _samples.Length).ToArray();
        for (var slot = 0; slot < _players.Length; slot++)
        {
            var player = _players[slot];
            if (player.SyncGroupId != _syncGroupId || _syncGroupId < 0 || !player.Loop)
                throw new InvalidOperationException("Cycle sources do not share the expected looping group.");
            var playerSamples = _samples.AsSpan(player.SampleStart, player.SampleCount).ToArray();
            var offset = player.Kind == AlsLocomotionSourceKind.Sequence ? 25 : Enumerable.Range(0, 6)
                .Select(d => 1 + d * 4).Single(o => playerSamples.Any(s => s.AnimationId == _ids[o + 1]));
            _poseOffsets[slot] = offset;
            for (var corner = 0; corner < player.SampleCount; corner++)
            {
                var source = playerSamples.Single(s => s.AnimationId == _ids[offset + corner]);
                _sampleByCorner[slot * 4 + corner] = source.SampleId;
                _poseBySample[source.SampleId] = offset + corner;
            }
        }

        var poseSources = library.MovementSources(set, profile.SkeletonId);
        var profileBones = new bool[poseSources.BoneCount];
        foreach (var entry in document.RootElement.GetProperty("blendProfile").GetProperty("entries").EnumerateArray())
        {
            var boneName = entry.GetProperty("bone").GetString()!;
            if (entry.GetProperty("scale").GetSingle() != 2f) throw new InvalidOperationException("Unsupported leg blend factor.");
            var bone = poseSources.Bone(boneName);
            profileBones[bone] = true;
        }
        _sprintInput=Own(new AlsSprintInputSource(library,set,sourceBindings.SourceProfile));
        _poseSampler = Own(new AlsCyclePoseSampler(library, set, _ids, profileBones, DirectionCacheProfile, _curveIds, _lengths,_sprintInput));
        _tail = Own(new AlsStandingCycleTail(library,set,profile.SkeletonId,sourceBindings.SourceProfile));
        _detail = Own(new AlsCycleDetailGraph(library, set, profile.SkeletonId, sources.Players.ToArray(), sources.Samples.ToArray(), poseProfile, refactoredMovementCurves));
        Root = Own(new AnimationNodeBlendTree());
        using var poseName = new StringName("als_cycle/pose");
        Root.AddNode("Pose", Own(new AnimationNodeAnimation { Animation = poseName }));
        Root.AddNode("Freeze", Own(new AnimationNodeTimeScale()));
        Root.AddNode("Seek", Own(new AnimationNodeTimeSeek()));
        Root.ConnectNode("Freeze", 0, "Pose");
        Root.ConnectNode("Seek", 0, "Freeze");
        Root.ConnectNode("output", 0, "Seek");
        _poseSeek = Own(new StringName($"parameters/{prefix}/Locomotion/Seek/seek_request"));
        T Own<T>(T value) where T : IDisposable { owned.Add(value); return value; }
    }

    internal ReadOnlySpan<AlsLocalPose> PoseOutput => _poseSampler.Output;
    internal ReadOnlySpan<string> CycleSourceCurveNames => _poseSampler.CurveNames;
    internal ReadOnlySpan<AlsInertialCurve> CycleSourceCurves => _poseSampler.CurveOutput;
    internal int DirectionCacheEvaluations => _poseSampler.DirectionCacheEvaluations;
    internal int DirectionInputSamples => _poseSampler.DirectionInputSamples;
    internal int DirectionInputMask => _poseSampler.DirectionInputMask;
    internal int DirectionCacheInitializations => _poseSampler.DirectionCacheInitializations;
    internal void SampleCachedCycle(in AlsStandingCycleFrame frame) => _poseSampler.Sample(frame);
    internal void SampleDirectCycle(in AlsStandingCycleFrame frame, Span<AlsLocalPose> output) => _poseSampler.SampleDirect(frame, output);
    internal AlsP5RuntimeBindings SourceBindings => _sourceSnapshot.CreateCoreView();
    internal void CommitPose(bool sourceEnabled = true)
    {
        _poseSampler.Commit();
        if (sourceEnabled) _detail.Commit();
    }
    internal void RestorePose() => _poseSampler.Restore();

    internal AlsStandingCycleFrame AdvanceInactiveFeedback(in AlsStandingCycleFrame previous, float delta)
    {
        var frame = previous;
        var detail = frame.Detail;
        detail.Standing.DirectionEvents = default;
        detail.Standing.Feedback = AlsDirectionFeedback.Complete(previous.Detail.Standing.Feedback,
            default, 0, delta, _directionFeedback);
        frame.Detail = detail;
        return frame;
    }

    internal AlsStandingMovementInput CreateMovementInput(in AlsFrameInput input)
    {
        input.MovementInput.Validate();
        var command = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance);
        // Physical acceleration magnitude is gathered before Character replaces
        // MaxAcceleration. ActualAcceleration also includes braking/collision.
        return AlsStandingMovementInputModel.Evaluate(input.Identity, input.ActualVelocity,
            input.MovementInput.Captured == 1 ? input.MovementInput.Amount : input.MaxAcceleration > 0 ? command.InputAmount : 0, _movementSettings) with
        { ControlRelativeYawDegrees = AlsYawOffset.VelocityRelativeControlDegrees(input.ActualVelocity, input.Command.ViewYaw) };
    }

    public AlsStandingCycleFrame Prepare(in AlsStandingCycleFrame previous, in AlsFrameResult result, float delta,
        System.Numerics.Vector3 animatedSpeeds, in AlsStandingMovementInput movement, bool includeDetail = false,
        AlsStandingTurnSlotInput turnSlot = default)
    {
        var frame = PrepareUpdate(previous, result, delta, animatedSpeeds, movement, includeDetail, turnSlot);
        Span<AlsLocomotionSourceUpdate> updates = stackalloc AlsLocomotionSourceUpdate[AlsCycleSyncFrame.PlayerCapacity];
        Span<AlsLocomotionSampleUpdate> samples = stackalloc AlsLocomotionSampleUpdate[AlsCycleSyncFrame.SampleCapacity];
        Span<bool> active = stackalloc bool[AlsCycleSyncFrame.PlayerCapacity];
        var candidate = previous.Sync; var playerCount = 0; var sampleCount = 0;
        CollectSources(frame, ref candidate, updates, samples, active, ref playerCount, ref sampleCount);
        var sync = AlsSharedSourceBatch.Evaluate(_sourceSnapshot.CreateCoreView(), candidate, updates[..playerCount],
            samples[..sampleCount], active[..playerCount], frame.State.PlayRate, frame.Detail.Standing.Rotation, null, delta);
        CompleteSources(ref frame, previous, sync);
        if (includeDetail) EvaluatePose(ref frame, previous, delta);
        return frame;
    }

    internal AlsStandingCycleFrame PrepareUpdate(in AlsStandingCycleFrame previous, in AlsFrameResult result, float delta,
        System.Numerics.Vector3 animatedSpeeds, in AlsStandingMovementInput movement, bool includeDetail,
        AlsStandingTurnSlotInput turnSlot = default) =>
        PrepareUpdateCore(previous, result, delta, animatedSpeeds, movement, includeDetail, turnSlot, false, default, default);

    internal AlsStandingUpdateInput ObserveSharedInput(in AlsStandingCycleFrame previous, in AlsFrameResult result,
        in AlsStandingMovementInput movement, float mainGroundedRecordedWeight, Span<AlsGroundedAutomaticTime> times,
        AlsAnimationInputFeedback? feedback = null) =>
        _detail.Observe(previous.Detail, previous.Sync, result, movement,
            feedback is { } global ? global.WeightGait.Present ? global.WeightGait.Value : 0 : Sample(previous, "Weight_Gait"),
            feedback is { } final ? final.FeetPosition.Present ? final.FeetPosition.Value : 0 : Sample(previous, "Feet_Position"),
            mainGroundedRecordedWeight, times);

    internal AlsStandingCycleFrame ConsumeSharedUpdate(in AlsStandingCycleFrame previous, in AlsFrameResult result, float delta,
        System.Numerics.Vector3 animatedSpeeds, in AlsStandingMovementInput movement, in AlsStandingCachedGraphUpdate update,
        in AlsSourceRotationInput rotation, AlsStandingTurnSlotInput turnSlot = default, AlsGroundedAnimationInput? globalInput = null,
        AlsGroundedControlInput? globalControl = null, AlsAnimationInputFeedback? feedback = null,
        AlsGraphTraversalCounter? initialization = null)
    {
        if (!update.State.HasUpdated || update.State.Identity != result.Identity ||
            update.StandingUpdated && (update.StandingContext.Identity != result.Identity || update.StandingContext.Delta != delta) ||
            update.StopUpdated && (update.StopContext.Identity != result.Identity || update.StopContext.Delta != delta) ||
            update.DetailUpdated && (update.DetailContext.Identity != result.Identity || update.DetailContext.Delta != delta) ||
            update.CycleUpdated && (update.CycleContext.Identity != result.Identity || update.CycleContext.Delta != delta))
            throw new ArgumentException("Standing requires this frame's completed shared update.");
        return PrepareUpdateCore(previous, result, delta, animatedSpeeds, movement, true, turnSlot, true, update, rotation, globalInput, globalControl, feedback, initialization);
    }

    private AlsStandingCycleFrame PrepareUpdateCore(in AlsStandingCycleFrame previous, in AlsFrameResult result, float delta,
        System.Numerics.Vector3 animatedSpeeds, in AlsStandingMovementInput movement, bool includeDetail,
        AlsStandingTurnSlotInput turnSlot, bool shared, in AlsStandingCachedGraphUpdate sharedUpdate, in AlsSourceRotationInput rotation,
        AlsGroundedAnimationInput? globalInput = null, AlsGroundedControlInput? globalControl = null, AlsAnimationInputFeedback? feedback = null,
        AlsGraphTraversalCounter? initialization = null)
    {
        var observedMovement = AlsStandingMovementInputModel.Evaluate(movement.Identity, new(movement.Speed, 0, 0), movement.MovementInputAmount, _movementSettings);
        if (movement.Identity != result.Identity || !float.IsFinite(movement.Speed) || movement.Speed < 0 ||
            !float.IsFinite(movement.MovementInputAmount) || movement.MovementInputAmount < 0 ||
            !float.IsFinite(movement.ControlRelativeYawDegrees) || movement.ControlRelativeYawDegrees is < -180 or > 180 ||
            movement with { ControlRelativeYawDegrees = 0, ShouldMove = observedMovement.ShouldMove } != observedMovement ||
            movement.ShouldMove != (globalInput?.ShouldMove ?? observedMovement.ShouldMove) ||
            globalInput.HasValue && globalInput.Value.Identity != result.Identity ||
            globalControl.HasValue && globalControl.Value.Identity != result.Identity)
            throw new ArgumentException("Standing movement input is stale or inconsistent.", nameof(movement));
        var crossing = feedback is { } final ? final.FeetCrossing.Present ? final.FeetCrossing.Value : 0 : Sample(previous, "Feet_Crossing");
        var hipBias = feedback is { } global ? global.HipOrientationBias.Present ? global.HipOrientationBias.Value : 0 : Sample(previous, "HipOrientation_Bias");
        var transitions = previous.DirectionInitialized ? previous.Transitions : AlsTransitionStack.Initialize((int)previous.State.Direction);
        var state = AlsStandingCycle.AdvanceDirection(previous.State, result.BlendCoordinates, delta, crossing, hipBias,
            AlsTransitionStack.Weight(transitions, transitions.CurrentState), result.AimRelativeYaw,
            result.ActualGait, result.ActualRotationMode, globalInput?.VelocityBlend, globalControl?.MovementDirection);
        var detail = shared ? _detail.Consume(previous.Detail, sharedUpdate, rotation, state.VelocityBlend) :
            includeDetail ? _detail.Prepare(previous.Detail, previous.Sync, result, movement, state.VelocityBlend,
                Sample(previous, "Weight_Gait"), Sample(previous, "Feet_Position"), delta) : default;
        detail.Standing.TurnSlot = turnSlot;
        var cycleRelevant = !includeDetail || detail.Standing.Update.CycleUpdated;
        var lifetime = AlsStandingCycleLifecycle.Prepare(previous.Lifetime, result.Identity, cycleRelevant,
            shared && sharedUpdate.CycleInitialized, initialization ?? new(0, 0),
            shared && cycleRelevant ? sharedUpdate.CycleContext.UpdateCounter : null);
        var reinitialize = lifetime.Initialized;
        byte initializedStates = reinitialize ? (byte)1 : (byte)0;
        if (reinitialize)
        {
            transitions = AlsTransitionStack.Initialize((int)AlsCycleDirection.Forward);
            state = state with { Direction = AlsCycleDirection.Forward, PreviousDirection = AlsCycleDirection.Forward,
                TransitionElapsed = 0, TransitionDuration = 0, HipTransition = false, WaitingForFeet = false };
            if (result.BlendCoordinates.LengthSquared() > 1e-12f) state = AlsStandingCycle.EvaluateDirection(state, 1);
        }
        if (!cycleRelevant && !reinitialize) state = state with { Direction = previous.State.Direction, PreviousDirection = previous.State.PreviousDirection,
            TransitionElapsed = previous.State.TransitionElapsed, TransitionDuration = previous.State.TransitionDuration,
            HipTransition = previous.State.HipTransition, WaitingForFeet = false };
        var started = 0;
        var firstUpdate = lifetime.FirstUpdate;
        AlsDirectionFeedbackEvents directionEvents = default;
        if (includeDetail && reinitialize) _directionFeedback.Enter(AlsCycleDirection.Forward, ref directionEvents);
        while (cycleRelevant && state.Direction != (AlsCycleDirection)transitions.CurrentState)
        {
            initializedStates |= (byte)(1 << (int)state.Direction);
            if (includeDetail) _directionFeedback.Transition((AlsCycleDirection)transitions.CurrentState, state.Direction,
                state.HipTransition, firstUpdate && _skipFirstUpdateTransition, ref directionEvents);
            transitions = AlsTransitionStack.Start(transitions, (int)state.Direction, state.TransitionDuration,
                state.HipTransition ? AlsTransitionBlend.Custom : AlsTransitionBlend.Cubic);
            if (++started == _maxTransitionsPerFrame) break;
            state = AlsStandingCycle.EvaluateDirection(state,
                AlsTransitionStack.Weight(transitions, transitions.CurrentState));
        }
        if (firstUpdate && _skipFirstUpdateTransition)
            transitions = AlsTransitionStack.Initialize((int)state.Direction);
        var beforeCleanup = transitions;
        if (cycleRelevant) transitions = AlsTransitionStack.Advance(transitions, delta, out beforeCleanup, _sampleTransitionCurve);
        var directionInputs = AlsStandingDirectionInputs.Prepare(previous.DirectionInputs, initializedStates,
            cycleRelevant, state.VelocityBlend, beforeCleanup, transitions);
        var yawInputs = globalControl?.Yaw ?? _yawOffsets.Update(previous.YawInputs, result.ResolvedLocomotionState == AlsLocomotionState.Grounded,
            movement.ShouldMove, movement.ControlRelativeYawDegrees);
        directionInputs = AlsStandingDirectionInputs.CaptureYaw(directionInputs, yawInputs, DirectionCacheProfile.YawAxes);
        var latest = transitions.Latest;
        state = state with { TransitionDuration = latest.Duration,
            TransitionElapsed = transitions.Count > 0 ? latest.Elapsed : latest.Duration,
            BlendAlpha = transitions.Count > 0 ? latest.Alpha : 1,
            ActiveTransitionCount = transitions.Count, TransitionsStartedThisFrame = started,
            CurrentStateWeight = AlsTransitionStack.Weight(transitions, transitions.CurrentState) };
        var speed = result.BlendCoordinates.Length();
        var gait = globalInput?.WalkRunBlend ?? (result.ActualGait == AlsGait.Walking ? 0f : 1f);
        var gaitCurve = Math.Clamp(Sample(previous, "Weight_Gait") - 1, 0, 1);
        var stride = globalInput?.Stride ?? Mathf.Lerp(Sample(_walkStride, speed * 100), Sample(_runStride, speed * 100), gaitCurve);
        // Retained for the legacy HUD only. Source sampling below uses independent
        // filters, because equal settings do not imply equal node visit histories.
        var filter = cycleRelevant ? AlsWalkRunBlendSpace.Advance(reinitialize ? default : previous.Filter,
            new System.Numerics.Vector2(stride, gait), delta, _filterWindows) : reinitialize ? default : previous.Filter;
        Span<int> sampleOrder = stackalloc int[4];
        AlsWalkRunBlendSpace.SampleEvaluation(filter.Output, sampleOrder, out var cornerWeights);
        var movingWeight = includeDetail ? AlsTransitionStack.Weight(detail.Standing.Update.State.Standing.Transitions, 1) +
            AlsTransitionStack.Weight(detail.Standing.Update.State.Standing.Transitions, 2) :
            Mathf.MoveToward(previous.State.MovingWeight, movement.ShouldMove ? 1 : 0, delta * 5);
        var updateWeight = includeDetail ? cycleRelevant ? detail.Standing.Update.CycleContext.Weight : 0 : movingWeight;
        var cachedUpdates = cycleRelevant && (includeDetail || movingWeight > 0)
            ? AlsCycleCacheWeights.Resolve(beforeCleanup, transitions, state.VelocityBlend, updateWeight, directionInputs.Cached) : default;
        var forwardRelevant = cachedUpdates[0].Present;
        var sprintMask = forwardRelevant ? Math.Clamp(Sample(previous, _sprintProfile.MaskCurveName), 0, 1) :
            lifetime.InitializeSprint ? 0 : previous.SprintMask;
        var sprintBlend = lifetime.InitializeSprint ? default : previous.SprintBlend;
        if (forwardRelevant && sprintMask < 1 - AlsPoseBlender.WeightThreshold)
            sprintBlend = AlsBinaryBlendList.Advance(sprintBlend,
                result.ActualGait == AlsGait.Sprinting ? 1 : 0, delta, _sprintProfile.Blend).State;
        var sprint = AlsStandingSprint.Weights(sprintBlend, sprintMask).PoseSprint;
        // Each native BlendSpace owns its filter. An unvisited player retains its
        // history; first use after initialization starts at that player's input.
        var sourceFilters = reinitialize ? default : previous.SourceFilters;
        var sprintUpdates = AlsStandingSprint.Weights(sprintBlend, sprintMask);
        for(var direction = 0; direction < 6; direction++)
            if(cachedUpdates[direction].Present &&
                (direction != 0 || sprintUpdates.ForwardUpdate > AlsPoseBlender.WeightThreshold))
                sourceFilters[direction] = AlsWalkRunBlendSpace.Advance(sourceFilters[direction],
                    new System.Numerics.Vector2(stride, gait), delta, _filterWindows);
        var rate = globalInput?.StandingPlayRate ?? Mathf.Lerp(Mathf.Lerp(speed / animatedSpeeds.X, speed / animatedSpeeds.Y, gaitCurve),
            speed / animatedSpeeds.Z, sprintBlend.SecondWeight) / MathF.Max(stride, 0.01f);
        rate = Math.Clamp(rate, 0, 3);
        state = state with { Stride = stride, PlayRate = rate, GaitWeight = gait, FilteredBlendInput = filter.Output,
            MovingWeight = movingWeight };
        AlsCycleStateWeights bodyWeights = default;
        AlsCycleStateWeights legWeights = default;
        for (var id = 0; id < 6; id++)
        {
            bodyWeights[id] = AlsTransitionStack.Weight(in transitions, id);
            legWeights[id] = AlsTransitionStack.Weight(in transitions, id, 2);
        }
        var frame = new AlsStandingCycleFrame(state, sprint, default, filter, cornerWeights, transitions, lifetime.State.HasUpdated, bodyWeights, legWeights,
            Movement: movement, SprintBlend: sprintBlend, SprintMask: sprintMask, CachedUpdates: cachedUpdates, Detail: detail,
            Lifetime: lifetime.State, InitializeOuterCycleSources: reinitialize && !(shared && sharedUpdate.CycleInitialized),
            DirectionInitializedStates: initializedStates, DirectionInputs: directionInputs, YawInputs: yawInputs,
            Tail:_tail.Prepare(previous.Tail,reinitialize,cycleRelevant,delta,
                feedback is { } tailFeedback ? tailFeedback.WeightGait.Present ? tailFeedback.WeightGait.Value : 0 : Sample(previous,_tail.CurveName),
                result.Lean,globalInput?.DiagonalScale ?? 0),
            SourceFilters:sourceFilters,
            SprintInput:AlsSprintInput.Prepare(previous.SprintInput.State,reinitialize,
                forwardRelevant && AlsStandingSprint.Weights(sprintBlend,sprintMask).SprintUpdate>AlsPoseBlender.WeightThreshold,
                -(globalInput?.RelativeAcceleration.Z ?? 0),delta,_sprintInput.Alpha));
        if (includeDetail)
        {
            var candidateDetail = frame.Detail;
            candidateDetail.Standing.DirectionEvents = directionEvents;
            candidateDetail.Standing.Feedback = AlsDirectionFeedback.Complete(previous.Detail.Standing.Feedback,
                directionEvents, movement.Speed, delta, _directionFeedback);
            frame.Detail = candidateDetail;
        }
        return frame;
    }

    internal void EvaluatePose(ref AlsStandingCycleFrame frame, in AlsStandingCycleFrame previous, float delta) =>
        _detail.Evaluate(ref frame, previous, _poseSampler, this, delta);

    internal ReadOnlySpan<string> CachedCurveNames => _detail.CurveNames;
    internal AlsRefactoredPoseCurveRuntime? RefactoredMovementCurves => _detail.RefactoredMovementCurves;
    internal void EvaluateCycleTail(in AlsStandingCycleFrame frame, ReadOnlySpan<string> names,
        Span<AlsLocalPose> bones,Span<AlsInertialCurve> curves)=>_tail.Evaluate(frame,names,bones,curves);
    internal void EvaluateCycleTailPrecise(in AlsStandingCycleFrame frame,ReadOnlySpan<string> names,
        Span<AlsPrecisePose> bones,Span<AlsInertialCurve> curves)=>_tail.EvaluatePrecise(frame,names,bones,curves);
    internal AlsStandingCachedGraphDefinition CacheDefinition => _detail.CacheDefinition;
    internal void EvaluateCachedSource(int node, in AlsStandingCycleFrame frame, IAlsGroundedPoseCacheReader reader,
        Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
        _detail.EvaluateCachedSource(node, frame, _poseSampler, this, reader, bones, curves);
    internal void StoreRawPose(ref AlsStandingCycleFrame frame, ReadOnlySpan<AlsLocalPose> bones, ReadOnlySpan<AlsInertialCurve> curves) =>
        _detail.StoreRawPose(ref frame, bones, curves);
    internal void EvaluateCachedSource(int node, in AlsStandingCycleFrame frame, IAlsGroundedPoseCacheReader reader,
        Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves) =>
        _detail.EvaluateCachedSource(node, frame, _poseSampler, this, reader, bones, curves);
    internal void StoreRawPose(ref AlsStandingCycleFrame frame, ReadOnlySpan<AlsPrecisePose> bones, ReadOnlySpan<AlsInertialCurve> curves) =>
        _detail.StoreRawPose(ref frame, bones, curves);
    internal void EvaluateUncachedRaw(in AlsStandingCycleFrame frame, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
        _detail.EvaluateUncachedPrecise(frame, _poseSampler, this, bones, curves);

    public void Apply(AnimationTree tree, in AlsStandingCycleFrame frame)
    {
        if (frame.Detail.HasPose)
        {
            var detail = frame.Detail;
            ReadOnlySpan<AlsLocalPose> output = detail.Pose;
            _poseSampler.SetOutput(output[..detail.BoneCount]);
        }
        else _poseSampler.Apply(frame);
        tree.Set(_poseSeek, 0f);
    }

    public float Sample(in AlsStandingCycleFrame frame, string name)
    {
        if (frame.Detail.HasPose) return _detail.SampleCurve(frame.Detail, name);
        return SampleCycle(frame, name);
    }

    internal float SampleCycle(in AlsStandingCycleFrame frame, string name)
    {
        var value = default(AlsInertialCurve);
        for (var clip = 0; clip < _ids.Length; clip++)
        {
            var weight = Weight(frame, clip, false);
            if (weight > 0) value = AlsStandingCycleCurves.Accumulate(value, _poseSampler.SourceCurve(frame, clip, name), weight);
        }
        return value.Value;
    }

    internal void SampleCycleCurves(in AlsStandingCycleFrame frame, ReadOnlySpan<string> names, Span<AlsInertialCurve> output)
    {
        if (output.Length != names.Length) throw new ArgumentException("Cycle curve output layout differs.");
        for (var curve = 0; curve < names.Length; curve++)
        {
            var value = default(AlsInertialCurve);
            for (var clip = 0; clip < _ids.Length; clip++)
            {
                var weight = Weight(frame, clip, false);
                if (weight > 0) value = AlsStandingCycleCurves.Accumulate(value, _poseSampler.SourceCurve(frame, clip, names[curve]), weight);
            }
            output[curve] = value;
        }
    }

    private static float Weight(in AlsStandingCycleFrame frame, int index, bool legs)
    {
        var state = frame.State;
        if (index == 0) return 1 - state.MovingWeight;
        if (index == 25) return state.MovingWeight * frame.SprintWeight * DirectionWeight(frame, 0, legs);
        var direction = (index - 1) / 4;
        var corner = (index - 1) % 4;
        if(!frame.SourceFilters[direction].HasSamples) return 0;
        Span<int> order = stackalloc int[4];
        AlsWalkRunBlendSpace.SampleEvaluation(frame.SourceFilters[direction].Output, order, out var weights);
        var cornerWeight = weights[corner];
        return state.MovingWeight * (direction == 0 ? 1 - frame.SprintWeight : 1) * DirectionWeight(frame, direction, legs) * cornerWeight;
    }

    private static float DirectionWeight(in AlsStandingCycleFrame frame, int direction, bool legs)
    {
        var stateWeights = legs ? frame.LegStateWeights : frame.BodyStateWeights;
        var velocity = AlsStandingCyclePose.NormalizeVelocityWeights(frame.State.VelocityBlend);
        var weight = 0f;
        for (var state = 0; state < 6; state++)
        {
            var contribution = AlsStandingCycle.DirectionWeight((AlsCycleDirection)state, direction, velocity);
            if (contribution > AlsPoseBlender.WeightThreshold) weight += stateWeights[state] * contribution;
        }
        return weight;
    }

    internal int InitializeCycleSources(ref AlsCycleSyncFrame candidate, float standingRate)
    {
        var binding = _sourceSnapshot.CreateCoreView();
        AlsSharedSourceBatch.Validate(binding, candidate);
        if (!float.IsFinite(standingRate)) throw new ArgumentOutOfRangeException(nameof(standingRate));
        var count = 0;
        foreach (var player in binding.Sources.Players)
        {
            if (player.Domain != AlsLocomotionSourceDomain.Cycle || player.Kind == AlsLocomotionSourceKind.TeleportEvaluator) continue;
            var sample = _samples[player.SampleStart];
            var time = AlsAssetSourceInitialization.Time(
                player.Kind == AlsLocomotionSourceKind.Sequence ? AlsAssetSyncKind.Sequence : AlsAssetSyncKind.BlendSpace,
                player.StartPosition, sample.DurationSeconds,
                (player.PlayRateInput == AlsSourceRateInput.StandingPlayRate ? standingRate : 1) * player.DefaultPlayRate,
                player.PlayRateBasis, sample.AssetRateScale);
            candidate.Epochs[player.PlayerId] = AlsAssetSourceInitialization.NextEpoch(candidate.Epochs[player.PlayerId]);
            candidate.Times[player.PlayerId] = time; candidate.CachedWeights[player.PlayerId] = 0;
            count++;
        }
        return count;
    }

    internal void CollectSources(in AlsStandingCycleFrame frame, ref AlsCycleSyncFrame candidate,
        Span<AlsLocomotionSourceUpdate> updates, Span<AlsLocomotionSampleUpdate> sampleUpdates, Span<bool> active,
        ref int playerCount, ref int sampleCount, bool ancestorsActive = true)
    {
        var binding = _sourceSnapshot.CreateCoreView();
        var sourceView = binding.Sources;
        AlsSharedSourceBatch.Validate(binding, candidate);
        if (updates.Length != AlsCycleSyncFrame.PlayerCapacity || sampleUpdates.Length != AlsCycleSyncFrame.SampleCapacity ||
            active.Length != updates.Length || (uint)playerCount > updates.Length || (uint)sampleCount > sampleUpdates.Length)
            throw new ArgumentException("Standing collection requires the full shared transaction capacity.");
        if (frame.InitializeOuterCycleSources) InitializeCycleSources(ref candidate, frame.State.PlayRate);
        if (frame.Detail.Standing.Update.State.HasUpdated && !frame.Detail.Standing.Update.StandingUpdated) return;
        var firstPlayer = playerCount;
        foreach (var player in sourceView.Players)
        {
            // Both the independent graph and Main now have an explicit lifetime. Collection cannot
            // manufacture a missing source identity or initialize an unvisited idle subgraph.
            if (player.Domain != AlsLocomotionSourceDomain.Cycle ||
                player.Kind == AlsLocomotionSourceKind.TeleportEvaluator || candidate.Epochs[player.PlayerId] != 0) continue;
            if (frame.Lifetime.HasInitialized)
                throw new InvalidOperationException("Standing Cycle lifetime has no matching source initialization.");
        }
        Span<int> sampleOrder = stackalloc int[4];
        _detail.Collect(frame.Detail, frame.State.VelocityBlend, ref candidate, updates, sampleUpdates, ref playerCount, ref sampleCount);
        var sprintWeights = AlsStandingSprint.Weights(frame.SprintBlend, frame.SprintMask);
        for (var slot = 0; slot < _players.Length; slot++)
        {
            var player = _players[slot];
            var sequence = player.Kind == AlsLocomotionSourceKind.Sequence;
            if(sequence)continue; // The two Sprint inputs are collected together below.
            var direction = sequence ? 0 : (_poseOffsets[slot] - 1) / 4;
            var update = frame.CachedUpdates[direction];
            var childWeight = sequence ? sprintWeights.SprintUpdate : direction == 0 ? sprintWeights.ForwardUpdate : 1;
            if (!update.Present || childWeight <= AlsPoseBlender.WeightThreshold) continue;
            var corners = AlsWalkRunBlendSpace.SampleEvaluation(frame.SourceFilters[direction].Output, sampleOrder, out var weights);
            var weight = update.Weight * childWeight;
            candidate.CachedWeights[player.PlayerId] = weight;
            var start = sampleCount;
            var count = sequence ? 1 : corners;
            for (var n = 0; n < count; n++)
            {
                var corner = sequence ? 0 : sampleOrder[n];
                var sample = _samples[_sampleByCorner[slot * 4 + corner]];
                sampleUpdates[sampleCount++] = new(sample.SampleId, sequence ? 1 : weights[corner], sample.SampleRateScale);
            }
            updates[playerCount++] = new(player.PlayerId, candidate.Epochs[player.PlayerId], candidate.Times[player.PlayerId], weight, start, count,
                frame.Detail.Standing.Update.CycleContext.InertializationSync);
        }
        for (var i = firstPlayer; i < playerCount; i++)
        {
            var source = sourceView.Players[updates[i].PlayerId];
            if (source.Domain == AlsLocomotionSourceDomain.Detail)
            {
                active[i] = frame.Detail.Update.State.CurrentState ==
                    (AlsDetailState)(2 + source.DetailSlot / 4) &&
                    _detail.IsActive(frame.Detail.Standing, frame.Detail.Standing.Update.DetailContext);
                continue;
            }
            if (source.Domain == AlsLocomotionSourceDomain.Standing)
            {
                active[i] = _detail.RotationActive(frame.Detail.Standing, source.PlayerId);
                continue;
            }
            var pose = _poseBySample[source.SampleStart];
            var direction = pose == 25 ? 0 : (pose - 1) / 4;
            var isActive = frame.CachedUpdates[direction].Active &&
                _detail.IsActive(frame.Detail.Standing, frame.Detail.Standing.Update.CycleContext);
            if (pose == 25) isActive &= sprintWeights.SprintActive;
            else if (direction == 0) isActive &= sprintWeights.ForwardActive;
            active[i] = isActive;
        }
        if(frame.SprintInput.Visited)
        {
            var update=frame.CachedUpdates[0];
            var context=frame.Detail.Standing.Update.CycleContext.WithWeight(update.Weight*sprintWeights.SprintUpdate);
            _sprintInput.Collect(frame.SprintInput,context,ancestorsActive && update.Active && sprintWeights.SprintActive &&
                _detail.IsActive(frame.Detail.Standing,frame.Detail.Standing.Update.CycleContext),frame.State.PlayRate,
                ref candidate,updates,sampleUpdates,active,ref playerCount,ref sampleCount);
        }
        if(frame.Detail.Standing.Update.CycleUpdated)
            _tail.Collect(frame.Tail,frame.Detail.Standing.Update.CycleContext,ref candidate,updates,sampleUpdates,active,ref playerCount,ref sampleCount,
                ancestorsActive && _detail.IsActive(frame.Detail.Standing,frame.Detail.Standing.Update.CycleContext));
        if (!ancestorsActive)
            active[firstPlayer..playerCount].Clear();
    }

    internal void CompleteSources(ref AlsStandingCycleFrame frame, in AlsStandingCycleFrame previous, in AlsCycleSyncFrame sources)
    {
        var sourceView = _sourceSnapshot.CreateCoreView().Sources;
        AlsSharedSourceBatch.Validate(_sourceSnapshot.CreateCoreView(), sources);
        if (!sources.Initialized || sources.GroupCount != sourceView.GroupIds.Length)
            throw new InvalidOperationException("Standing pose requires completed shared synchronization.");
        var candidate = sources;
        var group = candidate.Groups[sourceView.GroupIds.IndexOf(_syncGroupId)].Group;
        var times = previous.Times;
        var seconds = previous.SourceSeconds;
        if (!previous.HasSourceSeconds) for (var clip = 0; clip < _lengths.Length; clip++) seconds[clip] = times[clip] * _lengths[clip];
        seconds[0] = _idleSource.StartPosition;
        times[0] = _idleSource.StartPosition / _lengths[0];
        for (var i = 0; i < candidate.SampleCount; i++)
        {
            var sample = candidate.Samples[i]; var pose = _poseBySample[sample.SampleId];
            if (pose >= 0) { times[pose] = sample.Time / _lengths[pose]; seconds[pose] = sample.Time; }
        }
        candidate.Group = group;
        frame.Times = times; frame.SourceSeconds = seconds; frame.HasSourceSeconds = true;
        frame.Sync = candidate;
        frame.State = frame.State with { Phase = group.HasLeader
            ? AlsLocomotionSourceTiming.NormalizeLoopingPhase(group.Ratio) : previous.State.Phase };
    }

    private static AlsCurveSampler ReadCurve(JsonElement source)
    {
        if (source.GetProperty("preInfinity").GetString() != "RCCE_Constant" ||
            source.GetProperty("postInfinity").GetString() != "RCCE_Constant")
            throw new InvalidOperationException("Unsupported locomotion curve infinity mode.");
        var keys = source.GetProperty("keys").EnumerateArray().Select(key =>
        {
            if (key.GetProperty("tangent_weight_mode").GetString() != "RCTWM_WeightedNone")
                throw new InvalidOperationException("Weighted curve tangents require explicit support.");
            return new AlsFloatCurveKeyDefinition(key.GetProperty("time").GetSingle(), key.GetProperty("value").GetSingle(),
                key.GetProperty("arrive_tangent").GetSingle(), key.GetProperty("leave_tangent").GetSingle(),
                key.GetProperty("interp_mode").GetString() switch
                {
                    "RCIM_Constant" => AlsCurveInterpolation.Constant,
                    "RCIM_Linear" => AlsCurveInterpolation.Linear,
                    "RCIM_Cubic" => AlsCurveInterpolation.Cubic,
                    _ => throw new InvalidOperationException("Unsupported locomotion curve interpolation."),
                });
        }).ToArray();
        var sampler = new AlsCurveSampler(keys);
        foreach (var check in source.GetProperty("verification").EnumerateArray())
            if (MathF.Abs(Sample(sampler, check.GetProperty("input").GetSingle()) - check.GetProperty("value").GetSingle()) > 0.00005f)
                throw new InvalidOperationException($"Native curve verification failed: {source.GetProperty("name")}");
        return sampler;
    }

    private static float Sample(AlsCurveSampler sampler, float time) => sampler.TrySample(0, time, out var value)
        ? value : throw new InvalidOperationException("Curve sampling failed.");
}
