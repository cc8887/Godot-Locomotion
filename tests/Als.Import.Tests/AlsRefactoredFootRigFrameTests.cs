using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredFootRigFrameTests
{
    private static readonly string[] Names = ["pelvis", "thigh_l", "calf_l", "foot_l", "ball_l", "thigh_r", "calf_r", "foot_r", "ball_r", "ik_foot_root"];
    private static readonly int[] Parents = [-1, 0, 1, 2, 3, 0, 5, 6, 7, -1];
    private static readonly string[] CurveNames = ["FootLeftIk", "FootRightIk", "PoseMoving", "unrelated"];
    private static AlsPrecisePose[] Source() => new AlsDoubleVector[]
    {
        new(0, 0, 100), new(0, -7, 90), new(15, -7, 50), new(0, -7, 13.5), new(15, -7, 13.5),
        new(0, 7, 90), new(15, 7, 50), new(0, 7, 13.5), new(15, 7, 13.5), new(0,0,0)
    }.Select(p => new AlsPrecisePose(p, AlsQuaternion.Identity, AlsDoubleVector.One)).ToArray();
    private static AlsInertialCurve[] Curves(float moving = 0) => [new(1, true), new(1, true), new(moving, true), new(.7f, true)];
    private static AlsRefactoredFootRigFrame Create(ReadOnlySpan<string> curves = default, AlsFootSupportGeometry? supportGeometry = null) => AlsFootRigCompiler.CreateFrame(
        AlsFootRigCompilerTests.Source(), File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/refactored_foot_environment_inputs.json")),
        Names, Parents, Source(), curves.IsEmpty ? CurveNames : curves, true, supportGeometry);

    [Fact]
    public void ContactToeCannotBindToTheOppositeLeg()
    {
        var owner = Create(); var query = owner.Prepare(Input(owner), Source(), Curves());
        Assert.Throws<ArgumentException>(() => owner.Evaluate(Observe(query), new(1, new(20, -7, 10)),
            leftToe: new(1, 8, AlsPrecisePose.Identity)));
        Assert.Equal(default, owner.CommittedIdentity);
    }

    [Theory]
    [InlineData(1f)] [InlineData(.25f)] [InlineData(0f)]
    public void ToeContactKeepsAnkleAndHistoryWhileBlendingOnlyTheToe(float amount)
    {
        var owner = Create(); var baseline = Create();
        Run(owner, Input(owner), Source(), Curves()); Run(baseline, Input(baseline), Source(), Curves());
        var source = Source(); source[4] = source[4] with { Rotation = AlsQuaternion.FromAxisAngle(System.Numerics.Vector3.UnitY, .4f) };
        var toe = new AlsFootContactToeTarget(amount, 4, new(new(15, 0, 0), AlsQuaternion.Identity, AlsDoubleVector.One));
        var contact = new AlsFootContactTarget(1, owner.CommittedPose[3].Position);
        var query = owner.Prepare(Input(owner, 2), source, Curves());
        var other = baseline.Prepare(Input(baseline, 2), source, Curves());
        baseline.Evaluate(Observe(other), contact);
        var previous = owner.Committed;
        owner.Evaluate(Observe(query), contact, leftToe: toe);
        var pose = owner.Pose.ToArray(); var candidate = owner.Candidate;
        Assert.Equal(amount > 0, candidate.LeftToePinned); Assert.False(candidate.RightToePinned);
        Assert.Equal(baseline.Candidate.Left, candidate.Left); Assert.Equal(baseline.Candidate.PelvisSpring, candidate.PelvisSpring);
        for (var i = 0; i < pose.Length; i++) if (i != 4) Assert.Equal(baseline.Pose[i], pose[i]);
        var native = AlsPrecisePose.Relative(baseline.Pose[4], baseline.Pose[3]).Normalized();
        var expected = AlsPrecisePose.BlendTransform(native, toe.LocalPose, amount);
        var actual = AlsPrecisePose.Relative(pose[4], pose[3]).Normalized();
        Assert.InRange((expected.Position-actual.Position).LengthSquared, 0, 1e-16);
        Assert.InRange(1-System.Math.Abs(AlsQuaternion.Dot(expected.Rotation, actual.Rotation)), -1e-14, 1e-14);
        owner.Cancel(); Assert.Equal(previous, owner.Committed);
        var retry = owner.Prepare(Input(owner, 2), source, Curves());
        owner.Evaluate(Observe(retry), contact, leftToe: toe);
        Assert.Equal(candidate, owner.Candidate); Assert.Equal(pose, owner.Pose.ToArray());
    }

    [Fact]
    public void SupportRefinementUsesOneHistoryStepAndCancelsTheEntireCandidate()
    {
        var shape = new AlsFootSupportGeometry(Names.Length, [new(0, 1, true), new(1, 1, false)],
            [new(3, new(0, 0, -8), 1), new(7, new(0, 0, -8), 1)]);
        var owner = Create(supportGeometry: shape); var baseline = Create();
        Run(owner, Input(owner), Source(), Curves()); Run(baseline, Input(baseline), Source(), Curves());
        var old = owner.Committed; var input = Input(owner, 2);
        var query = owner.Prepare(input, Source(), Curves());
        var observations = new AlsFootRigObservations(query,
            new(true, new(query.Left.Start.X, query.Left.Start.Y, 0), new(0, 0, 1)),
            new(true, new(query.Right.Start.X, query.Right.Start.Y, 0), new(0, 0, 1)));
        owner.Evaluate(observations, calibrateLeft: true, calibrateRight: true);
        var candidate = owner.Candidate; var pose = owner.Pose.ToArray();
        Assert.True(candidate.LeftCalibrated && candidate.RightCalibrated && candidate.CalibrationPasses > 1);
        Assert.InRange(System.Math.Abs(candidate.LeftSupportDistance), 0, .01);
        Assert.InRange(System.Math.Abs(candidate.RightSupportDistance), 0, .01);
        var otherQuery = baseline.Prepare(Input(baseline, 2), Source(), Curves());
        baseline.Evaluate(observations with { Queries = otherQuery });
        Assert.Equal(baseline.Candidate.PelvisSpring, candidate.PelvisSpring);
        Assert.Equal(baseline.Candidate.Left.Location.Spring, candidate.Left.Location.Spring);
        Assert.Equal(old, owner.Committed); owner.Cancel(); Assert.Equal(old, owner.Committed);
        var retry = owner.Prepare(input, Source(), Curves());
        Assert.True(retry.RequestSerial > query.RequestSerial);
        owner.Evaluate(observations with { Queries = retry }, calibrateLeft: true, calibrateRight: true);
        Assert.Equal(candidate, owner.Candidate); Assert.Equal(pose, owner.Pose.ToArray());
        owner.Commit(input.Identity); Assert.Equal(candidate, owner.Committed);
    }
    private static AlsRefactoredFootRigInput Input(AlsRefactoredFootRigFrame owner, long frame = 1) => new(
        new(frame, 2, 1), owner.CommittedIdentity, 1f / 60, true, true, false, 1,
        new(new(20, -7, 13.5), AlsQuaternion.Identity), new(new(20, 7, 13.5), AlsQuaternion.Identity), AlsPrecisePose.Identity);

    [Theory]
    [InlineData(-20, true, 0)]
    [InlineData(20, false, 0)]
    [InlineData(-20, true, .25f)]
    [InlineData(20, false, .25f)]
    public void UnplantedClearanceOnlyRaisesPenetrationAndRetainsOneHistoryStep(double pointHeight, bool corrected, float releaseWeight)
    {
        var shape = new AlsFootSupportGeometry(Names.Length, [new(0, 1, true), new(1, 1, false)],
            [new(3, new(0, 0, pointHeight), 1), new(7, new(0, 0, 20), 1)]);
        var owner = Create(supportGeometry: shape); var baseline = Create();
        Run(owner, Input(owner), Source(), Curves(1)); Run(baseline, Input(baseline), Source(), Curves(1));
        var saved = owner.Committed; var input = Input(owner, 2);
        var query = owner.Prepare(input, Source(), Curves(1));
        var observation = new AlsFootRigObservations(query,
            new(true, new(query.Left.Start.X, query.Left.Start.Y, 0), new(0, 0, 1)),
            new(true, new(query.Right.Start.X, query.Right.Start.Y, 0), new(0, 0, 1)));
        var release = new AlsFootContactTarget(releaseWeight, new(10, -8, 12));
        owner.Evaluate(observation, release, clearLeftPenetration: true, clearRightPenetration: true);
        var candidate = owner.Candidate; var pose = owner.Pose.ToArray();
        var other = baseline.Prepare(Input(baseline, 2), Source(), Curves(1));
        baseline.Evaluate(observation with { Queries = other }, release);
        Assert.Equal(corrected, candidate.LeftClearanceCorrected);
        Assert.False(candidate.RightClearanceCorrected);
        Assert.False(candidate.LeftCalibrated);
        Assert.Equal(baseline.Candidate.PelvisSpring, candidate.PelvisSpring);
        Assert.Equal(baseline.Candidate.Left.Location.Spring, candidate.Left.Location.Spring);
        Assert.Equal(baseline.Candidate.Right.Location.Spring, candidate.Right.Location.Spring);
        Assert.Equal(baseline.Pose[7], pose[7]);
        if (corrected)
        {
            Assert.True(candidate.CalibrationPasses > 1);
            Assert.True(candidate.Left.Location.FootLocation.Z > baseline.Candidate.Left.Location.FootLocation.Z);
            Assert.InRange(System.Math.Abs(candidate.Left.Location.FootLocation.X - baseline.Candidate.Left.Location.FootLocation.X), 0, .00001);
            Assert.InRange(System.Math.Abs(candidate.Left.Location.FootLocation.Y - baseline.Candidate.Left.Location.FootLocation.Y), 0, .00001);
            Assert.InRange(shape.MinimumDistance(true, pose, input.ToWorld, observation.Left), -.01, .01);
        }
        else Assert.Equal(baseline.Pose.ToArray(), pose);
        owner.Cancel(); Assert.Equal(saved, owner.Committed);
        var retry = owner.Prepare(input, Source(), Curves(1));
        Assert.True(retry.RequestSerial > query.RequestSerial);
        owner.Evaluate(observation with { Queries = retry }, release, clearLeftPenetration: true, clearRightPenetration: true);
        Assert.Equal(candidate, owner.Candidate); Assert.Equal(pose, owner.Pose.ToArray());
        owner.Commit(input.Identity); Assert.Equal(candidate, owner.Committed);
    }
    private static AlsFootRigObservations Observe(AlsFootRigQueries query) => new(query,
        new(true, new(query.Left.Start.X, query.Left.Start.Y, -12), new(0, 0, 1)),
        new(true, new(query.Right.Start.X, query.Right.Start.Y, -6), new(0, 0, 1)));
    private static void Run(AlsRefactoredFootRigFrame owner, in AlsRefactoredFootRigInput input, AlsPrecisePose[] source, AlsInertialCurve[] curves)
    {
        var query = owner.Prepare(input, source, curves);
        owner.Evaluate(Observe(query)); owner.Commit(input.Identity);
    }

    [Fact]
    public void ReplayInputsAndFeetArePublishedOnlyWithTheCommittedFrame()
    {
        var owner = Create(); var source = Source();
        Run(owner, Input(owner), source, Curves());
        var published = owner.CommittedFeet;
        Assert.True(published.LocationEvaluated);
        Assert.Equal(owner.CommittedPose[3], published.Left);
        Assert.Equal(owner.CommittedPose[7], published.Right);
        Assert.Equal(source[3], published.SourceLeft);
        Assert.Equal(source[7], published.SourceRight);
        Assert.NotEqual(published.SourceLeft, published.Left);
        Assert.Equal(Input(owner).Left, published.TargetLeft);
        Assert.Equal(owner.CommittedPose[0].Position.Z, published.LeftInput.PelvisZ);
        Assert.Equal(owner.CommittedPose[1].Position, published.LeftInput.ThighLocation);
        Assert.Equal(owner.Committed.LeftOffset.OffsetZ, published.LeftInput.OffsetZ);
        Assert.Equal(owner.Committed.PelvisOffset, published.LeftInput.PelvisOffset);

        var changed = Input(owner, 2) with { Left = new(new(35, -7, 4), AlsQuaternion.Identity) };
        source[3] = source[3] with { Rotation = new AlsQuaternion(System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, .2f)) };
        var query = owner.Prepare(changed, source, Curves(1));
        owner.Evaluate(Observe(query));
        Assert.Equal(published, owner.CommittedFeet);
        owner.Cancel();
        Assert.Equal(published, owner.CommittedFeet);
        Run(owner, changed, source, Curves(1));
        Assert.Equal(source[3], owner.CommittedFeet.SourceLeft);
        Assert.Equal(changed.Left, owner.CommittedFeet.TargetLeft);
        Assert.Equal(changed.Left.Location, owner.CommittedFeet.LeftInput.TargetLocation);
        Assert.NotEqual(published.LeftInput.MinPelvisToFootDistance, owner.CommittedFeet.LeftInput.MinPelvisToFootDistance);
    }

    [Fact]
    public void ReplayMarksSkippedLocationEvaluationWithoutInventingSpringUpdates()
    {
        var owner = Create(); var source = Source();
        Run(owner, Input(owner), source, Curves());
        var location = owner.Committed.Left.Location;
        Run(owner, Input(owner, 2) with { FootTransformsValid = false }, source, Curves());
        Assert.False(owner.CommittedFeet.LocationEvaluated);
        Assert.Equal(location, owner.Committed.Left.Location);
        Run(owner, Input(owner, 3) with { ExecuteRig = false }, source, Curves());
        Assert.False(owner.CommittedFeet.LocationEvaluated);
        Assert.Equal(location, owner.Committed.Left.Location);
    }

    [Fact]
    public void LateRightLegFailureDiscardsLeftWritesAndAllHistoryThenAllowsIdenticalRetry()
    {
        var owner = Create(); var control = Create(); var source = Source(); var curves = Curves();
        var input = Input(owner); var oldPose = owner.CommittedPose.ToArray(); var oldState = owner.Committed;
        var broken = input with { Right = input.Right with { Rotation = new(double.NaN, 0, 0, 1) } };
        var query = owner.Prepare(broken, source, curves);
        Assert.Throws<ArgumentException>(() => owner.Evaluate(Observe(query)));
        Assert.Equal(oldState, owner.Committed); Assert.Equal(oldPose, owner.CommittedPose.ToArray()); Assert.Equal(default, owner.CommittedIdentity);
        Assert.Throws<InvalidOperationException>(() => owner.ValidateCommit(input.Identity));
        Run(owner, input, source, curves); Run(control, Input(control), source, curves);
        Assert.Equal(control.Committed, owner.Committed); Assert.Equal(control.CommittedPose.ToArray(), owner.CommittedPose.ToArray());
        Assert.Equal(curves, owner.CommittedCurves.ToArray());
    }

    [Fact]
    public void CancelledIdenticalQueriesCannotBeReusedForASameFrameRetry()
    {
        var owner = Create(); var source = Source(); var curves = Curves(); var input = Input(owner);
        var first = owner.Prepare(input, source, curves); owner.Cancel();
        var second = owner.Prepare(input, source, curves);
        Assert.Equal(first.Left, second.Left); Assert.NotEqual(first.RequestSerial, second.RequestSerial);
        Assert.Throws<ArgumentException>(() => owner.Evaluate(Observe(first)));
        Assert.Equal(default, owner.CommittedIdentity);
        Run(owner, input, source, curves);
        Assert.Equal(input.Identity, owner.CommittedIdentity);
    }

    [Fact]
    public void InvalidFootTransformsKeepOffsetAndLegHistoryButContinuePelvis()
    {
        var owner = Create(); var source = Source(); var curves = Curves();
        Run(owner, Input(owner), source, curves); var previous = owner.Committed;
        var input = Input(owner, 2) with { FootTransformsValid = false, PelvisAmount = .5f };
        var query = owner.Prepare(input, source, curves);
        Assert.False(query.LeftEnabled); Assert.False(query.RightEnabled);
        owner.Evaluate(new(query, default, default));
        Assert.Equal(previous.LeftOffset, owner.Candidate.LeftOffset); Assert.Equal(previous.RightOffset, owner.Candidate.RightOffset);
        Assert.Equal(previous.Left, owner.Candidate.Left); Assert.Equal(previous.Right, owner.Candidate.Right);
        Assert.NotEqual(previous.PelvisSpring, owner.Candidate.PelvisSpring);
        Assert.NotEqual(source[0].Position.Z, owner.Pose[0].Position.Z);
        owner.Commit(input.Identity);
    }

    [Fact]
    public void SkippedRigPreservesAllHistoryWhileCommittingPassThroughPoseAndCurves()
    {
        var owner = Create(); var source = Source(); var curves = Curves();
        Run(owner, Input(owner), source, curves); var previous = owner.Committed;
        var input = Input(owner, 2) with { ExecuteRig = false, PelvisAmount = 0 };
        var query = owner.Prepare(input, source, curves);
        Assert.False(query.LeftEnabled); Assert.False(query.RightEnabled);
        owner.Evaluate(new(query, default, default));
        Assert.Equal(previous, owner.Candidate); Assert.Equal(source, owner.Pose.ToArray()); Assert.Equal(curves, owner.Curves.ToArray());
        owner.Commit(input.Identity); Assert.Equal(input.Identity, owner.CommittedIdentity);
    }

    [Fact]
    public void DisabledCurveDiffersFromInvalidTransformsAndReinitializationIsTransactional()
    {
        var owner = Create(); var fresh = Create(); var source = Source(); var curves = Curves();
        Run(owner, Input(owner), source, curves);
        var disabled = Curves(); disabled[0] = new(0, true);
        var input = Input(owner, 2); var query = owner.Prepare(input, source, disabled);
        Assert.False(query.LeftEnabled); Assert.True(query.RightEnabled);
        owner.Evaluate(Observe(query));
        Assert.Equal(0, owner.Candidate.LeftOffset.OffsetZ); Assert.Equal(new(0, 0, 1), owner.Candidate.LeftOffset.OffsetNormal);
        Assert.NotEqual(owner.Committed.Left.Location.Spring, owner.Candidate.Left.Location.Spring);
        owner.Commit(input.Identity);
        var saved = owner.Committed;
        var reset = Input(owner, 3) with { Reinitialize = true };
        query = owner.Prepare(reset, source, curves); owner.Evaluate(Observe(query)); owner.Cancel();
        Assert.Equal(saved, owner.Committed);
        Run(owner, reset, source, curves); Run(fresh, Input(fresh, 3), source, curves);
        Assert.Equal(fresh.Committed, owner.Committed); Assert.Equal(fresh.CommittedPose.ToArray(), owner.CommittedPose.ToArray());
    }

    [Fact]
    public void NativeCurveLayoutIsRequiredAndAbsentCurveValueRemainsZero()
    {
        Assert.Throws<ArgumentException>(() => Create(new[] { "Enable_FootIK_L", "Enable_FootIK_R", "Weight_Gait", "unrelated" }));
        var owner = Create(); var source = Source(); var curves = Curves(); curves[0] = new(1, false);
        var query = owner.Prepare(Input(owner), source, curves);
        Assert.False(query.LeftEnabled); Assert.True(query.RightEnabled);
        owner.Evaluate(Observe(query)); Assert.Equal(0, owner.Candidate.LeftOffset.OffsetZ);
    }

    [Fact]
    public void IndependentOwnersMatchAcrossParallelSchedulingWithoutPerFrameAllocation()
    {
        var source = Source(); var curves = Curves(.5f);
        var serial = Enumerable.Range(0, 10).Select(_ => Create()).ToArray();
        var parallel = Enumerable.Range(0, 10).Select(_ => Create()).ToArray();
        for (var frame = 1; frame <= 48; frame++)
        {
            for (var i = 0; i < serial.Length; i++) Run(serial[i], Input(serial[i], frame), source, curves);
            Parallel.For(0, parallel.Length, i => Run(parallel[i], Input(parallel[i], frame), source, curves));
        }
        for (var i = 0; i < serial.Length; i++)
        {
            Assert.Equal(serial[i].Committed, parallel[i].Committed);
            Assert.Equal(serial[i].CommittedPose.ToArray(), parallel[i].CommittedPose.ToArray());
        }
        var owner = serial[0];
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 49; frame <= 96; frame++) Run(owner, Input(owner, frame), source, curves);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }
}
