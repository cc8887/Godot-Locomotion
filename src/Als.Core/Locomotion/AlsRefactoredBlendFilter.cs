using System.Numerics;
using System.Runtime.CompilerServices;

namespace GodotAls.Core.Locomotion;

internal struct AlsNativeFilterSample { public float Input,Time; }
[InlineArray(260)]
internal struct AlsNativeFilterHistory { private AlsNativeFilterSample _element; }
public struct AlsRefactoredBlendAxisState
{
    internal AlsNativeFilterHistory History;
    internal int Slots,Next;
    internal float Time;
    public float Output { get; internal set; }
}
public struct AlsRefactoredBlendFilterState
{
    internal AlsRefactoredBlendAxisState X,Y;
    public readonly Vector2 Output=>new(X.Output,Y.Output);
}

/// <summary>UE cubic time-window filter with native slot-order accumulation.
/// Caller owns candidate history. Capacity is explicit and overflow never evicts live samples.</summary>
public static class AlsRefactoredBlendFilter
{
    public static AlsRefactoredBlendFilterState Advance(in AlsRefactoredBlendFilterState previous,Vector2 input,float delta,Vector2 windows)
    {
        if(!float.IsFinite(input.X)||!float.IsFinite(input.Y)||!float.IsFinite(delta)||delta<0||
            !float.IsFinite(windows.X)||!float.IsFinite(windows.Y)||windows.X<0||windows.Y<0)
            throw new ArgumentException("Invalid original blend filter input.");
        var next=previous;
        AdvanceAxis(ref next.X,input.X,delta,windows.X);AdvanceAxis(ref next.Y,input.Y,delta,windows.Y);
        return next;
    }
    private static void AdvanceAxis(ref AlsRefactoredBlendAxisState state,float input,float delta,float window)
    {
        if(window<=0){state.Output=input;return;}
        if(delta<=.0001f)return;
        state.Time+=delta;
        if(!float.IsFinite(state.Time))throw new InvalidOperationException("Blend filter time overflow.");
        if(state.Slots==0){state.Slots=10;state.Next=0;}
        else for(var i=0;i<state.Slots;i++)if(state.Time-state.History[i].Time>window)state.History[i].Time=0;
        var index=-1;
        for(var offset=0;offset<state.Slots;offset++)
        {
            var candidate=(state.Next+offset)%state.Slots;
            if(state.History[candidate].Time<=0){index=candidate;break;}
        }
        if(index<0)
        {
            if(state.Slots==260)throw new InvalidOperationException("Blend filter history capacity exceeded.");
            index=state.Slots;state.Slots+=5;
        }
        state.History[index]=new(){Input=input,Time=state.Time};
        float coefficients=0,inputs=0;
        for(var i=0;i<state.Slots;i++)
        {
            var sample=state.History[i];var diff=state.Time-sample.Time;
            if(sample.Time<=0||diff>window)continue;
            var ratio=diff/window;var coefficient=1-ratio*ratio*ratio;
            if(coefficient<=0)continue;
            coefficients+=coefficient;inputs+=coefficient*sample.Input;
        }
        state.Output=coefficients>0?inputs/coefficients:0;
        if(!float.IsFinite(state.Output))throw new InvalidOperationException("Blend filter output overflow.");
        state.Next=(index+1)%state.Slots;
    }
}
