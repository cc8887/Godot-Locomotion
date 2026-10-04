namespace GodotAls.Core.Animation;

public enum AlsLinkedLayerExecutionPath
{
    LinkedRoot,
    FirstInput,
    ReferencePose
}

/// <summary>
/// Routing from UE FAnimNode_LinkedAnimGraph's four traversal phases. Binding identity,
/// input property propagation and inertial blend requests belong to the caller.
/// A missing root forwards the entire first input's output context. With no
/// inputs, only the pose resets; curves and attributes already in output remain.
/// The reset callback must honor the output context's additive flag.
/// </summary>
public static class AlsLinkedLayerExecution
{
    // Initialize/CacheBones enter the valid linked root first, then always
    // traverse every input. LinkedInputPose only synchronizes debug counters
    // in these phases; it does not initialize/cache its incoming pose. Dynamic
    // re-link initializes only the subgraph, through the separate entrypoint.
    public static void InitializeSubGraph(bool targetValid, bool rootValid, Action linkedRoot)
    {
        if (targetValid && rootValid) linkedRoot();
    }

    public static void CacheBonesSubGraph(bool targetValid, bool rootValid, Action linkedRoot)
    {
        if (targetValid && rootValid) linkedRoot();
    }

    public static void Initialize(bool targetValid, bool rootValid, ReadOnlySpan<Action> inputs, Action linkedRoot)
    {
        InitializeSubGraph(targetValid, rootValid, linkedRoot);
        foreach (var input in inputs) input();
    }

    public static void CacheBones(bool targetValid, bool rootValid, ReadOnlySpan<Action> inputs, Action linkedRoot)
    {
        CacheBonesSubGraph(targetValid, rootValid, linkedRoot);
        foreach (var input in inputs) input();
    }

    public static AlsLinkedLayerExecutionPath Select(bool targetValid, bool rootValid, int inputCount)
    {
        if (inputCount < 0) throw new ArgumentOutOfRangeException(nameof(inputCount));
        return targetValid && rootValid ? AlsLinkedLayerExecutionPath.LinkedRoot
            : inputCount > 0 ? AlsLinkedLayerExecutionPath.FirstInput : AlsLinkedLayerExecutionPath.ReferencePose;
    }

    public static void Update(bool targetValid, bool rootValid, ReadOnlySpan<Action> inputs, Action linkedRoot)
    {
        switch (Select(targetValid, rootValid, inputs.Length))
        {
            case AlsLinkedLayerExecutionPath.LinkedRoot: linkedRoot(); break;
            case AlsLinkedLayerExecutionPath.FirstInput: inputs[0](); break;
        }
    }

    public static void Evaluate<T>(bool targetValid, bool rootValid, ReadOnlySpan<Action<T>> inputs,
        Action<T> linkedRoot, Action<T> resetPose, T output) where T : class
    {
        switch (Select(targetValid, rootValid, inputs.Length))
        {
            case AlsLinkedLayerExecutionPath.LinkedRoot: linkedRoot(output); break;
            case AlsLinkedLayerExecutionPath.FirstInput: inputs[0](output); break;
            default: resetPose(output); break;
        }
    }
}
