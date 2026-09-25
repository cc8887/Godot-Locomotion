using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Evaluates one updated original weapon state. The caller owns machine
/// transitions and the shared source-player transaction; this sampler owns neither.</summary>
public sealed class AlsRefactoredWeaponStatePose
{
    private readonly AlsRefactoredWeaponEvaluators _evaluators;
    private readonly string[] _curves;
    private readonly int[] _evaluatorMap;
    private readonly string[][] _playerCurves;
    private readonly int[][] _playerMap;
    private readonly AlsPrecisePose[] _reference;
    public ReadOnlySpan<string> BoneNames => _evaluators.BoneNames;
    public ReadOnlySpan<string> CurveNames => _curves;
    public AlsRefactoredWeaponSourceProfile SourceProfile => _evaluators.SourceProfile;
    public AlsRefactoredWeaponStatePose(AlsRefactoredAnimationCatalog catalog, AlsRefactoredWeaponSourceProfile profile)
    {
        _evaluators = new(catalog, profile);
        var stand = catalog.CompileAbsolutePoseWithCurves("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose");
        if (!stand.Pose.BoneNames.SequenceEqual(BoneNames) || !stand.Pose.Parents.SequenceEqual(_evaluators.Parents)) throw new ArgumentException("Foreign weapon reference skeleton.");
        _reference = stand.Pose.ReferencePose.ToArray();
        _playerCurves = new string[profile.Players.Players.Length][];
        for (var i = 0; i < _playerCurves.Length; i++)
        {
            var path = profile.Players.Players[i].Source;
            if (path == AlsRefactoredDefaultOverlayProfile.IdleSource)
            {
                var source = catalog.CompileAdditivePose(path);
                if (!source.BoneNames.SequenceEqual(BoneNames) || !source.Parents.SequenceEqual(_evaluators.Parents)) throw new ArgumentException("Foreign weapon player skeleton.");
                _playerCurves[i] = source.CurveNames.ToArray();
            }
            else
            {
                var source = catalog.CompileAbsolutePoseWithCurves(path);
                if (!source.Pose.BoneNames.SequenceEqual(BoneNames) || !source.Pose.Parents.SequenceEqual(_evaluators.Parents)) throw new ArgumentException("Foreign weapon player skeleton.");
                _playerCurves[i] = source.Curves.Names.ToArray();
            }
        }
        _curves = _evaluators.CurveNames.ToArray().Concat(_playerCurves.SelectMany(n => n)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        int[] Map(string[] names) => names.Select(n => Array.FindIndex(_curves, c => c.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
        _evaluatorMap = Map(_evaluators.CurveNames.ToArray()); _playerMap = _playerCurves.Select(Map).ToArray();
    }
    public Sampler CreateSampler() => new(this);
    public sealed class Sampler
    {
        private readonly AlsRefactoredWeaponStatePose _profile;
        private readonly AlsRefactoredWeaponEvaluators.Sampler _evaluator;
        private readonly AlsPrecisePose[][] _poses;
        private readonly AlsInertialCurve[][] _curves;
        private readonly AlsInertialCurve[] _evaluatorCurves;
        private readonly AlsQuaternion[] _mesh;
        private int _busy;
        internal Sampler(AlsRefactoredWeaponStatePose profile)
        {
            _profile = profile; _evaluator = profile._evaluators.CreateSampler();
            var count = profile.SourceProfile.Nodes.Length;
            _poses = Enumerable.Range(0, count).Select(_ => new AlsPrecisePose[profile.BoneNames.Length]).ToArray();
            _curves = Enumerable.Range(0, count).Select(_ => new AlsInertialCurve[profile.CurveNames.Length]).ToArray();
            _evaluatorCurves = new AlsInertialCurve[profile._evaluatorMap.Length]; _mesh = new AlsQuaternion[profile.BoneNames.Length * 2];
        }
        public void Sample(long frame, int state, float pitch, AlsRefactoredWeaponSourceRuntime update,
            AlsRefactoredSourcePlayerRuntime players, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
        {
            if (!ReferenceEquals(update.Profile, _profile.SourceProfile) || state is < 0 or > 2 ||
                !float.IsFinite(pitch) || pitch is < 0 or > 1 || pose.Length != _profile.BoneNames.Length || curves.Length != _profile.CurveNames.Length ||
                players.CatalogDigest != _profile.SourceProfile.Players.CatalogDigest) throw new ArgumentException("Invalid weapon state sample.");
            update.ValidateCommit(frame); players.ValidateCommit(frame);
            var root = _profile.SourceProfile.Roots[state]; _ = update.PoseWeights(frame, root);
            for (var i = 0; i < _profile._playerCurves.Length; i++)
                if (players.Source(update.FirstPlayer + i) != _profile.SourceProfile.Players.Players[i].Source ||
                    !players.BoneNames(update.FirstPlayer + i).SequenceEqual(_profile.BoneNames) ||
                    !players.CurveNames(update.FirstPlayer + i).SequenceEqual(_profile._playerCurves[i])) throw new ArgumentException("Foreign weapon state player.");
            if (Interlocked.Exchange(ref _busy, 1) != 0) throw new InvalidOperationException("Reentrant weapon state sampler.");
            try { Evaluate(root); _poses[root].CopyTo(pose); _curves[root].CopyTo(curves); }
            finally { Volatile.Write(ref _busy, 0); }

            void Evaluate(int id)
            {
                var weights = update.PoseWeights(frame, id); var node = _profile.SourceProfile.Nodes[id]!;
                var output = _poses[id]; var outputCurves = _curves[id]; Array.Clear(outputCurves);
                if (node.Player >= 0)
                {
                    var player = update.FirstPlayer + node.Player; players.Evaluate(frame, player); players.Pose(player).CopyTo(output);
                    var map = _profile._playerMap[node.Player]; var input = players.Curves(player);
                    for (var c = 0; c < map.Length; c++) outputCurves[map[c]] = input[c]; return;
                }
                if (node.Kind == "AnimGraphNode_SequenceEvaluator")
                {
                    _evaluator.Sample(id, pitch, output, _evaluatorCurves);
                    for (var c = 0; c < _evaluatorCurves.Length; c++) outputCurves[_profile._evaluatorMap[c]] = _evaluatorCurves[c]; return;
                }
                if (node.Kind == "AnimGraphNode_StateResult") { Evaluate(node.Children[0]); Copy(node.Children[0]); return; }
                if (node.Kind == "AnimGraphNode_MultiWayBlend")
                {
                    var initialized = false;
                    for (var i = 0; i < node.Children.Length; i++)
                    {
                        var w = weights[i]; if (w <= AlsPoseBlender.WeightThreshold) continue;
                        var child = node.Children[i]; Evaluate(child);
                        for (var b = 0; b < output.Length; b++) output[b] = initialized ? AlsPrecisePoseBlender.Accumulate(output[b], _poses[child][b], w) : AlsPrecisePoseBlender.Scale(_poses[child][b], w);
                        for (var c = 0; c < outputCurves.Length; c++) outputCurves[c] = initialized ? AlsStandingCycleCurves.Accumulate(outputCurves[c], _curves[child][c], w) : AlsStandingCycleCurves.Scale(_curves[child][c], w);
                        initialized = true;
                    }
                    if (!initialized) _profile._reference.CopyTo(output, 0); else for (var b = 0; b < output.Length; b++) output[b] = output[b].Normalized();
                    return;
                }
                var a = node.Children[0]; var additive = node.Children[1]; var alpha = weights.X;
                if (node.Kind == "AnimGraphNode_TwoWayBlend")
                {
                    if (alpha <= AlsPoseBlender.WeightThreshold) { Evaluate(a); Copy(a); return; }
                    if (alpha >= 1 - AlsPoseBlender.WeightThreshold) { Evaluate(additive); Copy(additive); return; }
                    Evaluate(a); Evaluate(additive);
                    for (var b = 0; b < output.Length; b++) output[b] = AlsRefactoredDefaultOverlay.Two(_poses[a][b], _poses[additive][b], alpha);
                    for (var c = 0; c < outputCurves.Length; c++) outputCurves[c] = AlsRefactoredDefaultOverlay.TwoCurve(_curves[a][c], _curves[additive][c], alpha);
                    return;
                }
                Evaluate(a); Copy(a); if (alpha <= AlsPoseBlender.WeightThreshold) return; Evaluate(additive);
                if (node.Kind == "AnimGraphNode_ApplyMeshSpaceAdditive") AlsPrecisePoseBlender.MeshApply(_poses[a], _poses[additive], _profile._evaluators.Parents, _mesh, output, alpha);
                else if (node.Kind == "AnimGraphNode_ApplyAdditive") for (var b = 0; b < output.Length; b++) output[b] = AlsPrecisePoseBlender.LocalApply(_poses[a][b], _poses[additive][b], alpha);
                else throw new ArgumentException("Unknown weapon pose node.");
                for (var c = 0; c < outputCurves.Length; c++) outputCurves[c] = AlsStandingCycleCurves.Accumulate(outputCurves[c], _curves[additive][c], alpha);
                void Copy(int child) { _poses[child].CopyTo(output, 0); _curves[child].CopyTo(outputCurves, 0); }
            }
        }
    }
}
