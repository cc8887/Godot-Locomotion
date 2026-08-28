using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public partial class P4FootGatherSmoke : Node
{
    private static readonly NumericsVector3 LeftLocalOrigin = new(-0.2f, -0.77f, 0f);
    private static readonly NumericsVector3 RightLocalOrigin = new(0.2f, -0.77f, 0f);

    private readonly AlsP4FootProbeExchange _exchange = new();
    private AlsCharacterMotor _motor = null!;
    private AnimatableBody3D _platform = null!;
    private StaticBody3D _staticWorld = null!;
    private int _stage;
    private bool _finished;

    public override void _Ready()
    {
        try
        {
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = 4;

            _platform = new AnimatableBody3D
            {
                Name = "MovingPlatform",
                Position = new Vector3(0f, -0.1f, 0f),
                CollisionLayer = 1,
                CollisionMask = 1,
            };
            _platform.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(4f, 0.2f, 4f) },
            });
            AddChild(_platform);

            _staticWorld = new StaticBody3D
            {
                Name = "StaticWorld",
                Position = new Vector3(0f, -0.3f, 0f),
                CollisionLayer = 1,
                CollisionMask = 1,
            };
            _staticWorld.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(20f, 0.2f, 20f) },
            });
            AddChild(_staticWorld);

            _motor = new AlsCharacterMotor
            {
                Name = "Motor",
                Position = new Vector3(0f, 0.9f, 0f),
            };
            AddChild(_motor);
            _motor.Configure(
                CreateMotorSettings(),
                new IdleCommandSource(),
                _exchange,
                AlsP4FootGatherSettings.CreateReference(),
                runtimeContext: null);
            ValidateMainOwnershipSource();
        }
        catch (Exception exception)
        {
            Fail("init", exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
        {
            return;
        }

        try
        {
            if (_stage == 0)
            {
                CommitFrameOne(delta);
                _stage = 1;
                return;
            }
            GatherFrameTwoAndValidateLifecycle(delta);
        }
        catch (Exception exception)
        {
            Fail("smoke", exception);
        }
    }

    private void CommitFrameOne(double delta)
    {
        Require(
            AlsCharacterMotor.CreatePlatformId(0x00000001_00000002UL) ==
            AlsCharacterMotor.CreatePlatformId(0x00000002_00000001UL),
            "compact platform collision fixture was not constructed");
        var input = _motor.Step(1, 0, 1, checked((float)delta));
        Require(input.Identity.FrameId == 1,
            "motor/input frame one was delayed");
        Require(input.LeftFootHit == AlsFootHit.Invalid &&
                input.RightFootHit == AlsFootHit.Invalid,
            "first Gather consumed a request before Commit N");

        var result = AlsFrameResult.CreateDefault(input.Identity);
        result.NextLeftFootProbeOrigin = LeftLocalOrigin;
        result.NextRightFootProbeOrigin = RightLocalOrigin;
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                input.Identity,
                result),
            "Commit N rejected finite local probe requests");

        var rejectedExchange = new AlsP4FootProbeExchange();
        var wrongGeneration = result;
        wrongGeneration.Identity = new AlsFrameIdentity(1, 0, 2);
        Require(!AlsP3CommitStage.TryCopyFootProbeRequests(
                rejectedExchange,
                input.Identity,
                wrongGeneration) && !rejectedExchange.HasRequests,
            "Commit cached a generation-mismatched probe request");
        var nonFinite = result;
        nonFinite.NextLeftFootProbeOrigin = new NumericsVector3(float.NaN, 0f, 0f);
        Require(!AlsP3CommitStage.TryCopyFootProbeRequests(
                rejectedExchange,
                input.Identity,
                nonFinite) && !rejectedExchange.HasRequests,
            "Commit cached a non-finite probe request");

        _platform.Position = new Vector3(0.18f, -0.1f, 0.12f);
        _platform.Rotation = new Vector3(0f, 0.3f, 0f);
        _platform.ConstantLinearVelocity = new Vector3(0.4f, 0f, 0.2f);
        _platform.ConstantAngularVelocity = new Vector3(0f, 0.5f, 0f);
    }

    private void GatherFrameTwoAndValidateLifecycle(double delta)
    {
        var expectedPlatformTransform = _platform.GlobalTransform;
        var input = _motor.Step(2, 0, 1, checked((float)delta));
        Require(input.Identity.FrameId == 2,
            "motor/input frame N+1 was delayed by environment feedback");
        ValidateHit(input.LeftFootHit, LeftLocalOrigin, expectedPlatformTransform);
        ValidateHit(input.RightFootHit, RightLocalOrigin, expectedPlatformTransform);
        Require(input.Floor.PlatformId == input.LeftFootHit.PlatformId,
            "Gather did not publish current foot-platform identity into the floor evidence");
        Require(_motor.LastFootGatherManagedAllocations > 0,
            "Godot IntersectRay Dictionary allocation was incorrectly reported as zero bytes");

        var leftSnapshot = input.LeftFootHit;
        var rightSnapshot = input.RightFootHit;
        var originalPlatformPosition = _platform.Position;
        var originalPlatformRotation = _platform.Rotation;
        _platform.Position += new Vector3(0.5f, 0f, -0.25f);
        _platform.Rotation += new Vector3(0f, 0.2f, 0f);
        Require(input.LeftFootHit == leftSnapshot && input.RightFootHit == rightSnapshot,
            "published foot hits were not immutable value snapshots");
        _platform.Position = originalPlatformPosition;
        _platform.Rotation = originalPlatformRotation;

        var farProbe = AlsFrameResult.CreateDefault(new AlsFrameIdentity(2, 0, 1));
        farProbe.NextLeftFootProbeOrigin = new NumericsVector3(-3f, -0.77f, 0f);
        farProbe.NextRightFootProbeOrigin = new NumericsVector3(3f, -0.77f, 0f);
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                farProbe.Identity,
                farProbe),
            "static-world classification request setup failed");
        var splitEvidence = _motor.Step(3, 0, 1, checked((float)delta));
        Require(splitEvidence.LeftFootHit.Valid == 1 &&
                splitEvidence.RightFootHit.Valid == 1 &&
                splitEvidence.LeftFootHit.PlatformId == -1 &&
                splitEvidence.RightFootHit.PlatformId == -1 &&
                splitEvidence.LeftFootHit.ColliderId == checked((long)_staticWorld.GetInstanceId()) &&
                splitEvidence.RightFootHit.ColliderId == checked((long)_staticWorld.GetInstanceId()),
            "ordinary static world was misclassified as a moving platform");
        Require(splitEvidence.Floor.PlatformId ==
                AlsCharacterMotor.CreatePlatformId(_platform.GetInstanceId()),
            "floor evidence followed foot probes instead of the authoritative movement base");

        var missing = _motor.Step(4, 0, 1, checked((float)delta));
        Require(missing.Identity.FrameId == 4 &&
                missing.LeftFootHit == AlsFootHit.Invalid &&
                missing.RightFootHit == AlsFootHit.Invalid &&
                !_exchange.HasRequests,
            "missing Commit leaked a stale foot request into a later Gather");

        var generationOne = AlsFrameResult.CreateDefault(new AlsFrameIdentity(4, 0, 1));
        generationOne.NextLeftFootProbeOrigin = LeftLocalOrigin;
        generationOne.NextRightFootProbeOrigin = RightLocalOrigin;
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                generationOne.Identity,
                generationOne),
            "generation-one request setup failed");
        var replacement = _motor.Step(5, 0, 2, checked((float)delta));
        Require(replacement.LeftFootHit == AlsFootHit.Invalid &&
                replacement.RightFootHit == AlsFootHit.Invalid &&
                !_exchange.HasRequests,
            "replacement generation consumed an old probe request");

        var generationTwo = AlsFrameResult.CreateDefault(new AlsFrameIdentity(5, 0, 2));
        generationTwo.NextLeftFootProbeOrigin = LeftLocalOrigin;
        generationTwo.NextRightFootProbeOrigin = RightLocalOrigin;
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                generationTwo.Identity,
                generationTwo),
            "generation-two request setup failed");
        _exchange.Clear();
        var deactivated = _motor.Step(6, 0, 2, checked((float)delta));
        Require(deactivated.LeftFootHit == AlsFootHit.Invalid &&
                deactivated.RightFootHit == AlsFootHit.Invalid,
            "deactivation clear leaked a cached probe request");

        var beforeTeleport = AlsFrameResult.CreateDefault(new AlsFrameIdentity(6, 0, 2));
        beforeTeleport.NextLeftFootProbeOrigin = LeftLocalOrigin;
        beforeTeleport.NextRightFootProbeOrigin = RightLocalOrigin;
        Require(AlsP3CommitStage.TryCopyFootProbeRequests(
                _exchange,
                beforeTeleport.Identity,
                beforeTeleport),
            "teleport request setup failed");
        _motor.GlobalPosition += new Vector3(2f, 0f, 0f);
        var teleported = _motor.Step(7, 0, 2, checked((float)delta));
        Require(teleported.LeftFootHit == AlsFootHit.Invalid &&
                teleported.RightFootHit == AlsFootHit.Invalid &&
                !_exchange.HasRequests,
            "character teleport leaked a pre-discontinuity probe request");

        GD.Print("P4_FOOT_GATHER_OK latency=1");
        _finished = true;
        GetTree().Quit();
    }

    private void ValidateHit(
        in AlsFootHit hit,
        in NumericsVector3 localOrigin,
        in Transform3D expectedPlatformTransform)
    {
        Require(hit.Valid == 1 && hit.Walkable == 1,
            "Gather N+1 did not publish a walkable hit");
        var expectedRayOrigin = _motor.GlobalTransform * new Vector3(
            localOrigin.X,
            localOrigin.Y,
            localOrigin.Z);
        Require(Mathf.IsEqualApprox(hit.Position.X, expectedRayOrigin.X) &&
                Mathf.IsEqualApprox(hit.Position.Z, expectedRayOrigin.Z),
            "Gather did not transform the old pose-derived local request with the current character transform");
        Require(hit.ColliderId == checked((long)_platform.GetInstanceId()) &&
                hit.PlatformId == AlsCharacterMotor.CreatePlatformId(
                    _platform.GetInstanceId()),
            "Gather did not publish the hit collider identity");
        Require(IsApprox(hit.PlatformPosition, expectedPlatformTransform.Origin),
            "Gather did not publish the current platform position");
        var expectedRotation = expectedPlatformTransform.Basis.Orthonormalized()
            .GetRotationQuaternion().Normalized();
        Require(IsApprox(hit.PlatformRotation, expectedRotation),
            "Gather did not publish the current normalized platform rotation");

        var expectedVelocity = _platform.ConstantLinearVelocity +
                               _platform.ConstantAngularVelocity.Cross(
                                   new Vector3(hit.Position.X, hit.Position.Y, hit.Position.Z) -
                                   expectedPlatformTransform.Origin);
        Require(IsApprox(hit.PointVelocity, expectedVelocity),
            "Gather did not publish platform point velocity");
    }

    private static bool IsApprox(in NumericsVector3 actual, in Vector3 expected) =>
        Mathf.IsEqualApprox(actual.X, expected.X) &&
        Mathf.IsEqualApprox(actual.Y, expected.Y) &&
        Mathf.IsEqualApprox(actual.Z, expected.Z);

    private static bool IsApprox(in System.Numerics.Quaternion actual, in Quaternion expected) =>
        Mathf.IsEqualApprox(actual.X, expected.X) &&
        Mathf.IsEqualApprox(actual.Y, expected.Y) &&
        Mathf.IsEqualApprox(actual.Z, expected.Z) &&
        Mathf.IsEqualApprox(actual.W, expected.W);

    private static AlsMotorSettings CreateMotorSettings()
    {
        var walking = new AlsDirectionalSpeeds(1.75f, 1.5f, 1.25f);
        var running = new AlsDirectionalSpeeds(3.75f, 3.25f, 3f);
        var sprinting = new AlsDirectionalSpeeds(6.5f, 5f, 4f);
        var standing = new AlsStanceSpeeds(walking, running, sprinting);
        return new AlsMotorSettings(
            0.35f,
            1.8f,
            1.2f,
            standing,
            standing,
            20f,
            12.5f,
            9.81f,
            5f,
            1);
    }

    private static void ValidateMainOwnershipSource()
    {
        string[] mainOnlyFiles =
        [
            "res://src/Als.Godot/Locomotion/AlsCharacterMotor.cs",
            "res://src/Als.Godot/Locomotion/AlsP3CommitStage.cs",
        ];
        string[] forbiddenWorkerAccess =
        [
            "Skeleton3D",
            "AnimationTree",
            "FindChild",
            "VisualWorker",
        ];
        foreach (var resourcePath in mainOnlyFiles)
        {
            var source = File.ReadAllText(ProjectSettings.GlobalizePath(resourcePath));
            foreach (var token in forbiddenWorkerAccess)
            {
                Require(!source.Contains(token, StringComparison.Ordinal),
                    $"Main Gather/Commit source accessed Worker-owned token {token}");
            }
        }
    }

    private void Fail(string code, Exception exception)
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        GD.PushError($"GODOT_ALS_P4_FOOT_GATHER_FAIL code={code} {exception}");
        GetTree().Quit(1);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class IdleCommandSource : IAlsLocomotionCommandSource
    {
        public AlsLocomotionCommand GetCommand(long frameId) =>
            AlsLocomotionCommand.CreateDefault();
    }
}
