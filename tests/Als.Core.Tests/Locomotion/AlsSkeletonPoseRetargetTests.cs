using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsSkeletonPoseRetargetTests
{
    private static AlsPrecisePose P(double x, double y = 0, double z = 0) =>
        new(new(x, y, z), AlsQuaternion.Identity, AlsDoubleVector.One);
    private static readonly int[] Parents = [-1, 0, 1, 2, 3, 4];
    private static readonly AlsPrecisePose[] Source = [P(0), P(0, 0, 10), P(1), P(0, 0, -4), P(0, 0, -4), P(1, 0, -.3)];
    private static readonly AlsPrecisePose[] Target = [P(0), P(0, 0, 11), P(.8), P(0, 0, -5), P(0, 0, -5), P(.7, 0, -.1)];
    private static AlsSkeletonPoseRetargetModel Model(bool markers = false) => new(Parents, Source, Target, [0, 1], [new(2, 3, 4)],
        contactMarkers: markers ? [new(4, new(0, 0, -1), new(0, 0, -.5))] : []);
    private static AlsPrecisePose[] Components(AlsPrecisePose[] local)
    {
        var result = new AlsPrecisePose[local.Length];
        for (var i = 0; i < local.Length; i++) result[i] = Parents[i] < 0 ? local[i] : AlsPrecisePose.Compose(local[i], result[Parents[i]]);
        return result;
    }

    [Fact]
    public void TranslationsKeepTargetProportionsAndRootPelvisMotionWithoutStretching()
    {
        var model = Model(); var source = Source.ToArray(); var result = new AlsPrecisePose[source.Length];
        source[0] = source[0] with { Position = new(5, 3, 0) };
        source[1] = source[1] with { Position = new(0, 0, 9) };
        source[3] = source[3] with { Position = new(0, 0, -7), Scale = new(1, 1, 2) };
        model.Evaluate(source, result, false);
        Assert.Equal(new AlsDoubleVector(5, 3, 0), result[0].Position);
        Assert.Equal(new AlsDoubleVector(0, 0, 10), result[1].Position);
        for (var i = 2; i < result.Length; i++)
        {
            Assert.Equal(Target[i].Position, result[i].Position);
            Assert.Equal(Target[i].Scale, result[i].Scale);
        }
        Assert.Equal(new AlsDoubleVector(0, 0, -7), source[3].Position);
    }

    [Theory]
    [InlineData(0d)][InlineData(-3d)]
    public void ReachableContactsUseTargetLegLengthsAndPropagateToeDescendants(double pelvisOffset)
    {
        var source = Source.ToArray(); source[1] = source[1] with { Position = new(0, 0, 10 + pelvisOffset) };
        var output = new AlsPrecisePose[source.Length]; var model = Model(); model.Evaluate(source, output);
        var from = Components(source); var to = Components(output);
        Assert.True((to[4].Position - from[4].Position).LengthSquared < 1e-18);
        foreach (var i in new[] { 3, 4, 5 })
            Assert.True(System.Math.Abs(output[i].Position.LengthSquared - Target[i].Position.LengthSquared) < 1e-10);
        Assert.True((to[5].Position - to[4].Position).LengthSquared > .48);
        Assert.True(1 - System.Math.Abs(AlsQuaternion.Dot(from[4].Rotation, to[4].Rotation)) < 1e-12);
    }

    [Fact]
    public void UnreachableContactClampsWithoutChangingTheTargetBoneLengths()
    {
        var source = Source.ToArray(); source[4] = source[4] with { Position = new(0, 0, -50) };
        var output = new AlsPrecisePose[source.Length]; Model().Evaluate(source, output);
        foreach (var i in new[] { 3, 4 })
            Assert.True(System.Math.Abs(output[i].Position.LengthSquared - 25) < 1e-10);
        Assert.True((Components(output)[4].Position - Components(source)[4].Position).LengthSquared > 100);
    }

    [Fact]
    public void BadTailAndMismatchedAxesRejectBeforeTouchingTheOutput()
    {
        var source = Source.ToArray(); source[^1] = P(double.NaN);
        var output = Enumerable.Repeat(P(99), source.Length).ToArray(); var before = output.ToArray();
        Assert.Throws<ArgumentException>(() => Model().Evaluate(source, output)); Assert.Equal(before, output);
        Assert.Throws<ArgumentException>(() => Model().EvaluateComponents(source, output)); Assert.Equal(before, output);
        var target = Target.ToArray(); target[2] = target[2] with { Rotation = new(0, 0, System.Math.Sin(.1), System.Math.Cos(.1)) };
        Assert.Throws<ArgumentException>(() => new AlsSkeletonPoseRetargetModel(Parents, Source, target, [0, 1], []));
    }

    [Fact]
    public void ComponentOverflowRejectsBeforePublishingAnyBones()
    {
        var target = Target.ToArray();
        target[0] = target[0] with { Scale = new(1e200, 1e200, 1e200) };
        target[1] = target[1] with { Scale = new(1e200, 1e200, 1e200) };
        var model = new AlsSkeletonPoseRetargetModel(Parents, Source, target, [0, 1], []);
        var output = Enumerable.Repeat(P(99), Source.Length).ToArray(); var before = output.ToArray();
        Assert.Throws<ArgumentException>(() => model.EvaluateComponents(Components(Source), output));
        Assert.Equal(before, output);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ComponentMappingAndSupportGeometryUseTheDisplayedRetargetedToe(bool markers)
    {
        var model = Model(markers); var source = Components(Source); var result = new AlsPrecisePose[source.Length];
        model.EvaluateComponents(source, result);
        AlsFootSupportPoint[] points = [new(0, 1, true), new(1, 1, false)];
        AlsFootSupportInfluence[] influences = [new(5, new(0, 0, -.2), 1), new(5, new(0, 0, -.2), 1)];
        var geometry = new AlsFootSupportGeometry(source.Length, points, influences, model);
        var hit = new AlsFootTraceRigHit(true, default, new(0, 0, 1));
        var expected = result[5].Position.Z - .2;
        Assert.True(System.Math.Abs(geometry.MinimumDistance(true, source, AlsPrecisePose.Identity, hit) - expected) < 1e-10);
        var unretargeted = new AlsFootSupportGeometry(source.Length, points, influences);
        Assert.True(System.Math.Abs(unretargeted.MinimumDistance(true, source, AlsPrecisePose.Identity, hit) - expected) > .1);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void SteadyPoseAndSupportConversionsAllocateNoMemory(bool markers)
    {
        var model = Model(markers); var output = new AlsPrecisePose[Source.Length]; var components = Components(Source);
        for (var i = 0; i < 64; i++) { model.Evaluate(Source, output); model.EvaluateComponents(components, output); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++) { model.Evaluate(Source, output); model.EvaluateComponents(components, output); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(.2, 0)][InlineData(.8, 35)][InlineData(1.4, -40)]
    public void PhysicalContactMarkersPreserveSwingHeightThroughFootRotationAndDifferentSoleThickness(double targetDepth, double angle)
    {
        var sourceMarker = new AlsDoubleVector(0, 0, -1);
        var targetMarker = new AlsDoubleVector(0, 0, -targetDepth);
        var model = new AlsSkeletonPoseRetargetModel(Parents, Source, Target, [0, 1], [new(2, 3, 4)],
            contactMarkers: [new(4, sourceMarker, targetMarker)]);
        var source = Source.ToArray();
        var radians = angle * System.Math.PI / 180;
        source[4] = source[4] with { Rotation = new(System.Math.Sin(radians/2), 0, 0, System.Math.Cos(radians/2)) };
        foreach (var height in new[] { 0d, 5d })
        {
            source[0] = P(0, 0, height);
            var result = new AlsPrecisePose[source.Length];
            model.Evaluate(source, result);
            var from = Components(source); var to = Components(result);
            var wanted = from[4].Position + sourceMarker.Rotate(from[4].Rotation);
            var actual = to[4].Position + targetMarker.Rotate(to[4].Rotation);
            Assert.True((wanted - actual).LengthSquared < 1e-18);
            Assert.True(System.Math.Abs(actual.Z-height-(2-System.Math.Cos(radians))) < 1e-10);
            foreach (var bone in new[] { 3, 4, 5 })
                Assert.True(System.Math.Abs(result[bone].Position.LengthSquared-Target[bone].Position.LengthSquared) < 1e-10);
            // Matching only the ankle demonstrably fails physical contact.
            var old = new AlsPrecisePose[source.Length]; Model().Evaluate(source, old);
            var uncorrected = Components(old)[4];
            Assert.True((uncorrected.Position+targetMarker.Rotate(uncorrected.Rotation)-wanted).LengthSquared > .01);
        }
    }

    [Fact]
    public void ContactMarkersPreserveBoundedReachAndUnmatchedPoseSemantics()
    {
        var model = new AlsSkeletonPoseRetargetModel(Parents, Source, Target, [0, 1], [new(2, 3, 4)],
            contactMarkers: [new(4, new(0, 0, -100), default)]);
        var output = new AlsPrecisePose[Source.Length]; model.Evaluate(Source, output);
        foreach (var bone in new[] { 3, 4 }) Assert.True(System.Math.Abs(output[bone].Position.LengthSquared-25) < 1e-10);
        var unmarked = new AlsPrecisePose[Source.Length]; Model().Evaluate(Source, unmarked, false);
        model.Evaluate(Source, output, false); Assert.Equal(unmarked, output);
        Assert.Throws<ArgumentException>(() => new AlsSkeletonPoseRetargetModel(Parents, Source, Target, [0, 1], [new(2, 3, 4)],
            contactMarkers: [new(5, default, default)]));
        Assert.Throws<ArgumentException>(() => new AlsSkeletonPoseRetargetModel(Parents, Source, Target, [0, 1], [new(2, 3, 4)],
            contactMarkers: [new(4, new(double.NaN, 0, 0), default)]));
    }
}
