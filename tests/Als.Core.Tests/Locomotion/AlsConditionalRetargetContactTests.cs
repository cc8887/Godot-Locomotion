using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsConditionalRetargetContactTests
{
    private static readonly int[] Parents = [-1, 0, 1, 2, 3];
    private static AlsPrecisePose P(double x, double y = 0, double z = 0) =>
        new(new(x, y, z), AlsQuaternion.Identity, AlsDoubleVector.One);
    private static readonly AlsPrecisePose[] Reference = [P(0), P(0, 0, 10), P(10), P(10), P(2)];
    private static readonly AlsPrecisePose[] Target = [P(0), P(0, 0, 10), P(8), P(8), P(1)];
    private static AlsSkeletonPoseRetargetModel Model() => new(Parents, Reference, Target, [], [], [new(1, 2, 3)]);
    private static AlsPrecisePose[] Pose()
    {
        var pose = Reference.ToArray();
        pose[2] = pose[2] with { Rotation = new(0, 0, System.Math.Sin(System.Math.PI / 4), System.Math.Cos(System.Math.PI / 4)) };
        return pose;
    }
    private static AlsPrecisePose[] Components(AlsPrecisePose[] local)
    {
        var result = new AlsPrecisePose[local.Length];
        for (var i = 0; i < local.Length; i++) result[i] = Parents[i] < 0 ? local[i] : AlsPrecisePose.Compose(local[i], result[Parents[i]]);
        return result;
    }

    [Fact]
    public void ContactMatchesThePublishedWristWithoutStretchingTheShorterArmOrFinger()
    {
        var source = Pose(); var before = source.ToArray(); var output = new AlsPrecisePose[source.Length];
        var model = Model(); model.Evaluate(source, output, contactWeights: [1]);
        var original = Components(source); var actual = Components(output);
        Assert.InRange((actual[3].Position - original[3].Position).LengthSquared, 0, 1e-18);
        Assert.InRange(System.Math.Abs(1 - System.Math.Abs(AlsQuaternion.Dot(actual[3].Rotation, original[3].Rotation))), 0, 1e-12);
        Assert.InRange(model.MaximumConditionalResidual, 0, 1e-9);
        foreach (var i in new[] { 2, 3, 4 })
            Assert.InRange(System.Math.Abs(output[i].Position.LengthSquared - Target[i].Position.LengthSquared), 0, 1e-10);
        Assert.Equal(before, source);
    }

    [Fact]
    public void ReleaseUsesEffectorInterpolationAndTheNextFreePoseHasNoContactHistory()
    {
        var source = Pose(); var model = Model(); var free = new AlsPrecisePose[source.Length];
        var half = new AlsPrecisePose[source.Length]; var released = new AlsPrecisePose[source.Length];
        model.Evaluate(source, free);
        model.Evaluate(source, half, contactWeights: [.5f]);
        var expected = (Components(free)[3].Position + Components(source)[3].Position) * .5;
        Assert.InRange((Components(half)[3].Position - expected).LengthSquared, 0, 1e-18);
        model.Evaluate(source, released, contactWeights: [0]);
        Assert.Equal(free, released); Assert.Equal(0, model.MaximumConditionalResidual);
        model.Evaluate(source, released, false, [1]);
        Assert.Equal(free, released);
    }

    [Fact]
    public void UnreachableSupportReportsItsResidualAndKeepsBothBoneLengths()
    {
        var model = Model(); var output = new AlsPrecisePose[Reference.Length];
        model.Evaluate(Reference, output, contactWeights: [1]);
        Assert.InRange(model.MaximumConditionalResidual, 3.999999, 4.000001);
        Assert.InRange(Components(output)[3].Position.X, 15.999999, 16.000001);
        Assert.InRange(output[2].Position.LengthSquared, 63.999999, 64.000001);
        Assert.InRange(output[3].Position.LengthSquared, 63.999999, 64.000001);
    }

    [Fact]
    public void InvalidContactWeightsRejectBeforeWritingTheCallerOutput()
    {
        var model = Model(); var source = Pose(); var output = Enumerable.Repeat(P(99), source.Length).ToArray();
        var before = output.ToArray();
        foreach (var weight in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f })
        {
            Assert.Throws<ArgumentException>(() => model.Evaluate(source, output, contactWeights: [weight]));
            Assert.Equal(before, output);
        }
        Assert.Throws<ArgumentException>(() => model.Evaluate(source, output, contactWeights: [1, 1]));
        Assert.Equal(before, output);
    }

    [Fact]
    public void ActiveContactsAllocateNoMemoryAfterWarmup()
    {
        var model = Model(); var source = Pose(); var output = new AlsPrecisePose[source.Length]; float[] weights = [1];
        for (var i = 0; i < 100; i++) model.Evaluate(source, output, contactWeights: weights);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) model.Evaluate(source, output, contactWeights: weights);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void BodyReachUsesOnlyTheReferenceProportionBudgetAndResetsOnRelease()
    {
        var model = new AlsSkeletonPoseRetargetModel(Parents, Reference, Target, [], [], [new(1, 2, 3)], 0);
        var output = new AlsPrecisePose[Reference.Length];
        model.Evaluate(Reference, output, contactWeights: [1]);
        Assert.Equal(4, model.MaximumContactRootCorrection);
        Assert.InRange(model.ContactRootCorrection.X, 3.999999, 4.000001);
        Assert.InRange(model.MaximumConditionalResidual, 0, 1e-9);
        Assert.InRange(Components(output)[3].Position.X, 19.999999, 20.000001);
        Assert.InRange(output[2].Position.LengthSquared, 63.999999, 64.000001);
        Assert.InRange(output[3].Position.LengthSquared, 63.999999, 64.000001);
        model.Evaluate(Reference, output, contactWeights: [0]);
        Assert.Equal(AlsDoubleVector.Zero, model.ContactRootCorrection);
        Assert.Equal(Target, output);
        var bad = Reference.ToArray(); bad[3] = P(50);
        model.Evaluate(bad, output, contactWeights: [1]);
        Assert.InRange(model.ContactRootCorrection.LengthSquared, 15.999999, 16.000001);
        Assert.True(model.MaximumConditionalResidual > 30);
        Assert.InRange(output[3].Position.LengthSquared, 63.999999, 64.000001);
    }

    [Fact]
    public void BodyReachDoesNotAllocateOrAcceptAnUnrelatedContactRoot()
    {
        Assert.Throws<ArgumentException>(() => new AlsSkeletonPoseRetargetModel(Parents, Reference, Target, [], [], [new(1, 2, 3)], 3));
        var model = new AlsSkeletonPoseRetargetModel(Parents, Reference, Target, [], [], [new(1, 2, 3)], 0);
        var output = new AlsPrecisePose[Reference.Length]; float[] weights = [1];
        for (var i = 0; i < 100; i++) model.Evaluate(Reference, output, contactWeights: weights);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) model.Evaluate(Reference, output, contactWeights: weights);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
