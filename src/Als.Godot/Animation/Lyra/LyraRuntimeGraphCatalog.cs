using System.Security.Cryptography;
using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraCompiledExit(int Edge, int Delegate, bool Automatic, bool Desired,
    float AutomaticTriggerTime, string RequiredSyncGroup, bool OnlyWhenActive = false);
internal sealed record LyraCompiledState(string Name, bool Conduit, int EntryDelegate,
    IReadOnlyList<LyraCompiledExit> Exits, bool AlwaysResetOnEntry = false);
internal sealed record LyraCompiledEdge(int Previous, int Next, float Duration, bool Inertial, string BlendProfile);
internal sealed record LyraCompiledMachine(string Name, int InitialState,
    IReadOnlyList<LyraCompiledState> States, IReadOnlyList<LyraCompiledEdge> Edges);

// Runtime metadata, including the baked exit order. Editor priorities cannot
// establish the ordering of aliases and equal-priority transitions.
internal sealed class LyraRuntimeGraphCatalog
{
    private readonly Dictionary<string, IReadOnlyList<LyraCompiledMachine>> _classes;
    private LyraRuntimeGraphCatalog(Dictionary<string, IReadOnlyList<LyraCompiledMachine>> classes,
        string hash, string mainHash)
    { _classes = classes; Sha256 = hash; MainAssetSha256 = mainHash; }

    public string Sha256 { get; }
    public string MainAssetSha256 { get; }
    public LyraCompiledMachine Locomotion => _classes[MainClass].Single();
    public const string MainClass = "/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C";
    public IReadOnlyList<LyraCompiledMachine> ForClass(string path) => _classes.TryGetValue(path, out var machines)
        ? machines : throw new InvalidOperationException("Unknown compiled Lyra graph: " + path);

    public static LyraRuntimeGraphCatalog Load() => Parse(
        Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/runtime_graph.json"),
        Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/linked_layer_inventory.json"));

    internal static LyraRuntimeGraphCatalog Parse(byte[] bytes, byte[] inventoryBytes)
    {
        using var document = JsonDocument.Parse(bytes); using var inventory = JsonDocument.Parse(inventoryBytes);
        var root = document.RootElement;
        var rows = root.GetProperty("classes");
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || rows.EnumerateObject().Count() != 10 ||
            root.GetProperty("inventorySha256").GetString() != Hash(inventoryBytes))
            throw new InvalidOperationException("Stale compiled Lyra runtime graph.");
        var classes = new Dictionary<string, IReadOnlyList<LyraCompiledMachine>>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateObject())
        {
            var classPath = row.Value.GetProperty("class").GetString()!;
            if (row.Name == "main" ? classPath != MainClass :
                inventory.RootElement.GetProperty("classes").GetProperty(row.Name).GetProperty("class").GetString() != classPath)
                throw new InvalidOperationException("Wrong compiled graph class.");
            var machines = row.Value.GetProperty("machines").EnumerateArray().Select(ReadMachine).ToArray();
            foreach (var node in row.Value.GetProperty("nodes").EnumerateArray())
            {
                var settings = node.GetProperty("settings");
                if (node.GetProperty("type").GetString() == "/Script/Engine.AnimNode_StateMachine")
                {
                    var index = settings.GetProperty("stateMachineIndexInClass").GetInt32();
                    if ((uint)index >= machines.Length || settings.GetProperty("maxTransitionsPerFrame").GetInt32() != 1 ||
                        !settings.GetProperty("bSkipFirstUpdateTransition").GetBoolean() ||
                        !settings.GetProperty("bReinitializeOnBecomingRelevant").GetBoolean() ||
                        settings.GetProperty("bAllowConduitEntryStates").GetBoolean())
                        throw new NotSupportedException("Unsupported Lyra machine lifecycle.");
                }
                else if (row.Name != "main" || settings.GetProperty("defaultBlendProfile").GetString() != "" ||
                         settings.GetProperty("filteredCurves").GetArrayLength() != 0 ||
                         settings.GetProperty("filteredBones").GetArrayLength() != 0 ||
                         settings.GetProperty("bResetOnBecomingRelevant").GetBoolean() ||
                         !settings.GetProperty("bForwardRequestsThroughSkippedCachedPoseNodes").GetBoolean())
                    throw new NotSupportedException("Unsupported main inertialization settings.");
            }
            if (machines.Length != (row.Name == "main" ? 1 : 4) ||
                row.Value.GetProperty("nodes").GetArrayLength() != (row.Name == "main" ? 2 : 4))
                throw new InvalidOperationException("Incomplete compiled machine inventory.");
            classes.Add(classPath, machines);
        }
        var result = new LyraRuntimeGraphCatalog(classes, Hash(bytes), root.GetProperty("assetSha256").GetProperty("main").GetString()!);
        var expected = new[] { "Idle", "Start", "Cycle", "Stop", "Pivot", "JumpSelector", "JumpStart", "JumpApex",
            "FallLand", "EndInAir", "JumpStartLoop", "FallLoop" };
        if (!result.Locomotion.States.Select(s => s.Name).SequenceEqual(expected) ||
            result.Locomotion.InitialState != 0 || result.Locomotion.Edges.Count != 36 ||
            result.Locomotion.Edges.Any(e => e.BlendProfile != "") ||
            !result.Locomotion.States.Where(s => s.Conduit).Select(s => s.Name).SequenceEqual(new[] { "JumpSelector", "EndInAir" }))
            throw new InvalidOperationException("Changed compiled Lyra locomotion topology.");
        var endpoints = new (int From, int To)[] { (0,1),(0,5),(1,4),(1,2),(1,3),(1,2),(1,5),(1,2),(1,2),
            (2,4),(2,3),(2,5),(3,0),(3,1),(3,5),(3,0),(3,0),(4,2),(4,5),(4,2),(4,3),(4,2),
            (5,7),(5,6),(6,10),(6,9),(7,11),(7,9),(8,9),(8,9),(9,2),(9,0),(10,7),(10,9),(11,8),(11,9) };
        for (var index = 0; index < endpoints.Length; index++)
        {
            var edge = result.Locomotion.Edges[index];
            if ((edge.Previous, edge.Next) != endpoints[index] || edge.Inertial != (index is 3 or 12 or 15 or 17))
                throw new InvalidOperationException("Changed compiled predicate binding: " + index);
        }
        return result;
    }

    private static LyraCompiledMachine ReadMachine(JsonElement row)
    {
        var states = row.GetProperty("states").EnumerateArray().Select(s => new LyraCompiledState(
            s.GetProperty("stateName").GetString()!, s.GetProperty("bIsAConduit").GetBoolean(),
            s.GetProperty("entryRuleNodeIndex").GetInt32(), s.GetProperty("transitions").EnumerateArray().Select(e =>
                new LyraCompiledExit(e.GetProperty("transitionIndex").GetInt32(), e.GetProperty("canTakeDelegateIndex").GetInt32(),
                    e.GetProperty("bAutomaticRemainingTimeRule").GetBoolean(), e.GetProperty("bDesiredTransitionReturnValue").GetBoolean(),
                    e.GetProperty("automaticRuleTriggerTime").GetSingle(), e.GetProperty("syncGroupNameToRequireValidMarkersRule").GetString()!,
                    e.GetProperty("bOnlyEvaluateWhenActive").GetBoolean())).ToArray(), s.GetProperty("bAlwaysResetOnEntry").GetBoolean())).ToArray();
        var edges = row.GetProperty("transitions").EnumerateArray().Select(e =>
        {
            if (e.GetProperty("customCurve").GetString() != "" ||
                e.GetProperty("blendMode").GetString() != "HermiteCubic" ||
                e.GetProperty("minTimeBeforeReentry").GetSingle() >= 0 || e.GetProperty("bAllowInertializationForSelfTransitions").GetBoolean())
                throw new NotSupportedException("Unsupported Lyra transition profile.");
            return new LyraCompiledEdge(e.GetProperty("previousState").GetInt32(), e.GetProperty("nextState").GetInt32(),
                e.GetProperty("crossfadeDuration").GetSingle(), e.GetProperty("logicType").GetString() switch
                { "TLT_StandardBlend" => false, "TLT_Inertialization" => true, _ => throw new NotSupportedException("Custom Lyra transition logic.") },
                e.GetProperty("blendProfile").GetString()!);
        }).ToArray();
        var initial = row.GetProperty("initialState").GetInt32();
        if ((uint)initial >= states.Length || states[initial].Conduit || states.Select(s => s.Name).Distinct().Count() != states.Length)
            throw new InvalidOperationException("Invalid compiled machine states.");
        for (var state = 0; state < states.Length; state++)
        foreach (var exit in states[state].Exits)
            if ((uint)exit.Edge >= edges.Length || edges[exit.Edge].Previous != state || exit.Delegate < 0 ||
                (uint)edges[exit.Edge].Next >= states.Length || !float.IsFinite(edges[exit.Edge].Duration) || edges[exit.Edge].Duration < 0)
                throw new InvalidOperationException("Invalid compiled transition identity.");
        return new(row.GetProperty("machineName").GetString()!, initial, states, edges);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
