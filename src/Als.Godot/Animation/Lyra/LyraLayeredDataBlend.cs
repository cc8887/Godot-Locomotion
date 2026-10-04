using GodotAls.Core.Animation;
using GodotAls.Core.Curves;

namespace GodotAls.Animation.Lyra;

internal static class LyraLayeredDataBlend
{
    // Resource/graph policy adaptation; the data operators are shared Core.
    public static LyraAttributeSample BlendIntegerUniform(LyraAttributeSample basis,LyraAttributeSample layer,
        float layerWeight,bool useOverride)
        => AlsIntegerAnimationAttribute.BlendUniform(basis,layer,layerWeight,useOverride);

    public static LyraCurveSample OverrideCurve(LyraCurveSample basis, LyraCurveSample layer, int curveSource)
        => AlsAnimationCurveSample.Override(basis,layer,clearBaseWhenChildMissing:curveSource==0);

    public static LyraAttributeSample BlendInteger(LyraAttributeSample basis, LyraAttributeSample layer,
        float layerWeight, bool useOverride)
        => AlsIntegerAnimationAttribute.Blend(basis,layer,layerWeight,useOverride);
}
