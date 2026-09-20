using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

[InlineArray(128)] internal struct AlsDetailFrameBones { private AlsLocalPose _element; }
[InlineArray(128)] internal struct AlsDetailFrameCurves { private AlsInertialCurve _element; }
internal struct AlsCycleDetailFrame
{
    public bool Updated;
    public bool HasPose;
    public float RecordedWeight;
    public int BoneCount;
    public AlsDetailMachineUpdate Update;
    public AlsStandingBaseFrame Standing;
    public AlsDetailFrameBones Pose;
    public AlsDetailFrameCurves Curves;
}

// Production Detail consumer. Source times/epochs/notifications belong to the shared Cycle source transaction.
// Standing/Stop provide relevance through the native cache traversal. Main/Slot remain upstream owners.
internal sealed class AlsCycleDetailGraph : IDisposable
{
    private readonly AlsLocomotionDetailProfile _profile;
    private readonly AlsDetailPoseSampler _sampler;
    private readonly AlsStandingStateGraph _standing;
    private readonly AlsLocomotionSourcePlayerBinding[] _players;
    private readonly AlsLocomotionSourceSampleBinding[] _samples;
    private readonly string[] _curveNames;
    private readonly Dictionary<string, int> _curveIndices;
    private readonly AlsLocalPose[] _idle, _raw, _output, _detailPose, _target;
    private readonly AlsPrecisePose[] _preciseCycle;
    private readonly AlsPrecisePose[] _idlePrecise, _detailPosePrecise, _targetPrecise;
    private readonly AlsInertialCurve[] _inputCurves, _outputCurves, _idleCurves, _detailCurves;
    private readonly AlsInertialization _committedHistory, _candidateHistory;
    private readonly Godot.Skeleton3D _skeleton;
    private readonly long _attachParent;
    private bool _hasCandidate;
    internal ReadOnlySpan<string> CurveNames => _curveNames;
    internal AlsRefactoredPoseCurveRuntime? RefactoredMovementCurves { get; }
    internal AlsStandingCachedGraphDefinition CacheDefinition => _standing.Definition;

    public AlsCycleDetailGraph(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set, int skeleton,
        AlsLocomotionSourcePlayerBinding[] players, AlsLocomotionSourceSampleBinding[] samples, AlsPoseAnimationProfile? poseProfile,
        AlsRefactoredPoseCurveWrite[]? refactoredMovementCurves = null)
    {
        _preciseCycle=new AlsPrecisePose[library.MovementSources(set,skeleton).BoneCount];
        _profile = AlsLocomotionDetailCompiler.Compile(Godot.FileAccess.GetFileAsString(
            "res://assets/config/v4_locomotion_detail_graph.json"), set, skeleton);
        _players = players.Where(p => p.Domain == AlsLocomotionSourceDomain.Detail).OrderBy(p => p.DetailSlot).ToArray();
        _samples = _players.Select(p => samples[p.SampleStart]).ToArray();
        if (_players.Length != 16 || _players.Where((p, i) => p.DetailSlot != i || p.SampleCount != 1 ||
            p.Kind != AlsLocomotionSourceKind.Sequence || p.Loop || p.PlayRateInput != AlsSourceRateInput.Constant).Any())
            throw new InvalidOperationException("Detail source layout differs from its production sampler.");
        for (var slot = 0; slot < _players.Length; slot++)
        {
            var pose = _profile.States[2 + slot / 4].Players[slot % 4];
            if (_samples[slot].AnimationId != pose.AnimationId || _samples[slot].AdditiveBaseAnimationId != pose.AdditiveBaseAnimationId ||
                _players[slot].DefaultPlayRate != pose.PlayRate || _samples[slot].AssetRateScale != pose.AssetRateScale)
                throw new InvalidOperationException("Detail pose and shared source bindings disagree.");
        }
        _skeleton = library.Skeleton;
        _attachParent = unchecked((long)_skeleton.GetParent().GetInstanceId());
        _standing = new(library, set, skeleton, _profile, players, samples, poseProfile);
        // This is the Grounded cache payload, even when clocks come from the full movement
        // snapshot. Unvisited air/landing assets must not change its curve indices or cause
        // their missing curves to appear as authored zeroes. The outer pose owner maps names.
        _curveNames = players.Where(p => p.Domain is AlsLocomotionSourceDomain.Cycle or AlsLocomotionSourceDomain.Detail or
                AlsLocomotionSourceDomain.Stop or AlsLocomotionSourceDomain.Standing or
                AlsLocomotionSourceDomain.MainGrounded or AlsLocomotionSourceDomain.Crouching)
            .SelectMany(p => samples.AsSpan(p.SampleStart, p.SampleCount).ToArray()).SelectMany(s => s.AdditiveBaseAnimationId >= 0 ? new[] { s.AnimationId, s.AdditiveBaseAnimationId } : new[] { s.AnimationId })
            .Concat(poseProfile?.Turns.Select(t => t.AnimationId) ?? [])
            .Distinct().SelectMany(id => set.Animations[id].Curves).Where(c => c.Provenance == AlsCurveProvenance.SourceCurve).Select(c => c.SourceName).Append("YawOffset")
            .Concat(_standing.ModifiedCurveNames)
            .Concat(refactoredMovementCurves is null ? [] : new[] { "PoseMoving" })
            .Concat(AlsAnimationRuntimeOptions.Has("--refactored-pose-curves") || AlsAnimationRuntimeOptions.Has("--refactored-state-curves")
                ? AlsRefactoredV4SourceCurves.TargetNames : [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (refactoredMovementCurves is not null)
            RefactoredMovementCurves = new(refactoredMovementCurves, _curveNames,
                [AlsRefactoredPoseCurveSite.StandingMovement, AlsRefactoredPoseCurveSite.CrouchingMovement]);
        _curveIndices = _curveNames.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i, StringComparer.Ordinal);
        var bones = library.MovementSources(set, skeleton).BoneCount;
        if (bones > 128 || _curveNames.Length > 128) throw new InvalidOperationException("Detail output exceeds its fixed frame capacity.");
        _idlePrecise = new AlsPrecisePose[bones]; _detailPosePrecise = new AlsPrecisePose[bones]; _targetPrecise = new AlsPrecisePose[bones];
        _sampler = new(library, set, _profile);
        _idle = new AlsLocalPose[bones]; _raw = new AlsLocalPose[bones]; _output = new AlsLocalPose[bones];
        _detailPose = new AlsLocalPose[bones]; _target = new AlsLocalPose[bones];
        _inputCurves = new AlsInertialCurve[_curveNames.Length]; _outputCurves = new AlsInertialCurve[_curveNames.Length];
        _idleCurves = new AlsInertialCurve[_curveNames.Length];
        _detailCurves = new AlsInertialCurve[_curveNames.Length];
        _committedHistory = new(bones, _curveNames.Length, .01f, -System.Numerics.Vector3.UnitX);
        _candidateHistory = new(bones, _curveNames.Length, .01f, -System.Numerics.Vector3.UnitX);
    }

    public AlsCycleDetailFrame Prepare(in AlsCycleDetailFrame previous, in AlsCycleSyncFrame sources,
        in AlsFrameResult result, in AlsStandingMovementInput movement, NVector4 velocity, float gaitCurve, float feetPosition, float delta)
    {
        var standing = _standing.Prepare(previous.Standing, sources, result, movement, velocity, gaitCurve, feetPosition,
            Remaining(previous.Update.State, sources), delta);
        return new() { Standing = standing, Update = standing.Update.DetailUpdated ? standing.Update.Detail : previous.Update,
            Updated = standing.Update.DetailUpdated, RecordedWeight = standing.Update.State.DetailRecordedWeight };
    }

    public bool IsActive(in AlsStandingBaseFrame frame, in AlsPoseUpdateContext context) => _standing.IsActive(frame, context);
    public bool RotationActive(in AlsStandingBaseFrame frame, int playerId) => _standing.RotationActive(frame, playerId);

    public AlsStandingUpdateInput Observe(in AlsCycleDetailFrame previous, in AlsCycleSyncFrame sources,
        in AlsFrameResult result, in AlsStandingMovementInput movement, float gaitCurve, float feetPosition,
        float mainGroundedRecordedWeight, Span<AlsGroundedAutomaticTime> times) =>
        _standing.Observe(previous.Standing, sources, result, movement, gaitCurve, feetPosition,
            Remaining(previous.Update.State, sources), mainGroundedRecordedWeight, times);

    public AlsCycleDetailFrame Consume(in AlsCycleDetailFrame previous, in AlsStandingCachedGraphUpdate update,
        in AlsSourceRotationInput rotation, NVector4 velocity)
    {
        var standing = _standing.Consume(previous.Standing, update, rotation, velocity);
        var frame = update.StandingUpdated ? default : previous;
        frame.Standing = standing; frame.Update = update.DetailUpdated || update.DetailInitialized ? update.Detail : previous.Update;
        frame.Updated = update.DetailUpdated; frame.RecordedWeight = update.State.DetailRecordedWeight;
        return frame;
    }

    private float Remaining(in AlsDetailMachineState state, in AlsCycleSyncFrame sources)
    {
        if (!state.HasUpdated || state.CurrentState < AlsDetailState.WalkRun) return float.MaxValue;
        var config = _profile.States[(int)state.CurrentState];
        Span<AlsDetailPlayerObservation> observations = stackalloc AlsDetailPlayerObservation[4];
        for (var i = 0; i < 4; i++)
        {
            var direction = config.RelevancyPlayerOrder[i];
            var slot = ((int)state.CurrentState - 2) * 4 + direction;
            var player = _players[slot]; var sample = _samples[slot];
            var time = sources.Times[player.PlayerId];
            observations[i] = new(sample.DurationSeconds,
                player.DefaultPlayRate * sample.AssetRateScale < 0 ? sample.DurationSeconds - time : time,
                sources.CachedWeights[player.PlayerId]);
        }
        return AlsLocomotionDetailMachine.RelevantTimeRemaining(observations, out _);
    }

    public void Collect(in AlsCycleDetailFrame frame, NVector4 velocity, ref AlsCycleSyncFrame candidate,
        Span<AlsLocomotionSourceUpdate> updates, Span<AlsLocomotionSampleUpdate> samples, ref int playerCount, ref int sampleCount)
    {
        _standing.Collect(frame.Standing, ref candidate, updates, samples, ref playerCount, ref sampleCount);
        if (!frame.Updated) return;
        for (var slot = 0; slot < _players.Length; slot++)
        {
            var state = 2 + slot / 4; var player = _players[slot];
            if ((frame.Update.ClearCachedWeightStates & (1 << state)) != 0) candidate.CachedWeights[player.PlayerId] = 0;
        }
        for (var i = 0; i < frame.Update.InitializationCount; i++)
        {
            var state = frame.Update.GetInitialization(i);
            if (state < AlsDetailState.WalkRun) continue;
            for (var direction = 0; direction < 4; direction++)
            {
                var slot = ((int)state - 2) * 4 + direction;
                var player = _players[slot]; var sample = _samples[slot];
                candidate.Times[player.PlayerId] = AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence,
                    player.StartPosition, sample.DurationSeconds, player.DefaultPlayRate * sample.SampleRateScale,
                    player.PlayRateBasis, sample.AssetRateScale);
                candidate.Epochs[player.PlayerId] = AlsAssetSourceInitialization.NextEpoch(candidate.Epochs[player.PlayerId]);
            }
        }
        var weights = AlsDetailPoseComposer.SampleWeights(velocity);
        for (var n = 0; n < frame.Update.UpdateCount; n++)
        {
            var child = frame.Update.GetUpdate(n);
            if (child.State < AlsDetailState.WalkRun) continue;
            for (var direction = 0; direction < 4; direction++)
            {
                if (weights[direction] == 0) continue;
                var slot = ((int)child.State - 2) * 4 + direction;
                var player = _players[slot]; var sample = _samples[slot];
                var weight = child.Weight * weights[direction];
                candidate.CachedWeights[player.PlayerId] = weight;
                samples[sampleCount] = new(sample.SampleId, 1, sample.SampleRateScale);
                updates[playerCount++] = new(player.PlayerId, candidate.Epochs[player.PlayerId], candidate.Times[player.PlayerId],
                    weight, sampleCount++, 1, child.InertializationSync || frame.Standing.Update.DetailContext.InertializationSync);
            }
        }
    }

    internal void EvaluateRaw(in AlsStandingCycleFrame frame, AlsCyclePoseSampler cycle, AlsStandingCycleGraph graph,
        Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
    {
        Span<float> times = stackalloc float[16];
        for (var i = 0; i < times.Length; i++) times[i] = frame.Sync.Times[_players[i].PlayerId];
        var rawCycle = frame with { State = frame.State with { MovingWeight = 1 } };
        cycle.Sample(frame.Detail.Standing.Update.CycleUpdated ? rawCycle : frame with { State = frame.State with { MovingWeight = 0 } });
        if (frame.Detail.Updated)
            _sampler.ComposeMachine(frame.Detail.Update.State, times, frame.State.VelocityBlend, cycle.Output, _detailPose);
        else cycle.Output.CopyTo(_detailPose);
        if (!frame.Detail.Standing.Update.CycleUpdated || !cycle.CopyCurves(_curveNames, _detailCurves))
            graph.SampleCycleCurves(rawCycle, _curveNames, _detailCurves);
        for (var i = 0; i < _curveNames.Length; i++)
        {
            var value = _detailCurves[i];
            if (frame.Detail.Updated)
                value = _sampler.SampleMachineCurveWithPresence(frame.Detail.Update.State, times, frame.State.VelocityBlend, _curveNames[i], value);
            _detailCurves[i] = value;
        }
        RefactoredMovementCurves?.Apply(AlsRefactoredPoseCurveSite.StandingMovement, _detailCurves, 0);
        cycle.SampleIdle(frame, _idle);
        var idleFrame = frame with { State = frame.State with { MovingWeight = 0 } };
        graph.SampleCycleCurves(idleFrame, _curveNames, _idleCurves);
        ComposeRawStanding(frame, bones, curves);
    }

    // Cache-transparency oracle for isolated integration checks. Compose the visited Standing
    // inputs directly, without reading the outer SaveCachedPose bank or publishing any history.
    internal void EvaluateUncachedPrecise(in AlsStandingCycleFrame frame, AlsCyclePoseSampler cycle,
        AlsStandingCycleGraph graph, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
    {
        var stack = frame.Detail.Standing.Update.State.Standing.Transitions;
        var states = 1 << (stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState);
        for (var i = 0; i < stack.Count; i++) states |= 1 << stack.GetTransition(i).To;
        if ((states & ((1 << 1) | (1 << 2))) != 0)
        {
            var rawCycle = frame with { State = frame.State with { MovingWeight = 1 } };
            cycle.Sample(rawCycle); cycle.PreciseOutput.CopyTo(_preciseCycle);
            if (!cycle.CopyCurves(_curveNames, _detailCurves)) graph.SampleCycleCurves(rawCycle, _curveNames, _detailCurves);
            graph.EvaluateCycleTailPrecise(frame, _curveNames, _preciseCycle, _detailCurves);
            Span<float> times = stackalloc float[16];
            for (var i = 0; i < times.Length; i++) times[i] = frame.Sync.Times[_players[i].PlayerId];
            _sampler.ComposeMachine(frame.Detail.Update.State, times, frame.State.VelocityBlend, _preciseCycle, _detailPosePrecise);
            for (var i = 0; i < curves.Length; i++) _detailCurves[i] = _sampler.SampleMachineCurveWithPresence(
                frame.Detail.Update.State, times, frame.State.VelocityBlend, _curveNames[i], _detailCurves[i]);
            RefactoredMovementCurves?.Apply(AlsRefactoredPoseCurveSite.StandingMovement, _detailCurves, 0);
        }
        cycle.SampleIdle(frame, _idlePrecise);
        graph.SampleCycleCurves(frame with { State = frame.State with { MovingWeight = 0 } }, _curveNames, _idleCurves);
        ComposeRawStanding(frame, _preciseCycle, curves);
        for (var bone = 0; bone < bones.Length; bone++) bones[bone] = _preciseCycle[bone].ToSingle();
    }

    public void Evaluate(ref AlsStandingCycleFrame frame, in AlsStandingCycleFrame previous,
        AlsCyclePoseSampler cycle, AlsStandingCycleGraph graph, float delta)
    {
        EvaluateRaw(frame, cycle, graph, _raw, _inputCurves);
        _candidateHistory.CopyFrom(_committedHistory);
        if (!previous.Detail.HasPose) _candidateHistory.Reset();
        var standing = frame.Detail.Standing;
        _candidateHistory.Update(delta);
        if (standing.InertializationSeconds >= 0) _candidateHistory.Request(standing.InertializationSeconds);
        var world = _skeleton.GlobalTransform;
        var rotation = world.Basis.Orthonormalized().GetRotationQuaternion();
        var component = new AlsLocalPose(new(world.Origin.X, world.Origin.Y, world.Origin.Z),
            new(rotation.X, rotation.Y, rotation.Z, rotation.W), new(world.Basis.Scale.X, world.Basis.Scale.Y, world.Basis.Scale.Z));
        _candidateHistory.Evaluate(_raw, _inputCurves, component, _attachParent, 0, _output, _outputCurves);
        var detail = frame.Detail;
        for (var bone = 0; bone < _output.Length; bone++)
            detail.Pose[bone] = _output[bone];
        for (var i = 0; i < _curveNames.Length; i++)
            detail.Curves[i] = _outputCurves[i];
        detail.BoneCount = _output.Length;
        detail.HasPose = true;
        frame.Detail = detail;
        _hasCandidate = true;
    }

    // Raw SaveCachedPose producers. The outer BaseLayer owns the scope and final inertialization.
    // These methods sample the completed source transaction without advancing any source or history.
    internal void EvaluateCachedSource(int node, in AlsStandingCycleFrame frame, AlsCyclePoseSampler cycle,
        AlsStandingCycleGraph graph, IAlsGroundedPoseCacheReader reader,
        Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
    {
        if (bones.Length != _raw.Length || curves.Length != _curveNames.Length)
            throw new ArgumentException("Standing cache payload layout differs.");
        var definition = _standing.Definition;
        if (node == definition.CycleCacheIndex)
        {
            var rawCycle = frame with { State = frame.State with { MovingWeight = 1 } };
            cycle.Sample(rawCycle); cycle.PreciseOutput.CopyTo(_preciseCycle);
            if (!cycle.CopyCurves(_curveNames, curves)) graph.SampleCycleCurves(rawCycle, _curveNames, curves);
            graph.EvaluateCycleTailPrecise(frame,_curveNames,_preciseCycle,curves);
            for(var b=0;b<bones.Length;b++)bones[b]=_preciseCycle[b].ToSingle();
            return;
        }
        if (node == definition.DetailBinding.CacheNodeIndex)
        {
            var stack = frame.Detail.Update.State.Transitions;
            var visited = 0;
            ReadDetailCycle(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState);
            for (var i = 0; i < stack.Count; i++) ReadDetailCycle(stack.GetTransition(i).To);
            Span<float> times = stackalloc float[16];
            for (var i = 0; i < times.Length; i++) times[i] = frame.Sync.Times[_players[i].PlayerId];
            _sampler.ComposeMachine(frame.Detail.Update.State, times, frame.State.VelocityBlend, _detailPose, bones);
            for (var i = 0; i < curves.Length; i++) curves[i] = _sampler.SampleMachineCurveWithPresence(
                frame.Detail.Update.State, times, frame.State.VelocityBlend, _curveNames[i], _detailCurves[i]);
            // V4 Locomotion (Detail) is the cache read by Moving and Stop. The
            // Refactored Movement producer is inserted before those consumers.
            RefactoredMovementCurves?.Apply(AlsRefactoredPoseCurveSite.StandingMovement, curves, 0);
            return;
            void ReadDetailCycle(int state)
            {
                if ((visited & (1 << state)) != 0) return;
                visited |= 1 << state;
                reader.CacheState(AlsMainBoneMachine.Detail, state);
                reader.Read(definition.DetailReads[state], _detailPose, _detailCurves);
            }
        }
        if (node != definition.StandingBinding.CacheNodeIndex)
            throw new ArgumentException("Unknown Standing cached source.");
        var standing = frame.Detail.Standing;
        var transitions = standing.Update.State.Standing.Transitions;
        // Cache reads follow EvaluateState's first-From / subsequent-To traversal, including alpha zero.
        var seen = 0;
        ReadStandingInputs(transitions.Count > 0 ? transitions.GetTransition(0).From : transitions.CurrentState);
        for (var i = 0; i < transitions.Count; i++) ReadStandingInputs(transitions.GetTransition(i).To);
        cycle.SampleIdle(frame, _idle);
        var idleFrame = frame with { State = frame.State with { MovingWeight = 0 } };
        graph.SampleCycleCurves(idleFrame, _curveNames, _idleCurves);
        ComposeRawStanding(frame, bones, curves);

        void ReadStandingInputs(int state)
        {
            if ((seen & (1 << state)) != 0) return;
            seen |= 1 << state;
            reader.CacheState(AlsMainBoneMachine.Standing, state);
            if (state == 1) reader.Read(definition.MovingReadIndex, _detailPose, _detailCurves);
            if (state != 2) return;
            var stop = standing.Stop.Machine.State.Transitions;
            var seenStop = 0;
            ReadStop(stop.Count > 0 ? stop.GetTransition(0).From : stop.CurrentState);
            for (var i = 0; i < stop.Count; i++) ReadStop(stop.GetTransition(i).To);
            void ReadStop(int index)
            {
                if ((seenStop & (1 << index)) != 0) return;
                seenStop |= 1 << index;
                reader.CacheState(AlsMainBoneMachine.Stop, index);
                var read = definition.StopReads[index];
                if (read < 0) throw new InvalidOperationException("A Stop conduit cannot evaluate a cached pose.");
                reader.Read(read, _detailPose, _detailCurves);
            }
        }
    }

    private void ComposeRawStanding(in AlsStandingCycleFrame frame, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
    {
        var standing = frame.Detail.Standing;
        var stack = standing.Update.State.Standing.Transitions;
        var initial = stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState;
        _standing.ComposeState(initial, standing, frame.Sync, frame.State.VelocityBlend, _idle, _sampler.ReferencePose, _detailPose, bones);
        for (var i = 0; i < curves.Length; i++) curves[i] = _standing.StateCurveWithPresence(initial, standing, frame.Sync,
            frame.State.VelocityBlend, _curveNames[i], _idleCurves[i], _detailCurves[i]);
        for (var n = 0; n < stack.Count; n++)
        {
            var edge = stack.GetTransition(n);
            _standing.ComposeState(edge.To, standing, frame.Sync, frame.State.VelocityBlend, _idle, _sampler.ReferencePose, _detailPose, _target);
            for (var bone = 0; bone < bones.Length; bone++) bones[bone] = AlsPoseBlender.BlendRaw(bones[bone], _target[bone], edge.Alpha);
            for (var i = 0; i < curves.Length; i++) curves[i] = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(curves[i], 1 - edge.Alpha),
                _standing.StateCurveWithPresence(edge.To, standing, frame.Sync, frame.State.VelocityBlend, _curveNames[i], _idleCurves[i], _detailCurves[i]), edge.Alpha);
        }
        if (stack.Count > 0) for (var bone = 0; bone < bones.Length; bone++) bones[bone] = AlsPoseBlender.Normalize(bones[bone]);
    }

    internal void StoreRawPose(ref AlsStandingCycleFrame frame, ReadOnlySpan<AlsLocalPose> bones, ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (bones.Length != _raw.Length || curves.Length != _curveNames.Length)
            throw new ArgumentException("Standing raw output layout differs.");
        var detail = frame.Detail;
        bones.CopyTo(((Span<AlsLocalPose>)detail.Pose)[..bones.Length]);
        curves.CopyTo(((Span<AlsInertialCurve>)detail.Curves)[..curves.Length]);
        detail.BoneCount = bones.Length; detail.HasPose = true; frame.Detail = detail;
        _hasCandidate = false;
    }

    public float SampleCurve(in AlsCycleDetailFrame frame, string name) =>
        _curveIndices.TryGetValue(name, out var index) ? frame.Curves[index].Value : 0;

    public void Commit()
    {
        if (!_hasCandidate) return;
        _committedHistory.CopyFrom(_candidateHistory);
        _hasCandidate = false;
    }

    public void Dispose() { _sampler.Dispose(); _standing.Dispose(); }

    internal void EvaluateCachedSource(int node, in AlsStandingCycleFrame frame, AlsCyclePoseSampler cycle,
        AlsStandingCycleGraph graph, IAlsGroundedPoseCacheReader reader,
        Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
    {
        if (bones.Length != _raw.Length || curves.Length != _curveNames.Length)
            throw new ArgumentException("Standing cache payload layout differs.");
        var definition = _standing.Definition;
        if (node == definition.CycleCacheIndex)
        {
            var rawCycle = frame with { State = frame.State with { MovingWeight = 1 } };
            cycle.Sample(rawCycle); cycle.PreciseOutput.CopyTo(_preciseCycle);
            if (!cycle.CopyCurves(_curveNames, curves)) graph.SampleCycleCurves(rawCycle, _curveNames, curves);
            graph.EvaluateCycleTailPrecise(frame,_curveNames,_preciseCycle,curves);
            _preciseCycle.CopyTo(bones);
            return;
        }
        if (node == definition.DetailBinding.CacheNodeIndex)
        {
            var stack = frame.Detail.Update.State.Transitions;
            var visited = 0;
            ReadDetailCycle(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState);
            for (var i = 0; i < stack.Count; i++) ReadDetailCycle(stack.GetTransition(i).To);
            Span<float> times = stackalloc float[16];
            for (var i = 0; i < times.Length; i++) times[i] = frame.Sync.Times[_players[i].PlayerId];
            _sampler.ComposeMachine(frame.Detail.Update.State, times, frame.State.VelocityBlend, _detailPosePrecise, bones);
            for (var i = 0; i < curves.Length; i++) curves[i] = _sampler.SampleMachineCurveWithPresence(
                frame.Detail.Update.State, times, frame.State.VelocityBlend, _curveNames[i], _detailCurves[i]);
            RefactoredMovementCurves?.Apply(AlsRefactoredPoseCurveSite.StandingMovement, curves, 0);
            return;
            void ReadDetailCycle(int state)
            {
                if ((visited & (1 << state)) != 0) return;
                visited |= 1 << state;
                reader.CacheState(AlsMainBoneMachine.Detail, state);
                reader.Read(definition.DetailReads[state], _detailPosePrecise, _detailCurves);
            }
        }
        if (node != definition.StandingBinding.CacheNodeIndex)
            throw new ArgumentException("Unknown Standing cached source.");
        var standing = frame.Detail.Standing;
        var transitions = standing.Update.State.Standing.Transitions;
        // Cache reads follow EvaluateState's first-From / subsequent-To traversal, including alpha zero.
        var seen = 0;
        ReadStandingInputs(transitions.Count > 0 ? transitions.GetTransition(0).From : transitions.CurrentState);
        for (var i = 0; i < transitions.Count; i++) ReadStandingInputs(transitions.GetTransition(i).To);
        cycle.SampleIdle(frame, _idlePrecise);
        var idleFrame = frame with { State = frame.State with { MovingWeight = 0 } };
        graph.SampleCycleCurves(idleFrame, _curveNames, _idleCurves);
        ComposeRawStanding(frame, bones, curves);

        void ReadStandingInputs(int state)
        {
            if ((seen & (1 << state)) != 0) return;
            seen |= 1 << state;
            reader.CacheState(AlsMainBoneMachine.Standing, state);
            if (state == 1) reader.Read(definition.MovingReadIndex, _detailPosePrecise, _detailCurves);
            if (state != 2) return;
            var stop = standing.Stop.Machine.State.Transitions;
            var seenStop = 0;
            ReadStop(stop.Count > 0 ? stop.GetTransition(0).From : stop.CurrentState);
            for (var i = 0; i < stop.Count; i++) ReadStop(stop.GetTransition(i).To);
            void ReadStop(int index)
            {
                if ((seenStop & (1 << index)) != 0) return;
                seenStop |= 1 << index;
                reader.CacheState(AlsMainBoneMachine.Stop, index);
                var read = definition.StopReads[index];
                if (read < 0) throw new InvalidOperationException("A Stop conduit cannot evaluate a cached pose.");
                reader.Read(read, _detailPosePrecise, _detailCurves);
            }
        }
    }
    private void ComposeRawStanding(in AlsStandingCycleFrame frame, Span<AlsPrecisePose> bones, Span<AlsInertialCurve> curves)
    {
        var standing = frame.Detail.Standing;
        var stack = standing.Update.State.Standing.Transitions;
        var initial = stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState;
        _standing.ComposeState(initial, standing, frame.Sync, frame.State.VelocityBlend, _idlePrecise, _sampler.PreciseReferencePose, _detailPosePrecise, bones);
        for (var i = 0; i < curves.Length; i++) curves[i] = _standing.StateCurveWithPresence(initial, standing, frame.Sync,
            frame.State.VelocityBlend, _curveNames[i], _idleCurves[i], _detailCurves[i]);
        for (var n = 0; n < stack.Count; n++)
        {
            var edge = stack.GetTransition(n);
            _standing.ComposeState(edge.To, standing, frame.Sync, frame.State.VelocityBlend, _idlePrecise, _sampler.PreciseReferencePose, _detailPosePrecise, _targetPrecise);
            for (var bone = 0; bone < bones.Length; bone++) bones[bone] = AlsPrecisePoseBlender.BlendRaw(bones[bone], _targetPrecise[bone], edge.Alpha);
            for (var i = 0; i < curves.Length; i++) curves[i] = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(curves[i], 1 - edge.Alpha),
                _standing.StateCurveWithPresence(edge.To, standing, frame.Sync, frame.State.VelocityBlend, _curveNames[i], _idleCurves[i], _detailCurves[i]), edge.Alpha);
        }
        if (stack.Count > 0) for (var bone = 0; bone < bones.Length; bone++) bones[bone] = AlsPrecisePoseBlender.Normalize(bones[bone]);
    }
    internal void StoreRawPose(ref AlsStandingCycleFrame frame, ReadOnlySpan<AlsPrecisePose> bones, ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (bones.Length != _raw.Length || curves.Length != _curveNames.Length)
            throw new ArgumentException("Standing raw output layout differs.");
        // Legacy frame diagnostics/rendering are projected here; cache consumers retain the precise payload.
        for (var bone = 0; bone < bones.Length; bone++) _raw[bone] = bones[bone].ToSingle();
        StoreRawPose(ref frame, _raw, curves);
    }

}
