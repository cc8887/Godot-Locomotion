using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class StandingInitializationSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            foreach (var hz in new[] { 30, 60, 120 })
            {
                using var replay = new Replay(this, set, hz);
                replay.Run();
            }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private sealed class Replay : IDisposable, IAlsStandingCachedGraphSink
    {
        private readonly int _hz, _left, _right;
        private readonly AlsAnimationLibraryBuildResult _library;
        private readonly AlsLocomotionGraphBuildResult _graph;
        private readonly AlsStandingCycleGraph _standing;
        private readonly AlsStandingCachedGraph _runtime;
        private readonly AlsStandingMovementSettings _settings;
        private readonly AlsGroundedAutomaticTime[] _automatic = new AlsGroundedAutomaticTime[5];
        private readonly AlsPoseCacheCall[] _entries = new AlsPoseCacheCall[1];
        private readonly AlsLocomotionSourceUpdate[] _updates = new AlsLocomotionSourceUpdate[AlsCycleSyncFrame.PlayerCapacity];
        private readonly AlsLocomotionSampleUpdate[] _samples = new AlsLocomotionSampleUpdate[AlsCycleSyncFrame.SampleCapacity];
        private readonly bool[] _active = new bool[AlsCycleSyncFrame.PlayerCapacity];
        private AlsStandingCachedGraphUpdate _update;
        private AlsStandingCycleFrame _previous, _prepared;
        private AlsCycleSyncFrame _candidate, _expected;
        private AlsFrameResult _result;

        public Replay(Node parent, AlsAnimationSetDefinition set, int hz)
        {
            _hz = hz;
            var profile = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
            var turns = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, profile);
            _settings = AlsLocomotionInputCompiler.Compile(Read("v4_locomotion_inputs.json")).Movement;
            _library = AlsAnimationLibraryBuilder.Build(set, profile, turns); parent.AddChild(_library.Root);
            _graph = AlsLocomotionGraphBuilder.Build(_library, profile, turns, set); _standing = _graph.StandingCycle!;
            _runtime = new(_standing.CacheDefinition);
            var bindings = _standing.SourceBindings.Sources;
            foreach (var player in bindings.Players)
            {
                if (player.Domain != AlsLocomotionSourceDomain.Standing) continue;
                if (player.LoopInput == AlsSourceLoopInput.RotateLeft) _left = player.PlayerId;
                if (player.LoopInput == AlsSourceLoopInput.RotateRight) _right = player.PlayerId;
            }
            Require(_left != _right, "Standing rotation identities are missing.");
        }

        public void Run()
        {
            var reentries = 0;
            var rejected = 0;
            long allocated = 0;
            for (var frame = 1; frame <= _hz; frame++)
            {
                _result = AlsFrameResult.CreateDefault(new(frame, 19, 2));
                _result.ActualGait = AlsGait.Walking;
                if (frame == 1)
                {
                    Prepare(dualRotate: false);
                    foreach (var player in _standing.SourceBindings.Sources.Players)
                        if (player.Domain is AlsLocomotionSourceDomain.Standing or AlsLocomotionSourceDomain.Detail)
                            Require(_candidate.Epochs[player.PlayerId] == 0, "Idle initialized an unvisited Standing/Detail source.");
                }
                Prepare();
                if (frame == 1)
                {
                    Require(_update.Standing.InitializationCount == 4 && _update.Standing.GetInitialization(0) == 0 &&
                        _update.Standing.GetInitialization(1) == 3 && _update.Standing.GetInitialization(2) == 4 &&
                        _update.Standing.GetInitialization(3) == 3, "Native Standing first-entry traversal changed.");
                    Require(_candidate.Epochs[_left] == 2 && _candidate.Epochs[_right] == 1,
                        "Standing source epochs must count only actual entries: expected left=2/right=1.");
                }
                var left = frame == 1 ? 0L : _previous.Sync.Epochs[_left];
                var right = frame == 1 ? 0L : _previous.Sync.Epochs[_right];
                for (var i = 0; i < _update.Standing.InitializationCount; i++)
                {
                    var state = _update.Standing.GetInitialization(i);
                    if (state == 3) left++;
                    if (state == 4) right++;
                }
                Require(_candidate.Epochs[_left] == left && _candidate.Epochs[_right] == right,
                    "A repeated state entry failed to advance its independent source epoch.");
                if (_update.Standing.InitializationCount > 1) reentries++;
                _expected = _candidate;
                Prepare();
                Require(StandingCycleSmoke.SameSync(_expected, _candidate), "Retry consumed source initialization twice.");
                if (frame == _hz / 2)
                {
                    for (var i = 0; i < 32; i++) Prepare();
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var i = 0; i < 120; i++) Prepare();
                    allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                    Require(StandingCycleSmoke.SameSync(_expected, _candidate), "Initialization replay changed source histories.");
                }
                if (frame == _hz)
                {
                    var refused = false;
                    try { Prepare(overflow: true); }
                    catch (ArgumentOutOfRangeException) { refused = true; }
                    Require(refused, "Repeated source initialization silently saturated its epoch.");
                    rejected++;
                    Prepare();
                    Require(StandingCycleSmoke.SameSync(_expected, _candidate), "Failed initialization leaked into committed source history.");
                }
                _standing.CompleteSources(ref _prepared, _previous, _candidate);
                _previous = _prepared;
            }
            Require(reentries > 0 && allocated == 0 && rejected == 1, "Standing initialization coverage/allocation gate failed.");
            GD.Print($"STANDING_INITIALIZATION_OK hz={_hz} frames={_hz} repeated_entry_frames={reentries} first_order=0,3,4,3 first_epochs=2,1 unvisited_idle_sources=0 allocated={allocated} overflow_rejected={rejected} sources=actual rules=dual_rotate_fixture retry=identical");
        }

        private void Prepare(bool overflow = false, bool dualRotate = true)
        {
            // Both Rotate conditions deliberately exercise the native graph's same-frame reentry.
            // This is an adversarial source-lifecycle fixture, not a claim about normal keyboard input.
            var rotation = new AlsSourceRotationInput(1, dualRotate, dualRotate);
            var rules = new AlsGroundedRuleInput(false, dualRotate, dualRotate, AlsStance.Standing, true, false, 0, 0);
            _entries[0] = new(_standing.CacheDefinition.EntryReadIndex, new(_result.Identity, 1, 1f / _hz));
            _update = _runtime.Prepare(_previous.Detail.Standing.Update.State, _result.Identity, rules,
                new(AlsGait.Walking, 1, false, 1, 1), _automatic, _entries, this);
            var movement = AlsStandingMovementInputModel.Evaluate(_result.Identity, default, 0, _settings);
            _prepared = _standing.ConsumeSharedUpdate(_previous, _result, 1f / _hz, new(1.75f, 3.75f, 6.5f), movement, _update, rotation);
            _candidate = _previous.Sync;
            if (overflow)
            {
                var repeated = _update.Standing.GetInitialization(_update.Standing.InitializationCount - 1);
                _candidate.Epochs[repeated == 3 ? _left : _right] = long.MaxValue - 1;
            }
            var players = 0; var samples = 0;
            _standing.CollectSources(_prepared, ref _candidate, _updates, _samples, _active, ref players, ref samples);
            _candidate = AlsSharedSourceBatch.Evaluate(_standing.SourceBindings, _candidate, _updates.AsSpan(0, players),
                _samples.AsSpan(0, samples), _active.AsSpan(0, players), _prepared.State.PlayRate, rotation, 1, 1f / _hz);
        }

        public void UpdateStandingSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) { }
        public void UpdateStopSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) { }
        public void UpdateDetailSources(in AlsDetailMachineUpdate update, in AlsPoseUpdateContext context) { }
        public void UpdateCycleSource(in AlsPoseUpdateContext context) { }
        public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { }
        public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
        public void Dispose() { _graph.Dispose(); _library.Dispose(); }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
