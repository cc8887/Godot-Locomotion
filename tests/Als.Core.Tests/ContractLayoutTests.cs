using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Pose;

namespace GodotAls.Core.Tests;

public sealed class ContractLayoutTests
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceGodot()
    {
        var references = typeof(AlsFrameInput).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            name => name.Name?.StartsWith("Godot", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void FrameContractsContainOnlyUnmanagedData()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameIdentity>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameInput>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsRuntimeState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameResult>());
    }

    [Fact]
    public void P3ContractsContainOnlyUnmanagedData()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsLocomotionCommand>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameInput>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsRuntimeState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameResult>());
    }

    [Fact]
    public void P4ContractsContainOnlyUnmanagedData()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsViewPoseState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsTurnInPlaceState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsRotateInPlaceState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFootLockState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsPelvisCorrectionState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFootPoseOutput>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsLocomotionCommand>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameInput>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsRuntimeState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameResult>());
    }

    [Fact]
    public void P4StateContractsKeepTheirDeclaredFieldOrder()
    {
        AssertFieldOrder<AlsLocomotionCommand>(
            "MovementAxes", "ViewYaw", "ViewPitch", "AimYaw", "AimPitch", "RequestedGait",
            "RequestedStance", "RequestedRotationMode", "JumpPressed");
        AssertFieldOrder<AlsViewPoseState>(
            "RelativeYaw", "RelativePitch", "YawSpeed", "HeadWeight", "SpineWeight",
            "SpineResidualYaw", "LastWorldYaw");
        AssertFieldOrder<AlsTurnInPlaceState>(
            "ActivationSeconds", "Phase", "PlayRate", "RemainingYaw", "NominalDegrees",
            "Direction", "Active", "Stance");
        AssertFieldOrder<AlsRotateInPlaceState>("Phase", "PlayRate", "Direction", "Active", "Stance");
        AssertFieldOrder<AlsTurnRotateSelection>(
            "YawSource", "AnimationId", "CurveId", "PreviousPhase", "CurrentPhase", "DeltaTime",
            "PhasePlayRate", "YawScale", "EffectiveDeltaTime", "PhaseTravel", "Duration",
            "BlendSeconds", "RemainingYaw", "NominalDegrees", "Direction", "ScaleAngle", "Active");
        AssertStorageFieldOrder<AlsTurnRotateSelection>(
            "YawSource", "AnimationId", "CurveId", "PreviousPhase", "CurrentPhase", "DeltaTime",
            "PhasePlayRate", "YawScale", "EffectiveDeltaTime", "PhaseTravel", "Duration",
            "BlendSeconds", "RemainingYaw", "NominalDegrees", "Direction", "ScaleAngle", "Active");
        AssertFieldOrder<AlsFootLockState>(
            "LocalPosition", "LocalRotation", "Offset", "Rotation", "ProvenancePosition",
            "ProvenanceRotation", "PlatformId", "ColliderId", "Amount", "Locked", "ReleaseReason");
        AssertFieldOrder<AlsPelvisCorrectionState>(
            "CurrentOffset", "TargetOffset", "VerticalVelocity");
        AssertFieldOrder<AlsFootHit>(
            "Valid", "Walkable", "Position", "Normal", "PlatformId", "PlatformPosition",
            "PlatformRotation", "ColliderId", "PointVelocity");
        AssertFieldOrder<AlsFloorSample>(
            "IsGrounded", "Normal", "PlatformId", "PlatformTransform",
            "PlatformAngularVelocity", "ColliderId");
        AssertFieldOrder<AlsFootPoseOutput>("Position", "Rotation", "LockAmount", "PlatformId");
    }

    [Fact]
    public void FloorSamplePreservesLegacyArityAndExposesFullColliderIdentity()
    {
        var legacy = new AlsFloorSample(
            1,
            Vector3.UnitY,
            7,
            Matrix4x4.Identity,
            new Vector3(0f, 0.5f, 0f));
        var (grounded, normal, platformId, platformTransform, angularVelocity) = legacy;

        Assert.Equal((byte)1, grounded);
        Assert.Equal(Vector3.UnitY, normal);
        Assert.Equal(7, platformId);
        Assert.Equal(Matrix4x4.Identity, platformTransform);
        Assert.Equal(new Vector3(0f, 0.5f, 0f), angularVelocity);
        Assert.Equal(-1L, legacy.ColliderId);

        var current = new AlsFloorSample(
            1,
            Vector3.UnitY,
            7,
            Matrix4x4.Identity,
            new Vector3(0f, 0.5f, 0f),
            0x00000001_00000002L);
        var (_, _, _, _, _, colliderId) = current;

        Assert.Equal(0x00000001_00000002L, colliderId);
    }

    [Fact]
    public void TopLevelFrameContractsKeepTheirStorageFieldOrder()
    {
        AssertStorageFieldOrder<AlsFrameInput>(
            "Identity", "DeltaTime", "CharacterTransform", "ActualVelocity", "ActualAcceleration",
            "InputDirection", "DesiredSpeed", "ViewRotation", "AimRotation", "Floor",
            "LeftFootHit", "RightFootHit", "MantleProbe", "RequestedGait", "Stance",
            "RotationMode", "RequestedAction", "CurrentDriveMode", "RagdollState",
            "AnimationQualityTier", "Command", "CharacterYaw", "MaxAcceleration",
            "MaxBrakingDeceleration", "JumpAccepted");
        AssertStorageFieldOrder<AlsRuntimeState>(
            "LocomotionState", "SmoothedVelocity", "SmoothedAcceleration", "Lean",
            "LeftFootLocked", "RightFootLocked", "TurnInPlaceTime", "RotateInPlaceTime",
            "ActionPlaybackTime", "AnimationPhase", "PreviousCurveValue", "PendingRecoveryState",
            "LastCommittedRootMotionFeedback", "ActualGait", "PreviousLocomotionState",
            "GroundedEntrySpeed", "SmoothedLocalVelocity", "SmoothedLocalAcceleration",
            "SmoothedLean", "LandingRecoveryTime", "SmoothedTargetYaw", "TargetYaw",
            "YawSource", "JumpStartActive", "Initialized", "ViewPose", "TurnInPlace", "RotateInPlace",
            "LeftFootLock", "RightFootLock", "PelvisCorrection", "LeftFootProbeOrigin",
            "RightFootProbeOrigin");
        AssertStorageFieldOrder<AlsFrameResult>(
            "Identity", "ResolvedLocomotionState", "RequestedDriveMode", "ProposedRootMotionDelta",
            "PelvisTarget", "LeftFootTarget", "RightFootTarget", "MovementIntent",
            "RotationIntent", "TypedEvents", "WorkerElapsedTicks", "ErrorCode", "ActualGait",
            "ActualStance", "ActualRotationMode", "AnimationState", "BlendCoordinates", "Stride",
            "PlayRate", "Lean", "AnimationPhase", "TargetYaw", "AimRelativeYaw",
            "AimRelativePitch", "HeadWeight", "SpineWeight", "UpperBodyWeight",
            "SpineResidualYaw", "TurnAnimationId", "TurnCurveId", "TurnPhase", "TurnPlayRate",
            "TurnNominalDegrees", "TurnDirection", "TurnActive", "TurnYawDelta",
            "RotateAnimationId", "RotateCurveId", "RotatePhase", "RotatePlayRate",
            "RotateDirection", "RotateActive", "RotateYawDelta", "PelvisOffset", "LeftFootPose",
            "RightFootPose", "LeftFootReleaseReason", "RightFootReleaseReason",
            "LeftFootIkWeight", "RightFootIkWeight", "LeftFootLockCurve", "RightFootLockCurve",
            "NextLeftFootProbeOrigin", "NextRightFootProbeOrigin",
            "P4ModifierOperationTicks", "P4ReasonCode");
    }

    [Fact]
    public void TurnRotateSelectionHasReadOnlyPublicSurface()
    {
        var type = typeof(AlsTurnRotateSelection);

        Assert.Empty(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public));
        Assert.All(
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => Assert.False(property.SetMethod?.IsPublic == true));
        Assert.True(type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
        Assert.Equal(LayoutKind.Sequential, type.StructLayoutAttribute?.Value);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsTurnRotateSelection>());
    }

    [Fact]
    public void CoreInternalsAreVisibleOnlyToCoreTests()
    {
        var friendAssemblies = typeof(AlsTurnRotateSelection).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToArray();

        Assert.Equal(new[] { "Als.Core.Tests" }, friendAssemblies);
    }

    [Fact]
    public void P4ReasonAndTimingStorageWidthsAreStable()
    {
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(AlsP4ReasonCode)));
        Assert.Equal(
            typeof(long),
            typeof(AlsFrameResult).GetField(nameof(AlsFrameResult.P4ModifierOperationTicks))?.FieldType);
    }

    [Fact]
    public void FootReleaseReasonIsAByteContractsEnumStoredByFootLockState()
    {
        Assert.Equal("GodotAls.Core.Contracts", typeof(AlsFootReleaseReason).Namespace);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(AlsFootReleaseReason)));
        Assert.Equal(
            typeof(AlsFootReleaseReason),
            typeof(AlsFootLockState).GetProperty("ReleaseReason")?.PropertyType);
    }

    [Fact]
    public void DefaultCommandIsStandingRunningLookingDirection()
    {
        var command = AlsLocomotionCommand.CreateDefault();

        Assert.Equal(Vector2.Zero, command.MovementAxes);
        Assert.Equal(0f, command.ViewYaw);
        Assert.Equal(0f, command.ViewPitch);
        Assert.Equal(0f, command.AimYaw);
        Assert.Equal(0f, command.AimPitch);
        Assert.Equal(AlsGait.Running, command.RequestedGait);
        Assert.Equal(AlsStance.Standing, command.RequestedStance);
        Assert.Equal(AlsRotationMode.LookingDirection, command.RequestedRotationMode);
        Assert.Equal((byte)0, command.JumpPressed);
    }

    [Fact]
    public void DefaultFrameInputUsesInvalidFootHitIdentities()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(0, 0, 1), 1f / 60f);

        Assert.Equal(-1L, input.Floor.ColliderId);
        Assert.Equal((byte)0, input.LeftFootHit.Valid);
        Assert.Equal((byte)0, input.LeftFootHit.Walkable);
        Assert.Equal(-1, input.LeftFootHit.PlatformId);
        Assert.Equal(-1L, input.LeftFootHit.ColliderId);
        Assert.Equal(Quaternion.Identity, input.LeftFootHit.PlatformRotation);
        Assert.Equal((byte)0, input.RightFootHit.Valid);
        Assert.Equal((byte)0, input.RightFootHit.Walkable);
        Assert.Equal(-1, input.RightFootHit.PlatformId);
        Assert.Equal(-1L, input.RightFootHit.ColliderId);
        Assert.Equal(Quaternion.Identity, input.RightFootHit.PlatformRotation);
    }

    [Fact]
    public void DefaultRuntimeStateUsesInvalidFootLockIdentities()
    {
        var state = AlsRuntimeState.CreateDefault();

        Assert.Equal(-1, state.LeftFootLock.PlatformId);
        Assert.Equal(-1L, state.LeftFootLock.ColliderId);
        Assert.Equal(Quaternion.Identity, state.LeftFootLock.LocalRotation);
        Assert.Equal(Quaternion.Identity, state.LeftFootLock.Rotation);
        Assert.Equal(-1, state.RightFootLock.PlatformId);
        Assert.Equal(-1L, state.RightFootLock.ColliderId);
        Assert.Equal(Quaternion.Identity, state.RightFootLock.LocalRotation);
        Assert.Equal(Quaternion.Identity, state.RightFootLock.Rotation);
        Assert.Equal(Vector3.Zero, state.LeftFootProbeOrigin);
        Assert.Equal(Vector3.Zero, state.RightFootProbeOrigin);
        Assert.Equal(default, state.ViewPose);
        Assert.Equal(0f, state.ViewPose.LastWorldYaw);
        Assert.Equal(
            unchecked((int)0x80000000),
            BitConverter.SingleToInt32Bits(state.ViewPose.LastWorldYaw));
        Assert.Equal(default, state.TurnInPlace);
        Assert.Equal(default, state.RotateInPlace);
        Assert.Equal(default, state.PelvisCorrection);
    }

    [Fact]
    public void ProductionEntryValidationRejectsClrZeroRuntimeState()
    {
        var initialized = AlsRuntimeState.CreateDefault();
        var clrZero = default(AlsRuntimeState);

        AlsRuntimeState.ValidateP4Defaults(in initialized);
        Assert.Throws<InvalidOperationException>(() =>
            AlsRuntimeState.ValidateP4Defaults(in clrZero));
    }

    [Fact]
    public void ProductionEntryValidationRejectsPositiveZeroViewPoseMarker()
    {
        var state = AlsRuntimeState.CreateDefault();
        state.ViewPose = default;

        Assert.Throws<InvalidOperationException>(() =>
            AlsRuntimeState.ValidateP4Defaults(in state));
    }

    [Fact]
    public void DefaultFrameResultUsesInactiveP4Outputs()
    {
        var result = AlsFrameResult.CreateDefault(new AlsFrameIdentity(0, 0, 1));

        Assert.Equal(-1, result.TurnAnimationId);
        Assert.Equal(-1, result.TurnCurveId);
        Assert.Equal(-1, result.RotateAnimationId);
        Assert.Equal(-1, result.RotateCurveId);
        Assert.Equal(-1, result.LeftFootPose.PlatformId);
        Assert.Equal(Quaternion.Identity, result.LeftFootPose.Rotation);
        Assert.Equal(-1, result.RightFootPose.PlatformId);
        Assert.Equal(Quaternion.Identity, result.RightFootPose.Rotation);
        Assert.Equal(AlsFootReleaseReason.None, result.LeftFootReleaseReason);
        Assert.Equal(AlsFootReleaseReason.None, result.RightFootReleaseReason);
        Assert.Equal(0f, result.LeftFootIkWeight);
        Assert.Equal(0f, result.RightFootIkWeight);
        Assert.Equal(0f, result.LeftFootLockCurve);
        Assert.Equal(0f, result.RightFootLockCurve);
        Assert.Equal(0f, result.AimRelativeYaw);
        Assert.Equal(0f, result.AimRelativePitch);
        Assert.Equal(0f, result.HeadWeight);
        Assert.Equal(0f, result.SpineWeight);
        Assert.Equal(0f, result.UpperBodyWeight);
        Assert.Equal(0f, result.SpineResidualYaw);
        Assert.Equal((short)0, result.TurnNominalDegrees);
        Assert.Equal((sbyte)0, result.TurnDirection);
        Assert.Equal((byte)0, result.TurnActive);
        Assert.Equal((sbyte)0, result.RotateDirection);
        Assert.Equal((byte)0, result.RotateActive);
        Assert.Equal(Vector3.Zero, result.PelvisOffset);
        Assert.Equal(Vector3.Zero, result.NextLeftFootProbeOrigin);
        Assert.Equal(Vector3.Zero, result.NextRightFootProbeOrigin);
        Assert.Equal(0L, result.P4ModifierOperationTicks);
        Assert.Equal(AlsP4ReasonCode.None, result.P4ReasonCode);
    }

    [Fact]
    public void IdentityRequiresNonNegativeFrameAndPositiveGeneration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsFrameIdentity(-1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsFrameIdentity(0, 1, 0));
        Assert.Equal(new AlsFrameIdentity(12, 3, 4), new AlsFrameIdentity(12, 3, 4));
    }

    private static void AssertFieldOrder<T>(params string[] expected)
    {
        var actual = typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static property => property.MetadataToken)
            .Select(static property => property.Name);

        Assert.Equal(expected, actual);
    }

    private static void AssertStorageFieldOrder<T>(params string[] expected)
    {
        var actual = typeof(T)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(static field => field.MetadataToken)
            .Select(static field => NormalizeStorageFieldName(field.Name));

        Assert.Equal(expected, actual);
    }

    private static string NormalizeStorageFieldName(string name)
    {
        const string backingFieldSuffix = ">k__BackingField";
        return name.Length > backingFieldSuffix.Length + 1 &&
            name[0] == '<' &&
            name.EndsWith(backingFieldSuffix, StringComparison.Ordinal)
                ? name[1..^backingFieldSuffix.Length]
                : name;
    }
}
