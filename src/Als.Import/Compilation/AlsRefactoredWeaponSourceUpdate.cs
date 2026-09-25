using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredWeaponPoseInput(float Walking, float Running, float Sprinting,
    float Standing, float Crouching, float InAir, float Prediction, float SprintAcceleration, Vector4 Velocity)
{
    internal float Read(string path) => path switch
    {
        "GetParent.PoseState.GaitWalkingAmount" => Walking, "GetParent.PoseState.GaitRunningAmount" => Running,
        "GetParent.PoseState.GaitSprintingAmount" => Sprinting, "GetParent.PoseState.StandingAmount" => Standing,
        "GetParent.PoseState.CrouchingAmount" => Crouching, "GetParent.PoseState.InAirAmount" => InAir,
        "GetParent.InAirState.GroundPredictionAmount" => Prediction, "GetParent.StandingState.SprintAccelerationAmount" => SprintAcceleration,
        "GetParent.GroundedState.VelocityBlend.ForwardAmount" => Velocity.X, "GetParent.GroundedState.VelocityBlend.BackwardAmount" => Velocity.Y,
        "GetParent.GroundedState.VelocityBlend.LeftAmount" => Velocity.Z, "GetParent.GroundedState.VelocityBlend.RightAmount" => Velocity.W,
        _ => throw new ArgumentException("Unsupported weapon source input: " + path)
    };
    internal void Validate()
    {
        ReadOnlySpan<float> values = stackalloc float[] { Walking, Running, Sprinting, Standing, Crouching, InAir, Prediction, SprintAcceleration, Velocity.X, Velocity.Y, Velocity.Z, Velocity.W };
        foreach (var v in values)
            if (!float.IsFinite(v) || v is < 0 or > 1) throw new ArgumentException("Invalid weapon pose input.");
    }
}

public sealed class AlsRefactoredWeaponSourceProfile
{
    internal sealed record Node(string Kind, int[] Children, string[] Bindings, float[] Constants, AlsOverlayAlphaPolicy Alpha, bool Reset, int Player);
    internal readonly Node?[] Nodes;
    internal readonly int[] Roots;
    public AlsRefactoredWeaponMachineProfile Machine { get; }
    public AlsRefactoredWeaponPlayers Players { get; }
    public AlsRefactoredWeaponSourceProfile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredWeaponMachineProfile machine)
    {
        Machine = machine; Players = new(catalog, machine.Resources);
        var source = AlsRefactoredWeaponMachineResources.Blueprint(machine.Resources.Kind); var payload = catalog.Read(source);
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        var compiled = nodes.Values.ToDictionary(n => n.GetProperty("compiledNodeIndex").GetInt32());
        Nodes = new Node[nodes.Count]; Roots = new int[3];
        for (var s = 0; s < 3; s++)
        {
            Roots[s] = compiled[machine.Resources.States[s].RootNode].GetProperty("propertyIndex").GetInt32();
            var path = nodes[Roots[s]].GetProperty("graph").GetString()!;
            var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, source, path), true);
            var visiting = new HashSet<int>();
            Compile(Roots[s]);
            if (nodes.Values.Any(n => n.GetProperty("graph").GetString() == path && Nodes[n.GetProperty("propertyIndex").GetInt32()] is null))
                throw new ArgumentException("Unconsumed weapon state node.");
            void Compile(int id)
            {
                if (!visiting.Add(id)) throw new ArgumentException("Weapon state graph is not a tree.");
                var item = nodes[id]; var kind = item.GetProperty("class").GetString()!; var runtime = item.GetProperty("runtime");
                if (item.GetProperty("graph").GetString() != path) throw new ArgumentException("Foreign weapon state link.");
                var authored = graph.Named(item.GetProperty("path").GetString()!.Split('.')[^1]);
                if (authored.Kind != kind) throw new ArgumentException("Weapon graph type differs.");
                var properties = item.GetProperty("authoredProperties"); var policy = properties.GetProperty(kind == "AnimGraphNode_TwoWayBlend" ? "BlendNode" : "Node");
                foreach (var p in new[] { runtime, policy }) foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
                    Expect(p.GetProperty(cb), new { functionName = "None" });
                var links = new List<int>(); var bindings = new List<string>(); var constants = new List<float>();
                var alpha = default(AlsOverlayAlphaPolicy); var reset = false;
                void Link(string field, string pin, int index = -1)
                {
                    var link = runtime.GetProperty(field); if (index >= 0) link = link[index];
                    var child = link.GetProperty("linkId").GetInt32(); Expect(link, new { sourceLinkId = id });
                    if (graph.FollowReroutes(authored, pin).Item1.Name != nodes[child].GetProperty("path").GetString()!.Split('.')[^1])
                        throw new ArgumentException("Authored weapon state link differs.");
                    links.Add(child); Compile(child);
                }
                void Value(string name, float constant)
                {
                    var match = Regex.Matches(authored.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                        .Cast<Match>().Where(m => m.Groups[1].Value == name).ToArray();
                    if (match.Length > 1) throw new ArgumentException("Ambiguous weapon binding.");
                    var binding = match.Length == 0 ? "" : string.Join(".", Regex.Matches(match[0].Groups[2].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value));
                    if (binding != "") _ = default(AlsRefactoredWeaponPoseInput).Read(binding);
                    else if (authored.Pins.Values.Any(p => p.Name == name && !p.Output))
                        constant = float.Parse(graph.Literal(authored, name), CultureInfo.InvariantCulture);
                    bindings.Add(binding); constants.Add(constant);
                }
                if (kind == "AnimGraphNode_StateResult") Link("result", "Result");
                else if (kind is "AnimGraphNode_ApplyAdditive" or "AnimGraphNode_ApplyMeshSpaceAdditive" or "AnimGraphNode_TwoWayBlend")
                {
                    foreach (var p in new[] { runtime, policy }) { Expect(p, new { alphaInputType = "Float" }); Scale(p.GetProperty("alphaScaleBias")); }
                    if (kind == "AnimGraphNode_TwoWayBlend")
                    {
                        foreach (var p in new[] { runtime, policy }) Expect(p, new { bAlwaysUpdateChildren = false });
                        reset = runtime.GetProperty("bResetChildOnActivation").GetBoolean(); Expect(policy, new { bResetChildOnActivation = reset });
                        Link("a", "A"); Link("b", "B");
                    }
                    else { foreach (var p in new[] { runtime, policy }) Expect(p, new { lODThreshold = -1 }); Link("base", "Base"); Link("additive", "Additive"); }
                    Value("Alpha", runtime.GetProperty("alpha").GetSingle());
                    var clamp = runtime.GetProperty("alphaScaleBiasClamp");
                    if (clamp.GetRawText() != policy.GetProperty("alphaScaleBiasClamp").GetRawText()) throw new ArgumentException("Weapon alpha policies differ.");
                    float F(string key) => clamp.GetProperty(key).GetSingle(); bool B(string key) => clamp.GetProperty(key).GetBoolean();
                    alpha = new(F("scale"), F("bias"), B("bClampResult"), F("clampMin"), F("clampMax"), B("bInterpResult"), F("interpSpeedIncreasing"), F("interpSpeedDecreasing"),
                        B("bMapRange"), clamp.GetProperty("inRange").GetProperty("min").GetSingle(), clamp.GetProperty("inRange").GetProperty("max").GetSingle(),
                        clamp.GetProperty("outRange").GetProperty("min").GetSingle(), clamp.GetProperty("outRange").GetProperty("max").GetSingle());
                }
                else if (kind == "AnimGraphNode_MultiWayBlend")
                {
                    foreach (var p in new[] { runtime, policy }) { Expect(p, new { bNormalizeAlpha = true, bAdditiveNode = false }); Scale(p.GetProperty("alphaScaleBias")); }
                    for (var i = 0; i < runtime.GetProperty("poses").GetArrayLength(); i++)
                    { Link("poses", "Poses_" + i, i); Value("DesiredAlphas_" + i, runtime.GetProperty("desiredAlphas")[i].GetSingle()); }
                }
                else if (kind is not ("AnimGraphNode_SequencePlayer" or "AnimGraphNode_SequenceEvaluator")) throw new ArgumentException("Unsupported weapon source update node: " + kind);
                var player = Array.FindIndex(Players.Players.ToArray(), p => p.PropertyIndex == id);
                Nodes[id] = new(kind, links.ToArray(), bindings.ToArray(), constants.ToArray(), alpha, reset, player);
            }
        }
    }
    public AlsRefactoredWeaponSourceRuntime CreateRuntime(int firstPlayer) => new(this, firstPlayer);
}

public sealed class AlsRefactoredWeaponSourceRuntime
{
    private struct State { public bool Initialized, A, B; public float History; }
    private readonly AlsRefactoredWeaponSourceProfile _profile; private readonly int _first;
    private State[] _states, _next; private bool[] _reset, _nextReset;
    private readonly AlsRefactoredSourcePlayerInput[] _inputs; private int _count;
    private AlsRefactoredWeaponMachineRuntime? _owner;
    private bool _prepared; private long _frame, _committed = -1;
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs => _prepared ? _inputs.AsSpan(0, _count) : throw new InvalidOperationException("No weapon source frame.");
    internal AlsRefactoredWeaponSourceRuntime(AlsRefactoredWeaponSourceProfile profile, int firstPlayer)
    {
        if (firstPlayer < 0 || firstPlayer > int.MaxValue - profile.Players.Players.Length) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
        _profile = profile; _first = firstPlayer; _states = new State[profile.Nodes.Length]; _next = new State[_states.Length];
        _reset = new bool[profile.Players.Players.Length]; _nextReset = new bool[_reset.Length]; _inputs = new AlsRefactoredSourcePlayerInput[_reset.Length];
    }
    public void Prepare(long frame, AlsRefactoredWeaponMachineRuntime machine, AlsRefactoredWeaponPoseInput input, float delta)
    {
        if (_prepared || frame < 0 || frame <= _committed || !ReferenceEquals(machine.Profile, _profile.Machine) ||
            _owner is not null && !ReferenceEquals(machine, _owner) || !float.IsFinite(delta) || delta < 0) throw new ArgumentException("Invalid weapon source frame.");
        machine.ValidateCommit(frame); input.Validate(); _states.CopyTo(_next, 0); _reset.CopyTo(_nextReset, 0); _count = 0;
        var update = machine.Candidate;
        for (var i = 0; i < update.EntryCount; i++) { var entry = update.GetEntry(i); if (entry.Initialize) Initialize(_profile.Roots[entry.State]); }
        for (var i = 0; i < update.UpdateCount; i++) { var child = update.GetUpdate(i); Update(_profile.Roots[child.State], child.Weight); }
        _owner ??= machine; _frame = frame; _prepared = true;
        void Initialize(int id)
        {
            _next[id] = default; var node = _profile.Nodes[id]!;
            if (node.Player >= 0) _nextReset[node.Player] = true;
            foreach (var child in node.Children) Initialize(child);
        }
        void Update(int id, float weight)
        {
            var node = _profile.Nodes[id]!; ref var state = ref _next[id];
            if (node.Player >= 0)
            {
                _inputs[_count++] = new(_first + node.Player, default, _profile.Players.Players[node.Player].PlayRate, weight, _nextReset[node.Player]);
                _nextReset[node.Player] = false; return;
            }
            if (node.Children.Length == 0) return;
            if (node.Children.Length == 1) { Update(node.Children[0], weight); return; }
            float Value(int i) => node.Bindings[i] == "" ? node.Constants[i] : input.Read(node.Bindings[i]);
            if (node.Kind == "AnimGraphNode_MultiWayBlend")
            {
                var desired = Vector4.Zero; for (var i = 0; i < node.Constants.Length; i++) desired[i] = Value(i);
                var weights = AlsOverlayPoseWeights.MultiWay(desired, node.Children.Length);
                for (var i = 0; i < node.Children.Length; i++) if (weights[i] > AlsPoseBlender.WeightThreshold) Update(node.Children[i], weight * weights[i]);
                return;
            }
            var alpha = AlsOverlayPoseWeights.Alpha(Value(0), node.Alpha, delta, ref state.Initialized, ref state.History);
            if (node.Kind == "AnimGraphNode_TwoWayBlend")
            {
                var a = alpha < 1 - AlsPoseBlender.WeightThreshold; var b = alpha > AlsPoseBlender.WeightThreshold;
                if (node.Reset) { if (a && !state.A) Initialize(node.Children[0]); if (b && !state.B) Initialize(node.Children[1]); }
                state.A = a; state.B = b;
                if (a) Update(node.Children[0], weight * (b ? 1 - alpha : 1)); if (b) Update(node.Children[1], weight * (a ? alpha : 1));
            }
            else { Update(node.Children[0], weight); if (alpha > AlsPoseBlender.WeightThreshold) Update(node.Children[1], weight * alpha); }
        }
    }
    public void ValidateCommit(long frame) { if (!_prepared || frame != _frame) throw new ArgumentException("Wrong weapon source commit."); }
    public void Commit(long frame) { ValidateCommit(frame); (_states, _next) = (_next, _states); (_reset, _nextReset) = (_nextReset, _reset); _committed = frame; Cancel(); }
    public void Cancel() { _prepared = false; _count = 0; }
}
