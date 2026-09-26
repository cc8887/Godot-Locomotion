using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Movement Details pose before the outer inertialization node. The
/// shared Movement cache must already be evaluated for this frame. No clocks,
/// cache-update traversal or inertialization histories are advanced here.</summary>
public sealed class AlsRefactoredMovementDetailsPose
{
    public AlsRefactoredMovementDetailsPoseGraph Graph { get; }
    public AlsRefactoredMovementCacheProfile Movement { get; }
    private readonly string[] _bones, _curves, _movementCurves;
    private readonly int[] _properties, _locals, _movementMap;
    private readonly string[][] _playerCurves;
    private readonly int[][] _maps;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<string> CurveNames => _curves;
    public AlsRefactoredMovementDetailsPose(AlsRefactoredAnimationCatalog catalog,
        AlsRefactoredMovementDetailsPoseGraph graph, AlsRefactoredMovementCacheProfile movement, ReadOnlySpan<string> movementCurves)
    {
        if (catalog.IndexDigest != graph.Resources.CatalogDigest || catalog.IndexDigest != movement.CatalogDigest)
            throw new ArgumentException("Foreign Movement Details pose catalog.");
        Graph = graph; Movement = movement; _bones = movement.Lean.BoneNames.ToArray(); _movementCurves = movementCurves.ToArray();
        if (_movementCurves.Any(string.IsNullOrEmpty) || _movementCurves.Distinct(StringComparer.OrdinalIgnoreCase).Count() != _movementCurves.Length ||
            !_movementCurves.Contains("PoseMoving", StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Invalid Movement cache curve layout.");
        _properties = graph.Resources.TimingPlayers.ToArray().Select(p => p.PropertyIndex).ToArray();
        _locals = _properties.Select(p => Array.FindIndex(graph.Players.Players.ToArray(), n => n.PropertyIndex == p)).ToArray();
        _playerCurves = new string[_properties.Length][];
        for (var i = 0; i < _properties.Length; i++)
        {
            var additive = catalog.CompileAdditivePose(graph.Resources.TimingPlayers[i].Source);
            if (!additive.BoneNames.SequenceEqual(_bones) || !additive.Parents.SequenceEqual(movement.Lean.Parents))
                throw new ArgumentException("Foreign Movement Details additive skeleton.");
            _playerCurves[i] = additive.CurveNames.ToArray();
        }
        _curves = _movementCurves.Concat(_playerCurves.SelectMany(c => c)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _movementMap = _movementCurves.Select(Index).ToArray(); _maps = _playerCurves.Select(c => c.Select(Index).ToArray()).ToArray();
        int Index(string name) => Array.FindIndex(_curves, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    public Sampler CreateSampler() => new(this);
    public sealed class Sampler
    {
        private readonly AlsRefactoredMovementDetailsPose _profile;
        private readonly AlsPrecisePose[] _base, _result;
        private readonly AlsInertialCurve[] _baseCurves, _resultCurves;
        private readonly AlsPrecisePose[][] _poses;
        private readonly AlsInertialCurve[][] _curves;
        private readonly bool[] _evaluated = new bool[6];
        private readonly Dictionary<int, int> _slots;
        private int _busy;
        public int StateEvaluations { get; private set; }
        public int PlayerEvaluations { get; private set; }
        internal Sampler(AlsRefactoredMovementDetailsPose profile)
        {
            _profile = profile; _base = new AlsPrecisePose[profile._bones.Length]; _result = new AlsPrecisePose[_base.Length];
            _baseCurves = new AlsInertialCurve[profile._curves.Length]; _resultCurves = new AlsInertialCurve[_baseCurves.Length];
            _poses = Enumerable.Range(0, 6).Select(_ => new AlsPrecisePose[_base.Length]).ToArray();
            _curves = Enumerable.Range(0, 6).Select(_ => new AlsInertialCurve[_baseCurves.Length]).ToArray();
            _slots = profile._properties.Select((p, i) => (p, i)).ToDictionary(v => v.p, v => v.i);
        }
        public void Sample(long frame, AlsRefactoredMovementDetailsRuntime machine, AlsRefactoredMovementDetailsSourceRuntime update,
            IAlsRefactoredSourcePlayers players, AlsRefactoredMovementCacheRuntime movement,
            Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
        {
            if (!ReferenceEquals(update.Profile, _profile.Graph) || !ReferenceEquals(movement.Profile, _profile.Movement) ||
                !movement.BoneNames.SequenceEqual(_profile._bones) || !movement.CurveNames.SequenceEqual(_profile._movementCurves) ||
                pose.Length != _result.Length || curves.Length != _resultCurves.Length)
                throw new ArgumentException("Invalid Movement Details pose owner/layout.");
            update.ValidateEvaluation(frame, machine, players); movement.ValidateCommit(frame);
            for (var i = 0; i < _profile._locals.Length; i++)
            {
                var id = update.FirstPlayer + _profile._locals[i];
                if (players.Source(id) != _profile.Graph.Resources.TimingPlayers[i].Source ||
                    !players.BoneNames(id).SequenceEqual(_profile._bones) || !players.CurveNames(id).SequenceEqual(_profile._playerCurves[i]))
                    throw new ArgumentException("Foreign Movement Details source layout.");
            }
            if (Interlocked.Exchange(ref _busy, 1) != 0) throw new InvalidOperationException("Reentrant Movement Details sampling.");
            try
            {
                movement.Pose.CopyTo(_base); Array.Clear(_baseCurves);
                for (var c = 0; c < _profile._movementMap.Length; c++) _baseCurves[_profile._movementMap[c]] = movement.Curves[c];
                Array.Clear(_evaluated); StateEvaluations = PlayerEvaluations = 0;
                var state = machine.Candidate.State; var stack = state.Transitions;
                var first = stack.Count == 0 ? state.CurrentState : stack.GetTransition(0).From;
                State(first); _poses[first].CopyTo(_result, 0); _curves[first].CopyTo(_resultCurves, 0);
                for (var i = 0; i < stack.Count; i++)
                {
                    // Original inertial entries have zero duration and are
                    // removed during Update. Only standard blends remain here.
                    if (_profile.Graph.Resources.Edges[state.GetActiveEdge(i)].Inertialization)
                        throw new InvalidOperationException("Unexpected live inertial transition in Details stack.");
                    var edge = stack.GetTransition(i); State(edge.To);
                    for (var b = 0; b < _result.Length; b++) _result[b] = AlsPrecisePoseBlender.BlendRaw(_result[b], _poses[edge.To][b], edge.Alpha);
                    for (var c = 0; c < _resultCurves.Length; c++) _resultCurves[c] = AlsStandingCycleCurves.Accumulate(
                        AlsStandingCycleCurves.Scale(_resultCurves[c], 1 - edge.Alpha), _curves[edge.To][c], edge.Alpha);
                }
                if (stack.Count > 0) for (var b = 0; b < _result.Length; b++) _result[b] = _result[b].Normalized();
                _result.CopyTo(pose); _resultCurves.CopyTo(curves);
            }
            finally { Volatile.Write(ref _busy, 0); }

            void State(int index)
            {
                if (_evaluated[index]) return;
                var state = _profile.Graph.States[index]; var output = _poses[index]; var outputCurves = _curves[index];
                var initialized = false; Array.Clear(outputCurves);
                for (var channel = 0; channel < state.PlayerPropertyIndices.Length; channel++)
                {
                    var weight = update.DirectionWeights[channel]; if (weight <= AlsPoseBlender.WeightThreshold) continue;
                    var slot = _slots[state.PlayerPropertyIndices[channel]]; var id = update.FirstPlayer + _profile._locals[slot];
                    players.Evaluate(frame, id); var p = players.Pose(id); var c = players.Curves(id);
                    for (var b = 0; b < output.Length; b++) output[b] = initialized
                        ? AlsPrecisePoseBlender.Accumulate(output[b], p[b], weight) : AlsPrecisePoseBlender.Scale(p[b], weight);
                    for (var curve = 0; curve < c.Length; curve++)
                    {
                        var target = _profile._maps[slot][curve];
                        outputCurves[target] = AlsStandingCycleCurves.Accumulate(outputCurves[target], c[curve], weight);
                    }
                    initialized = true; PlayerEvaluations++;
                }
                if (initialized)
                {
                    for (var b = 0; b < output.Length; b++) output[b] = AlsPrecisePoseBlender.LocalApply(_base[b], output[b].Normalized(), 1);
                    for (var c = 0; c < outputCurves.Length; c++) outputCurves[c] = AlsStandingCycleCurves.Accumulate(_baseCurves[c], outputCurves[c], 1);
                }
                else
                {
                    // The MultiWay child inherits bExpectsAdditivePose=true;
                    // ResetToRefPose therefore means additive identity here.
                    _base.CopyTo(output, 0); _baseCurves.CopyTo(outputCurves, 0);
                }
                _evaluated[index] = true; StateEvaluations++;
            }
        }
    }
}
