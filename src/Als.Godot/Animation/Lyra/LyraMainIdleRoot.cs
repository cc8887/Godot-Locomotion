namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraMainIdleRootResult(double TurnYaw,int Mode,bool ApplyYaw,double RootYaw,bool BlendingOut);

// Main StateResult8. Its curves come from the previous enclosing Main output,
// independently of the linked Idle provider's own TurnYawWeight feedback.
internal static class LyraMainIdleRoot
{
    public static LyraMainIdleRootResult Update(bool visited,int current,float previousIdleWeight,
        double previousTurnYaw,int mode,double rootYaw,float remaining,float weight)
    {
        if(current is <0 or >11 || !float.IsFinite(previousIdleWeight) || previousIdleWeight is <0 or >1 ||
            !double.IsFinite(previousTurnYaw) || !double.IsFinite(rootYaw) || !float.IsFinite(remaining) || !float.IsFinite(weight))
            throw new ArgumentException("Invalid Main Idle root context.");
        if(!visited)return new(previousTurnYaw,mode,false,rootYaw,false);
        if(previousIdleWeight>0 && current!=0)return new(0,mode,false,rootYaw,true);
        if(Math.Abs((double)weight)<=.0001d)return new(0,2,false,rootYaw,false);
        var turn=(double)remaining/(double)weight;
        return new(turn,2,previousTurnYaw!=0,rootYaw-(turn-previousTurnYaw),false);
    }
}
