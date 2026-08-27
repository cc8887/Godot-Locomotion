using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

public partial class P3PresentationSmoke : Node
{
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const int CharacterCount = 3;
    private const int MaximumPhysicsTicks = 240;
    private const float TransformTolerance = 0.00001f;

    private static readonly float[] LogicalYaws = [0f, MathF.PI / 2f, -MathF.PI / 2f];

    private readonly AlsP3Character[] _characters = new AlsP3Character[CharacterCount];
    private readonly long[] _lastObservedFrames = new long[CharacterCount];
    private AlsP3RuntimeContext _context = null!;
    private AlsLocomotionAnimationProfile _profile = null!;
    private AlsP3Character? _failureCharacter;
    private Node3D? _failureVisualRoot;
    private Skeleton3D? _failureSkeleton;
    private Vector3[] _failurePositions = [];
    private Quaternion[] _failureRotations = [];
    private Vector3[] _failureScales = [];
    private Transform3D _failureRootTransform;
    private ulong _failureFullPoseDigest;
    private ulong _failureRootDigest;
    private int _physicsTicks;
    private bool _initialFailure;
    private bool _quitting;

    public override void _Ready()
    {
        try
        {
            _initialFailure = ReadInitialFailureOption();
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = 3;

            var animationSetResource = ResourceLoader.Load<AlsAnimationSetResource>(
                AlsGodotImportCoordinator.CompiledResourcePath)
                ?? throw new InvalidOperationException("P3 presentation animation set resource is missing.");
            var animationSet = animationSetResource.LoadDefinition();
            _profile = AlsLocomotionProfileCompiler.Compile(
                Godot.FileAccess.GetFileAsString(ProfilePath), animationSet);
            var settings = AlsLocomotionSettings.Load(
                Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));
            _context = new AlsP3RuntimeContext(
                AlsHarnessMode.Parallel,
                settings,
                CreateMotorSettings(settings),
                animationSet,
                _profile,
                System.Environment.CurrentManagedThreadId,
                headlessOrDebug: !_initialFailure);

            ValidateProfileDefinition();
            AddChild(CreateFloor());
            if (_initialFailure)
            {
                ConfigureInitialFailure();
            }
            else
            {
                ConfigureNormalCharacters();
            }
        }
        catch (Exception exception)
        {
            Fail("init", exception);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_quitting)
        {
            return;
        }

        try
        {
            _physicsTicks++;
            Require(_physicsTicks <= MaximumPhysicsTicks,
                "P3 presentation smoke exceeded its fixed physics-tick budget.");
            if (_initialFailure)
            {
                ValidateInitialFailure();
            }
            else
            {
                ValidateNormalCharacters();
            }
        }
        catch (Exception exception)
        {
            Fail("smoke", exception);
        }
    }

    private void ConfigureNormalCharacters()
    {
        var standingOrigin = _context.MotorSettings.StandingHeight * 0.5f;
        RequireNear(standingOrigin, 0.9f, "logical standing origin did not equal 0.90 meters");
        for (var index = 0; index < CharacterCount; index++)
        {
            var character = new AlsP3Character { Name = $"PresentationCharacter_{index}" };
            AddChild(character);
            character.GlobalTransform = new Transform3D(
                new Basis(Vector3.Up, LogicalYaws[index]),
                new Vector3(index * 4f, standingOrigin, 0f));
            character.Configure(
                _context,
                new AlsSlotHandle(checked((uint)index), 1),
                new FixedIdleCommandSource(LogicalYaws[index]),
                new AlsP3ExchangeSlot());
            character.SetActive(true);
            _characters[index] = character;
        }
    }

    private void ValidateNormalCharacters()
    {
        for (var index = 0; index < CharacterCount; index++)
        {
            var character = _characters[index];
            var frame = character.Diagnostics;
            if (frame.CommittedFrameId <= 0 ||
                frame.CommittedFrameId != character.PublishedFrameId)
            {
                return;
            }
            Require(frame.CommandFrameId == frame.CommittedFrameId &&
                frame.MotorSnapshotFrameId == frame.CommittedFrameId &&
                frame.ModelResultFrameId == frame.CommittedFrameId &&
                frame.PoseAdvanceFrameId == frame.CommittedFrameId,
                $"character {index} did not publish same-frame presentation evidence");
            Require(frame.CommittedFrameId >= _lastObservedFrames[index],
                $"character {index} diagnostics moved backwards");
            _lastObservedFrames[index] = frame.CommittedFrameId;
        }

        var presentation = default(Transform3D);
        ulong markerRootDigest = 0;
        for (var index = 0; index < CharacterCount; index++)
        {
            var character = _characters[index];
            var frame = character.Diagnostics;
            var logical = character.MovementAnchor.GlobalTransform;
            var (actual, rootDigest) = ReadVisualEvidence(frame);
            if (index == 0)
            {
                presentation = ReadPresentationTransform();
                RequireVectorNear(
                    presentation.Basis * Vector3.Right,
                    Vector3.Forward,
                    "logical identity did not map raw local +X to world -Z");
            }
            var expected = logical * presentation;
            RequireTransformNear(actual, expected,
                $"character {index} visual root did not equal logical * presentation");
            RequireTransformNear(logical.AffineInverse() * actual, presentation,
                $"character {index} did not preserve the local presentation relation");
            Require(rootDigest == ComputeTransformDigest(actual),
                $"character {index} published root digest did not match its numerical snapshot");
            Require(frame.Identity == character.HandleIdentity(frame.CommittedFrameId) &&
                frame.Result.Identity == frame.Identity,
                $"character {index} published candidate identity did not equal committed identity");
            if (index == 0)
            {
                RequireNear(actual.Origin.Y, -0.02f,
                    "logical origin Y=0.90 did not produce visual origin Y=-0.02");
                markerRootDigest = rootDigest;
            }
        }

        for (var index = 0; index < CharacterCount; index++)
        {
            _characters[index].SetActive(false);
            _characters[index].DisposeRuntime();
            RemoveChild(_characters[index]);
            _characters[index].Free();
        }
        GD.Print(
            $"GODOT_ALS_P3_PRESENTATION_OK yaws={CharacterCount} identity=1 root={markerRootDigest:X16}");
        _quitting = true;
        GetTree().Quit();
    }

    private void ConfigureInitialFailure()
    {
        var standingOrigin = _context.MotorSettings.StandingHeight * 0.5f;
        var character = new AlsP3Character { Name = "InitialRollbackCharacter" };
        AddChild(character);
        character.GlobalTransform = new Transform3D(
            Basis.Identity,
            new Vector3(0f, standingOrigin, 0f));
        character.Configure(
            _context,
            new AlsSlotHandle(0, 1),
            new FixedIdleCommandSource(0f),
            new AlsP3ExchangeSlot());
        character.SetActive(true);

        var animationTree = character.FindChild(
            "AlsLocomotionAnimationTree", recursive: true, owned: false) as AnimationTree;
        Require(animationTree is not null, "real locomotion AnimationTree was not found");
        _failureVisualRoot = animationTree!.GetParent() as Node3D;
        Require(_failureVisualRoot is not null, "real locomotion visual root was not found");
        _failureSkeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(_failureVisualRoot!);
        Require(_failureSkeleton is not null, "real locomotion skeleton was not found");

        var expected = character.MovementAnchor.GlobalTransform * ReadPresentationTransform();
        RequireTransformNear(_failureVisualRoot!.GlobalTransform, expected,
            "initial visual root did not apply the corrected presentation baseline");
        CaptureFailurePose();
        animationTree.Free();
        _failureCharacter = character;
    }

    private void ValidateInitialFailure()
    {
        var character = _failureCharacter!;
        if (character.FailureDiagnosticCount == 0 || !character.IsPoseFrozen ||
            character.WorkerInFlight != 0)
        {
            return;
        }

        character.RetireForReplacement();
        Require(character.WorkerInFlight == 0,
            "initial rollback worker was still in flight after suspension");
        ValidateFailurePose();
        var runtime = character.RuntimeDiagnostics;
        Require(runtime.RollbackVerified,
            "first Worker frame did not restore the corrected baseline");
        Require(runtime.RollbackFullPoseDigest == _failureFullPoseDigest,
            "first Worker rollback full pose differed from the configured baseline");
        Require(runtime.RollbackRootDigest == _failureRootDigest,
            "first Worker rollback root differed from the corrected baseline");
        Require(character.Diagnostics.CommittedFrameId == 0,
            "failed first Worker frame advanced the committed identity");
        Require(runtime.LastPublishedPoseDigest == 0 &&
            runtime.LastPublishedFullPoseDigest == 0 &&
            runtime.LastPublishedRootDigest == 0,
            "failed first Worker frame published visual evidence");

        character.DisposeRuntime();
        RemoveChild(character);
        character.Free();
        GD.Print(
            "GODOT_ALS_P3B_INITIAL_ROLLBACK_OK mode=parallel corrected=1 " +
            $"full_pose={runtime.RollbackFullPoseDigest:X16} root={runtime.RollbackRootDigest:X16}");
        _quitting = true;
        GetTree().Quit();
    }

    private void CaptureFailurePose()
    {
        var boneCount = _failureSkeleton!.GetBoneCount();
        _failurePositions = new Vector3[boneCount];
        _failureRotations = new Quaternion[boneCount];
        _failureScales = new Vector3[boneCount];
        for (var index = 0; index < boneCount; index++)
        {
            _failurePositions[index] = _failureSkeleton.GetBonePosePosition(index);
            _failureRotations[index] = _failureSkeleton.GetBonePoseRotation(index);
            _failureScales[index] = _failureSkeleton.GetBonePoseScale(index);
        }
        _failureRootTransform = _failureVisualRoot!.GlobalTransform;
        _failureFullPoseDigest = ComputePoseDigest(_failureSkeleton);
        _failureRootDigest = ComputeTransformDigest(_failureRootTransform);
    }

    private void ValidateFailurePose()
    {
        Require(_failureVisualRoot!.GlobalTransform == _failureRootTransform,
            "initial rollback did not restore the exact corrected visual root");
        for (var index = 0; index < _failurePositions.Length; index++)
        {
            Require(_failureSkeleton!.GetBonePosePosition(index) == _failurePositions[index] &&
                _failureSkeleton.GetBonePoseRotation(index) == _failureRotations[index] &&
                _failureSkeleton.GetBonePoseScale(index) == _failureScales[index],
                $"initial rollback did not restore exact bone pose index {index}");
        }
        Require(ComputePoseDigest(_failureSkeleton!) == _failureFullPoseDigest,
            "initial rollback did not restore the quantized full pose");
        Require(ComputeTransformDigest(_failureVisualRoot.GlobalTransform) == _failureRootDigest,
            "initial rollback did not restore the quantized corrected root");
    }

    private void ValidateProfileDefinition()
    {
        RequireNear(_profile.Presentation.TranslationMeters.X, 0f,
            "profile presentation translation X drifted");
        RequireNear(_profile.Presentation.TranslationMeters.Y, -0.92f,
            "profile presentation translation Y drifted");
        RequireNear(_profile.Presentation.TranslationMeters.Z, 0f,
            "profile presentation translation Z drifted");
        RequireNear(_profile.Presentation.YawRadians, MathF.PI / 2f,
            "profile presentation yaw drifted");
    }

    private Transform3D ReadPresentationTransform() => _context.PresentationTransform;

    private static (Transform3D Transform, ulong RootDigest) ReadVisualEvidence(
        in AlsP3FrameDiagnostics frame)
    {
        var snapshot = frame.VisualRootTransform;
        return (new Transform3D(
            new Basis(
                ToGodot(snapshot.BasisX),
                ToGodot(snapshot.BasisY),
                ToGodot(snapshot.BasisZ)),
            ToGodot(snapshot.Origin)), frame.RootDigest);
    }

    private static bool ReadInitialFailureOption()
    {
        var arguments = OS.GetCmdlineUserArgs();
        return arguments.Length switch
        {
            0 => false,
            1 when arguments[0] == "--als-failure-policy=initial" => true,
            _ => throw new InvalidOperationException(
                "P3 presentation smoke accepts only optional --als-failure-policy=initial."),
        };
    }

    private static AlsMotorSettings CreateMotorSettings(AlsLocomotionSettings settings) => new(
        0.35f,
        settings.StandingHalfHeight * 2f,
        settings.CrouchedHalfHeight * 2f,
        settings.Standing,
        settings.Crouching,
        settings.InitialMaxAcceleration,
        settings.InitialMaxBrakingDeceleration,
        settings.Gravity,
        settings.JumpSpeed,
        1,
        settings.VelocityAngleInterpolationStart,
        settings.VelocityAngleInterpolationEnd);

    private static StaticBody3D CreateFloor()
    {
        var floor = new StaticBody3D
        {
            Name = "Floor",
            Position = new Vector3(0f, -0.5f, 0f),
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(100f, 1f, 100f) },
        });
        return floor;
    }

    private void Fail(string code, Exception exception)
    {
        if (_quitting)
        {
            return;
        }
        _quitting = true;
        GD.PushError($"GODOT_ALS_P3_PRESENTATION_FAIL code={code} {exception}");
        GetTree().Quit(1);
    }

    private static ulong ComputePoseDigest(Skeleton3D skeleton)
    {
        var digest = AlsResultDigest.OffsetBasis;
        for (var index = 0; index < skeleton.GetBoneCount(); index++)
        {
            Append(ref digest, skeleton.GetBonePosePosition(index));
            Append(ref digest, skeleton.GetBonePoseRotation(index));
            Append(ref digest, skeleton.GetBonePoseScale(index));
        }
        return digest;
    }

    private static ulong ComputeTransformDigest(in Transform3D transform)
    {
        var digest = AlsResultDigest.OffsetBasis;
        Append(ref digest, transform.Basis.X);
        Append(ref digest, transform.Basis.Y);
        Append(ref digest, transform.Basis.Z);
        Append(ref digest, transform.Origin);
        return digest;
    }

    private static void Append(ref ulong digest, Vector3 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
    }

    private static void Append(ref ulong digest, Quaternion value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
        Append(ref digest, value.W);
    }

    private static void Append(ref ulong digest, float value)
    {
        const ulong prime = 1099511628211UL;
        var quantized = checked((int)MathF.Round(value * 100_000f, MidpointRounding.AwayFromZero));
        for (var shift = 0; shift < 32; shift += 8)
        {
            digest ^= (byte)(quantized >> shift);
            digest *= prime;
        }
    }

    private static Vector3 ToGodot(in NumericsVector3 value) =>
        new(value.X, value.Y, value.Z);

    private static void RequireTransformNear(
        in Transform3D actual,
        in Transform3D expected,
        string message)
    {
        RequireVectorNear(actual.Basis.X, expected.Basis.X, message);
        RequireVectorNear(actual.Basis.Y, expected.Basis.Y, message);
        RequireVectorNear(actual.Basis.Z, expected.Basis.Z, message);
        RequireVectorNear(actual.Origin, expected.Origin, message);
    }

    private static void RequireVectorNear(in Vector3 actual, in Vector3 expected, string message)
    {
        Require((actual - expected).Length() <= TransformTolerance,
            $"{message}: expected={expected} actual={actual}");
    }

    private static void RequireNear(float actual, float expected, string message)
    {
        Require(MathF.Abs(actual - expected) <= TransformTolerance,
            $"{message}: expected={expected} actual={actual}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FixedIdleCommandSource(float yaw) : IAlsLocomotionCommandSource
    {
        private readonly AlsLocomotionCommand _command =
            AlsLocomotionCommand.CreateDefault() with { ViewYaw = yaw, AimYaw = yaw };

        public AlsLocomotionCommand GetCommand(long frameId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameId);
            return _command;
        }
    }
}
