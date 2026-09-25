using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original 79-bone centimeter poses. One sampling call is one cache
/// evaluation scope; clocks and update history remain in the caller's candidates.</summary>
public sealed class AlsRefactoredDirectionPose
{
    public AlsRefactoredDirectionSourceProfile Source { get; }
    private readonly string[] _bones, _curves;
    private readonly AlsPrecisePose[] _reference;
    private readonly int[] _used;
    private readonly string[][] _playerCurves;
    private readonly int[][] _maps;
    private readonly int _yaw;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<string> CurveNames => _curves;
    public AlsRefactoredDirectionPose(AlsRefactoredAnimationCatalog catalog, AlsRefactoredDirectionSourceProfile source,
        IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> triangles)
    {
        if (catalog.IndexDigest != source.Graph.Resources.CatalogDigest) throw new ArgumentException("Foreign direction pose catalog.");
        Source = source;
        var reference = catalog.CompileAbsolutePoseWithCurves("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose");
        _bones = reference.Pose.BoneNames.ToArray(); _reference = reference.Pose.ReferencePose.ToArray();
        if (!_bones.AsSpan().SequenceEqual(source.Graph.Resources.BlendProfile.BoneNames)) throw new ArgumentException("Foreign direction transition skeleton.");
        _used = source.CachePlayers.Values.Concat(source.Forward is { } f ? new[] { f.SprintPlayer, f.AccelerationPlayer } : []).Distinct().Order().ToArray();
        _playerCurves = new string[_used.Length][];
        for (var i = 0; i < _used.Length; i++)
        {
            var player = source.Players.Players[_used[i]];
            string[] bones; int[] parents;
            if (player.BlendSpace)
            {
                var blend = new AlsRefactoredBlendPoseSource(triangles[player.Source], catalog);
                if (blend.IsAdditive) throw new ArgumentException("Direction cache requires absolute poses.");
                bones = blend.BoneNames.ToArray(); parents = blend.Parents.ToArray(); _playerCurves[i] = blend.CurveNames.ToArray();
            }
            else
            {
                var clip = catalog.CompileAbsolutePoseWithCurves(player.Source);
                bones = clip.Pose.BoneNames.ToArray(); parents = clip.Pose.Parents.ToArray(); _playerCurves[i] = clip.Curves.Names.ToArray();
            }
            if (!bones.AsSpan().SequenceEqual(_bones) || !parents.AsSpan().SequenceEqual(reference.Pose.Parents)) throw new ArgumentException("Direction source skeleton differs.");
        }
        _curves = _playerCurves.SelectMany(c => c).Append("RotationYawOffset").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        _maps = _playerCurves.Select(c => c.Select(n => Array.FindIndex(_curves, v => v.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray()).ToArray();
        _yaw = Array.FindIndex(_curves, c => c.Equals("RotationYawOffset", StringComparison.OrdinalIgnoreCase));
    }
    public Sampler CreateSampler() => new(this);
    public sealed class Sampler
    {
        private readonly AlsRefactoredDirectionPose _profile;
        private readonly Dictionary<int, int> _cacheSlots, _playerSlots;
        private readonly AlsPrecisePose[][] _cachePoses, _playerPoses, _statePoses;
        private readonly AlsInertialCurve[][] _cacheCurves, _playerCurves, _stateCurves;
        private readonly bool[] _cached, _played, _stated;
        private readonly AlsPrecisePose[] _sprint, _gait, _result;
        private readonly AlsInertialCurve[] _sprintCurves, _gaitCurves, _resultCurves;
        private readonly AlsOverlayActionMix _gaitMix = new();
        private int _busy;
        public int CacheEvaluations { get; private set; }
        public int PlayerEvaluations { get; private set; }
        internal Sampler(AlsRefactoredDirectionPose profile)
        {
            _profile = profile;
            _cacheSlots = profile.Source.Caches.UpdateOrder.ToArray().Select((id, i) => (id, i)).ToDictionary(v => v.id, v => v.i);
            _playerSlots = profile._used.Select((id, i) => (id, i)).ToDictionary(v => v.id, v => v.i);
            AlsPrecisePose[][] Poses(int count) => Enumerable.Range(0, count).Select(_ => new AlsPrecisePose[profile._bones.Length]).ToArray();
            AlsInertialCurve[][] Curves(int count) => Enumerable.Range(0, count).Select(_ => new AlsInertialCurve[profile._curves.Length]).ToArray();
            _cachePoses = Poses(_cacheSlots.Count); _cacheCurves = Curves(_cacheSlots.Count); _cached = new bool[_cacheSlots.Count];
            _playerPoses = Poses(_playerSlots.Count); _playerCurves = Curves(_playerSlots.Count); _played = new bool[_playerSlots.Count];
            _statePoses = Poses(6); _stateCurves = Curves(6); _stated = new bool[6];
            _sprint = new AlsPrecisePose[profile._bones.Length]; _gait = new AlsPrecisePose[_sprint.Length]; _result = new AlsPrecisePose[_sprint.Length];
            _sprintCurves = new AlsInertialCurve[profile._curves.Length]; _gaitCurves = new AlsInertialCurve[_sprintCurves.Length]; _resultCurves = new AlsInertialCurve[_sprintCurves.Length];
        }
        public void Sample(long frame, AlsRefactoredDirectionRuntime machine, AlsRefactoredDirectionSourceRuntime update,
            AlsRefactoredSourcePlayerRuntime players, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
        {
            if (!ReferenceEquals(update.Profile, _profile.Source) || players.CatalogDigest != _profile.Source.Graph.Resources.CatalogDigest ||
                pose.Length != _result.Length || curves.Length != _resultCurves.Length) throw new ArgumentException("Invalid direction pose owner/layout.");
            update.ValidateMachine(frame, machine); players.ValidateCommit(frame);
            for (var i = 0; i < _profile._used.Length; i++)
            {
                var id = update.FirstPlayer + _profile._used[i];
                if (players.Source(id) != _profile.Source.Players.Players[_profile._used[i]].Source || !players.BoneNames(id).SequenceEqual(_profile._bones) ||
                    !players.CurveNames(id).SequenceEqual(_profile._playerCurves[i])) throw new ArgumentException("Foreign direction source player.");
            }
            if (Interlocked.Exchange(ref _busy, 1) != 0) throw new InvalidOperationException("Reentrant direction sampler.");
            try
            {
                Array.Clear(_cached); Array.Clear(_played); Array.Clear(_stated); CacheEvaluations = PlayerEvaluations = 0;
                var state = machine.Candidate.State; var stack = state.Transitions;
                var first = stack.Count == 0 ? state.CurrentState : stack.GetTransition(0).From;
                State(first); _statePoses[first].CopyTo(_result, 0); _stateCurves[first].CopyTo(_resultCurves, 0);
                for (var i = 0; i < stack.Count; i++)
                {
                    var edge = stack.GetTransition(i); State(edge.To);
                    for (var b = 0; b < _result.Length; b++)
                    {
                        var weights = _profile.Source.Graph.Resources.BlendProfile.Weights(b, edge.Alpha);
                        _result[b] = AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(_result[b], weights.Y), _statePoses[edge.To][b], weights.X);
                    }
                    for (var c = 0; c < _resultCurves.Length; c++) _resultCurves[c] = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(_resultCurves[c], 1 - edge.Alpha), _stateCurves[edge.To][c], edge.Alpha);
                }
                if (stack.Count > 0) for (var b = 0; b < _result.Length; b++) _result[b] = _result[b].Normalized();
                _result.CopyTo(pose); _resultCurves.CopyTo(curves);
            }
            finally { Volatile.Write(ref _busy, 0); }

            int Player(int local)
            {
                var slot = _playerSlots[local]; if (_played[slot]) return slot;
                var id = update.FirstPlayer + local; players.Evaluate(frame, id); players.Pose(id).CopyTo(_playerPoses[slot]);
                Array.Clear(_playerCurves[slot]); var input = players.Curves(id);
                for (var c = 0; c < input.Length; c++) _playerCurves[slot][_profile._maps[slot][c]] = input[c];
                _played[slot] = true; PlayerEvaluations++; return slot;
            }
            int Cache(int id)
            {
                var slot = _cacheSlots[id]; if (_cached[slot]) return slot;
                var updated = false; foreach (var u in update.CacheUpdates) if (u.PropertyIndex == id) updated = true;
                if (!updated) throw new InvalidOperationException("Direction cache was not updated.");
                var output = _cachePoses[slot]; var outputCurves = _cacheCurves[slot];
                if (_profile.Source.Forward is not { } f || f.Cache != id)
                {
                    var player = Player(_profile.Source.CachePlayers[id]); _playerPoses[player].CopyTo(output, 0); _playerCurves[player].CopyTo(outputCurves, 0);
                }
                else
                {
                    var w = update.ForwardWeights; var baseSlot = -1;
                    if (w.Block > AlsPoseBlender.WeightThreshold || w.GaitUpdated && w.Gait.X > AlsPoseBlender.WeightThreshold) baseSlot = Cache(f.BaseCache);
                    if (w.GaitUpdated)
                    {
                        if (w.Gait.Y > AlsPoseBlender.WeightThreshold)
                        {
                            var a = w.Acceleration < 1 - AlsPoseBlender.WeightThreshold ? Player(f.SprintPlayer) : -1;
                            var b = w.Acceleration > AlsPoseBlender.WeightThreshold ? Player(f.AccelerationPlayer) : -1;
                            MixTwo(a < 0 ? _playerPoses[b] : _playerPoses[a], b < 0 ? _playerPoses[a] : _playerPoses[b],
                                a < 0 ? _playerCurves[b] : _playerCurves[a], b < 0 ? _playerCurves[a] : _playerCurves[b], w.Acceleration, _sprint, _sprintCurves);
                        }
                        _gaitMix.Prepare(w.Gait);
                        Span<AlsPrecisePose> poses = stackalloc AlsPrecisePose[4]; Span<AlsInertialCurve> values = stackalloc AlsInertialCurve[4];
                        for (var b = 0; b < _gait.Length; b++) { poses[0] = baseSlot >= 0 ? _cachePoses[baseSlot][b] : default; poses[1] = _sprint[b]; _gait[b] = _gaitMix.Pose(poses); }
                        for (var c = 0; c < _gaitCurves.Length; c++) { values[0] = baseSlot >= 0 ? _cacheCurves[baseSlot][c] : default; values[1] = _sprintCurves[c]; _gaitCurves[c] = _gaitMix.Curve(values); }
                    }
                    if (!w.GaitUpdated) { _cachePoses[baseSlot].CopyTo(output, 0); _cacheCurves[baseSlot].CopyTo(outputCurves, 0); }
                    else if (w.Block <= AlsPoseBlender.WeightThreshold) { _gait.CopyTo(output, 0); _gaitCurves.CopyTo(outputCurves, 0); }
                    else MixTwo(_gait, _cachePoses[baseSlot], _gaitCurves, _cacheCurves[baseSlot], w.Block, output, outputCurves);
                }
                _cached[slot] = true; CacheEvaluations++; return slot;
            }
            void State(int state)
            {
                if (_stated[state]) return;
                var output = _statePoses[state]; var outputCurves = _stateCurves[state]; Array.Clear(outputCurves);
                var initialized = false; var weights = update.DirectionWeights;
                for (var channel = 0; channel < 4; channel++)
                {
                    var weight = weights[channel]; if (weight <= AlsPoseBlender.WeightThreshold) continue;
                    var cache = Cache(_profile.Source.Graph.States[state].CachePropertyIndices[channel]);
                    for (var b = 0; b < output.Length; b++) output[b] = initialized ? AlsPrecisePoseBlender.Accumulate(output[b], _cachePoses[cache][b], weight) : AlsPrecisePoseBlender.Scale(_cachePoses[cache][b], weight);
                    for (var c = 0; c < outputCurves.Length; c++) outputCurves[c] = initialized ? AlsStandingCycleCurves.Accumulate(outputCurves[c], _cacheCurves[cache][c], weight) : AlsStandingCycleCurves.Scale(_cacheCurves[cache][c], weight);
                    initialized = true;
                }
                if (!initialized) _profile._reference.CopyTo(output, 0); else for (var b = 0; b < output.Length; b++) output[b] = output[b].Normalized();
                var channelYaw = state < 2 ? state : state < 4 ? 3 : 2;
                outputCurves[_profile._yaw] = AlsStandingCycleCurves.ModifyBlend(outputCurves[_profile._yaw], update.RotationYawOffsets[channelYaw], 1);
                _stated[state] = true;
            }
        }
        private static void MixTwo(AlsPrecisePose[] a, AlsPrecisePose[] b, AlsInertialCurve[] ac, AlsInertialCurve[] bc,
            float alpha, AlsPrecisePose[] output, AlsInertialCurve[] curves)
        {
            for (var i = 0; i < output.Length; i++) output[i] = AlsRefactoredDefaultOverlay.Two(a[i], b[i], alpha);
            for (var i = 0; i < curves.Length; i++) curves[i] = AlsRefactoredDefaultOverlay.TwoCurve(ac[i], bc[i], alpha);
        }
    }
}
