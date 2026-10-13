using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsTwistBoneReconstructionTests
{
    private static readonly string[] Names = ["root", "upperarm_l", "lowerarm_l", "hand_l", "upperarm_twist_01_l", "lowerarm_twist_01_l"];
    private static readonly int[] Parents = [-1, 0, 1, 2, 1, 2];
    private static AlsPrecisePose P(double y) => new(new(0, y, 0), AlsQuaternion.Identity, AlsDoubleVector.One);
    private static readonly AlsPrecisePose[] Reference = [P(0), P(0), P(10), P(10), P(2), P(8)];
    private static AlsQuaternion Y(double degrees) => AlsQuaternion.FromAxisAngle(new AlsDoubleVector(0, 1, 0), degrees * System.Math.PI / 180);
    private static void SameRotation(AlsQuaternion expected, AlsQuaternion actual) =>
        Assert.InRange(1 - System.Math.Abs(AlsQuaternion.Dot(expected, actual)), -1e-12, 1e-12);

    [Fact]
    public void IkAddsHalfRollAlongTheActualSegmentAxisAndPreservesAuthoredTwist()
    {
        var model = new AlsTwistBoneReconstruction(Names, Parents, Reference);
        var input = Reference.ToArray(); input[4] = input[4] with { Rotation = Y(-20) }; input[5] = input[5] with { Rotation = Y(30) };
        var original = input.ToArray(); var output = input.ToArray();
        output[1] = output[1] with { Rotation = Y(60) }; output[3] = output[3] with { Rotation = Y(80) };
        var main = output.Take(4).ToArray(); model.Adjust(input, output);
        SameRotation(Y(-50), output[4].Rotation); SameRotation(Y(70), output[5].Rotation);
        Assert.Equal(original, input); Assert.Equal(main, output.Take(4));
        for (var bone = 0; bone < output.Length; bone++)
        { Assert.Equal(input[bone].Position, output[bone].Position); Assert.Equal(input[bone].Scale, output[bone].Scale); }
        // A later free/native frame starts from its own authored input.
        var free = original.ToArray(); model.Adjust(original, free);
        Assert.Equal(original, free);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollCrossing180KeepsTheAuthoredBranchWithoutHistory(bool oppositeQuaternionSign)
    {
        var model = new AlsTwistBoneReconstruction(Names, Parents, Reference);
        var input = Reference.ToArray(); input[3] = input[3] with { Rotation = Y(170) }; input[5] = input[5] with { Rotation = Y(85) };
        var output = input.ToArray(); output[3] = output[3] with { Rotation = oppositeQuaternionSign ? -Y(190) : Y(190) };
        model.Adjust(input, output); SameRotation(Y(95), output[5].Rotation);
        var repeat = input.ToArray(); repeat[3] = output[3]; model.Adjust(input, repeat);
        Assert.Equal(output, repeat);
    }

    [Fact]
    public void SwingAndNonidentityBindRotationDoNotBecomeAxialRoll()
    {
        var reference = Reference.ToArray();
        var rest = AlsQuaternion.FromAxisAngle(new AlsDoubleVector(0, 0, 1), .6);
        reference[1] = reference[1] with { Rotation = rest };
        var model = new AlsTwistBoneReconstruction(Names, Parents, reference);
        var output = reference.ToArray();
        output[1] = output[1] with { Rotation = rest * AlsQuaternion.FromAxisAngle(new AlsDoubleVector(1, 0, 0), 1.2) };
        model.Adjust(reference, output); SameRotation(reference[4].Rotation, output[4].Rotation);
    }

    [Fact]
    public void SingularOrInvalidTwistRejectsBeforeChangingAnyCallerBones()
    {
        var model = new AlsTwistBoneReconstruction(Names, Parents, Reference);
        var output = Reference.ToArray(); output[1] = output[1] with { Rotation = Y(60) };
        output[3] = output[3] with { Rotation = AlsQuaternion.FromAxisAngle(new AlsDoubleVector(1, 0, 0), System.Math.PI) };
        var original = output.ToArray(); Assert.Throws<ArgumentException>(() => model.Adjust(Reference, output)); Assert.Equal(original, output);
        var invalid = Parents.ToArray(); invalid[5] = 4;
        Assert.Throws<ArgumentException>(() => new AlsTwistBoneReconstruction(Names, invalid, Reference));
    }

    [Fact]
    public void ProportionalContactIkUpdatesTwistWhileKeepingTheWristAndBoneLengths()
    {
        var target = Reference.ToArray(); target[2] = P(8); target[3] = P(8);
        var input = Reference.ToArray(); input[2] = input[2] with { Rotation = AlsQuaternion.FromAxisAngle(new AlsDoubleVector(0, 0, 1), 1.5) };
        input[3] = input[3] with { Rotation = Y(1.1 * 180 / System.Math.PI) *
            AlsQuaternion.FromAxisAngle(new AlsDoubleVector(1, 0, 0), .7) };
        input[5] = input[5] with { Rotation = Y(.55 * 180 / System.Math.PI) };
        var enabled = new AlsSkeletonPoseRetargetModel(Parents, Reference, target, [], [], [new(1, 2, 3)], boneNames: Names);
        var baseline = new AlsSkeletonPoseRetargetModel(Parents, Reference, target, [], [], [new(1, 2, 3)]);
        var actual = new AlsPrecisePose[input.Length]; var old = new AlsPrecisePose[input.Length];
        enabled.Evaluate(input, actual, contactWeights: [1]); baseline.Evaluate(input, old, contactWeights: [1]);
        Assert.Equal(old.Take(4), actual.Take(4));
        Assert.Equal(old[4].Position, actual[4].Position); Assert.Equal(old[5].Position, actual[5].Position);
        Assert.InRange(1 - System.Math.Abs(AlsQuaternion.Dot(old[5].Rotation, actual[5].Rotation)), 1e-8, 1);
        Assert.InRange(enabled.MaximumConditionalResidual, 0, 1e-9);
        for (var i = 0; i < 100; i++) enabled.Evaluate(input, actual, contactWeights: [1]);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++) enabled.Evaluate(input, actual, contactWeights: [1]);
        Assert.Equal(allocated, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void EvaluationsAllocateNothingAfterWarmup()
    {
        var model = new AlsTwistBoneReconstruction(Names, Parents, Reference);
        var output = Reference.ToArray();
        for (var i = 0; i < 100; i++) { Reference.CopyTo(output, 0); model.Adjust(Reference, output); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++) { Reference.CopyTo(output, 0); model.Adjust(Reference, output); }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        var singleInput = Reference.Select(p => p.ToSingle()).ToArray();
        var singleOutput = singleInput.ToArray();
        for (var i = 0; i < 100; i++) { singleInput.CopyTo(singleOutput, 0); model.Adjust(singleInput, singleOutput); }
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++) { singleInput.CopyTo(singleOutput, 0); model.Adjust(singleInput, singleOutput); }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
