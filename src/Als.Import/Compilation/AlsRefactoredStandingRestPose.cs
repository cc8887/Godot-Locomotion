using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Idle evaluator + pre-Slot curves, post-Slot yaw, and original Rotate
/// player outputs. Slot mixing and Standing's transition stack are host stages.</summary>
public sealed class AlsRefactoredStandingRestPose
{
    public AlsRefactoredStandingRestGraph Graph { get; }
    private readonly string[] _bones, _curves;
    private readonly int[] _parents;
    private readonly AlsPrecisePose[] _idle;
    private readonly AlsInertialCurve[] _idleCurves;
    private readonly int _yaw;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<int> Parents => _parents;
    public ReadOnlySpan<string> CurveNames => _curves;

    public AlsRefactoredStandingRestPose(AlsRefactoredAnimationCatalog catalog, AlsRefactoredStandingRestGraph graph,
        AlsRefactoredSkeletonCurves metadata, IEnumerable<string> slotCurveNames)
    {
        if (catalog.IndexDigest != graph.CatalogDigest || catalog.IndexDigest != metadata.CatalogDigest)
            throw new ArgumentException("Foreign Standing rest catalog.");
        Graph = graph;
        var idle = catalog.CompileAbsolutePoseWithCurves(graph.IdleSource);
        _bones = idle.Pose.BoneNames.ToArray(); _parents = idle.Pose.Parents.ToArray();
        var rotate = graph.RotatePlayers.Players.ToArray().Select(p => catalog.CompileAbsolutePoseWithCurves(p.Source)).ToArray();
        if (rotate.Any(p => !p.Pose.BoneNames.SequenceEqual(_bones) || !p.Pose.Parents.SequenceEqual(_parents)))
            throw new ArgumentException("Standing rest skeleton differs.");
        var slot = slotCurveNames.ToArray();
        if (slot.Any(string.IsNullOrWhiteSpace) || slot.Distinct(StringComparer.OrdinalIgnoreCase).Count() != slot.Length)
            throw new ArgumentException("Invalid Slot curve layout.");
        _curves = idle.Curves.Names.ToArray().Concat(rotate.SelectMany(p => p.Curves.Names.ToArray())).Concat(slot)
            .Concat(new[] {"FootLeftLock","FootRightLock","AllowTransitions","RotationYawSpeed"})
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _yaw = Index("RotationYawSpeed");
        _idle = new AlsPrecisePose[_bones.Length]; _idleCurves = new AlsInertialCurve[_curves.Length];
        var scratch = new AlsInertialCurve[idle.Curves.Names.Length];
        idle.Pose.CreateSampler(idle.Curves).Sample(0,true,false,false,_idle,scratch);
        for (var c = 0; c < scratch.Length; c++) _idleCurves[Index(idle.Curves.Names[c])] = scratch[c];
        foreach (var name in new[] {"FootLeftLock","FootRightLock","AllowTransitions"})
        {
            var c = Index(name); _idleCurves[c] = AlsStandingCycleCurves.ModifyBlend(_idleCurves[c],1,1);
        }
    }
    private int Index(string name) => Array.FindIndex(_curves,c => c.Equals(name,StringComparison.OrdinalIgnoreCase));

    /// <summary>The output is node63, before Slot60. Do not reapply these locks after a montage.</summary>
    public void SampleIdleSource(Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
    {
        Layout(output,curves); _idle.AsSpan().CopyTo(output); _idleCurves.AsSpan().CopyTo(curves);
    }

    /// <summary>Node62 consumes the evaluated Slot60 output in this profile's curve layout.</summary>
    public void FinishIdleSlot(ReadOnlySpan<AlsPrecisePose> slotPose, ReadOnlySpan<AlsInertialCurve> slotCurves,
        float turnPlayRate, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
    {
        Layout(output,curves);
        if (slotPose.Length != _bones.Length || slotCurves.Length != _curves.Length || !float.IsFinite(turnPlayRate))
            throw new ArgumentException("Invalid Standing Idle Slot output.");
        foreach (var p in slotPose)
            if (!p.Position.IsFinite || !p.Scale.IsFinite || !double.IsFinite(p.Rotation.LengthSquared) || p.Rotation.LengthSquared < 1e-8)
                throw new ArgumentException("Invalid Standing Idle Slot pose.");
        foreach (var c in slotCurves) if (c.Present && !float.IsFinite(c.Value)) throw new ArgumentException("Invalid Standing Idle Slot curve.");
        var yaw = ScaleYaw(slotCurves[_yaw],turnPlayRate);
        slotPose.CopyTo(output); slotCurves.CopyTo(curves); curves[_yaw] = yaw;
    }

    public RotateSampler BindRotate(IAlsRefactoredSourcePlayers players, int firstPlayer) => new(this,players,firstPlayer);

    public sealed class RotateSampler
    {
        private readonly AlsRefactoredStandingRestPose _profile;
        private readonly IAlsRefactoredSourcePlayers _players;
        private readonly int _first;
        private readonly int[][] _maps;
        internal RotateSampler(AlsRefactoredStandingRestPose profile, IAlsRefactoredSourcePlayers players, int first)
        {
            _profile = profile; _players = players; _first = first;
            var bindings = profile.Graph.RotatePlayers.Bind(first);
            if (players.CatalogDigest != profile.Graph.CatalogDigest) throw new ArgumentException("Foreign Rotate player catalog.");
            _maps = new int[2][];
            for (var i = 0; i < 2; i++)
            {
                if (players.Source(first+i) != bindings[i].Source || !players.BoneNames(first+i).SequenceEqual(profile._bones))
                    throw new ArgumentException("Foreign Rotate player identity/layout.");
                _maps[i] = players.CurveNames(first+i).ToArray().Select(profile.Index).ToArray();
                if (_maps[i].Any(c => c < 0)) throw new ArgumentException("Foreign Rotate curve layout.");
            }
        }
        public void Sample(long frame, bool left, float rotatePlayRate, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
        {
            _profile.Layout(output,curves); _players.ValidateCommit(frame);
            if (!float.IsFinite(rotatePlayRate)) throw new ArgumentException("Invalid Rotate play rate.");
            var side = left ? 0 : 1; var pose = _players.Pose(_first+side); var source = _players.Curves(_first+side);
            // Use the evaluated player's curve, not its accumulated clock or Loop flag.
            var yaw = default(AlsInertialCurve);
            for (var c = 0; c < source.Length; c++) if (_maps[side][c] == _profile._yaw) yaw = source[c];
            yaw = ScaleYaw(yaw,rotatePlayRate);
            pose.CopyTo(output); curves.Clear();
            for (var c = 0; c < source.Length; c++) curves[_maps[side][c]] = source[c];
            curves[_profile._yaw] = yaw;
        }
    }
    private void Layout(Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
    {
        if (output.Length != _bones.Length || curves.Length != _curves.Length) throw new ArgumentException("Invalid Standing rest output layout.");
    }
    private static AlsInertialCurve ScaleYaw(AlsInertialCurve curve, float rate)
    {
        var current = curve.Present ? curve.Value : 0;
        // AnimNode_ModifyCurve::ProcessCurveOperation retains Lerp arithmetic at alpha=1.
        var result = AlsStandingCycleCurves.ModifyBlend(curve,current*rate,1);
        if (!float.IsFinite(result.Value)) throw new ArgumentException("Standing yaw curve overflow.");
        return result;
    }
}
