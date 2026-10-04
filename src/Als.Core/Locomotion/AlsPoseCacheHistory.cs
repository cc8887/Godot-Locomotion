namespace GodotAls.Core.Locomotion;

public readonly record struct AlsPoseCacheNodeHistory(int Node,
    AlsGraphTraversalCounter Initialization,AlsGraphTraversalCounter Bones,
    AlsGraphTraversalCounter Evaluation,float GlobalWeight);

// Shared by the existing ALS pose evaluator and graph-instance lifecycle.
// Pose memory and traversal scopes remain in AlsPoseCacheEvaluation/Traversal.
public static class AlsPoseCacheHistory
{
    public static bool TryInitialize(ref AlsPoseCacheNodeHistory history,AlsGraphTraversalCounter counter)
    {
        if(history.Initialization.MatchesCounter(counter))return false;
        history=history with{Initialization=counter};return true;
    }
    public static bool TryCacheBones(ref AlsPoseCacheNodeHistory history,AlsGraphTraversalCounter counter)
    {
        if(history.Bones.MatchesAll(counter))return false;
        history=history with{Bones=counter,Evaluation=default};return true;
    }
    public static void RecordEvaluation(ref AlsPoseCacheNodeHistory history,AlsGraphTraversalCounter counter)
        =>history=history with{Evaluation=counter};
}
