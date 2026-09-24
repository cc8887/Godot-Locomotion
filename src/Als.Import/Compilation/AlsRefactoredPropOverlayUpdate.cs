using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public enum AlsRefactoredPropOverlayKind { Binoculars, Torch }

/// <summary>Validates only the nested update path. This is not a pose-graph compiler.</summary>
public sealed class AlsRefactoredPropOverlayUpdateProfile
{
    public AlsRefactoredPropOverlayKind Kind { get; }
    public string CatalogDigest { get; }
    public static string Blueprint(AlsRefactoredPropOverlayKind kind) =>
        $"/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_{kind}.AB_Als_{kind}";
    public AlsRefactoredPropOverlayUpdateProfile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredPropOverlayKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Validate(catalog.Read(Blueprint(kind)), kind); Kind = kind; CatalogDigest = catalog.IndexDigest;
    }
    public static void Validate(JsonElement payload, AlsRefactoredPropOverlayKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var binoculars = kind == AlsRefactoredPropOverlayKind.Binoculars;
        var source = Blueprint(kind); var count = binoculars ? 28 : 26;
        var action = binoculars ? 24 : 23; var aim = binoculars ? 6 : 7;
        var add = binoculars ? 4 : 5; var idle = binoculars ? 5 : 6;
        Expect(payload, new { source, @class = "AnimBlueprint" });
        Expect(payload.GetProperty("compiled"), new { source, generatedClass = source + "_C", compiledPropertyCount = count });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        if (nodes.Count != count) throw new ArgumentException("Prop graph node count differs.");
        var text = payload.GetProperty("nativeText").GetString()!.Replace("\r", "");
        var declarations = Regex.Match(text, "(?ms)^   Begin Object Class=/Script/AnimGraph.AnimationGraph Name=\"Overlay\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
        var body = Regex.Match(text, "(?ms)^   Begin Object Name=\"Overlay\"[^\\n]*\\n(.*?)^   End Object").Groups[1].Value;
        if (declarations.Length == 0 || body.Length == 0) throw new ArgumentException("Missing authored prop graph.");
        var graph = new AlsYawOffsetCompiler.Graph(Regex.Replace(declarations + body, "(?m)^   ", ""), true);
        string Name(int id) => nodes[id].GetProperty("path").GetString()!.Split('.')[^1];
        foreach (var (id, tag, times, blend) in new[]
        {
            (action, new[]{"Als.LocomotionAction.Mantling","Als.LocomotionAction.GettingUp","Als.LocomotionAction.Rolling"}, new[]{.5f,.2f,0,.2f}, "Linear"),
            (aim, new[]{"Als.RotationMode.Aiming"}, new[]{.75f,.2f}, "HermiteCubic")
        })
        {
            Expect(nodes[id], new { @class = "AlsAnimGraphNode_GameplayTagsBlend" });
            foreach (var p in Policies(id))
            {
                Expect(p, new { transitionType = "StandardBlend", blendType = blend, childUpateMode = "Default", customBlendCurve = "", blendProfile = "" });
                if (!p.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("tagName").GetString()).SequenceEqual(tag) ||
                    p.GetProperty("blendPose").GetArrayLength() != times.Length)
                    throw new ArgumentException("Prop tag mapping differs.");
            }
            if (!nodes[id].GetProperty("runtime").GetProperty("blendTime").EnumerateArray().Select(t => t.GetSingle()).SequenceEqual(times))
                throw new ArgumentException("Prop tag times differ.");
            var authored = graph.Named(Name(id));
            for (var i = 0; i < times.Length; i++)
                if (float.Parse(graph.Literal(authored, "BlendTime_" + i), System.Globalization.CultureInfo.InvariantCulture) != times[i])
                    throw new ArgumentException("Authored prop blend time differs.");
            var bindings = Regex.Matches(authored.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True");
            var expectedPath = id == action ? "\"GetParent\",\"LocomotionAction\"" : "\"GetParent\",\"RotationMode\"";
            if (bindings.Count != 1 || bindings[0].Groups[1].Value != "ActiveTag" || bindings[0].Groups[2].Value != expectedPath)
                throw new ArgumentException("Prop active tag binding differs.");
        }
        Expect(nodes[0].GetProperty("runtime").GetProperty("result"), new { linkId = action, sourceLinkId = 0 });
        Expect(nodes[action].GetProperty("runtime").GetProperty("blendPose")[0], new { linkId = add, sourceLinkId = action });
        Expect(nodes[add], new { @class = "AnimGraphNode_ApplyAdditive" });
        var additive = nodes[add].GetProperty("runtime");
        Expect(additive, new { alpha = .5f, alphaInputType = "Float", lODThreshold = -1 });
        Expect(additive.GetProperty("base"), new { linkId = aim, sourceLinkId = add });
        Expect(additive.GetProperty("additive"), new { linkId = idle, sourceLinkId = add });
        Scale(additive.GetProperty("alphaScaleBias")); Clamp(additive.GetProperty("alphaScaleBiasClamp"));
        if (float.Parse(graph.Literal(graph.Named(Name(add)), "Alpha"), System.Globalization.CultureInfo.InvariantCulture) != .5f ||
            graph.FollowReroutes(graph.Named(Name(0)), "Result").Item1.Name != Name(action) ||
            graph.FollowReroutes(graph.Named(Name(action)), "BlendPose_0").Item1.Name != Name(add) ||
            graph.FollowReroutes(graph.Named(Name(add)), "Base").Item1.Name != Name(aim) ||
            graph.FollowReroutes(graph.Named(Name(add)), "Additive").Item1.Name != Name(idle))
            throw new ArgumentException("Authored prop update links differ.");
        Expect(nodes[idle], new { @class = "AnimGraphNode_SequencePlayer" });
        foreach (var p in Policies(idle))
        {
            Expect(p, new { sequence = AlsRefactoredDefaultOverlayProfile.IdleSource, groupName = "Secondary Motion",
                groupRole = "CanBeLeader", method = "SyncGroup", playRate = 1, playRateBasis = 1, startPosition = 0,
                bLoopAnimation = true, bOverridePositionWhenJoiningSyncGroupAsLeader = false, bStartFromMatchingPose = false });
            Clamp(p.GetProperty("playRateScaleBiasClampConstants"));
        }
        // Update-only support deliberately rejects added players/caches/callbacks.
        if (nodes.Values.Count(n => n.GetProperty("class").GetString() == "AnimGraphNode_SequencePlayer") != 1 ||
            nodes.Values.Any(n => n.GetProperty("class").GetString()!.Contains("CachedPose", StringComparison.Ordinal)))
            throw new ArgumentException("Additional update ownership requires compilation.");
        foreach (var n in nodes.Values)
            foreach (var callback in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                Expect(n.GetProperty("runtime").GetProperty(callback), new { functionName = "None" });
        IEnumerable<JsonElement> Policies(int id) => [nodes[id].GetProperty("runtime"), nodes[id].GetProperty("authoredProperties").GetProperty("Node")];
    }
    public AlsRefactoredSourcePlayerDefinition PlayerDefinition(int player, int group) => group >= 0 ?
        new(player, AlsRefactoredDefaultOverlayProfile.IdleSource, group) : throw new ArgumentOutOfRangeException(nameof(group));
    public AlsRefactoredPropOverlayUpdateRuntime CreateRuntime(int player) => new(player);
}

public sealed class AlsRefactoredPropOverlayUpdateRuntime
{
    private readonly int _player;
    private readonly AlsRefactoredSourcePlayerInput[] _inputs = new AlsRefactoredSourcePlayerInput[1];
    private AlsPropOverlayUpdateState _state;
    private AlsPropOverlayUpdateResult _next;
    private bool _prepared;
    private long _frame, _committed = -1;
    public AlsPropOverlayUpdateState CommittedState => _state;
    public AlsPropOverlayUpdateResult Candidate => _prepared ? _next : throw new InvalidOperationException("No prop update.");
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs => _prepared ? _inputs.AsSpan(0, _next.UpdateIdle ? 1 : 0) : throw new InvalidOperationException("No prop update.");
    internal AlsRefactoredPropOverlayUpdateRuntime(int player)
    { if (player < 0) throw new ArgumentOutOfRangeException(nameof(player)); _player = player; }
    public void Prepare(long frame, AlsOverlayAction action, bool aiming, float delta, bool reinitialize = false)
    {
        if (_prepared || frame <= _committed) throw new ArgumentException("Invalid prop update frame.");
        var next = AlsPropOverlayUpdate.Advance(_state, action, aiming, delta, reinitialize);
        _inputs[0] = new(_player, default, 1, next.IdleWeight, next.ResetIdle);
        _next = next; _frame = frame; _prepared = true;
    }
    public void ValidateCommit(long frame)
    { if (!_prepared || frame != _frame) throw new ArgumentException("Wrong prop update commit."); }
    public void Commit(long frame) { ValidateCommit(frame); _state = _next.State; _committed = frame; Cancel(); }
    public void Cancel() { _prepared = false; }
}
