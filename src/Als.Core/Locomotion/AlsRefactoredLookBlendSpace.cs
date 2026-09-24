namespace GodotAls.Core.Locomotion;

// The authored Look asset uses two 1D segments, not V4's five-point grid.
// Its compiler validates the segment indices/vertices and disabled smoothing.
public static class AlsRefactoredLookBlendSpace
{
    public static int Evaluate(float pitch,Span<AlsAimGridVertex> output)
    {
        if(!float.IsFinite(pitch)||output.Length<2)throw new ArgumentException("Invalid Look blend input.");
        // GetNormalizedBlendInput uses double, GetSamples1D casts its coordinate to float.
        var position=(float)((System.Math.Clamp((double)pitch,-90,90)+90)/180);
        var left=position<.5f;
        var fraction=System.Math.Clamp((position-(left?0:.5f))/.5f,0,1);
        var first=new AlsAimGridVertex(left?1:0,1-fraction);
        var second=new AlsAimGridVertex(left?0:2,fraction);
        // Native small-array sort also reverses equal weights.
        if(first.Weight<=second.Weight)(first,second)=(second,first);
        if(second.Weight<AlsPoseBlender.WeightThreshold)
        {output[0]=first with {Weight=1};output[1]=default;return 1;}
        var total=first.Weight+second.Weight;
        output[0]=first with {Weight=first.Weight/total};output[1]=second with {Weight=second.Weight/total};return 2;
    }
}
