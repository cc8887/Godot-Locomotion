namespace GodotAls.Core.Locomotion;

public readonly record struct AlsSprintInputState(bool Initialized,float History,float Alpha,bool FirstRelevant,bool ImpulseRelevant)
{
    public float FirstWeight=>ImpulseRelevant ? FirstRelevant ? 1-Alpha : 0 : 1;
    public float ImpulseWeight=>ImpulseRelevant ? FirstRelevant ? Alpha : 1 : 0;
}
public readonly record struct AlsSprintInputUpdate(AlsSprintInputState State,bool Visited,bool ResetFirst,bool ResetImpulse);

public static class AlsSprintInput
{
    public static AlsSprintInputUpdate Prepare(AlsSprintInputState previous,bool initialize,bool visit,
        float acceleration,float delta,in AlsOverlayAlphaPolicy policy)
    {
        var state=initialize ? default : previous;
        if(!visit)return new(state,false,false,false);
        var initialized=state.Initialized;var history=state.History;
        var alpha=AlsOverlayPoseWeights.Alpha(acceleration,policy,delta,ref initialized,ref history);
        var first=alpha<1-AlsPoseBlender.WeightThreshold;var second=alpha>AlsPoseBlender.WeightThreshold;
        return new(new(initialized,history,alpha,first,second),true,first&&!state.FirstRelevant,second&&!state.ImpulseRelevant);
    }
}
