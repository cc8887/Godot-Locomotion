using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsAdditiveMontageTests
{
    private static AlsFrameIdentity Id(int frame) => new(frame, 7, 2);
    private static AlsSequenceMontageCommand Command(int id) => new(id, AlsMontageSlot.Grounded, 1.75f, .3f, .2f, .2f);
    private static AlsPrecisePose Pose(double x, AlsQuaternion? rotation = null, double scale = 1) =>
        new(new(x, 0, 0), rotation ?? AlsQuaternion.Identity, new(scale, scale, scale));

    [Fact]
    public void TransitionUsesExistingGroupAndOnlyEntersTheNextFrozenEvaluation()
    {
        var owner = new AlsMontageRuntime([new(1, AlsTurnSlot.Standing, 1, 2)], sequences: [new(2, AlsMontageSlot.Grounded, 1, 2, 2)]);
        owner.Begin(Id(1), .1f); owner.Play(new(1, AlsTurnSlot.Standing, 1, 0, .2f, .2f, 1, 0)); owner.Commit(Id(1));
        owner.Begin(Id(2), .1f); var frozen = owner.Evaluation.ToArray(); owner.PlaySequence(Command(2));
        Assert.Equal(frozen, owner.Evaluation.ToArray()); Assert.True(owner.Candidate[0].Interrupted);
        Assert.Equal(.3f, owner.Candidate[1].Position); Assert.Equal(2, owner.Candidate[1].AdditiveType);
        var expected = owner.Candidate.ToArray(); owner.Discard();
        owner.Begin(Id(2), .1f); owner.PlaySequence(Command(2)); Assert.Equal(expected, owner.Candidate.ToArray()); owner.Commit(Id(2));
        owner.Begin(Id(3), .1f);
        var additive = owner.Evaluation.ToArray().Single(e => e.AnimationId == 2);
        Assert.Equal(.475f, additive.Position, 6); Assert.Equal(.5f, additive.Weight, 6);
        Assert.Equal(new AlsSlotWeights(1, .5f, .5f), owner.SlotWeights(AlsMontageSlot.Grounded));
    }

    [Fact]
    public void MixedSlotBlendsItsOrdinaryBaseBeforeAddingDeltaAndCurves()
    {
        var owner = Owner(0, 1); var mixer = new AlsMontageSlotPose([Pose(10)], [-1], 1);
        var sample = new Samples([Pose(4)], [Pose(2, scale: 0)], 8, 6);
        AlsPrecisePose[] output = [Pose(-1)]; AlsInertialCurve[] curves = [default];
        mixer.Evaluate(owner.Frame, Id(2), AlsMontageSlot.Grounded, [Pose(10)], [new(2)], output, curves, sample);
        Assert.Equal(8, output[0].Position.X, 7); Assert.Equal(8, curves[0].Value, 6); Assert.Equal(AlsDoubleVector.One, output[0].Scale);
    }

    [Fact]
    public void MeshAdditiveUsesComponentRotationAndKeepsLocalTranslation()
    {
        var half = System.Math.Sqrt(.5); var z = new AlsQuaternion(0, 0, half, half); var x = new AlsQuaternion(half, 0, 0, half);
        AlsPrecisePose[] source = [Pose(0, z), Pose(2)];
        var sampler = new Samples([], [Pose(0, scale: 0), Pose(1, x, 0)], 0, 0);
        var owner = new AlsMontageRuntime([], sequences: [new(1, AlsMontageSlot.Grounded, 1, 3, 2)]);
        owner.Begin(Id(1), .2f); owner.PlaySequence(Command(1)); owner.Commit(Id(1)); owner.Begin(Id(2), .2f);
        var mixer = new AlsMontageSlotPose(source, [-1, 0], 1); var result = new AlsPrecisePose[2];
        mixer.Evaluate(owner.Frame, Id(2), AlsMontageSlot.Grounded, source, [default], result, new AlsInertialCurve[1], sampler);
        Assert.True(System.Math.Abs(AlsQuaternion.Dot(result[0].Rotation * result[1].Rotation, x * z)) > 1 - 1e-12);
        Assert.True(System.Math.Abs(AlsQuaternion.Dot(result[0].Rotation * result[1].Rotation, z * x)) < .6);
        Assert.Equal(3, result[1].Position.X, 7);
    }

    [Fact]
    public void AdditiveWeightParticipatesInNormalizationWithoutSuppressingSource()
    {
        var owner = Owner(0, 1, .2f);
        Assert.Equal(new AlsSlotWeights(.5f, 1, 2), owner.SlotWeights(AlsMontageSlot.Grounded));
        var mixer = new AlsMontageSlotPose([Pose(10)], [-1], 1); var output = new AlsPrecisePose[1]; var curves = new AlsInertialCurve[1];
        mixer.Evaluate(owner.Frame, Id(2), AlsMontageSlot.Grounded, [Pose(10)], [new(2)], output, curves,
            new Samples([Pose(4)], [Pose(2, scale: 0)], 8, 6));
        Assert.Equal(8, output[0].Position.X, 7); Assert.Equal(8, curves[0].Value, 6);
    }

    [Fact]
    public void LateSamplingFailureLeavesCallerPoseUntouchedAndRetryIsDeterministic()
    {
        var owner = Owner(0, 1); var mixer = new AlsMontageSlotPose([Pose(10)], [-1], 1);
        var sample = new Samples([Pose(4)], [Pose(2, scale: 0)], 8, 6) { Fail = true };
        AlsPrecisePose[] output = [Pose(-1)]; AlsInertialCurve[] curves = [new(-1)];
        Assert.Throws<InvalidOperationException>(() => mixer.Evaluate(owner.Frame, Id(2), AlsMontageSlot.Grounded, [Pose(10)], [new(2)], output, curves, sample));
        Assert.Equal(-1, output[0].Position.X); Assert.Equal(-1, curves[0].Value);
        sample.Fail = false; mixer.Evaluate(owner.Frame, Id(2), AlsMontageSlot.Grounded, [Pose(10)], [new(2)], output, curves, sample);
        Assert.Equal(8, output[0].Position.X, 7);
        Assert.Throws<ArgumentException>(() => mixer.Evaluate(owner.Frame, Id(3), AlsMontageSlot.Grounded, [Pose(10)], [new(2)], output, curves, sample));
        Assert.Throws<ArgumentException>(() => mixer.Evaluate(owner.Frame, Id(2), AlsMontageSlot.Grounded, [], [], output, curves, sample));
    }

    [Fact]
    public void WarmEvaluationDoesNotAllocateOrAdvanceMontageTime()
    {
        var owner = Owner(0, 1); var beforeState = owner.Candidate.ToArray(); var mixer = new AlsMontageSlotPose([Pose(10)], [-1], 1);
        var sampler = new Samples([Pose(4)], [Pose(2, scale: 0)], 8, 6);
        AlsPrecisePose[] source = [Pose(10)], output = [default]; AlsInertialCurve[] input = [new(2)], curves = [default];
        for (var i = 0; i < 1000; i++) mixer.Evaluate(owner.Frame, Id(2), AlsMontageSlot.Grounded, source, input, output, curves, sampler);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) mixer.Evaluate(owner.Frame, Id(2), AlsMontageSlot.Grounded, source, input, output, curves, sampler);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Assert.Equal(beforeState, owner.Candidate.ToArray());
    }

    private static AlsMontageRuntime Owner(int first, int second, float delta = .1f)
    {
        var owner = new AlsMontageRuntime([], sequences: [new(0, AlsMontageSlot.Grounded, 1, 3, first), new(1, AlsMontageSlot.Grounded, 2, 3, second)]);
        owner.Begin(Id(1), delta); owner.PlaySequence(Command(0)); owner.PlaySequence(Command(1)); owner.Commit(Id(1)); owner.Begin(Id(2), delta); return owner;
    }
    private sealed class Samples(AlsPrecisePose[] normal, AlsPrecisePose[] additive, float normalCurve, float additiveCurve) : IAlsMontagePoseSource
    {
        public bool Fail;
        public void Sample(in AlsMontageEvaluation entry, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
        {
            if (Fail && entry.AdditiveType != 0) throw new InvalidOperationException("Late additive failure");
            (entry.AdditiveType == 0 ? normal : additive).CopyTo(pose); curves[0] = new(entry.AdditiveType == 0 ? normalCurve : additiveCurve);
        }
    }
}
