using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredFootAnimationFrameTests
{
    private static readonly string[] Bones = ["root", "Pelvis", "thigh_l", "calf_l", "foot_l", "thigh_r", "calf_r", "foot_r", "ik_foot_root", "ik_foot_l", "ik_foot_r"];
    private static readonly int[] Parents = [-1, 0, 1, 2, 3, 1, 5, 6, 0, 8, 8];
    private static readonly string[] Names = ["FootLeftIk", "FootRightIk", "FootLeftLock", "FootRightLock", "PoseMoving"];
    private static AlsPrecisePose[] Reference()
    {
        AlsDoubleVector[] positions = [new(0, 0, 0), new(0, 0, 100), new(0, -7, 90), new(15, -7, 50), new(0, -7, 13.5),
            new(0, 7, 90), new(15, 7, 50), new(0, 7, 13.5), new(0, 0, 0), new(0, -7, 13.5), new(0, 7, 13.5)];
        return positions.Select((p, i) =>
        {
            var local = Parents[i] < 0 ? p : p - positions[Parents[i]];
            return new AlsPrecisePose(new(local.X * .01, -local.Y * .01, local.Z * .01), AlsQuaternion.Identity, AlsDoubleVector.One);
        }).ToArray();
    }
    private static AlsRefactoredFootAnimationFrame Create(bool captureTrace = false,
        AlsFootLockBaseRotationMode mode = AlsFootLockBaseRotationMode.FullRotation, bool pinContact = false,
        bool correctUnplanted = false, AlsFootSupportGeometry? geometry = null) => AlsFootRigCompiler.CreateAnimationFrame(AlsFootRigCompilerTests.Source(),
        File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/refactored_foot_environment_inputs.json")), Bones, Parents, Reference(), Names, captureTrace,
        AlsBasedFootLockSettings.Default with { BaseRotationMode = mode, PinFinalContact = pinContact,
            CorrectUnplantedPenetration = correctUnplanted }, geometry);
    private static AlsFrameInput Frame(AlsRefactoredFootAnimationFrame owner, int frame) => AlsFrameInput.CreateDefault(new(frame, 2, 1), 1f / 60) with
    {
        FootIk = new(1, owner.CommittedIdentity, new(Vector3.Zero, AlsFootIkCoordinates.FbxToGodotRotation, Vector3.One),
            AlsLocalPose.Identity, AlsLocalPose.Identity, default, Quaternion.Identity, default, 1f / 60),
        Floor = new(1, Vector3.UnitY, -1, Matrix4x4.Identity, default)
    };
    private static AlsLocalPose[] Pose() => Reference().Select(p => p.ToSingle()).ToArray();
    private static AlsInertialCurve[] Curves() => [new(1), new(1), new(0), new(0), new(0)];
    private static void Prepare(AlsRefactoredFootAnimationFrame owner, int frame) => owner.PrepareGlobal(Frame(owner, frame),
        AlsMovementStateInput.Grounded, new(owner.CommittedIdentity, 1, 0, 0), 0);
    private static AlsFootRigObservations Miss(AlsFootRigQueries queries) => new(queries, default, default);

    [Fact]
    public void FootLockUsesSharedConfiguredMotionInsteadOfItsLegacySpeedFallback()
    {
        var feet = Create(true); var curves = Curves(); curves[2] = curves[3] = new(1);
        for (var f = 1; f <= 3; f++)
        {
            var input = Frame(feet, f) with { ActualVelocity = f == 3 ? Vector3.UnitX : Vector3.Zero };
            var observation = AlsRefactoredMotionObservation.Capture(input, 50, 75);
            Assert.Throws<ArgumentException>(() => feet.PrepareGlobal(input, AlsMovementStateInput.Grounded,
                new(feet.CommittedIdentity, 1, 0, 0), 0, observation with { Identity = new(f, 3, 1) }));
            var committed = feet.CommittedLocomotion;
            feet.PrepareGlobal(input, AlsMovementStateInput.Grounded, new(feet.CommittedIdentity, 1, 0, 0), 0, observation);
            feet.Cancel();
            Assert.Equal(committed, feet.CommittedLocomotion);
            feet.PrepareGlobal(input, AlsMovementStateInput.Grounded, new(feet.CommittedIdentity, 1, 0, 0), 0, observation);
            if (f == 3) Assert.Equal(1 - input.DeltaTime * 5, feet.CandidateLocks.Left.Amount);
            var query = feet.PrepareQueries(Pose(), curves, true); feet.Evaluate(Miss(query));
            feet.CompleteFinalOutput(input.Identity, feet.Pose, curves); feet.Commit(input.Identity);
            Assert.Equal(observation, feet.CommittedLocomotion);
        }
    }

    [Theory]
    [InlineData(30, false)] [InlineData(60, false)] [InlineData(120, false)]
    [InlineData(30, true)] [InlineData(60, true)] [InlineData(120, true)]
    public void RestReadsCurrentLocksAndPreviousTargetsBeforeGraphAndRollsBack(int hz, bool crouch)
    {
        var feet = Create(true);
        var profile = AlsRefactoredCharacterActionTests.Data.Value.Profile;
        var callbacks = new AlsRefactoredStanceCallbacks(profile.Standing.Catalog, crouch);
        var rest = new AlsRefactoredRestParentRuntime(profile.Standing.Montages.Settings, callbacks);
        var command = callbacks.Nodes.ToArray().First(c => c.Function == AlsRefactoredStanceFunction.RefreshDynamicTransitions);
        var curves = Curves(); curves[2] = curves[3] = new(1);
        for (var frame = 1; frame <= 5; frame++)
        {
            var input = Frame(feet, frame) with { DeltaTime = 1f / hz };
            // Move the animated left IK target after lock capture; next frame
            // sees this socket under a translated, uniformly scaled component.
            input = input with { FootIk = input.FootIk with { ComponentToWorld = input.FootIk.ComponentToWorld with {
                Position = new(1, 2, 3), Scale = new(1.5f) } } };
            var restInput = new AlsRefactoredRestInput(1f / hz, 0, 0, false, false, AlsRefactoredRestRotation.ViewDirection,
                crouch ? AlsRefactoredRestStance.Crouching : AlsRefactoredRestStance.Standing, true, frame == 1,
                1, 0, 0, default, default, default, default);
            void Start()
            {
                feet.PrepareGlobal(input, AlsMovementStateInput.Grounded, new(feet.CommittedIdentity, 1, 0, 0), 0);
                rest.Prepare(input.Identity, restInput);
            }
            Start(); var feedback = feet.TransitionFeedback; feedback.Validate();
            Assert.Equal(input.Identity, feedback.Identity); Assert.Equal(feet.CommittedIdentity, feedback.PoseIdentity);
            Assert.Equal(1.5f, feedback.Scale);
            var expectedTarget = new AlsDoubleVector(frame >= 3 ? -255 : -300, 89.5, 220.25);
            Assert.InRange((feedback.LeftTarget - expectedTarget).LengthSquared, 0, 1e-8);
            Assert.Equal(feet.CandidateLocks.Left.Amount, feedback.LeftAmount);
            Assert.Equal(feet.CandidateLocks.Left.WorldLock.Position, feedback.LeftLock);
            if (frame == 2) { Assert.Equal(0, feet.CommittedLocks.Left.Amount); Assert.Equal(1, feedback.LeftAmount); }
            if (frame >= 3)
                Assert.True((feedback.LeftTarget - feedback.LeftLock).LengthSquared > 20 * 20);
            rest.UpdateFeet(feedback); rest.Apply(input.Identity, command);
            var state = rest.Candidate;
            Assert.Equal(frame == 3 ? 1 : 0, rest.DynamicRequestCount);
            if (frame == 3)
                Assert.Equal(rest.Settings.DynamicSequence(crouch, true), state.QueuedTransition!.Sequence);
            var query = feet.PrepareQueries(Pose(), curves, true); feet.Evaluate(Miss(query));
            var final = feet.Pose.ToArray();
            if (frame >= 2) final[9] = final[9] with { Position = final[9].Position + new Vector3(.3f, 0, 0) };
            feet.CompleteFinalOutput(input.Identity, final, curves);
            Assert.Throws<InvalidOperationException>(() => feet.TransitionFeedback);
            // Cancel after final output has overwritten the candidate sockets.
            feet.Cancel(); rest.Cancel(); Start();
            Assert.Equal(feedback, feet.TransitionFeedback);
            rest.UpdateFeet(feet.TransitionFeedback); rest.Apply(input.Identity, command); Assert.Equal(state, rest.Candidate);
            if (rest.Candidate.QueuedTransition is { } request) rest.AcceptPlayback(frame, request, false);
            query = feet.PrepareQueries(Pose(), curves, true); feet.Evaluate(Miss(query));
            feet.CompleteFinalOutput(input.Identity, final, curves); feet.Commit(input.Identity); rest.Commit(frame);
        }
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void ClearanceRequiresAnUnplantedFootAndItsActualGroundCollider(bool locked, bool matchingCollider, bool corrected)
    {
        var shape = new AlsFootSupportGeometry(Bones.Length, [new(0, 1, true), new(1, 1, false)],
            [new(4, new(0, 0, -20), 1), new(7, new(0, 0, -20), 1)]);
        var owner = Create(mode: AlsFootLockBaseRotationMode.GravityTwist, pinContact: true, correctUnplanted: true, geometry: shape);
        var curves = Curves(); curves[2] = curves[3] = new(locked ? 1 : 0);
        for (var frame = 1; frame <= 2; frame++)
        {
            var input = Frame(owner, frame);
            input = input with { Floor = input.Floor with { ColliderId = 41 } };
            owner.PrepareGlobal(input, AlsMovementStateInput.Grounded, new(owner.CommittedIdentity, 1, 0, 0), 0);
            var query = owner.PrepareQueries(Pose(), curves, true);
            owner.Evaluate(new(query,
                new(true, new(query.Left.Start.X, query.Left.Start.Y, 0), new(0, 0, 1)) { ColliderIdentity = matchingCollider ? 41UL : 42UL },
                new(true, new(query.Right.Start.X, query.Right.Start.Y, 0), new(0, 0, 1)) { ColliderIdentity = matchingCollider ? 41UL : 42UL }));
            Assert.Equal(frame == 2 && corrected, owner.CandidateRig.LeftClearanceCorrected);
            Assert.Equal(frame == 2 && corrected, owner.CandidateRig.RightClearanceCorrected);
            owner.CompleteFinalOutput(input.Identity, owner.Pose, curves); owner.Commit(input.Identity);
        }
    }

    [Fact]
    public void ToeContactRequiresActualGeometryAndBothToeBindings()
    {
        var settings = AlsBasedFootLockSettings.Default with {
            BaseRotationMode = AlsFootLockBaseRotationMode.GravityTwist, PinFinalContact = true, PinContactToes = true };
        var environment = File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/refactored_foot_environment_inputs.json"));
        var noGeometry = Assert.Throws<ArgumentException>(() => AlsFootRigCompiler.CreateAnimationFrame(
            AlsFootRigCompilerTests.Source(), environment, Bones, Parents, Reference(), Names, false, settings));
        Assert.Contains("geometry", noGeometry.Message);
        var geometry = new AlsFootSupportGeometry(Bones.Length, [new(0, 1, true), new(1, 1, false)],
            [new(4, new(0, 0, -20), 1), new(7, new(0, 0, -20), 1)]);
        var noToes = Assert.Throws<ArgumentException>(() => AlsFootRigCompiler.CreateAnimationFrame(
            AlsFootRigCompilerTests.Source(), environment, Bones, Parents, Reference(), Names, false, settings, geometry));
        Assert.Contains("ball_l", noToes.Message);
    }

    [Fact]
    public void ToeContactHistoryFollowsItsSurfaceAndSurvivesLateRollback()
    {
        string[] bones = [..Bones, "ball_l", "ball_r"]; int[] parents = [..Parents, 4, 7];
        AlsPrecisePose[] reference = [..Reference(), new(new(.15, 0, 0), AlsQuaternion.Identity, AlsDoubleVector.One),
            new(new(.15, 0, 0), AlsQuaternion.Identity, AlsDoubleVector.One)];
        var geometry = new AlsFootSupportGeometry(bones.Length, [new(0, 1, true), new(1, 1, false)],
            [new(11, new(0, 0, -20), 1), new(12, new(0, 0, -20), 1)]);
        var owner = AlsFootRigCompiler.CreateAnimationFrame(AlsFootRigCompilerTests.Source(),
            File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/refactored_foot_environment_inputs.json")),
            bones, parents, reference, Names, true, AlsBasedFootLockSettings.Default with {
                BaseRotationMode = AlsFootLockBaseRotationMode.GravityTwist, PinFinalContact = true, PinContactToes = true }, geometry);
        AlsLocalPose savedToe = default;
        for (var frame = 1; frame <= 8; frame++)
        {
            var source = reference.Select(p=>p.ToSingle()).ToArray();
            source[11] = source[11] with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, frame * .08f) };
            var curves = Curves(); curves[2] = curves[3] = new(1); if (frame == 7) curves[0] = new(0);
            var input = Frame(owner, frame); input = input with { Floor = input.Floor with { ColliderId = 41 } };
            void Start() => owner.PrepareGlobal(input, AlsMovementStateInput.Grounded, new(owner.CommittedIdentity,1,0,0),0);
            AlsFootRigObservations Observe(AlsFootRigQueries q) => new(q,
                new(true,new(q.Left.Start.X,q.Left.Start.Y,0),new(0,0,1)) { ColliderIdentity = frame == 4 ? 42UL : 41UL },
                new(true,new(q.Right.Start.X,q.Right.Start.Y,0),new(0,0,1)) { ColliderIdentity = 41 });
            Start(); var query = owner.PrepareQueries(source, curves, true); owner.Evaluate(Observe(query));
            var candidate = owner.CandidateRig; var pose = owner.Pose.ToArray();
            Assert.Equal(frame is 3 or 6, candidate.LeftToePinned);
            if (frame is 2 or 5) savedToe = pose[11];
            if (frame is 3 or 6) {
                Assert.InRange(Vector3.Distance(savedToe.Position, pose[11].Position), 0, 1e-6f);
                Assert.InRange(1-MathF.Abs(Quaternion.Dot(savedToe.Rotation,pose[11].Rotation)), -1e-6f, 1e-6f);
            }
            var broken = (AlsInertialCurve[])curves.Clone(); broken[0]=new(float.NaN);
            Assert.Throws<ArgumentException>(()=>owner.CompleteFinalOutput(input.Identity,pose,broken));
            Start(); query = owner.PrepareQueries(source, curves, true); owner.Evaluate(Observe(query));
            Assert.Equal(candidate,owner.CandidateRig); Assert.Equal(pose,owner.Pose.ToArray());
            owner.CompleteFinalOutput(input.Identity,pose,curves); owner.Commit(input.Identity);
        }
    }

    [Fact]
    public void ReleasedStaticLockRetainsItsCurveAmountWhileCorrectingPenetration()
    {
        var shape = new AlsFootSupportGeometry(Bones.Length, [new(0, 1, true), new(1, 1, false)],
            [new(4, new(0, 0, -20), 1), new(7, new(0, 0, -20), 1)]);
        var owner = Create(mode: AlsFootLockBaseRotationMode.GravityTwist, pinContact: true, correctUnplanted: true, geometry: shape);
        var curves = Curves(); curves[2] = curves[3] = new(1);
        for (var frame = 1; frame <= 3; frame++)
        {
            var input = Frame(owner, frame);
            input = input with { Floor = input.Floor with { ColliderId = 41 } };
            owner.PrepareGlobal(input, AlsMovementStateInput.Grounded, new(owner.CommittedIdentity, 1, 0, 0), 0);
            var query = owner.PrepareQueries(Pose(), curves, true);
            // A lower first support establishes a lower anchor. During release,
            // the newly observed higher plane must not drag that anchor upward.
            var z = frame < 3 ? -5d : 0d;
            owner.Evaluate(new(query,
                new(true, new(query.Left.Start.X, query.Left.Start.Y, z), new(0, 0, 1)) { ColliderIdentity = 41 },
                new(true, new(query.Right.Start.X, query.Right.Start.Y, z), new(0, 0, 1)) { ColliderIdentity = 41 }));
            if (frame == 3)
            {
                Assert.Equal(.25f, owner.CandidateLocks.Left.Amount);
                Assert.True(owner.CandidateRig.LeftClearanceCorrected);
                Assert.False(owner.CandidateRig.LeftCalibrated);
            }
            if (frame == 2) curves[2] = curves[3] = new(.25f);
            owner.CompleteFinalOutput(input.Identity, owner.Pose, curves); owner.Commit(input.Identity);
        }
    }

    [Fact]
    public void StaticContactCalibratesOnceReleasesOnSupportChangeAndSurvivesLateRetry()
    {
        var shape = new AlsFootSupportGeometry(Bones.Length, [new(0, 1, true), new(1, 1, false)],
            [new(4, new(0, 0, -20), 1), new(7, new(0, 0, -20), 1)]);
        var owner = Create(true, AlsFootLockBaseRotationMode.GravityTwist, true, geometry: shape);
        var curves = Curves(); curves[2] = curves[3] = new(1);
        var pinnedFrames = 0;
        for (var frame = 1; frame <= 16; frame++)
        {
            var input = Frame(owner, frame);
            // No platform matrix is required for a StaticBody. Replacing the
            // actual support must invalidate the old world anchor.
            input = input with { Floor = input.Floor with { ColliderId = frame >= 6 ? 42 : 41,
                PlatformTransform = default, IsGrounded = frame == 10 ? (byte)0 : (byte)1 } };
            void Start() => owner.PrepareGlobal(input, frame == 10 ? AlsMovementStateInput.InAir : AlsMovementStateInput.Grounded,
                new(owner.CommittedIdentity, 1, 0, 0), 0);
            AlsFootRigObservations Observe(AlsFootRigQueries q)
            {
                var collider = frame >= 6 ? 42UL : 41UL;
                if (frame == 8) collider = 43;
                var z = frame >= 6 ? 1d : 0d;
                return new(q, new(true, new(q.Left.Start.X, q.Left.Start.Y, z), new(0, 0, 1)) { ColliderIdentity = collider },
                    new(true, new(q.Right.Start.X, q.Right.Start.Y, z), new(0, 0, 1)) { ColliderIdentity = collider });
            }
            Start(); var query = owner.PrepareQueries(Pose(), curves, true); owner.Evaluate(Observe(query));
            var rig = owner.CandidateRig; var locked = owner.CandidateLocks; var pose = owner.Pose.ToArray();
            Assert.Equal(0UL, locked.Left.BaseIdentity);
            if (frame is 2 or 6 or 9 or 11)
            {
                Assert.True(rig.LeftCalibrated, $"Expected new static contact at frame {frame}; amount={locked.Left.Amount}, constrained={locked.Left.ThighConstrained}.");
                Assert.InRange(System.Math.Abs(rig.LeftSupportDistance), 0, .05);
            }
            if (frame is 1 or 8 or 10) Assert.False(rig.Left.Location.ContactApplied);
            if (rig.Left.Location.ContactApplied && !rig.LeftCalibrated) pinnedFrames++;
            var previous = owner.CommittedRig;
            var broken = (AlsInertialCurve[])curves.Clone(); broken[0] = new(float.NaN);
            Assert.Throws<ArgumentException>(() => owner.CompleteFinalOutput(input.Identity, pose, broken));
            Assert.Equal(previous, owner.CommittedRig);
            Start(); var retry = owner.PrepareQueries(Pose(), curves, true);
            Assert.NotEqual(query.RequestSerial, retry.RequestSerial);
            owner.Evaluate(Observe(retry));
            Assert.Equal(rig, owner.CandidateRig); Assert.Equal(locked, owner.CandidateLocks); Assert.Equal(pose, owner.Pose.ToArray());
            owner.CompleteFinalOutput(input.Identity, owner.Pose, curves); owner.Commit(input.Identity);
        }
        Assert.True(pinnedFrames >= 6);
    }

    [Fact]
    public void ClearanceCannotSilentlyRunWithoutSupportGeometry() => Assert.Throws<ArgumentException>(() =>
        Create(mode: AlsFootLockBaseRotationMode.GravityTwist, pinContact: true, correctUnplanted: true));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalLockTracePublishesOnlyCommittedInputsAndSurvivesCancellation(bool captureTrace)
    {
        var owner = Create(captureTrace);
        for (var frame = 1; frame <= 2; frame++)
        {
            Prepare(owner, frame); var query = owner.PrepareQueries(Pose(), Curves(), true);
            owner.Evaluate(Miss(query)); owner.CompleteFinalOutput(new(frame, 2, 1), Pose(), Curves());
            owner.Commit(new(frame, 2, 1));
        }
        var published = owner.CommittedLockTrace;
        if (captureTrace)
        {
            Assert.NotNull(published);
            Assert.Equal(owner.CommittedIdentity, published.Identity);
            Assert.Equal(owner.CommittedLocks.Left, published.Left.Result);
        }
        else Assert.Null(published);
        Prepare(owner, 3); var pending = owner.PrepareQueries(Pose(), Curves(), true);
        owner.Evaluate(Miss(pending));
        Assert.Same(published, owner.CommittedLockTrace);
        owner.Cancel(); Assert.Same(published, owner.CommittedLockTrace);
        Assert.Equal(new(2, 2, 1), owner.CommittedIdentity);
    }

    [Fact]
    public void PendingFirstFrameSkipsTracesThenUsesPreviousFinalSocketsInsteadOfCurrentTargets()
    {
        var owner = Create(); var final = Pose(); final[9] = final[9] with { Position = final[9].Position + new Vector3(.25f, 0, 0) };
        Prepare(owner, 1); var cold = owner.PrepareQueries(Pose(), Curves(), true);
        Assert.False(cold.LeftEnabled); Assert.False(cold.RightEnabled);
        owner.Evaluate(Miss(cold)); owner.CompleteFinalOutput(Frame(owner, 1).Identity, final, Curves()); owner.Commit(new(1, 2, 1));
        var source = Pose(); source[9] = source[9] with { Position = new(99, 99, 99) };
        Prepare(owner, 2); var queries = owner.PrepareQueries(source, Curves(), true);
        Assert.True(queries.LeftEnabled); Assert.InRange(queries.Left.Start.X, 24.99999, 25.00001);
        Assert.InRange(owner.CandidateLocks.Left.FinalComponent.Position.X, 24.99999, 25.00001);
        owner.Cancel(); Assert.Equal(new(1, 2, 1), owner.CommittedIdentity);
    }

    [Fact]
    public void InvalidInitialPelvisDelaysFootValidityUntilARealFinalSocketIsAvailable()
    {
        var owner = Create(); var invalid = Pose(); invalid[1] = AlsLocalPose.Identity;
        Prepare(owner, 1); var query = owner.PrepareQueries(Pose(), Curves(), true); owner.Evaluate(Miss(query));
        owner.CompleteFinalOutput(new(1, 2, 1), invalid, Curves()); owner.Commit(new(1, 2, 1));
        Prepare(owner, 2); query = owner.PrepareQueries(Pose(), Curves(), true);
        Assert.False(query.LeftEnabled); owner.Evaluate(Miss(query));
        owner.CompleteFinalOutput(new(2, 2, 1), Pose(), Curves()); owner.Commit(new(2, 2, 1));
        Prepare(owner, 3); query = owner.PrepareQueries(Pose(), Curves(), true); Assert.True(query.LeftEnabled); owner.Cancel();
    }

    [Fact]
    public void CurrentGraphIkCurveControlsQueriesButLockUpdateReadsPreviousFinalCurve()
    {
        var owner = Create(); var curves = Curves(); curves[2] = new(1);
        Prepare(owner, 1); var query = owner.PrepareQueries(Pose(), curves, true); owner.Evaluate(Miss(query));
        owner.CompleteFinalOutput(new(1, 2, 1), Pose(), curves); owner.Commit(new(1, 2, 1));
        Prepare(owner, 2);
        Assert.Equal(1, owner.CandidateLocks.Left.Amount); // previous final curve
        curves[0] = new(0); curves[2] = new(0);
        query = owner.PrepareQueries(Pose(), curves, true);
        Assert.False(query.LeftEnabled); Assert.True(query.RightEnabled); // current pre-foot graph
        owner.Cancel();
    }

    [Theory]
    [InlineData(AlsFootLockBaseRotationMode.FullRotation)]
    [InlineData(AlsFootLockBaseRotationMode.GravityTwist)]
    public void CanceledQueryAndLateFinalFailureCannotCommitAnyLockOrRigHistory(AlsFootLockBaseRotationMode mode)
    {
        var owner = Create(mode: mode); var oldRig = owner.CommittedRig; var oldLocks = owner.CommittedLocks;
        Prepare(owner, 1); var stale = owner.PrepareQueries(Pose(), Curves(), true); owner.Cancel();
        Prepare(owner, 1); var current = owner.PrepareQueries(Pose(), Curves(), true);
        Assert.NotEqual(stale.RequestSerial, current.RequestSerial);
        Assert.Throws<ArgumentException>(() => owner.Evaluate(Miss(stale)));
        Prepare(owner, 1); current = owner.PrepareQueries(Pose(), Curves(), true); owner.Evaluate(Miss(current));
        Assert.Throws<InvalidOperationException>(() => owner.Commit(new(1, 2, 1)));
        var broken = Curves(); broken[0] = new(float.NaN);
        Assert.Throws<ArgumentException>(() => owner.CompleteFinalOutput(new(1, 2, 1), Pose(), broken));
        Assert.Equal(default, owner.CommittedIdentity); Assert.Equal(oldRig, owner.CommittedRig); Assert.Equal(oldLocks, owner.CommittedLocks);
    }

    [Theory]
    [InlineData(AlsFootLockBaseRotationMode.FullRotation, false)]
    [InlineData(AlsFootLockBaseRotationMode.GravityTwist, false)]
    [InlineData(AlsFootLockBaseRotationMode.GravityTwist, true)]
    public void TiltedPlatformWarmHistorySurvivesLateCancellationAndExactRetry(AlsFootLockBaseRotationMode mode, bool pinContact)
    {
        var owner = Create(true, mode, pinContact); var pinned = 0;
        var curves = Curves(); curves[2] = curves[3] = new(1);
        for (var frame = 1; frame <= 60; frame++)
        {
            var input = Frame(owner, frame) with { Floor = new(1, Vector3.UnitY, 7,
                Matrix4x4.CreateRotationZ(frame * .002f), default, 7) };
            void Start() => owner.PrepareGlobal(input, AlsMovementStateInput.Grounded,
                new(owner.CommittedIdentity, 1, 0, 0), 0);
            AlsFootRigObservations Observe(AlsFootRigQueries query)
            {
                if (!pinContact) return Miss(query);
                var normal = new AlsDoubleVector(0, 0, 1).Rotate(AlsQuaternion.FromAxisAngle(Vector3.UnitX, frame * .002f));
                AlsFootTraceRigHit Hit(AlsFootTraceSegment ray)
                {
                    var direction = ray.End - ray.Start;
                    static double Dot(AlsDoubleVector a, AlsDoubleVector b) => a.X*b.X + a.Y*b.Y + a.Z*b.Z;
                    var point = ray.Start + direction * (-Dot(ray.Start, normal) / Dot(direction, normal));
                    return new(true, point, normal) { ColliderIdentity = frame == 30 ? 8ul : 7ul };
                }
                return new(query, query.LeftEnabled ? Hit(query.Left) : default, query.RightEnabled ? Hit(query.Right) : default);
            }
            Start(); var query = owner.PrepareQueries(Pose(), curves, true);
            owner.Evaluate(Observe(query));
            var candidate = owner.CandidateLocks; var rig = owner.CandidateRig;
            if (rig.Left.Location.ContactApplied) pinned++;
            if (frame == 30) Assert.False(rig.Left.Location.ContactApplied);
            var pose = owner.Pose.ToArray();
            var published = owner.CommittedLocks; var trace = owner.CommittedLockTrace;
            owner.CompleteFinalOutput(input.Identity, pose, curves);
            owner.Cancel();
            Assert.Equal(published, owner.CommittedLocks); Assert.Same(trace, owner.CommittedLockTrace);
            Start(); query = owner.PrepareQueries(Pose(), curves, true);
            owner.Evaluate(Observe(query));
            Assert.Equal(candidate, owner.CandidateLocks); Assert.Equal(rig, owner.CandidateRig);
            Assert.Equal(pose, owner.Pose.ToArray());
            owner.CompleteFinalOutput(input.Identity, pose, curves); owner.Commit(input.Identity);
            if (frame > 1) Assert.Equal(1, owner.CommittedLocks.Left.Amount);
        }
        if (pinContact) Assert.True(pinned > 40, "Warm rollback must exercise actual pinned contacts.");
    }

    [Fact]
    public void NoMovingBaseDoesNotReadAnUnspecifiedPlatformMatrix()
    {
        var owner = Create(true, AlsFootLockBaseRotationMode.GravityTwist, true);
        var curves = Curves(); curves[2] = curves[3] = new(1);
        for (var frame = 1; frame <= 3; frame++)
        {
            var input = Frame(owner, frame) with { Floor = default };
            owner.PrepareGlobal(input, AlsMovementStateInput.InAir, new(owner.CommittedIdentity, 1, 0, 0), 0);
            var query = owner.PrepareQueries(Pose(), curves, true);
            owner.Evaluate(Miss(query));
            Assert.False(owner.CandidateRig.Left.Location.ContactApplied);
            owner.CompleteFinalOutput(input.Identity, owner.Pose, curves); owner.Commit(input.Identity);
        }
    }

    [Fact]
    public void HiddenRigCommitsActualAlternateFinalSocketsWithoutPublishingAStaleFootPose()
    {
        var owner = Create(); var alternate = Pose(); alternate[9] = alternate[9] with { Position = new(.1f, .2f, .3f) };
        Prepare(owner, 1); var queries = owner.PrepareQueries([], Curves(), false); owner.Evaluate(Miss(queries));
        Assert.Throws<InvalidOperationException>(() => owner.Pose.ToArray());
        owner.CompleteFinalOutput(new(1, 2, 1), alternate, Curves()); owner.Commit(new(1, 2, 1));
        Assert.Equal(default, owner.CommittedPoseIdentity); Assert.Equal(new(1, 2, 1), owner.CommittedIdentity);
        Assert.InRange(owner.CommittedLocks.LeftTarget.Position.X, 9.99999, 10.00001);
        Assert.InRange(owner.CommittedLocks.LeftTarget.Position.Y, -20.00001, -19.99999);
        Assert.InRange(owner.CommittedLocks.LeftTarget.Position.Z, 29.99999, 30.00001);
    }
}
