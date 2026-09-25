using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredWeaponEvaluator(int PropertyIndex, string Source, int Frame, bool Pitch);

/// <summary>State-local evaluator leaves. Fixed poses and pitch-driven mesh deltas
/// use the original skeleton and centimeters; no playback clock is created.</summary>
public sealed class AlsRefactoredWeaponEvaluators
{
    private sealed record Leaf(AlsRefactoredWeaponEvaluator Definition, AlsPrecisePose[]? Pose,
        AlsInertialCurve[]? Curves, AlsRefactoredAdditiveSource? Aim, int[] Map);
    private readonly Dictionary<int, Leaf> _leaves = new();
    private readonly AlsRefactoredWeaponEvaluator[] _definitions;
    private readonly string[] _bones, _curves;
    private readonly int[] _parents;
    public ReadOnlySpan<AlsRefactoredWeaponEvaluator> Evaluators => _definitions;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<int> Parents => _parents;
    public ReadOnlySpan<string> CurveNames => _curves;
    public AlsRefactoredWeaponSourceProfile SourceProfile { get; }

    public AlsRefactoredWeaponEvaluators(AlsRefactoredAnimationCatalog catalog, AlsRefactoredWeaponSourceProfile profile)
    {
        if (catalog.IndexDigest != profile.Players.CatalogDigest) throw new ArgumentException("Foreign weapon evaluator catalog.");
        SourceProfile = profile;
        var kind = profile.Machine.Resources.Kind;
        var blueprint = AlsRefactoredWeaponMachineResources.Blueprint(kind);
        var payload = catalog.Read(blueprint);
        _definitions = Compile(payload, profile);
        var prefix = $"/ALS/ALS/Animations/Overlays/{kind}/A_Als_{kind}";
        string Path(string suffix) => prefix + suffix + ".A_Als_" + kind + suffix;
        var poses = catalog.CompileAbsolutePoseWithCurves(Path("_Poses"));
        var aims = new[] { Path("_Aim"), Path("_Aim_Crouch") }.ToDictionary(p => p, catalog.CompileAdditivePose);
        foreach (var path in aims.Keys) Expect(catalog.Read(path).GetProperty("evaluation"), new { additiveType = "AAT_RotationOffsetMeshSpace" });
        _bones = poses.Pose.BoneNames.ToArray(); _parents = poses.Pose.Parents.ToArray();
        foreach (var aim in aims.Values)
            if (!aim.BoneNames.SequenceEqual(_bones) || !aim.Parents.SequenceEqual(_parents)) throw new ArgumentException("Weapon evaluator skeleton differs.");
        _curves = poses.Curves.Names.ToArray().Concat(aims.Values.SelectMany(a => a.CurveNames.ToArray()))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        int[] Map(string[] names) => names.Select(n => Array.FindIndex(_curves, c => c.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
        var poseMap = Map(poses.Curves.Names.ToArray()); var sampler = poses.Pose.CreateSampler(poses.Curves);
        var scratch = new AlsInertialCurve[poseMap.Length];
        foreach (var d in _definitions)
        {
            if (d.Pitch) { var aim = aims[d.Source]; _leaves.Add(d.PropertyIndex, new(d, null, null, aim, Map(aim.CurveNames.ToArray()))); continue; }
            if (d.Frame >= poses.Pose.Data.SampledKeyCount) throw new ArgumentException("Weapon pose frame exceeds the source.");
            var pose = new AlsPrecisePose[_bones.Length]; var curves = new AlsInertialCurve[_curves.Length];
            // UE converts the frame number to seconds, then stores it as float.
            var time = (float)((double)d.Frame * poses.Pose.Data.FrameRateDenominator / poses.Pose.Data.FrameRateNumerator);
            sampler.Sample(time, true, false, false, pose, scratch);
            for (var c = 0; c < poseMap.Length; c++) curves[poseMap[c]] = scratch[c];
            _leaves.Add(d.PropertyIndex, new(d, pose, curves, null, []));
        }
    }

    internal static AlsRefactoredWeaponEvaluator[] Compile(JsonElement payload, AlsRefactoredWeaponSourceProfile profile)
    {
        var kind = profile.Machine.Resources.Kind;
        var blueprint = AlsRefactoredWeaponMachineResources.Blueprint(kind);
        var definitions = new List<AlsRefactoredWeaponEvaluator>();
        foreach (var node in payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray())
        {
            var id = node.GetProperty("propertyIndex").GetInt32();
            if (id < 0 || id >= profile.Nodes.Length) throw new ArgumentException("Invalid weapon evaluator index.");
            if (profile.Nodes[id]?.Kind != "AnimGraphNode_SequenceEvaluator") continue;
            Expect(node, new { @class = "AnimGraphNode_SequenceEvaluator" });
            var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, blueprint, node.GetProperty("graph").GetString()!), true);
            var authored = graph.Named(node.GetProperty("path").GetString()!.Split('.')[^1]);
            if (authored.Kind != "AnimGraphNode_SequenceEvaluator") throw new ArgumentException("Weapon evaluator authored type differs.");
            var runtime = node.GetProperty("runtime"); var path = runtime.GetProperty("sequence").GetString()!;
            var frame = runtime.GetProperty("bUseExplicitFrame").GetBoolean();
            var prefix = $"/ALS/ALS/Animations/Overlays/{kind}/A_Als_{kind}";
            string Path(string suffix) => prefix + suffix + ".A_Als_" + kind + suffix;
            if (frame ? path != Path("_Poses") : path != Path("_Aim") && path != Path("_Aim_Crouch")) throw new ArgumentException("Unknown weapon evaluator source.");
            foreach (var policy in new[] { runtime, node.GetProperty("authoredProperties").GetProperty("Node") })
            {
                Expect(policy, new { sequence = path, bUseExplicitFrame = frame, method = "DoNotSync", groupName = "None",
                    groupRole = "CanBeLeader", bTeleportToExplicitTime = true, bShouldLoop = true, reinitializationBehavior = "ExplicitTime" });
                foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(policy.GetProperty(cb), new { functionName = "None" });
            }
            var bindings = Regex.Matches(authored.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                .Select(m => m.Groups[1].Value + ":" + string.Join(":", Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value))).ToArray();
            if (!bindings.SequenceEqual(frame ? Array.Empty<string>() : ["ExplicitTime:GetParent:ViewState:PitchAmount"]) ||
                authored.Pins.Values.Any(p => !p.Output && p.Links != "") ||
                !authored.Body.Contains("Sequence=\"/Script/Engine.AnimSequence'" + path + "'\"", StringComparison.Ordinal))
                throw new ArgumentException("Unsupported weapon evaluator expression.");
            var number = frame ? runtime.GetProperty("explicitFrame").GetInt32() : -1;
            if (frame && (number < 0 || int.Parse(graph.Literal(authored, "ExplicitFrame"), CultureInfo.InvariantCulture) != number))
                throw new ArgumentException("Weapon evaluator frame differs.");
            definitions.Add(new(id, path, number, !frame));
        }
        if (definitions.Count != profile.Nodes.Count(n => n?.Kind == "AnimGraphNode_SequenceEvaluator") || definitions.Select(d => d.PropertyIndex).Distinct().Count() != definitions.Count)
            throw new ArgumentException("Incomplete weapon evaluators.");
        return definitions.OrderBy(d => d.PropertyIndex).ToArray();
    }

    public Sampler CreateSampler() => new(this);
    public sealed class Sampler
    {
        private readonly AlsRefactoredWeaponEvaluators _profile;
        private readonly Dictionary<int, (AlsRefactoredAdditiveSource.Sampler Sampler, AlsInertialCurve[] Curves)> _aim;
        private readonly AlsPrecisePose[] _pose;
        private readonly AlsInertialCurve[] _curves;
        private int _busy;
        internal Sampler(AlsRefactoredWeaponEvaluators profile)
        {
            _profile = profile; _pose = new AlsPrecisePose[profile._bones.Length]; _curves = new AlsInertialCurve[profile._curves.Length];
            _aim = profile._leaves.Where(p => p.Value.Aim is not null).ToDictionary(p => p.Key,
                p => (p.Value.Aim!.CreateSampler(), new AlsInertialCurve[p.Value.Map.Length]));
        }
        public void Sample(int propertyIndex, float pitch, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
        {
            if (!_profile._leaves.TryGetValue(propertyIndex, out var leaf) || !float.IsFinite(pitch) || pitch is < 0 or > 1 || pose.Length != _pose.Length || curves.Length != _curves.Length)
                throw new ArgumentException("Invalid weapon evaluator sample.");
            if (Interlocked.Exchange(ref _busy, 1) != 0) throw new InvalidOperationException("Reentrant weapon evaluator sampler.");
            try
            {
                if (!leaf.Definition.Pitch) { leaf.Pose!.CopyTo(pose); leaf.Curves!.CopyTo(curves); return; }
                var aim = _aim[propertyIndex]; aim.Sampler.Sample(pitch, _pose, aim.Curves); _curves.AsSpan().Clear();
                for (var c = 0; c < leaf.Map.Length; c++) _curves[leaf.Map[c]] = aim.Curves[c];
                _pose.CopyTo(pose); _curves.CopyTo(curves);
            }
            finally { Volatile.Write(ref _busy, 0); }
        }
    }
}
