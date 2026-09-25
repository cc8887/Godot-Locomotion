using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Candidate-owned CallFunction relevance. The graph visitor calls Enter,
/// applies the returned command to candidate Parent state, visits the source, then
/// calls Leave. No external notifications or Parent mutations are dispatched here.</summary>
public sealed class AlsRefactoredStanceCallbackRuntime
{
    private readonly AlsRefactoredStanceCallback[] _nodes;
    private readonly Dictionary<int, int> _indices;
    private AlsGraphTraversalCounter[] _committed, _candidate;
    private readonly int[] _stack;
    private int _depth;
    private long _frame, _lastFrame = -1;
    private AlsGraphTraversalCounter _update;
    private bool _prepared, _faulted;

    public AlsRefactoredStanceCallbackRuntime(AlsRefactoredStanceCallbacks profile)
    {
        _nodes = profile.Nodes.ToArray();
        _indices = _nodes.Select((n, i) => (n.PropertyIndex, i)).ToDictionary(p => p.PropertyIndex, p => p.i);
        _committed = new AlsGraphTraversalCounter[_nodes.Length];
        _candidate = new AlsGraphTraversalCounter[_nodes.Length];
        _stack = new int[_nodes.Length];
    }
    public void Prepare(long frame, AlsGraphTraversalCounter update, bool initializeInstance = false)
    {
        if (_prepared || frame <= _lastFrame || frame < 0 || !update.HasUpdated) throw new ArgumentException("Invalid callback candidate.");
        if (initializeInstance) Array.Clear(_candidate); else _committed.CopyTo(_candidate, 0);
        _frame = frame; _update = update; _depth = 0; _faulted = false; _prepared = true;
    }
    public AlsRefactoredStanceCallback? Enter(long frame, int propertyIndex)
    {
        Check(frame);
        if (!_indices.TryGetValue(propertyIndex, out var index) || _stack.AsSpan(0, _depth).Contains(index))
        { _faulted = true; throw new ArgumentException("Unknown or recursive callback node."); }
        var node = _nodes[index];
        var previous = _candidate[index];
        var relevant = !previous.HasUpdated || !previous.WasSynchronizedCounter(_update);
        _stack[_depth++] = index;
        return !node.OnBecomeRelevant || relevant ? node : null;
    }
    public void Leave(long frame, int propertyIndex)
    {
        Check(frame);
        if (_depth == 0 || _nodes[_stack[_depth - 1]].PropertyIndex != propertyIndex)
        { _faulted = true; throw new ArgumentException("Callback recursion order differs."); }
        // UE synchronizes after Source.Update, not at callback entry.
        _candidate[_stack[--_depth]] = _update;
    }
    public void ValidateCommit(long frame)
    {
        Check(frame);
        if (_depth != 0) throw new InvalidOperationException("Callback source traversal is incomplete.");
    }
    public void Commit(long frame)
    {
        ValidateCommit(frame); (_committed, _candidate) = (_candidate, _committed);
        _lastFrame = frame; _prepared = false;
    }
    public void Cancel() { _prepared = false; _faulted = false; _depth = 0; }
    private void Check(long frame)
    {
        if (!_prepared || _faulted || frame != _frame) throw new InvalidOperationException("No valid callback candidate for frame.");
    }
}
