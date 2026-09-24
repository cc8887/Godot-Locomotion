using System.Globalization;
using System.Text.RegularExpressions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredWeaponNotifyBinding(int GeneratedIndex, int Edge, string Name,
    string Sequence, float BlendIn, float BlendOut, float PlayRate, float StartTime);
public readonly record struct AlsRefactoredWeaponTransitionRequest(AlsFrameIdentity Identity, int QueueOrdinal,
    AlsRefactoredWeaponNotifyBinding Binding)
{
    public string Slot => "Transition";
    public int LoopCount => 1;
    public float BlendOutTriggerTime => 0;
}

/// <summary>Original EventGraph dispatch and standing-idle-only transition requests.
/// Generated notify IDs are scoped to Machine, never global animation IDs.</summary>
public sealed class AlsRefactoredWeaponNotifyProfile
{
    private readonly AlsRefactoredWeaponNotifyBinding[] _bindings;
    public AlsRefactoredWeaponMachineProfile Machine { get; }
    public ReadOnlySpan<AlsRefactoredWeaponNotifyBinding> Bindings => _bindings;
    public AlsRefactoredWeaponNotifyProfile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredWeaponMachineProfile machine)
    {
        if (catalog.IndexDigest != machine.Resources.CatalogDigest) throw new ArgumentException("Foreign weapon notify catalog.");
        Machine = machine; var kind = machine.Resources.Kind; var source = AlsRefactoredWeaponMachineResources.Blueprint(kind);
        var payload = catalog.Read(source); var text = payload.GetProperty("nativeText").GetString()!;
        _bindings = CompileEvents(AlsNativeNestedGraph.Extract(text, source, source + ":EventGraph"), kind);
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => n.GetProperty("compiledNodeIndex").GetInt32());
        foreach (var binding in _bindings)
        {
            var edge = machine.Resources.Edges[binding.Edge];
            var graph = nodes[edge.RuleNode].GetProperty("graph").GetString()!;
            var authored = AlsNativeNestedGraph.Extract(text, source, graph[..graph.LastIndexOf('.')]);
            var start = Regex.Match(authored, "(?m)^   TransitionStart=\\(NotifyName=\"([^\"]+)\"");
            Require(edge.StartNotify == binding.GeneratedIndex && start.Success && start.Groups[1].Value == binding.Name,
                "Authored edge and generated notify identity differ.");
        }
        var settings = catalog.Read("/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default").GetProperty("nativeText").GetString()!;
        var transitions = Regex.Match(settings, "(?m)^   Transitions=\\(([^\\r\\n]+)\\)").Groups[1].Value;
        var side = kind is AlsRefactoredWeaponKind.Bow or AlsRefactoredWeaponKind.Rifle ? "Left" : "Right";
        var sequence = Regex.Match(transitions, "Standing" + side + "Sequence=\"/Script/Engine.AnimSequence'([^']+)'\"").Groups[1].Value;
        var expected = $"/ALS/ALS/Animations/Transitions/A_Als_Stand_Transition_{side}.A_Als_Stand_Transition_{side}";
        Require(sequence == expected, "Original standing transition setting differs.");
        var asset = catalog.Read(sequence); var policy = asset.GetProperty("evaluation");
        Require(asset.GetProperty("class").GetString() == "AnimSequence" && !policy.GetProperty("enableRootMotion").GetBoolean() &&
            policy.GetProperty("additiveType").GetString() == "AAT_RotationOffsetMeshSpace" && policy.GetProperty("sequencePlayLength").GetSingle() > .3f,
            "Unsupported transition sequence policy.");
        for (var i = 0; i < _bindings.Length; i++) _bindings[i] = _bindings[i] with { Sequence = sequence };
    }
    public AlsRefactoredWeaponNotifyBinding Resolve(in AlsOverlayTransitionNotify notify)
    {
        foreach (var binding in _bindings)
            if (binding.GeneratedIndex == notify.GeneratedIndex && binding.Edge == notify.Edge) return binding;
        throw new ArgumentException("Foreign weapon transition notify.");
    }
    public AlsRefactoredWeaponNotifyRuntime CreateRuntime(uint character, uint generation) => new(this, character, generation);

    internal static AlsRefactoredWeaponNotifyBinding[] CompileEvents(string text, AlsRefactoredWeaponKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var graph = new Graph(text, true); var visited = new HashSet<string>();
        var result = new AlsRefactoredWeaponNotifyBinding[2]; var entries = new Dictionary<Node, List<Node>>();
        for (var i = 0; i < 2; i++)
        {
            var name = i == 0 ? "RelaxedToReady" : "ReadyToRelaxed";
            var entry = graph.Nodes.Single(n => n.Kind == "K2Node_Event" && n.Body.Contains(
                "EventReference=(MemberParent=\"/Script/CoreUObject.Class'/Script/Engine.AnimInstance'\",MemberName=\"AnimNotify_" + name + "\")", StringComparison.Ordinal));
            visited.Add(entry.Name);
            var then = entry.Pins.Values.Single(p => p.Name == "then" && p.Output);
            var links = Regex.Matches(then.Links, "(\\w+) (\\w+),"); Require(links.Count == 1, "Ambiguous notify consumer.");
            var call = graph.Named(links[0].Groups[1].Value); visited.Add(call.Name);
            Require(call.Pins[links[0].Groups[2].Value].Name == "execute", "Wrong notify execution pin.");
            var side = kind is AlsRefactoredWeaponKind.Bow or AlsRefactoredWeaponKind.Rifle ? "Left" : "Right";
            graph.Function(call, "PlayTransition" + side + "Animation", "ALS.AlsAnimationInstance");
            Require(call.Pins.Values.Single(p => p.Name == "then").Links == "", "Unexpected notify continuation.");
            var (parent, output) = graph.Follow(call, "self"); graph.Self(parent); visited.Add(parent.Name);
            Require(parent.Kind == "K2Node_CallFunction" && parent.Member == "GetParent" && output.Name == "ReturnValue", "Wrong transition receiver.");
            Require(graph.Literal(call, "bFromStandingIdleOnly") == "true", "Missing standing idle gate.");
            float Number(string pin) => float.Parse(graph.Literal(call, pin), CultureInfo.InvariantCulture);
            var rate = kind == AlsRefactoredWeaponKind.Bow || i == 1 ? 1.5f : 1.75f;
            Require(Number("BlendInDuration") == .2f && Number("BlendOutDuration") == .2f && Number("PlayRate") == rate && Number("StartTime") == .3f,
                "Original weapon transition parameters differ.");
            result[i] = new(i, i == 0 ? 0 : 2, name, "", .2f, .2f, rate, .3f);
            if (!entries.TryGetValue(call, out var events)) entries.Add(call, events = []);
            events.Add(entry);
        }
        foreach (var (call, events) in entries) graph.Links(call, "execute", events.Select(e => (e, "then")).ToArray());
        Require(graph.Nodes.All(n => visited.Contains(n.Name) || n.Kind == "EdGraphNode_Comment" && n.Pins.Count == 0), "Unconsumed weapon EventGraph nodes.");
        return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}

/// <summary>Ordered main-thread notification dispatch requests. This does not emulate the
/// separate last-write-wins worker queue used by direct transition function calls.</summary>
public sealed class AlsRefactoredWeaponNotifyRuntime
{
    private readonly AlsRefactoredWeaponNotifyProfile _profile;
    private readonly uint _character, _generation;
    private AlsRefactoredWeaponTransitionRequest[] _candidate = new AlsRefactoredWeaponTransitionRequest[3], _committed = new AlsRefactoredWeaponTransitionRequest[3];
    private AlsFrameIdentity _identity;
    private bool _prepared;
    private int _count, _committedCount;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public ReadOnlySpan<AlsRefactoredWeaponTransitionRequest> Commands => _prepared ? _candidate.AsSpan(0, _count) : throw new InvalidOperationException("No weapon notify frame.");
    public ReadOnlySpan<AlsRefactoredWeaponTransitionRequest> CommittedCommands => _committed.AsSpan(0, _committedCount);
    internal AlsRefactoredWeaponNotifyRuntime(AlsRefactoredWeaponNotifyProfile profile, uint character, uint generation)
    { ArgumentOutOfRangeException.ThrowIfZero(generation); _profile = profile; _character = character; _generation = generation; }
    public void Prepare(AlsFrameIdentity identity, AlsRefactoredWeaponMachineRuntime machine, string stance, bool moving)
    {
        if (_prepared || identity.CharacterId != _character || identity.SlotGeneration != _generation ||
            CommittedIdentity != default && identity.FrameId <= CommittedIdentity.FrameId || !ReferenceEquals(machine.Profile, _profile.Machine))
            throw new ArgumentException("Foreign weapon notify owner/frame.");
        machine.ValidateCommit(identity.FrameId); var update = machine.Candidate; var count = 0;
        for (var i = 0; i < update.NotifyCount; i++)
        {
            var binding = _profile.Resolve(update.GetNotify(i));
            // The original guard requires the exact Standing tag, including when stance is unset.
            if (!moving && stance == "Als.Stance.Standing") _candidate[count++] = new(identity, i, binding);
        }
        _identity = identity; _count = count; _prepared = true;
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    { if (!_prepared || identity != _identity) throw new ArgumentException("Wrong weapon notify commit."); }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); (_candidate, _committed) = (_committed, _candidate); _committedCount = _count;
        CommittedIdentity = identity; Cancel();
    }
    public void Cancel() { _prepared = false; _count = 0; }
}
