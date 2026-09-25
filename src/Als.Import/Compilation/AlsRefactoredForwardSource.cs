using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Standing forward's original cache boundary and sprint subtree.</summary>
public sealed class AlsRefactoredForwardSource
{
    public AlsRefactoredMovementPlayers Players { get; }
    public int Cache { get; }
    public int BaseCache { get; }
    public int BasePlayer { get; }
    public int SprintPlayer { get; }
    public int AccelerationPlayer { get; }
    public int GaitBaseRead { get; }
    public int BlockBaseRead { get; }
    public AlsOverlayAlphaPolicy AccelerationPolicy { get; }
    public AlsOverlayAlphaPolicy BlockPolicy { get; }
    private readonly float[] _times;
    public ReadOnlySpan<float> GaitBlendTimes => _times;

    public AlsRefactoredForwardSource(AlsRefactoredAnimationCatalog catalog, AlsRefactoredDirectionPoseGraph directions)
    {
        if (directions.Resources.Crouching || directions.Resources.CatalogDigest != catalog.IndexDigest)
            throw new ArgumentException("Standing forward requires its own direction graph.");
        Players = new(catalog, false);
        var data = Compile(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false)), directions);
        Cache = data.Cache; BaseCache = data.BaseCache; GaitBaseRead = data.GaitRead; BlockBaseRead = data.BlockRead;
        BasePlayer = Local(data.Base); SprintPlayer = Local(data.Sprint); AccelerationPlayer = Local(data.Acceleration);
        AccelerationPolicy = new(1, 0, false, 0, 1, true, 20, 4, true, 0, .25f, 0, 1);
        BlockPolicy = new(1, 0, false, 0, 1, false, 10, 10);
        _times = [.3f, .2f];
        int Local(int id)
        {
            var index = Array.FindIndex(Players.Players.ToArray(), p => p.PropertyIndex == id);
            if (index < 0) throw new ArgumentException("Missing forward movement player.");
            return index;
        }
    }

    internal readonly record struct Binding(int Cache, int BaseCache, int Base, int Sprint, int Acceleration, int GaitRead, int BlockRead);
    internal static Binding Compile(JsonElement payload, AlsRefactoredDirectionPoseGraph directions)
    {
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(false);
        Expect(payload, new { source = blueprint, @class = "AnimBlueprint" });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, blueprint, blueprint + ":AnimGraph"), true);
        var cache = directions.Caches.ToArray().Single(c => c.Name == "Move Forward");
        var outer = Node(cache.SourcePropertyIndex, "AnimGraphNode_TwoWayBlend");
        Two(outer, false, false, "GetParent.StandingState.SprintBlockAmount");
        var gaitId = Link(cache.SourcePropertyIndex, "a", "A");
        var gait = Node(gaitId, "AlsAnimGraphNode_GameplayTagsBlend");
        foreach (var policy in Policies(gait))
        {
            Expect(policy, new { tags = new[] { new { tagName = "Als.Gait.Sprinting" } }, transitionType = "StandardBlend", blendType = "Cubic", childUpateMode = "Default", customBlendCurve = "", blendProfile = "" });
            var times = policy.GetProperty("blendTime");
            Require(times.GetArrayLength() == 2 && policy.GetProperty("blendPose").GetArrayLength() == 2, "Forward gait channel count differs.");
        }
        // Exposed literals override the editor struct's .1/.1 defaults.
        var gaitTimes = gait.GetProperty("runtime").GetProperty("blendTime");
        Require(gaitTimes[0].GetSingle() == .3f && gaitTimes[1].GetSingle() == .2f &&
            float.Parse(graph.Literal(Authored(gait), "BlendTime_0"), CultureInfo.InvariantCulture) == .3f &&
            float.Parse(graph.Literal(Authored(gait), "BlendTime_1"), CultureInfo.InvariantCulture) == .2f, "Forward gait timing differs.");
        BindingPath(gait, "ActiveTag", "GetParent.Gait");
        var gaitRead = Link(gaitId, "blendPose", "BlendPose_0", 0);
        var sprintId = Link(gaitId, "blendPose", "BlendPose_1", 1);
        var sprint = Node(sprintId, "AnimGraphNode_TwoWayBlend");
        Two(sprint, true, true, "GetParent.StandingState.SprintAccelerationAmount");
        var a = Link(sprintId, "a", "A"); var b = Link(sprintId, "b", "B");
        Node(a, "AnimGraphNode_SequencePlayer"); Node(b, "AnimGraphNode_SequencePlayer");
        var blockRead = Link(cache.SourcePropertyIndex, "b", "B");
        var baseCache = Read(gaitRead); Require(Read(blockRead) == baseCache, "Forward base cache is not shared.");
        var basePlayer = Link(baseCache, "pose", "Pose"); Node(basePlayer, "AnimGraphNode_BlendSpacePlayer");
        Require(nodes[a].GetProperty("runtime").GetProperty("sequence").GetString() == "/ALS/ALS/Animations/Grounded/Sprint/A_Als_Sprint.A_Als_Sprint" &&
            nodes[b].GetProperty("runtime").GetProperty("sequence").GetString() == "/ALS/ALS/Animations/Grounded/Sprint/A_Als_Sprint_Acceleration.A_Als_Sprint_Acceleration", "Forward sprint assets differ.");
        return new(cache.PropertyIndex, baseCache, basePlayer, a, b, gaitRead, blockRead);

        JsonElement[] Policies(JsonElement n) => [n.GetProperty("runtime"), n.GetProperty("authoredProperties").GetProperty(n.GetProperty("class").GetString() == "AnimGraphNode_TwoWayBlend" ? "BlendNode" : "Node")];
        JsonElement Node(int id, string kind)
        {
            Require(nodes.TryGetValue(id, out var n) && n.GetProperty("class").GetString() == kind && n.GetProperty("graph").GetString() == blueprint + ":AnimGraph", "Foreign forward node.");
            foreach (var p in Policies(n)) foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(p.GetProperty(cb), new { functionName = "None" });
            return n;
        }
        AlsYawOffsetCompiler.Node Authored(JsonElement n) => graph.Named(n.GetProperty("path").GetString()!.Split('.').Last());
        int Link(int parent, string property, string pin, int index = -1)
        {
            var link = nodes[parent].GetProperty("runtime").GetProperty(property); if (index >= 0) link = link[index];
            Expect(link, new { sourceLinkId = parent }); var child = link.GetProperty("linkId").GetInt32();
            var (node, output) = graph.Follow(Authored(nodes[parent]), pin);
            Require(nodes.ContainsKey(child) && output.Name == "Pose" && nodes[child].GetProperty("path").GetString() == blueprint + ":AnimGraph." + node.Name, "Forward authored link differs.");
            return child;
        }
        void BindingPath(JsonElement n, string name, string path)
        {
            var node = Authored(n);
            var matches = Regex.Matches(node.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True");
            Require(matches.Count == 1 && matches[0].Groups[1].Value == name && string.Join(".", Regex.Matches(matches[0].Groups[2].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value)) == path, "Forward binding differs.");
            Require(node.Pins.Values.All(p => p.Output || p.Links == "" || p.Name is "A" or "B" or "BlendPose_0" or "BlendPose_1"), "Unsupported forward expression.");
        }
        void Two(JsonElement n, bool acceleration, bool reset, string binding)
        {
            foreach (var p in Policies(n))
            {
                Expect(p, new { alphaInputType = "Float", bResetChildOnActivation = reset, bAlwaysUpdateChildren = false, alphaScaleBias = new { scale = 1, bias = 0 } });
                Expect(p.GetProperty("alphaScaleBiasClamp"), new { bMapRange = acceleration, bClampResult = false, bInterpResult = acceleration,
                    inRange = new { min = 0, max = acceleration ? .25 : 1 }, outRange = new { min = 0, max = 1 }, scale = 1, bias = 0,
                    clampMin = 0, clampMax = 1, interpSpeedIncreasing = acceleration ? 20 : 10, interpSpeedDecreasing = acceleration ? 4 : 10 });
            }
            BindingPath(n, "Alpha", binding);
        }
        int Read(int id)
        {
            var n = Node(id, "AnimGraphNode_UseCachedPose"); var link = n.GetProperty("runtime").GetProperty("linkToCachingNode");
            Expect(link, new { sourceLinkId = id }); var target = link.GetProperty("linkId").GetInt32(); var save = Node(target, "AnimGraphNode_SaveCachedPose");
            foreach (var item in new[] { n, save }) Expect(item.GetProperty("runtime"), new { cachePoseName = "Move Forward Base" });
            var shortPath = blueprint.Split('.').Last() + ":" + save.GetProperty("path").GetString()!.Split(':')[1];
            Require(Authored(n).Body.Contains("SaveCachedPoseNode=\"/Script/AnimGraph.AnimGraphNode_SaveCachedPose'" + shortPath + "'\"", StringComparison.Ordinal) &&
                Authored(n).Body.Contains("NameOfCache=\"Move Forward Base\"", StringComparison.Ordinal) && Authored(save).Body.Contains("CacheName=\"Move Forward Base\"", StringComparison.Ordinal), "Forward authored cache differs.");
            return target;
        }
    }
    private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
}
