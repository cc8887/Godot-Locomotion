using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Frozen original machine topology/rules; pose and notify consumers are separate.</summary>
public sealed class AlsRefactoredWeaponMachineProfile
{
    public AlsRefactoredWeaponMachineResources Resources { get; }
    public AlsOverlayMachineDefinition Definition { get; }
    public AlsRefactoredWeaponMachineProfile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredWeaponMachineResources resources)
    {
        var rules = AlsRefactoredWeaponRuleCompiler.Compile(catalog, resources);
        var kind = resources.Kind switch
        {
            AlsRefactoredWeaponKind.Bow => AlsOverlayMachineKind.Bow,
            AlsRefactoredWeaponKind.PistolOneHanded => AlsOverlayMachineKind.Pistol1H,
            AlsRefactoredWeaponKind.PistolTwoHanded => AlsOverlayMachineKind.Pistol2H,
            AlsRefactoredWeaponKind.Rifle => AlsOverlayMachineKind.Rifle,
            _ => throw new ArgumentOutOfRangeException(nameof(resources))
        };
        var payload = catalog.Read(AlsRefactoredWeaponMachineResources.Blueprint(resources.Kind));
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => n.GetProperty("compiledNodeIndex").GetInt32());
        // Entry/reset policy is part of the baked resource validation. No state callbacks
        // can be silently omitted by the shared machine update implementation.
        foreach (var index in resources.States.ToArray().Select(s => s.RootNode).Append(resources.CompiledNode))
        {
            var runtime = nodes[index].GetProperty("runtime");
            foreach (var callback in runtime.EnumerateObject().Where(p => p.Name.EndsWith("Function", StringComparison.Ordinal)))
                if (callback.Value.GetProperty("functionName").GetString() != "None")
                    throw new ArgumentException("Weapon state callbacks require an executor.");
        }
        var states = resources.States.ToArray().Select(s => new AlsOverlayStateDefinition(s.Name, s.RootNode, -1, -1,
            s.Exits.ToArray(), s.Players.ToArray())).ToArray();
        var edges = resources.Edges.ToArray().Select((e, i) => new AlsOverlayEdgeDefinition(
            nodes[e.RuleNode].GetProperty("path").GetString()!, e.From, e.To, e.RuleNode,
            Array.IndexOf(states[e.From].Exits.ToArray(), i), true, default, e.Seconds, e.Blend, e.Curve,
            e.QuickFeet, e.StartNotify, false) { RefactoredRule = rules[i] }).ToArray();
        Definition = new(kind, resources.CompiledNode, 0, 0, 3, true, states, edges); Resources = resources;
    }
    public AlsRefactoredWeaponMachineRuntime CreateRuntime() => new(this);
}

/// <summary>Exclusive transactional update owner. Notify indices remain local to this profile;
/// they must be bound and dispatched after the encompassing character frame commits.</summary>
public sealed class AlsRefactoredWeaponMachineRuntime
{
    private readonly AlsOverlayStateMachine _machine;
    private AlsOverlayMachineState _state;
    private AlsOverlayMachineUpdate _next;
    private bool _prepared;
    private long _frame, _committed = -1;
    public AlsRefactoredWeaponMachineProfile Profile { get; }
    public AlsOverlayMachineState CommittedState => _state;
    public AlsOverlayMachineUpdate Candidate => _prepared ? _next : throw new InvalidOperationException("No weapon update prepared.");
    internal AlsRefactoredWeaponMachineRuntime(AlsRefactoredWeaponMachineProfile profile)
    {
        Profile = profile;
        _machine = new(profile.Definition, profile.Resources.Curves, profile.Resources.QuickFeet);
    }
    public void Prepare(long frame, in AlsRefactoredWeaponRuleInput input, float delta, float weight = 1,
        bool inactive = false, bool reinitialize = false, AlsGraphTraversalCounter? updateCounter = null)
    {
        if (_prepared || frame < 0 || frame <= _committed) throw new ArgumentException("Invalid weapon frame.");
        var prior = reinitialize ? default : _state;
        var next = _machine.UpdateRefactored(prior, input, weight, delta, frame, inactive, updateCounter);
        _next = next; _frame = frame; _prepared = true;
    }
    public float CandidateBoneWeight(int state, int bone)
    {
        if (!_prepared) throw new InvalidOperationException("No weapon update prepared.");
        return _machine.BoneStateWeight(_next.State, state, bone);
    }
    public void ValidateCommit(long frame)
    { if (!_prepared || frame != _frame) throw new ArgumentException("Wrong weapon commit frame."); }
    public void Commit(long frame) { ValidateCommit(frame); _state = _next.State; _committed = frame; Cancel(); }
    public void Cancel() { _prepared = false; }
}
