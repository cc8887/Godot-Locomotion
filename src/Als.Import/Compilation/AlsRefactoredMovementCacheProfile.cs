using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>The original standing Movement cache envelope, outside the direction machine.</summary>
public sealed class AlsRefactoredMovementCacheProfile
{
    public const string LeanSource = "/ALS/ALS/Animations/Grounded/Lean/BS_Als_Lean.BS_Als_Lean";
    public string CatalogDigest { get; }
    public AlsRefactoredBlendPoseSource Lean { get; }
    public AlsOverlayAlphaPolicy AlphaPolicy { get; } = new(1, 0, true, .5f, 1, true, 20, 1);
    public AlsRefactoredMovementCacheProfile(AlsRefactoredAnimationCatalog catalog,
        IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> triangles)
    {
        Compile(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false)));
        CatalogDigest = catalog.IndexDigest;
        Lean = new(triangles[LeanSource], catalog);
        if (!Lean.IsAdditive) throw new ArgumentException("Movement lean must be local additive.");
    }

    internal static void Compile(JsonElement payload)
    {
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(false);
        Expect(payload, new { source = blueprint, @class = "AnimBlueprint" });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, blueprint, blueprint + ":AnimGraph"), true);
        var cache = Node(67, "SaveCachedPose", ["Pose"]);
        Expect(cache.GetProperty("runtime"), new { cachePoseName = "Movement" });
        Require(Authored(cache).Body.Contains("CacheName=\"Movement\"", StringComparison.Ordinal));
        Link(67, "pose", "Pose", 122);
        var curve = Node(122, "ModifyCurve", ["SourcePose"]);
        foreach (var p in Policies(curve)) Expect(p, new { curveNames = new[] { "PoseMoving" }, alpha = 1, applyMode = "Blend", curveMap = new { } });
        Expect(curve.GetProperty("runtime"), new { curveValues = new[] { 1 } });
        Require(float.Parse(graph.Literal(Authored(curve), "CurveValues_0"), CultureInfo.InvariantCulture) == 1);
        Bindings(curve, []); Link(122, "sourcePose", "SourcePose", 120);
        var apply = Node(120, "ApplyAdditive", ["Base", "Additive"]);
        foreach (var p in Policies(apply))
        {
            Expect(p, new { alphaInputType = "Float", alphaCurveName = "None", lODThreshold = -1, alphaScaleBias = new { scale = 1, bias = 0 } });
            Expect(p.GetProperty("alphaScaleBiasClamp"), new { bMapRange = false, bClampResult = true, bInterpResult = true,
                inRange = new { min = 0, max = 1 }, outRange = new { min = 0, max = 1 }, scale = 1, bias = 0,
                clampMin = .5, clampMax = 1, interpSpeedIncreasing = 20, interpSpeedDecreasing = 1 });
        }
        Bindings(apply, [("Alpha", "GetParent.PoseState.UnweightedGaitRunningAmount")]);
        Link(120, "base", "Base", 201); Link(120, "additive", "Additive", 202);
        Require(nodes[201].GetProperty("class").GetString() == "AnimGraphNode_StateMachine");
        var lean = Node(202, "BlendSpaceEvaluator", []);
        foreach (var p in Policies(lean)) Expect(p, new { normalizedTime = 0, bTeleportToNormalizedTime = true,
            groupName = "None", groupRole = "CanBeLeader", method = "DoNotSync", bIgnoreForRelevancyTest = false,
            bOverridePositionWhenJoiningSyncGroupAsLeader = false, playRate = 1, bLoop = true,
            bResetPlayTimeWhenBlendSpaceChanges = true, startPosition = 0, blendSpace = LeanSource });
        Bindings(lean, [("X", "GetParent.LeanState.RightAmount"), ("Y", "GetParent.LeanState.ForwardAmount")]);

        JsonElement[] Policies(JsonElement n) => [n.GetProperty("runtime"), n.GetProperty("authoredProperties").GetProperty("Node")];
        AlsYawOffsetCompiler.Node Authored(JsonElement n) => graph.Named(n.GetProperty("path").GetString()!.Split('.').Last());
        JsonElement Node(int id, string kind, string[] posePins)
        {
            Require(nodes.TryGetValue(id, out var n) && n.GetProperty("class").GetString() == "AnimGraphNode_" + kind && n.GetProperty("graph").GetString() == blueprint + ":AnimGraph");
            var authored = Authored(n); Require(authored.Kind == "AnimGraphNode_" + kind);
            Require(authored.Pins.Values.All(p => p.Output || p.Links == "" || posePins.Contains(p.Name)));
            foreach (var p in Policies(n)) foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(p.GetProperty(cb), new { functionName = "None" });
            return n;
        }
        void Link(int id, string property, string pin, int target)
        {
            Expect(nodes[id].GetProperty("runtime").GetProperty(property), new { linkId = target, sourceLinkId = id });
            var (child, output) = graph.FollowReroutes(Authored(nodes[id]), pin);
            Require(output.Name == "Pose" && nodes[target].GetProperty("path").GetString() == blueprint + ":AnimGraph." + child.Name);
        }
        void Bindings(JsonElement n, (string Name, string Path)[] expected)
        {
            var actual = Regex.Matches(Authored(n).Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                .Select(m => (m.Groups[1].Value, string.Join(".", Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value))));
            Require(actual.OrderBy(b => b.Item1).SequenceEqual(expected.OrderBy(b => b.Name)));
        }
    }
    private static void Require(bool valid) { if (!valid) throw new ArgumentException("Original Movement cache envelope differs."); }
}
