namespace GodotAls.Core.Locomotion;

/// <summary>A capability for the innermost payload scope of one cache instance.</summary>
public readonly record struct AlsCachedPoseScope(ulong Serial, int Depth)
{
    internal AlsScopedPoseCache? Owner { get; init; }
}

/// <summary>
/// Shared scoped evaluation control. Payload storage, source evaluation and node
/// history belong to the caller; scopes neither tick sources nor own animation clocks.
/// </summary>
public sealed class AlsScopedPoseCache
{
    private readonly int _nodeCount;
    private readonly bool[] _present, _evaluating;
    private readonly int[] _evaluations;
    private readonly ulong[] _serials;
    private ulong _serial;
    public int Depth { get; private set; }
    public bool IsFaulted { get; private set; }
    public int SourceEvaluations { get; private set; }

    public AlsScopedPoseCache(int nodeCount, int scopeCapacity = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scopeCapacity);
        _nodeCount = nodeCount;
        var slots = checked(nodeCount * scopeCapacity);
        _present = new bool[slots]; _evaluating = new bool[slots];
        _evaluations = new int[nodeCount]; _serials = new ulong[scopeCapacity];
    }

    public void Reset()
    {
        if (Depth != 0) throw new InvalidOperationException("Cannot reset an open pose-cache scope.");
        Array.Clear(_present); Array.Clear(_evaluating); Array.Clear(_evaluations);
        IsFaulted = false; SourceEvaluations = 0;
        // Serials survive reset: a new scope must reject previously returned views.
    }

    public void ThrowIfFaulted()
    {
        if (IsFaulted) throw new InvalidOperationException("Pose-cache payload evaluation is faulted.");
    }

    public AlsCachedPoseScope PushScope()
    {
        ThrowIfFaulted();
        if (Depth == _serials.Length || _serial == ulong.MaxValue)
            throw new InvalidOperationException("Pose-cache scope capacity exceeded.");
        var offset = Depth * _nodeCount;
        Array.Clear(_present, offset, _nodeCount);
        Array.Clear(_evaluating, offset, _nodeCount);
        _serials[Depth++] = ++_serial;
        return new(_serial, Depth) { Owner = this };
    }

    public void ValidateScope(AlsCachedPoseScope scope) => ValidateLifetime(scope, innermost: true);

    public void PopScope(AlsCachedPoseScope scope)
    {
        ValidateScope(scope);
        if (_evaluating.AsSpan((Depth - 1) * _nodeCount, _nodeCount).Contains(true))
            throw new InvalidOperationException("Cannot close a scope during source evaluation.");
        Depth--;
    }

    public int Slot(AlsCachedPoseScope scope, int node)
    {
        ThrowIfFaulted();
        return Resolve(scope, node, innermost: true);
    }

    public bool BeginEvaluation(AlsCachedPoseScope scope, int node, bool counterMatches)
    {
        var slot = Slot(scope, node);
        if (_evaluating[slot])
        {
            IsFaulted = true;
            throw new InvalidOperationException("Recursive pose-cache source.");
        }
        if (counterMatches && _present[slot]) return false;
        _present[slot] = false; _evaluating[slot] = true;
        return true;
    }

    public void CompleteEvaluation(AlsCachedPoseScope scope, int node)
    {
        var slot = Slot(scope, node);
        if (!_evaluating[slot] || _present[slot]) throw new InvalidOperationException("Pose-cache source is absent or already completed.");
        _present[slot] = true; _evaluations[node]++; SourceEvaluations++;
    }

    // Finally must release the source even when it left an inner scope open.
    // Failed scopes may be unwound, but cannot publish or start another evaluation.
    public void EndEvaluation(AlsCachedPoseScope scope, int node, bool failed)
    {
        var slot = Resolve(scope, node, innermost: false);
        if (!_evaluating[slot]) throw new InvalidOperationException("Pose-cache source is not evaluating.");
        _evaluating[slot] = false;
        if (failed) { IsFaulted = true; _present[slot] = false; }
        else if (!_present[slot])
        {
            IsFaulted = true;
            throw new InvalidOperationException("Pose-cache source ended without completing its payload.");
        }
    }

    public void RequireReadable(AlsCachedPoseScope scope, int node)
    {
        var slot = Slot(scope, node);
        if (!_present[slot] || _evaluating[slot])
            throw new InvalidOperationException("Cached pose is absent or incomplete.");
    }

    public int Evaluations(int node)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        if (node >= _nodeCount) throw new ArgumentOutOfRangeException(nameof(node));
        return _evaluations[node];
    }

    private int Resolve(AlsCachedPoseScope scope, int node, bool innermost)
    {
        ValidateLifetime(scope, innermost);
        if ((uint)node >= _nodeCount) throw new ArgumentOutOfRangeException(nameof(node));
        return (scope.Depth - 1) * _nodeCount + node;
    }

    private void ValidateLifetime(AlsCachedPoseScope scope, bool innermost)
    {
        if (!ReferenceEquals(scope.Owner, this) || scope.Depth <= 0 || scope.Depth > Depth ||
            innermost && scope.Depth != Depth || scope.Serial != _serials[scope.Depth - 1])
            throw new InvalidOperationException("Pose-cache scope is stale, foreign or not innermost.");
    }
}
