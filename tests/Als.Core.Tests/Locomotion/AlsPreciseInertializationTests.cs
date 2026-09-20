using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsPreciseInertializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallAngleFallbackMustBeTransformedWithThePoseCoordinateSystem(bool precise)
    {
        var native = new AlsInertialization(1, 0);
        var imported = new AlsInertialization(1, 0, rotationFallbackAxis: -Vector3.UnitX);
        var wrongAxis = new AlsInertialization(1, 0);
        var output = new AlsLocalPose[1]; var qOutput = new AlsQuaternion[1];
        AlsQuaternion Run(AlsInertialization owner, AlsQuaternion q)
        {
            owner.Update(.01f); owner.Request(.3f);
            var input = new[] { AlsLocalPose.Identity with { Rotation = q.ToSingle() } };
            if (precise)
            {
                owner.EvaluatePrecise(input, [], AlsLocalPose.Identity, 0, 0, output, [], [q], AlsQuaternion.Identity, qOutput);
                return qOutput[0];
            }
            owner.Evaluate(input, [], AlsLocalPose.Identity, 0, 0, output, []);
            return new(output[0].Rotation);
        }
        var first = AlsQuaternion.Identity;
        // A near-unit input with a tiny vector part exercises the native fallback axis.
        var next = new AlsQuaternion(.00006, 0, 0, .99999997);
        Run(native, first); Run(imported, first); Run(wrongAxis, first);
        var expected = Reflect(Run(native, next));
        var actual = Run(imported, Reflect(next));
        var wrong = Run(wrongAxis, Reflect(next));
        Assert.True(Distance(expected, actual) < 1e-12);
        Assert.True(Distance(expected, wrong) > 1e-5, "The regression must expose the unconverted fallback axis.");
        Assert.Throws<ArgumentException>(() => native.CopyFrom(imported));
    }

    private static AlsQuaternion Reflect(AlsQuaternion q) => new(-q.X, q.Y, -q.Z, q.W);

    [Fact]
    public void RepeatedRequestsOnUnchangingPoseDoNotCreateSinglePrecisionRotationDrift()
    {
        var rotation = new AlsQuaternion(-.0028417804, -.014530888, .5679666, .8229184).Normalized();
        var input = new[] { new AlsLocalPose(Vector3.Zero, rotation.ToSingle(), Vector3.One) };
        var precise = new[] { rotation }; var output = new AlsLocalPose[1]; var result = new AlsQuaternion[1];
        var owner = new AlsInertialization(1, 0);
        for (var i = 0; i < 300; i++)
        {
            owner.Update(1f / 60); owner.Request(.5f);
            owner.EvaluatePrecise(input, [], AlsLocalPose.Identity, 0, 0, output, [], precise, AlsQuaternion.Identity, result);
            Assert.True(Distance(rotation, result[0]) < 1e-7);
        }
    }

    [Fact]
    public void CopyFromPreservesPreciseHistoryDuringInterruptionsAndRootRebasingWithoutAllocation()
    {
        var owner = new AlsInertialization(2, 0); var candidate = new AlsInertialization(2, 0);
        var input = new AlsLocalPose[2]; var rotations = new AlsQuaternion[2];
        var output = new AlsLocalPose[2]; var retry = new AlsLocalPose[2];
        var result = new AlsQuaternion[2]; var retryResult = new AlsQuaternion[2];
        void Frame(int frame)
        {
            var componentQ = AlsQuaternion.FromAxisAngle(Vector3.UnitZ, frame % 19 * .04f);
            var component = AlsLocalPose.Identity with { Rotation = componentQ.ToSingle() };
            for (var bone = 0; bone < 2; bone++)
            {
                rotations[bone] = AlsQuaternion.FromAxisAngle(Vector3.UnitY, (frame % 17 + bone) * .023f);
                input[bone] = AlsLocalPose.Identity with { Rotation = rotations[bone].ToSingle() };
            }
            owner.Update(1f / 60); if (frame % 3 == 0) owner.Request(.3f);
            candidate.CopyFrom(owner);
            owner.EvaluatePrecise(input, [], component, frame % 4, 0, output, [], rotations, componentQ, result);
            candidate.EvaluatePrecise(input, [], component, frame % 4, 0, retry, [], rotations, componentQ, retryResult);
        }
        for (var i = 0; i < 300; i++)
        {
            Frame(i); Assert.Equal(output, retry); Assert.Equal(result, retryResult);
        }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Frame(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        Assert.Equal(output, retry); Assert.Equal(result, retryResult);
    }

    [Fact]
    public void PrecisionModeCannotChangeWithLiveHistoryAndInvalidProjectionDoesNotConsumeUpdate()
    {
        var owner = new AlsInertialization(1, 0); var baseline = new AlsInertialization(1, 0);
        var input = new[] { AlsLocalPose.Identity }; var output = new AlsLocalPose[1];
        var rotations = new[] { AlsQuaternion.Identity }; var result = new AlsQuaternion[1];
        owner.Update(.01f); owner.Request(.3f); baseline.CopyFrom(owner);
        Assert.Throws<ArgumentException>(() => owner.EvaluatePrecise(input, [], AlsLocalPose.Identity, 0, 0,
            output, [], [AlsQuaternion.FromAxisAngle(Vector3.UnitX, .1f)], AlsQuaternion.Identity, result));
        owner.EvaluatePrecise(input, [], AlsLocalPose.Identity, 0, 0, output, [], rotations, AlsQuaternion.Identity, result);
        baseline.EvaluatePrecise(input, [], AlsLocalPose.Identity, 0, 0, output, [], rotations, AlsQuaternion.Identity, result);
        Assert.Equal(baseline.HistoryCount, owner.HistoryCount);
        Assert.Throws<InvalidOperationException>(() => owner.Evaluate(input, [], AlsLocalPose.Identity, 0, 0, output, []));
        owner.Reset(); owner.Evaluate(input, [], AlsLocalPose.Identity, 0, 0, output, []);
        Assert.Throws<InvalidOperationException>(() => owner.EvaluatePrecise(input, [], AlsLocalPose.Identity, 0, 0, output, [], rotations, AlsQuaternion.Identity, result));
        owner.Reset();
        owner.EvaluatePrecise(input, [], AlsLocalPose.Identity, 0, 0, input, [], rotations, AlsQuaternion.Identity, rotations);
        Assert.Equal(AlsQuaternion.Identity, rotations[0]);
    }

    private static double Distance(AlsQuaternion a, AlsQuaternion b) => System.Math.Sqrt(
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) +
        (a.Z - b.Z) * (a.Z - b.Z) + (a.W - b.W) * (a.W - b.W));
}
