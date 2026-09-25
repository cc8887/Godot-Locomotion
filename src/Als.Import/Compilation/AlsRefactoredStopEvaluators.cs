using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredStopEvaluator(int PropertyIndex,string Source,int Frame);

/// <summary>Frozen original Plant Left/Right evaluator leaves. These are absolute
/// fixed-frame poses, not advancing SequencePlayers or additive stop animations.</summary>
public sealed class AlsRefactoredStopEvaluators
{
    private readonly AlsRefactoredStopEvaluator[] _definitions;
    private readonly Dictionary<int,(AlsPrecisePose[] Pose,AlsInertialCurve[] Curves)> _leaves = new();
    private readonly string[] _bones, _curves;
    private readonly int[] _parents;
    public string CatalogDigest { get; }
    public ReadOnlySpan<AlsRefactoredStopEvaluator> Evaluators => _definitions;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<int> Parents => _parents;
    public ReadOnlySpan<string> CurveNames => _curves;

    public AlsRefactoredStopEvaluators(AlsRefactoredAnimationCatalog catalog,AlsRefactoredStopResources resources)
    {
        if (catalog.IndexDigest != resources.CatalogDigest) throw new ArgumentException("Foreign Stop evaluator catalog.");
        CatalogDigest = catalog.IndexDigest;
        _definitions = Compile(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false)),resources);
        var sources = _definitions.Select(d => d.Source).Distinct(StringComparer.Ordinal).ToDictionary(p => p,catalog.CompileAbsolutePoseWithCurves);
        var first = sources[_definitions[0].Source]; _bones = first.Pose.BoneNames.ToArray(); _parents = first.Pose.Parents.ToArray();
        if (sources.Values.Any(s => !s.Pose.BoneNames.SequenceEqual(_bones) || !s.Pose.Parents.SequenceEqual(_parents)))
            throw new ArgumentException("Stop evaluator skeleton differs.");
        _curves = sources.Values.SelectMany(s => s.Curves.Names.ToArray()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        foreach (var group in _definitions.GroupBy(d => d.Source))
        {
            var source = sources[group.Key]; var sampler = source.Pose.CreateSampler(source.Curves);
            var map = source.Curves.Names.ToArray().Select(n => Array.FindIndex(_curves,c => c.Equals(n,StringComparison.OrdinalIgnoreCase))).ToArray();
            var scratch = new AlsInertialCurve[map.Length];
            foreach (var d in group)
            {
                if (d.Frame >= source.Pose.Data.SampledKeyCount) throw new ArgumentException("Stop frame exceeds sequence.");
                var pose = new AlsPrecisePose[_bones.Length]; var curves = new AlsInertialCurve[_curves.Length];
                var time = (float)((double)d.Frame * source.Pose.Data.FrameRateDenominator / source.Pose.Data.FrameRateNumerator);
                sampler.Sample(time,true,false,false,pose,scratch);
                for (var c = 0; c < map.Length; c++) curves[map[c]] = scratch[c];
                _leaves.Add(d.PropertyIndex,(pose,curves));
            }
        }
    }

    internal static AlsRefactoredStopEvaluator[] Compile(JsonElement payload,AlsRefactoredStopResources resources)
    {
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(false); Expect(payload,new { source = blueprint, @class = "AnimBlueprint" });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        string[] directions = ["Forward","Backward","Left_Forward","Left_Backward","Right_Forward","Right_Backward"];
        int[][] frames = [[4,4,6,4,7,7],[21,21,24,24,23,21]];
        var definitions = new List<AlsRefactoredStopEvaluator>();
        for (var side = 0; side < 2; side++)
        {
            var state = resources.States[3+side];
            for (var i = 0; i < 6; i++)
            {
                var id = state.PlayerPropertyIndices[i]; var node = nodes[id]; var runtime = node.GetProperty("runtime");
                Expect(node,new { @class = "AnimGraphNode_SequenceEvaluator" });
                if (node.GetProperty("graph").GetString() != nodes[state.RootPropertyIndex].GetProperty("graph").GetString()) throw new ArgumentException("Foreign Stop evaluator graph.");
                var name = "A_Als_Walk_" + directions[i]; var path = "/ALS/ALS/Animations/Grounded/WalkRun/" + name + "." + name;
                foreach (var policy in new[] {runtime,node.GetProperty("authoredProperties").GetProperty("Node")})
                {
                    Expect(policy,new { sequence = path, explicitTime = 0, bUseExplicitFrame = true, explicitFrame = frames[side][i],
                        groupName = "None", groupRole = "CanBeLeader", method = "DoNotSync", bIgnoreForRelevancyTest = false,
                        bShouldLoop = true, bTeleportToExplicitTime = true, reinitializationBehavior = "ExplicitTime", startPosition = 0 });
                    foreach (var cb in new[] {"initialUpdateFunction","becomeRelevantFunction","updateFunction"}) Expect(policy.GetProperty(cb),new { functionName = "None" });
                }
                var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!,blueprint,node.GetProperty("graph").GetString()!),true);
                var authored = graph.Named(node.GetProperty("path").GetString()!.Split('.')[^1]);
                if (authored.Kind != "AnimGraphNode_SequenceEvaluator" || authored.Pins.Values.Any(p => !p.Output && p.Links != "") ||
                    authored.Body.Contains("bIsBound=True",StringComparison.Ordinal) ||
                    !authored.Body.Contains("Sequence=\"/Script/Engine.AnimSequence'" + path + "'\"",StringComparison.Ordinal) ||
                    int.Parse(graph.Literal(authored,"ExplicitFrame"),CultureInfo.InvariantCulture) != frames[side][i])
                    throw new ArgumentException("Stop evaluator expression differs.");
                definitions.Add(new(id,path,frames[side][i]));
            }
        }
        return definitions.ToArray();
    }

    public void Sample(int propertyIndex,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
    {
        if (!_leaves.TryGetValue(propertyIndex,out var leaf) || pose.Length != _bones.Length || curves.Length != _curves.Length)
            throw new ArgumentException("Invalid Stop evaluator output layout.");
        leaf.Pose.AsSpan().CopyTo(pose); leaf.Curves.AsSpan().CopyTo(curves);
    }
}
