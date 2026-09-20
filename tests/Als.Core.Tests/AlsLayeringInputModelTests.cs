using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsLayeringInputModelTests
{
    private static readonly AlsFrameIdentity Current = new(12, 4, 3);
    private static readonly AlsFrameIdentity Previous = new(11, 4, 3);
    private static string[] Names() => Enum.GetNames<AlsLayeringCurve>();
    private static AlsLayeringInputModel Model() => new(Names());

    [Fact]
    public void ColdUpdateReadsMissingCurvesAsZeroWithoutPretendingThereWasAFrame()
    {
        var result = Model().Evaluate(Current, default, [], []);
        Assert.Equal(default, result.FeedbackIdentity);
        Assert.Equal(1, result.EnableAimOffset);
        Assert.Equal(1, result.LeftArmMeshSpace); Assert.Equal(1, result.RightArmMeshSpace);
        Assert.Equal(0, result.BasePoseNormal); Assert.Equal(0, result.BasePoseCrouching);
        Assert.Equal(0, result.LeftHandIk); Assert.Equal(0, result.RightHandIk);
    }

    [Fact]
    public void MapsDistinctFinalCurvesAndPerSideProductsWithoutSharingArms()
    {
        float[] values = [.25f, .7f, .3f, .2f, .4f, .6f, .8f, .5f, .9f, .75f, .5f, .25f, .8f, .99f, 1];
        var result = Model().Evaluate(Current, Previous, Names(), values.Select(v => new AlsInertialCurve(v)).ToArray());
        Assert.Equal(Current, result.Identity); Assert.Equal(Previous, result.FeedbackIdentity);
        Assert.Equal(.75, result.EnableAimOffset);
        Assert.Equal((double).7f, result.BasePoseNormal); Assert.Equal((double).3f, result.BasePoseCrouching);
        Assert.Equal((double).2f, result.SpineAdditive); Assert.Equal((double).4f, result.HeadAdditive);
        Assert.Equal((double).6f, result.LeftArmAdditive); Assert.Equal((double).8f, result.RightArmAdditive);
        Assert.Equal((double).5f, result.LeftHand); Assert.Equal((double).9f, result.RightHand);
        Assert.Equal(.375, result.LeftHandIk); Assert.Equal((double).25f * .8f, result.RightHandIk);
        Assert.Equal((double).99f, result.LeftArmLocalSpace); Assert.Equal(1, result.LeftArmMeshSpace);
        Assert.Equal(1, result.RightArmLocalSpace); Assert.Equal(0, result.RightArmMeshSpace);
        string[] properties = ["Enable_AimOffset", "BasePose_N", "BasePose_CLF", "Spine_Add", "Head_Add", "Arm_L_Add", "Arm_R_Add",
            "Hand_L", "Hand_R", "Enable_HandIK_L", "Enable_HandIK_R", "Arm_L_LS", "Arm_L_MS", "Arm_R_LS", "Arm_R_MS"];
        double[] expected = [.75, .7f, .3f, .2f, .4f, .6f, .8f, .5f, .9f, .375, (double).25f * .8f, .99f, 1, 1, 0];
        for (var i = 0; i < properties.Length; i++) Assert.Equal(expected[i], result.GetValue(properties[i]));
        Assert.Throws<ArgumentException>(() => result.GetValue("Layering_Legs"));
    }

    [Theory]
    [InlineData(-2.1f, 4d)] [InlineData(-.1f, 2d)] [InlineData(0f, 1d)]
    [InlineData(.99f, 1d)] [InlineData(1f, 0d)] [InlineData(1.1f, 0d)] [InlineData(2f, -1d)]
    [InlineData(2147483648f, -2147483647d)] [InlineData(-2147483648f, -2147483647d)]
    [InlineData(4294967296f, 1d)]
    public void MeshWeightUsesIntegerFloorAndNarrowingWithoutAUnitClamp(float local, double mesh)
    {
        var result = Model().Evaluate(Current, Previous,
            [nameof(AlsLayeringCurve.LeftArmLocalSpace), nameof(AlsLayeringCurve.RightArmLocalSpace)],
            [new(local), new(local)]);
        Assert.Equal((double)local, result.LeftArmLocalSpace); Assert.Equal((double)local, result.RightArmLocalSpace);
        Assert.Equal(mesh, result.LeftArmMeshSpace); Assert.Equal(mesh, result.RightArmMeshSpace);
    }

    [Fact]
    public void LerpExpressionsRetainOutOfRangeInputsAndDoubleIntermediatePrecision()
    {
        var result = Model().Evaluate(Current, Previous,
            [nameof(AlsLayeringCurve.AimOffsetMask), nameof(AlsLayeringCurve.LeftHandIk), nameof(AlsLayeringCurve.LeftArmLayer)],
            [new(2), new(float.MaxValue), new(float.MaxValue)]);
        Assert.Equal(-1, result.EnableAimOffset);
        Assert.Equal((double)float.MaxValue * float.MaxValue, result.LeftHandIk);
        Assert.True(double.IsFinite(result.LeftHandIk));
    }

    [Fact]
    public void MissingAndAbsentInputsReadZeroButTheFrameIdentityIsRetained()
    {
        var model = Model();
        var missing = model.Evaluate(Current, Previous, [], []);
        var absent = model.Evaluate(Current, Previous, Names(), Enumerable.Repeat(new AlsInertialCurve(37, false), 15).ToArray());
        Assert.Equal(missing, absent);
        Assert.Equal(Previous, absent.FeedbackIdentity);
    }

    [Fact]
    public void ReorderedCurvesAndRetriedCandidatesDoNotChangeTheSourceFrame()
    {
        var model = Model(); var names = Names();
        var curves = Enumerable.Range(0, 15).Select(i => new AlsInertialCurve(i / 10f)).ToArray();
        var expected = model.Evaluate(Current, Previous, names, curves);
        Array.Reverse(names); Array.Reverse(curves);
        Assert.Equal(expected, model.Evaluate(Current, Previous, names, curves));
        _ = model.Evaluate(new(13, 4, 3), Previous, names, curves);
        Assert.Equal(expected, model.Evaluate(Current, Previous, names, curves));
    }

    [Theory]
    [InlineData(12, 4, 3)] [InlineData(13, 4, 3)] [InlineData(11, 5, 3)] [InlineData(11, 4, 2)]
    public void RejectsCurrentFutureAndForeignFinalCurves(long frame, uint character, uint generation)
    {
        Assert.Throws<ArgumentException>(() => Model().Evaluate(Current, new(frame, character, generation), [], []));
    }

    [Theory]
    [InlineData(true, float.NaN)] [InlineData(false, float.NaN)]
    [InlineData(true, float.PositiveInfinity)] [InlineData(false, float.NegativeInfinity)]
    public void RejectsNonfiniteStoredValuesBeforeCurvePresenceCanHideThem(bool present, float value)
    {
        Assert.Throws<ArgumentException>(() => Model().Evaluate(Current, Previous,
            [nameof(AlsLayeringCurve.LeftArmLocalSpace)], [new(value, present)]));
    }

    [Fact]
    public void RejectsUncommittedDataDuplicateNamesAndInvalidLayouts()
    {
        var model = Model();
        Assert.Throws<ArgumentException>(() => model.Evaluate(Current, default, ["unused"], [new(0)]));
        Assert.Throws<ArgumentException>(() => model.Evaluate(default, Previous, [], []));
        Assert.Throws<ArgumentException>(() => model.Evaluate(Current, Previous, ["unused"], []));
        Assert.Throws<ArgumentException>(() => model.Evaluate(Current, Previous,
            [nameof(AlsLayeringCurve.LeftHand), nameof(AlsLayeringCurve.LeftHand)], [new(0), new(1)]));
        Assert.Throws<ArgumentException>(() => new AlsLayeringInputModel(Enumerable.Repeat("same", 15).ToArray()));
    }
}
