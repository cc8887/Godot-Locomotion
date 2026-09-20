using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

public partial class DetailMachineSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var profile = AlsLocomotionProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_cycle_locomotion_profile.json"), set);
            var pose = AlsPoseProfileCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/p4_pose_profile.json"), set, profile);
            var detail = AlsLocomotionDetailCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_locomotion_detail_graph.json"), set, profile.SkeletonId);
            var sources = AlsLocomotionSourceCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_locomotion_source_graph.json"), set, profile.SkeletonId);
            var boneChecks = 0; var requests = 0; var retries = 0; var resets = 0; var seen = 0; var maxCorrection = 0f;
            foreach (var hz in new[] { 30, 60, 120 })
            {
                using var library = AlsAnimationLibraryBuilder.Build(set, profile, pose, detail);
                AddChild(library.Root);
                using var sampler = new AlsDetailPoseSampler(library, set, detail);
                var bones = sampler.ReferencePose.Length;
                var committed = new AlsInertialization(bones, 1, .01f, -NVector3.UnitX);
                var candidate = new AlsInertialization(bones, 1, .01f, -NVector3.UnitX);
                var retry = new AlsInertialization(bones, 1, .01f, -NVector3.UnitX);
                var raw = new AlsLocalPose[bones]; var output = new AlsLocalPose[bones]; var replay = new AlsLocalPose[bones];
                var manual = new AlsLocalPose[bones]; var target = new AlsLocalPose[bones];
                var curves = new AlsInertialCurve[1]; var resultCurves = new AlsInertialCurve[1]; var retryCurves = new AlsInertialCurve[1];
                var sourceCommitted = new SourceState(sources); var sourceCandidate = new SourceState(sources); var sourceRetry = new SourceState(sources);
                var times = sourceCandidate.Times;
                var observations = new AlsDetailPlayerObservation[4];
                var machine = default(AlsDetailMachineState);
                var delta = 1f / hz;
                for (var frame = 0; frame < 5 * hz; frame++)
                {
                    var velocity = frame % hz < hz / 2 ? new NVector4(.1f, .2f, .6f, .1f) : new NVector4(.1f, .2f, .1f, .6f);
                    var remaining = float.MaxValue;
                    if (machine.HasUpdated && machine.CurrentState >= AlsDetailState.WalkRun)
                    {
                        var state = detail.States[(int)machine.CurrentState];
                        for (var i = 0; i < 4; i++)
                        {
                            var source = state.RelevancyPlayerOrder[i];
                            var slot = ((int)state.Id - 2) * 4 + source;
                            var player = state.Players[source];
                            var length = set.Animations[player.AnimationId].PlayLength;
                            var adjusted = player.PlayRate * player.AssetRateScale < 0 ? length - sourceCommitted.Times[slot] : sourceCommitted.Times[slot];
                            observations[i] = new(length, adjusted, sourceCommitted.CachedWeights[slot]);
                        }
                        remaining = AlsLocomotionDetailMachine.RelevantTimeRemaining(observations, out _);
                    }
                    var walking = frame >= 3 * hz && frame < 4 * hz;
                    var input = new AlsDetailMachineInput(walking ? AlsGait.Walking : AlsGait.Running, walking ? 1 : 2,
                        frame >= hz && frame < 2 * hz, remaining, 1, frame == 0 ? .5f : 1, 1);
                    var serial = frame + (frame >= 4 * hz ? 2 : 0);
                    var update = AlsLocomotionDetailMachine.Update(detail.Transitions, machine, input, delta, serial);
                    var replayUpdate = AlsLocomotionDetailMachine.Update(detail.Transitions, machine, input, delta, serial);
                    Require(update.State.CurrentState == replayUpdate.State.CurrentState && update.State.ElapsedSeconds == replayUpdate.State.ElapsedSeconds,
                        "Detail state candidate retry differs.");
                    seen |= 1 << (int)update.State.CurrentState;
                    if (update.Reinitialized) resets++;
                    sourceCandidate.CopyFrom(sourceCommitted); sourceRetry.CopyFrom(sourceCommitted);
                    sourceCandidate.Tick(update, velocity, delta);
                    sourceRetry.Tick(replayUpdate, velocity, delta);
                    Require(sourceCandidate.SameAs(sourceRetry), "Detail source time/group/epoch retry differs.");
                    sampler.ComposeMachine(update.State, times, velocity, sampler.AdditiveBasePose, raw);
                    var stack = update.State.Transitions;
                    var initial = (AlsDetailState)(stack.Count > 0 ? stack.GetTransition(0).From : stack.CurrentState);
                    sampler.Compose(initial, Times(initial, times), velocity, sampler.AdditiveBasePose, manual);
                    var expectedCurve = sampler.SampleCurve(initial, Times(initial, times), velocity, "Mask_Lean", 0);
                    for (var i = 0; i < stack.Count; i++)
                    {
                        var edge = stack.GetTransition(i);
                        sampler.Compose((AlsDetailState)edge.To, Times((AlsDetailState)edge.To, times), velocity, sampler.AdditiveBasePose, target);
                        for (var bone = 0; bone < bones; bone++) manual[bone] = AlsPoseBlender.BlendRaw(manual[bone], target[bone], edge.Alpha);
                        expectedCurve = expectedCurve * (1 - edge.Alpha) +
                            sampler.SampleCurve((AlsDetailState)edge.To, Times((AlsDetailState)edge.To, times), velocity, "Mask_Lean", 0) * edge.Alpha;
                    }
                    if (stack.Count > 0) for (var bone = 0; bone < bones; bone++) manual[bone] = AlsPoseBlender.Normalize(manual[bone]);
                    Require(raw.AsSpan().SequenceEqual(manual), "Detail machine pose composition differs from its source states.");
                    curves[0] = new(sampler.SampleMachineCurve(update.State, times, velocity, "Mask_Lean", 0));
                    Require(curves[0].Value == expectedCurve, "Detail machine curve composition differs from its state weights.");
                    candidate.CopyFrom(committed); retry.CopyFrom(committed);
                    candidate.Update(delta); retry.Update(delta);
                    if (update.InertializationSeconds >= 0)
                    {
                        candidate.Request(update.InertializationSeconds); retry.Request(replayUpdate.InertializationSeconds); requests++;
                    }
                    candidate.Evaluate(raw, curves, AlsLocalPose.Identity, 0, 5, output, resultCurves);
                    retry.Evaluate(raw, curves, AlsLocalPose.Identity, 0, 5, replay, retryCurves);
                    Require(output.AsSpan().SequenceEqual(replay) && resultCurves.AsSpan().SequenceEqual(retryCurves), "Detail history retry differs.");
                    for (var bone = 0; bone < bones; bone++)
                    {
                        Require(float.IsFinite(output[bone].Position.LengthSquared()) && MathF.Abs(output[bone].Rotation.LengthSquared() - 1) < .00001f,
                            "Invalid Detail state pose.");
                        maxCorrection = MathF.Max(maxCorrection, NVector3.Distance(raw[bone].Position, output[bone].Position)); boneChecks++;
                    }
                    committed.CopyFrom(candidate); sourceCommitted.CopyFrom(sourceCandidate); machine = update.State; retries++;
                }
                sampler.ComposeMachine(machine, times, NVector4.One, sampler.AdditiveBasePose, raw);
                sampler.AdditiveBasePose.CopyTo(manual);
                sampler.ComposeMachine(machine, times, NVector4.One, manual, manual);
                Require(raw.AsSpan().SequenceEqual(manual), "In-place Detail machine composition differs.");
                var savedTime = times[0]; times[0] = float.NaN;
                var rejected = false;
                try { sampler.ComposeMachine(machine, times, NVector4.One, sampler.AdditiveBasePose, manual); }
                catch (ArgumentException) { rejected = true; }
                times[0] = savedTime;
                Require(rejected && raw.AsSpan().SequenceEqual(manual), "Invalid source time modified Detail output.");
                for (var i = 0; i < 64; i++) Measure();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++) Measure();
                Require(GC.GetAllocatedBytesForCurrentThread() == before, "Detail state/pose/history hot path allocated.");
                void Measure()
                {
                    var step = AlsLocomotionDetailMachine.Update(detail.Transitions, machine,
                        new(AlsGait.Running, 2, true, 1, 1, 1, 1), delta, machine.LastUpdateSerial + 1);
                    sourceCandidate.CopyFrom(sourceCommitted);
                    sourceCandidate.Tick(step, NVector4.One, delta);
                    sampler.ComposeMachine(step.State, times, NVector4.One, sampler.AdditiveBasePose, raw);
                    curves[0] = new(sampler.SampleMachineCurve(step.State, times, NVector4.One, "Mask_Lean", 0));
                    candidate.CopyFrom(committed); candidate.Update(delta);
                    if (step.InertializationSeconds >= 0) candidate.Request(step.InertializationSeconds);
                    candidate.Evaluate(raw, curves, AlsLocalPose.Identity, 0, 5, output, resultCurves);
                }
            }
            Require(seen == 63 && requests > 0 && resets == 6 && maxCorrection > .0001f, "Detail state scenario coverage incomplete.");
            GD.Print($"DETAIL_MACHINE_OK rates=30,60,120 states=6 bone_checks={boneChecks} retries={retries} requests={requests} resets={resets} correction={maxCorrection:F6} alloc=0B sync=asset_runtime identities=source_graph demo=not_connected");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError($"DETAIL_MACHINE_FAILED {error}"); GetTree().Quit(1); }
    }

    // Component-test ownership only. Production source handles and commit belong to the P5 graph bindings.
    internal sealed class SourceState
    {
        public readonly float[] Times = new float[16];
        public readonly float[] CachedWeights = new float[16];
        private readonly long[] _epochs = new long[16];
        private readonly AlsAssetSyncBatchGroupHistory[] _groups;
        private readonly AlsAssetPlayerHistory[] _playerHistory;
        private readonly AlsAssetSampleHistory[] _sampleHistory;
        private int _historyCount;
        private bool _hasHistory;
        private readonly AlsAssetSyncSequence[] _sequences;
        private readonly AlsAssetSyncMarker[] _markers;
        private readonly AlsLocomotionSourceSyncBinding[] _syncBindings;
        private readonly AlsLocomotionSourcePlayerBinding[] _players;
        private readonly AlsLocomotionSourceSampleBinding[] _samples;
        private readonly int[] _slotByPlayer;
        private readonly int[] _groupIds;
        private readonly string _bindingDigest;
        private readonly AlsLocomotionSourceProfile _sourceSnapshot;

        public SourceState(AlsLocomotionSourceProfile sources)
        {
            _sourceSnapshot = sources;
            _bindingDigest = sources.Digest;
            _playerHistory = new AlsAssetPlayerHistory[16];
            _sampleHistory = new AlsAssetSampleHistory[16];
            _players = sources.RuntimePlayers.Where(p => p.Domain == AlsLocomotionSourceDomain.Detail).OrderBy(p => p.DetailSlot).ToArray();
            var samples = sources.RuntimeSamples;
            _samples = _players.Select(p => samples[p.SampleStart]).ToArray();
            _slotByPlayer = Enumerable.Repeat(-1, sources.RuntimePlayers.Length).ToArray();
            _sequences = sources.SyncSequences; _markers = sources.SyncMarkers;
            var syncBindings = sources.RuntimeSyncPlayers;
            _syncBindings = _players.Select(p => syncBindings[p.PlayerId]).ToArray();
            _groupIds = _players.Select(p => p.SyncGroupId).Distinct().Order().ToArray();
            _groups = new AlsAssetSyncBatchGroupHistory[_groupIds.Length];
            Require(_players.Length == 16 && _groupIds.Length == 3, "Incomplete Detail source identities.");
            for (var slot = 0; slot < _players.Length; slot++)
            {
                var player = _players[slot];
                Require(player.DetailSlot == slot && player.SampleCount == 1 && player.Kind == AlsLocomotionSourceKind.Sequence &&
                    !player.Loop && player.SyncGroupId >= 0 && player.PlayRateInput == AlsSourceRateInput.Constant, "Unsupported Detail source binding.");
                Require(_syncBindings[slot].PlayerId == player.PlayerId && _syncBindings[slot].MarkerMask == 0 &&
                    _sequences[_samples[slot].SequenceIndex].AnimationId == _samples[slot].AnimationId, "Invalid compiled Detail Sync metadata.");
                _slotByPlayer[player.PlayerId] = slot;
            }
        }

        public void CopyFrom(SourceState source)
        {
            Require(_bindingDigest == source._bindingDigest, "Cannot copy source state across different graph bindings.");
            source.Times.CopyTo(Times, 0); source.CachedWeights.CopyTo(CachedWeights, 0);
            source._epochs.CopyTo(_epochs, 0); source._groups.CopyTo(_groups, 0);
            source._playerHistory.CopyTo(_playerHistory, 0); source._sampleHistory.CopyTo(_sampleHistory, 0);
            _historyCount = source._historyCount; _hasHistory = source._hasHistory;
        }

        public bool SameAs(SourceState other) => Times.AsSpan().SequenceEqual(other.Times) &&
            CachedWeights.AsSpan().SequenceEqual(other.CachedWeights) && _epochs.AsSpan().SequenceEqual(other._epochs) &&
            _groups.AsSpan().SequenceEqual(other._groups) && _historyCount == other._historyCount && _hasHistory == other._hasHistory &&
            _playerHistory.AsSpan().SequenceEqual(other._playerHistory) && _sampleHistory.AsSpan().SequenceEqual(other._sampleHistory);

        public void Tick(in AlsDetailMachineUpdate update, NVector4 velocity, float delta, bool inheritedInertialization = false)
        {
            for (var state = 2; state < 6; state++)
            for (var source = 0; source < 4; source++)
            {
                var slot = (state - 2) * 4 + source;
                if ((update.ClearCachedWeightStates & (1 << state)) != 0) CachedWeights[slot] = 0;
                if ((update.InitializeStates & (1 << state)) == 0) continue;
                var player = _players[slot]; var sample = _samples[slot];
                Times[slot] = AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence, player.StartPosition,
                    sample.DurationSeconds, player.DefaultPlayRate * sample.SampleRateScale, player.PlayRateBasis, sample.AssetRateScale);
                _epochs[slot] = AlsAssetSourceInitialization.NextEpoch(_epochs[slot]);
            }
            var weights = AlsDetailPoseComposer.SampleWeights(velocity);
            var sourceView = _sourceSnapshot.CreateCoreView();
            Span<AlsLocomotionSourceUpdate> updates = stackalloc AlsLocomotionSourceUpdate[16];
            Span<AlsLocomotionSampleUpdate> sampleUpdates = stackalloc AlsLocomotionSampleUpdate[16];
            Span<AlsAssetSyncPlayer> ticks = stackalloc AlsAssetSyncPlayer[16];
            Span<AlsAssetSyncSample> sampleTicks = stackalloc AlsAssetSyncSample[16];
            Span<AlsAssetPlayerHistory> mapped = stackalloc AlsAssetPlayerHistory[16];
            Span<AlsAssetSampleHistory> mappedSamples = stackalloc AlsAssetSampleHistory[16];
            Span<int> playerGroups = stackalloc int[16];
            var count = 0;
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var child = update.GetUpdate(i);
                if (child.State < AlsDetailState.WalkRun) continue;
                for (var source = 0; source < 4; source++)
                {
                    var slot = ((int)child.State - 2) * 4 + source;
                    var player = _players[slot]; var sample = _samples[slot];
                    if (weights[source] == 0) continue;
                    CachedWeights[slot] = child.Weight * weights[source];
                    sampleUpdates[count] = new(sample.SampleId, 1, 1);
                    updates[count] = new(player.PlayerId, _epochs[slot], Times[slot], CachedWeights[slot], count, 1,
                        child.InertializationSync || inheritedInertialization);
                    count++;
                }
            }
            Require(AlsLocomotionSourceRuntime.TryBuildTicks(sourceView, sourceView.Stamp, updates[..count], sampleUpdates[..count], 1,
                ticks, sampleTicks, playerGroups, out _), "Detail source contribution binding failed.");
            Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch(_groupIds, playerGroups[..count], ticks[..count], sampleTicks[..count],
                sourceView.Sequences, sourceView.Markers, _hasHistory ? _groups : [], _playerHistory.AsSpan(0, _historyCount),
                _sampleHistory.AsSpan(0, _historyCount), delta, _groups, mapped, mappedSamples, out _), "Detail asset Sync batch failed.");
            for (var i = 0; i < count; i++) Times[_slotByPlayer[mapped[i].PlayerId]] = mapped[i].Time;
            Array.Clear(_playerHistory); Array.Clear(_sampleHistory);
            mapped[..count].CopyTo(_playerHistory); mappedSamples[..count].CopyTo(_sampleHistory);
            _historyCount = count; _hasHistory = true;
        }
    }
    private static ReadOnlySpan<float> Times(AlsDetailState state, float[] times) => state < AlsDetailState.WalkRun ? [] : times.AsSpan(((int)state - 2) * 4, 4);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
