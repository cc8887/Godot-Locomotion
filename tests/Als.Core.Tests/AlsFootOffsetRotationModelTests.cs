using GodotAls.Core.Locomotion;
using M = System.Math;

namespace GodotAls.Core.Tests;

public sealed class AlsFootOffsetRotationModelTests
{
    private static AlsQuaternion Axis(int axis, double degrees)
    {
        var sine = M.Sin(degrees * M.PI / 360);
        return new(axis == 0 ? -sine : 0, axis == 1 ? -sine : 0,
            axis == 2 ? sine : 0, M.Cos(degrees * M.PI / 360));
    }

    private static AlsFootOffsetRotationInput Input => new(1f / 60,
        AlsQuaternion.Identity, AlsQuaternion.Identity, AlsQuaternion.Identity,
        AlsQuaternion.Identity, AlsQuaternion.Identity, new(0, 0, 1),
        new(-20, 40), new(-15, 5), new(0, 0), .1f);

    private static void Near(AlsQuaternion expected, AlsQuaternion actual) =>
        Assert.True(1 - M.Abs(AlsQuaternion.Dot(expected.Normalized(), actual.Normalized())) < 1e-12,
            $"Expected {expected}, actual {actual}");

    [Theory]
    [InlineData(2, 70, 40)] [InlineData(2, -50, -20)]
    [InlineData(1, 40, 5)] [InlineData(1, -40, -15)]
    [InlineData(0, 30, 0)] [InlineData(0, -30, 0)]
    public void FlatGroundLimitsEachAnkleAxisInCalfSpace(int axis, double target, double expected)
    {
        var result = AlsFootOffsetRotationModel.Evaluate(default, Input with { TargetRotation = Axis(axis, target) });
        Near(Axis(axis, expected), result.FootRotation);
    }

    [Theory]
    [InlineData(65, 80, 65)] [InlineData(-45, -60, -45)] [InlineData(65, 30, 30)]
    public void AuthoredCurrentRotationExpandsTheLimitWithoutFreezingTheFoot(double current, double target, double expected)
    {
        var result = AlsFootOffsetRotationModel.Evaluate(default, Input with
        { FootRotation = Axis(2, current), TargetRotation = Axis(2, target) });
        Near(Axis(2, expected), result.FootRotation);
    }

    [Fact]
    public void ReversedIntervalEndpointsHaveTheSameMeaning()
    {
        var input = Input with { TargetRotation = Axis(2, 90) };
        Assert.Equal(AlsFootOffsetRotationModel.Evaluate(default, input),
            AlsFootOffsetRotationModel.Evaluate(default, input with { Swing1 = new(40, -20) }));
    }

    [Fact]
    public void InitialReferenceRotationAndCurrentCalfRotationAreBothRespected()
    {
        var initialCalf = Axis(0, 35); var reference = Axis(1, 25);
        var calf = Axis(2, 100) * Axis(0, -20);
        var input = Input with { InitialCalfRotation = initialCalf,
            InitialFootRotation = initialCalf * reference, CalfRotation = calf,
            FootRotation = calf * reference, TargetRotation = calf * Axis(2, 80) * reference };
        var result = AlsFootOffsetRotationModel.Evaluate(default, input);
        Near(calf * Axis(2, 40) * reference, result.FootRotation);
        // Initial hierarchy is cached until rig initialization is explicitly reset.
        var next = input with { InitialCalfRotation = Axis(0, 90), InitialFootRotation = Axis(2, 75) };
        Near(result.FootRotation, AlsFootOffsetRotationModel.Evaluate(result, next).FootRotation);
    }

    [Theory]
    [InlineData(0, -15, 5, 20)] [InlineData(.5, -7.5, 2.5, 35)]
    [InlineData(1, 0, 0, 50)] [InlineData(2, 15, -5, 80)]
    public void MovingCurveUsesTheAuthoredUnclampedDoubleInterpolation(double moving, float min, float max, float distance)
    {
        Assert.Equal(new AlsFootRotationInterval(min, max), AlsRefactoredFootGraphSettings.Swing2(moving));
        Assert.Equal(distance, AlsRefactoredFootGraphSettings.MinPelvisToFootDistance(moving));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void SlopeNormalHistoryIsContinuousAndDiscardedCandidatesDoNotAdvanceIt(int hz)
    {
        var input = Input with { DeltaTime = 1f / hz, Swing2 = new(-180, 180), Swing1 = new(-180, 180), Twist = new(-180, 180) };
        var state = AlsFootOffsetRotationModel.Evaluate(default, input);
        var slope = new AlsDoubleVector(.5, 0, M.Sqrt(.75));
        input = input with { OffsetNormal = slope };
        for (var frame = 0; frame < hz; frame++)
        {
            var first = AlsFootOffsetRotationModel.Evaluate(state, input);
            Assert.Equal(first, AlsFootOffsetRotationModel.Evaluate(state, input));
            Assert.True(first.OffsetNormal.X > state.OffsetNormal.X);
            Assert.True(first.OffsetNormal.X < slope.X);
            Assert.InRange(first.FootRotation.LengthSquared, .999999999999, 1.000000000001);
            state = first;
        }
        Assert.InRange((state.OffsetNormal - slope).LengthSquared, 0, 1e-6);
    }

    [Fact]
    public void ColdNormalHasNoArtificialRampAndZeroDeltaKeepsTheExistingNormal()
    {
        var slope = new AlsDoubleVector(.5, 0, M.Sqrt(.75));
        var input = Input with { OffsetNormal = slope, Swing2 = new(-180, 180) };
        var state = AlsFootOffsetRotationModel.Evaluate(default, input);
        Assert.Equal(slope, state.OffsetNormal);
        Near(Axis(1, -30), state.FootRotation);
        var paused = AlsFootOffsetRotationModel.Evaluate(state, input with { DeltaTime = 0, OffsetNormal = new(0, 0, 1), HalfLife = 0 });
        Assert.Equal(slope, paused.OffsetNormal);
        var reset = AlsFootOffsetRotationModel.Evaluate(default, input with { OffsetNormal = new(0, 0, 1) });
        Near(AlsQuaternion.Identity, reset.FootRotation);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void DegenerateAndOppositeNormalsProduceFiniteUnitRotations(double z)
    {
        var result = AlsFootOffsetRotationModel.Evaluate(default, Input with
        { OffsetNormal = new(0, 0, z), Swing1 = new(-180, 180), Swing2 = new(-180, 180), Twist = new(-180, 180) });
        Assert.InRange(result.FootRotation.LengthSquared, .999999999999, 1.000000000001);
        var up = new AlsDoubleVector(0, 0, 1).Rotate(result.FootRotation);
        Assert.InRange(up.Z, (z == 0 ? 1 : -1) - 1e-12, (z == 0 ? 1 : -1) + 1e-12);
    }

    [Theory]
    [InlineData(90)] [InlineData(-90)]
    public void NativeDoubleRotatorSingularityKeepsTheEquivalentOrientation(double pitch)
    {
        var rotation = Axis(2, 50) * Axis(1, pitch) * Axis(0, 20);
        var result = AlsFootOffsetRotationModel.Evaluate(default, Input with
        { TargetRotation = rotation, Swing1 = new(-180, 180), Swing2 = new(-180, 180), Twist = new(-180, 180) });
        Near(rotation, result.FootRotation);
    }

    [Fact]
    public void InvalidCandidateDoesNotMutatePreviouslyCommittedState()
    {
        var state = AlsFootOffsetRotationModel.Evaluate(default, Input);
        var copy = state;
        Assert.Throws<ArgumentException>(() => AlsFootOffsetRotationModel.Evaluate(state, Input with { HalfLife = float.NaN }));
        Assert.Throws<ArgumentException>(() => AlsFootOffsetRotationModel.Evaluate(state, Input with { TargetRotation = default }));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsRefactoredFootGraphSettings.Swing2(double.NaN));
        Assert.Equal(copy, state);
    }
}
