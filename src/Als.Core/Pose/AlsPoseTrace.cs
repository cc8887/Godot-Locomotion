using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Pose;

public sealed class AlsPoseTrace
{
    public const string ReferenceCommit = "b754d6f0f2bb03741d301f8fb88077ebfe561e17";
    public const string PatchHash = "3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f";

    private const float FixedDeltaSeconds = 1f / 60f;
    private const float UnitTolerance = 1e-3f;

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedCases =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["aim"] = ["aim_center", "aim_up", "aim_down", "aim_left", "aim_right"],
            ["turn"] =
            [
                "turn_standing_left_90", "turn_standing_right_90",
                "turn_standing_left_180", "turn_standing_right_180",
                "turn_crouching_left_90", "turn_crouching_right_90",
                "turn_crouching_left_180", "turn_crouching_right_180",
            ],
            ["rotate"] =
            [
                "rotate_standing_left", "rotate_standing_right",
                "rotate_crouching_left", "rotate_crouching_right",
            ],
            ["feet"] = ["feet_flat", "feet_slope", "feet_stairs"],
            ["platform"] =
            [
                "platform_translate", "platform_rotate", "platform_base_change",
                "platform_teleport", "platform_release",
            ],
        };

    private static readonly IReadOnlyDictionary<string, AlsPoseCaseDescriptor> CaseDescriptors =
        CreateCaseDescriptors();

    private static readonly string[] RootProperties =
    [
        "schemaVersion", "kind", "name", "fixedDeltaSeconds", "reference",
        "coordinateSystem", "sources", "tolerances", "nativeProvenance",
        "portProvenance", "cases",
    ];

    private AlsPoseTrace(
        string name,
        float positionToleranceMeters,
        float rotationToleranceRadians,
        AlsPoseTraceCase[] cases)
    {
        Name = name;
        PositionToleranceMeters = positionToleranceMeters;
        RotationToleranceRadians = rotationToleranceRadians;
        Cases = cases;
    }

    public string Name { get; }

    public float PositionToleranceMeters { get; }

    public float RotationToleranceRadians { get; }

    public IReadOnlyList<AlsPoseTraceCase> Cases { get; }

    public static AlsPoseTrace Load(string path) => Load(path, requireExpectedDescriptors: true);

    private static AlsPoseTrace Load(string path, bool requireExpectedDescriptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            ValidateObject(root, RootProperties, "root");
            RequireInteger(root, "schemaVersion", 1);
            RequireString(root, "kind", "p4_pose_trace");
            RequireNumber(root, "fixedDeltaSeconds", 1d / 60d, 1e-15d);
            RequireString(root, "nativeProvenance", "als_runtime");
            RequireString(root, "portProvenance", "port_oracle_v1");

            var name = ReadName(root, path);
            ReadReference(root.GetProperty("reference"));
            ReadCoordinateSystem(root.GetProperty("coordinateSystem"));
            ReadSources(root.GetProperty("sources"));
            var (positionTolerance, rotationTolerance) = ReadTolerances(root.GetProperty("tolerances"));
            var cases = ReadCases(root.GetProperty("cases"), name, requireExpectedDescriptors);
            return new AlsPoseTrace(name, positionTolerance, rotationTolerance, cases);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or InvalidOperationException or OverflowException)
        {
            throw new FormatException("Invalid P4 pose trace JSON.", exception);
        }
    }

    public static AlsPoseTraceIssue[] Compare(AlsPoseTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var issues = new List<AlsPoseTraceIssue>();
        foreach (var traceCase in trace.Cases)
        {
            CompareCase(trace, traceCase, issues);
        }

        return [.. issues];
    }

    public static void WritePortOracle(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var trace = Load(sourcePath, requireExpectedDescriptors: false);
        var root = JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject() ??
                   throw new FormatException("Invalid P4 pose trace JSON.");
        var cases = root["cases"]?.AsArray() ??
                    throw new FormatException("cases must be an array.");
        for (var index = 0; index < trace.Cases.Count; index++)
        {
            if (!TryReplay(trace.Cases[index], out var replay, out var issue))
            {
                throw new InvalidOperationException(
                    $"P4 oracle replay failed for {issue.CaseId}.{issue.Field}: {issue.Actual}");
            }

            cases[index]!.AsObject()["portExpected"] = WriteExpected(replay, trace.Cases[index].Category);
        }

        var json = root.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        File.WriteAllText(destinationPath, json, new System.Text.UTF8Encoding(false));
    }

    private static void CompareCase(
        AlsPoseTrace trace,
        AlsPoseTraceCase traceCase,
        List<AlsPoseTraceIssue> issues)
    {
        if (!TryReplay(traceCase, out var replay, out var replayIssue))
        {
            issues.Add(replayIssue);
            return;
        }

        var view = replay.View;
        var selection = replay.Selection;
        var yaw = replay.Yaw;
        var feet = replay.Feet;

        var expected = traceCase.Expected;
        var native = traceCase.Native;
        CompareFloatExact(issues, traceCase.CaseId, "aimRelativeYaw", expected.AimRelativeYaw, view.AimRelativeYaw);
        CompareFloatExact(issues, traceCase.CaseId, "aimRelativePitch", expected.AimRelativePitch, view.AimRelativePitch);
        CompareFloatExact(issues, traceCase.CaseId, "headWeight", expected.HeadWeight, view.HeadWeight);
        CompareFloatExact(issues, traceCase.CaseId, "spineWeight", expected.SpineWeight, view.SpineWeight);
        CompareFloatExact(issues, traceCase.CaseId, "upperBodyWeight", expected.UpperBodyWeight, view.UpperBodyWeight);
        CompareFloatExact(issues, traceCase.CaseId, "spineResidualYaw", expected.SpineResidualYaw, view.SpineResidualYaw);

        if (traceCase.Category == "Aim")
        {
            CompareFloatExact(issues, traceCase.CaseId, "aimRelativeYaw.native", expected.AimRelativeYaw, native.ViewYaw);
            CompareFloatExact(issues, traceCase.CaseId, "aimRelativePitch.native", expected.AimRelativePitch, native.ViewPitch);
        }

        var isTurn = traceCase.Category == "Turn" &&
                     selection.Active == 1 && selection.YawSource == AlsYawSource.TurnInPlace;
        var isRotate = traceCase.Category == "Rotate" &&
                       selection.Active == 1 && selection.YawSource == AlsYawSource.RotateInPlace;
        CompareExact(issues, traceCase.CaseId, "turnAnimationId", expected.TurnAnimationId, isTurn ? selection.AnimationId : -1);
        CompareExact(issues, traceCase.CaseId, "turnCurveId", expected.TurnCurveId, isTurn ? selection.CurveId : -1);
        CompareFloatExact(issues, traceCase.CaseId, "turnPhase", expected.TurnPhase, isTurn ? selection.CurrentPhase : 0f);
        CompareFloatExact(issues, traceCase.CaseId, "turnPlayRate", expected.TurnPlayRate, isTurn ? selection.PhasePlayRate : 0f);
        CompareExact(issues, traceCase.CaseId, "turnNominalDegrees", expected.TurnNominalDegrees, isTurn ? selection.NominalDegrees : (short)0);
        CompareExact(issues, traceCase.CaseId, "turnDirection", expected.TurnDirection, isTurn ? selection.Direction : (sbyte)0);
        CompareExact(issues, traceCase.CaseId, "turnActive", expected.TurnActive, isTurn ? (byte)1 : (byte)0);
        CompareFloatExact(issues, traceCase.CaseId, "turnYawDelta", expected.TurnYawDelta, isTurn ? yaw.YawDelta : 0f);

        CompareExact(issues, traceCase.CaseId, "rotateAnimationId", expected.RotateAnimationId, isRotate ? selection.AnimationId : -1);
        CompareExact(issues, traceCase.CaseId, "rotateCurveId", expected.RotateCurveId, isRotate ? selection.CurveId : -1);
        CompareFloatExact(issues, traceCase.CaseId, "rotatePhase", expected.RotatePhase, isRotate ? selection.CurrentPhase : 0f);
        CompareFloatExact(issues, traceCase.CaseId, "rotatePlayRate", expected.RotatePlayRate, isRotate ? selection.PhasePlayRate : 0f);
        CompareExact(issues, traceCase.CaseId, "rotateDirection", expected.RotateDirection, isRotate ? selection.Direction : (sbyte)0);
        CompareExact(issues, traceCase.CaseId, "rotateActive", expected.RotateActive, isRotate ? (byte)1 : (byte)0);
        CompareFloatExact(issues, traceCase.CaseId, "rotateYawDelta", expected.RotateYawDelta, isRotate ? yaw.YawDelta : 0f);

        ComparePosition(issues, traceCase.CaseId, "pelvisOffset", expected.PelvisOffset, feet.PelvisOffset, trace.PositionToleranceMeters);
        CompareFoot(issues, traceCase.CaseId, "leftFoot", expected.LeftFoot, feet.LeftFoot, trace);
        CompareFoot(issues, traceCase.CaseId, "rightFoot", expected.RightFoot, feet.RightFoot, trace);
        CompareExact(issues, traceCase.CaseId, "leftReleaseReason", expected.LeftReleaseReason, feet.LeftReleaseReason);
        CompareExact(issues, traceCase.CaseId, "rightReleaseReason", expected.RightReleaseReason, feet.RightReleaseReason);

        if (traceCase.Category == "Turn")
        {
            CompareExact(issues, traceCase.CaseId, "turnAnimationId.native", expected.TurnAnimationId, native.TurnAnimationId);
            CompareExact(issues, traceCase.CaseId, "turnCurveId.native", expected.TurnCurveId, native.TurnCurveId);
            CompareFloatExact(issues, traceCase.CaseId, "turnPhase.native", expected.TurnPhase, native.TurnPhase);
            CompareExact(issues, traceCase.CaseId, "turnNominalDegrees.native", expected.TurnNominalDegrees, native.TurnNominalDegrees);
            CompareExact(issues, traceCase.CaseId, "turnDirection.native", expected.TurnDirection, native.TurnDirection);
            CompareExact(issues, traceCase.CaseId, "turnActive.native", expected.TurnActive, native.TurnActive);
        }
        else if (traceCase.Category == "Rotate")
        {
            CompareExact(issues, traceCase.CaseId, "rotateAnimationId.native", expected.RotateAnimationId, native.RotateAnimationId);
            CompareExact(issues, traceCase.CaseId, "rotateCurveId.native", expected.RotateCurveId, native.RotateCurveId);
            CompareFloatExact(issues, traceCase.CaseId, "rotatePhase.native", expected.RotatePhase, native.RotatePhase);
            CompareExact(issues, traceCase.CaseId, "rotateDirection.native", expected.RotateDirection, native.RotateDirection);
            CompareExact(issues, traceCase.CaseId, "rotateActive.native", expected.RotateActive, native.RotateActive);
        }
        else if (traceCase.Category is "Feet" or "Platform")
        {
            CompareExact(issues, traceCase.CaseId, "leftFoot.platformId.native", expected.LeftFoot.PlatformId, native.LeftFootPlatformId);
            CompareExact(issues, traceCase.CaseId, "rightFoot.platformId.native", expected.RightFoot.PlatformId, native.RightFootPlatformId);
            CompareExact(issues, traceCase.CaseId, "leftReleaseReason.native", expected.LeftReleaseReason, native.LeftReleaseReason);
            CompareExact(issues, traceCase.CaseId, "rightReleaseReason.native", expected.RightReleaseReason, native.RightReleaseReason);
        }
    }

    private static bool TryReplay(
        AlsPoseTraceCase traceCase,
        out AlsPoseReplayResult replay,
        out AlsPoseTraceIssue issue)
    {
        replay = default;
        issue = default;
        var stimulus = traceCase.Stimulus;
        var current = CreateInitialState(traceCase);
        var input = CreateInput(stimulus);
        if (!AlsViewPoseModel.TryEvaluate(
                AlsViewPoseSettings.CreateDefault(), input, current,
                out var afterView, out var view, out var viewReason))
        {
            issue = new AlsPoseTraceIssue(traceCase.CaseId, "viewReason", "None", viewReason.ToString());
            return false;
        }

        if (!AlsTurnRotateModel.TrySelectAndAdvance(
                CreateTurnRotateSettings(traceCase), input, view, afterView,
                out var afterTurn, out var selection, out var selectionReason))
        {
            issue = new AlsPoseTraceIssue(traceCase.CaseId, "turnReason", "None", selectionReason.ToString());
            return false;
        }

        var yaw = default(AlsTurnRotateOutput);
        if (selection.Active == 1 &&
            !AlsTurnRotateModel.TryFinalizeYaw(
                selection, stimulus.PreviousYawCurve, stimulus.CurrentYawCurve,
                out yaw, out var yawReason))
        {
            issue = new AlsPoseTraceIssue(traceCase.CaseId, "yawReason", "None", yawReason.ToString());
            return false;
        }

        var origins = new AlsFootProbeWorldOrigins(stimulus.LeftFootOrigin, stimulus.RightFootOrigin);
        var releaseSignals = stimulus.PlatformRemovalSignals;
        if (!AlsFootPlacementModel.TryEvaluate(
                AlsFootPlacementSettings.CreateReference(), input,
                stimulus.LeftIkWeight, stimulus.RightIkWeight,
                stimulus.LeftLockCurve, stimulus.RightLockCurve,
                origins, releaseSignals, afterTurn,
                out _, out var feet, out var footReason))
        {
            issue = new AlsPoseTraceIssue(traceCase.CaseId, "footReason", "None", footReason.ToString());
            return false;
        }

        replay = new AlsPoseReplayResult(view, selection, yaw, feet);
        return true;
    }

    private static JsonObject WriteExpected(in AlsPoseReplayResult replay, string category)
    {
        var isTurn = category == "Turn" &&
                     replay.Selection.Active == 1 && replay.Selection.YawSource == AlsYawSource.TurnInPlace;
        var isRotate = category == "Rotate" &&
                       replay.Selection.Active == 1 && replay.Selection.YawSource == AlsYawSource.RotateInPlace;
        return new JsonObject
        {
            ["aimRelativeYaw"] = replay.View.AimRelativeYaw,
            ["aimRelativePitch"] = replay.View.AimRelativePitch,
            ["headWeight"] = replay.View.HeadWeight,
            ["spineWeight"] = replay.View.SpineWeight,
            ["upperBodyWeight"] = replay.View.UpperBodyWeight,
            ["spineResidualYaw"] = replay.View.SpineResidualYaw,
            ["turnAnimationId"] = isTurn ? replay.Selection.AnimationId : -1,
            ["turnCurveId"] = isTurn ? replay.Selection.CurveId : -1,
            ["turnPhase"] = isTurn ? replay.Selection.CurrentPhase : 0f,
            ["turnPlayRate"] = isTurn ? replay.Selection.PhasePlayRate : 0f,
            ["turnNominalDegrees"] = isTurn ? replay.Selection.NominalDegrees : 0,
            ["turnDirection"] = isTurn ? replay.Selection.Direction : 0,
            ["turnActive"] = isTurn ? 1 : 0,
            ["turnYawDelta"] = isTurn ? replay.Yaw.YawDelta : 0f,
            ["rotateAnimationId"] = isRotate ? replay.Selection.AnimationId : -1,
            ["rotateCurveId"] = isRotate ? replay.Selection.CurveId : -1,
            ["rotatePhase"] = isRotate ? replay.Selection.CurrentPhase : 0f,
            ["rotatePlayRate"] = isRotate ? replay.Selection.PhasePlayRate : 0f,
            ["rotateDirection"] = isRotate ? replay.Selection.Direction : 0,
            ["rotateActive"] = isRotate ? 1 : 0,
            ["rotateYawDelta"] = isRotate ? replay.Yaw.YawDelta : 0f,
            ["pelvisOffset"] = WriteVector(replay.Feet.PelvisOffset),
            ["leftFoot"] = WriteFoot(replay.Feet.LeftFoot),
            ["rightFoot"] = WriteFoot(replay.Feet.RightFoot),
            ["leftReleaseReason"] = replay.Feet.LeftReleaseReason.ToString(),
            ["rightReleaseReason"] = replay.Feet.RightReleaseReason.ToString(),
        };
    }

    private static JsonObject WriteFoot(in AlsFootPoseOutput foot) => new()
    {
        ["position"] = WriteVector(foot.Position),
        ["rotation"] = WriteQuaternion(foot.Rotation),
        ["lockAmount"] = foot.LockAmount,
        ["platformId"] = foot.PlatformId,
    };

    private static JsonObject WriteVector(in Vector3 value) => new()
    {
        ["x"] = value.X,
        ["y"] = value.Y,
        ["z"] = value.Z,
    };

    private static JsonObject WriteQuaternion(in Quaternion value) => new()
    {
        ["x"] = value.X,
        ["y"] = value.Y,
        ["z"] = value.Z,
        ["w"] = value.W,
    };

    private static AlsRuntimeState CreateInitialState(AlsPoseTraceCase traceCase)
    {
        var stimulus = traceCase.Stimulus;
        var state = AlsRuntimeState.CreateDefault();
        state.Initialized = 1;
        state.LocomotionState = stimulus.Grounded == 1
            ? AlsLocomotionState.Grounded
            : AlsLocomotionState.InAir;
        state.ViewPose = new AlsViewPoseState(
            Normalize(stimulus.ViewYaw - stimulus.CharacterYaw),
            stimulus.ViewPitch,
            0f,
            1f,
            0f,
            0f,
            stimulus.ViewYaw);
        if (traceCase.Category == "Turn")
        {
            var descriptor = CaseDescriptors[traceCase.CaseId];
            var direction = PortDirection(stimulus);
            state.TurnInPlace = new AlsTurnInPlaceState(
                0f, stimulus.PreviousYawPhase, stimulus.YawPhasePlayRate,
                Normalize(stimulus.ViewYaw - stimulus.CharacterYaw),
                descriptor.NominalDegrees, direction, 1, stimulus.Stance);
        }
        else if (traceCase.Category == "Rotate")
        {
            var direction = PortDirection(stimulus);
            state.RotateInPlace = new AlsRotateInPlaceState(
                stimulus.PreviousYawPhase, stimulus.YawPhasePlayRate, direction, 1, stimulus.Stance);
        }

        state.LeftFootLock = CreateInitialLock(stimulus, stimulus.InitialLeftLock, stimulus.LeftFootHit.ColliderId);
        state.RightFootLock = CreateInitialLock(stimulus, stimulus.InitialRightLock, stimulus.RightFootHit.ColliderId);
        var inverseCharacterRotation = Quaternion.Conjugate(
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, stimulus.CharacterYaw));
        state.LeftFootProbeOrigin = Vector3.Transform(
            stimulus.LeftFootOrigin - stimulus.CharacterPosition, inverseCharacterRotation);
        state.RightFootProbeOrigin = Vector3.Transform(
            stimulus.RightFootOrigin - stimulus.CharacterPosition, inverseCharacterRotation);
        return state;
    }

    private static AlsTurnRotateSettings CreateTurnRotateSettings(AlsPoseTraceCase traceCase)
    {
        // The trace loader locks provenance to ALS-Refactored, not the V4 Demo.
        var settings = AlsTurnRotateSettings.CreateRefactoredReference();
        var rate = traceCase.Stimulus.YawPhasePlayRate;
        if (traceCase.Category == "Turn")
        {
            var direction = PortDirection(traceCase.Stimulus) < 0 ? "Left" : "Right";
            var clip = SelectTurnClip(settings, traceCase.Stance, direction,
                CaseDescriptors[traceCase.CaseId].NominalDegrees) with { BasePlayRate = rate };
            return ReplaceTurnClip(settings, traceCase.Stance, direction,
                CaseDescriptors[traceCase.CaseId].NominalDegrees, clip);
        }

        return traceCase.Category == "Rotate"
            ? settings with
            {
                RotatePlayRateMinimum = rate,
                RotatePlayRateMaximum = rate,
                RotatePlayRateHalfLife = 0f,
            }
            : settings;
    }

    private static sbyte PortDirection(in AlsPoseStimulus stimulus) =>
        Normalize(stimulus.ViewYaw - stimulus.CharacterYaw) < 0f ? (sbyte)-1 : (sbyte)1;

    private static AlsTurnClipSettings SelectTurnClip(
        in AlsTurnRotateSettings settings,
        AlsStance stance,
        string direction,
        short nominal) => (stance, direction, nominal) switch
    {
        (AlsStance.Standing, "Left", 90) => settings.StandingTurn90Left,
        (AlsStance.Standing, "Right", 90) => settings.StandingTurn90Right,
        (AlsStance.Standing, "Left", 180) => settings.StandingTurn180Left,
        (AlsStance.Standing, "Right", 180) => settings.StandingTurn180Right,
        (AlsStance.Crouching, "Left", 90) => settings.CrouchingTurn90Left,
        (AlsStance.Crouching, "Right", 90) => settings.CrouchingTurn90Right,
        (AlsStance.Crouching, "Left", 180) => settings.CrouchingTurn180Left,
        (AlsStance.Crouching, "Right", 180) => settings.CrouchingTurn180Right,
        _ => throw new FormatException("Invalid locked turn descriptor."),
    };

    private static AlsTurnRotateSettings ReplaceTurnClip(
        in AlsTurnRotateSettings settings,
        AlsStance stance,
        string direction,
        short nominal,
        in AlsTurnClipSettings clip) => (stance, direction, nominal) switch
    {
        (AlsStance.Standing, "Left", 90) => settings with { StandingTurn90Left = clip },
        (AlsStance.Standing, "Right", 90) => settings with { StandingTurn90Right = clip },
        (AlsStance.Standing, "Left", 180) => settings with { StandingTurn180Left = clip },
        (AlsStance.Standing, "Right", 180) => settings with { StandingTurn180Right = clip },
        (AlsStance.Crouching, "Left", 90) => settings with { CrouchingTurn90Left = clip },
        (AlsStance.Crouching, "Right", 90) => settings with { CrouchingTurn90Right = clip },
        (AlsStance.Crouching, "Left", 180) => settings with { CrouchingTurn180Left = clip },
        (AlsStance.Crouching, "Right", 180) => settings with { CrouchingTurn180Right = clip },
        _ => throw new FormatException("Invalid locked turn descriptor."),
    };

    private static AlsFootLockState CreateInitialLock(
        in AlsPoseStimulus stimulus,
        in AlsPoseInitialLock initial,
        long staticColliderId)
    {
        if (initial.Locked == 0)
        {
            return AlsFootLockState.CreateDefault();
        }

        return new AlsFootLockState(
            initial.LocalPosition,
            initial.LocalRotation,
            Vector3.Zero,
            Quaternion.Identity,
            stimulus.PreviousPlatformId >= 0 ? stimulus.PreviousPlatformPosition : stimulus.CharacterPosition,
            stimulus.PreviousPlatformId >= 0
                ? stimulus.PreviousPlatformRotation
                : Quaternion.CreateFromAxisAngle(Vector3.UnitY, stimulus.CharacterYaw),
            stimulus.PreviousPlatformId,
            stimulus.PreviousPlatformId >= 0 ? stimulus.PreviousPlatformColliderId : staticColliderId,
            initial.Amount,
            initial.Locked,
            AlsFootReleaseReason.None);
    }

    private static AlsFrameInput CreateInput(in AlsPoseStimulus stimulus)
    {
        var characterTransform = Matrix4x4.CreateRotationY(stimulus.CharacterYaw);
        characterTransform.Translation = stimulus.CharacterPosition;
        var platformTransform = Matrix4x4.CreateFromQuaternion(stimulus.PlatformRotation);
        platformTransform.Translation = stimulus.PlatformPosition;
        var command = AlsLocomotionCommand.CreateDefault() with
        {
            ViewYaw = stimulus.ViewYaw,
            ViewPitch = stimulus.ViewPitch,
            AimYaw = stimulus.AimYaw,
            AimPitch = stimulus.AimPitch,
            RequestedStance = stimulus.Stance,
            RequestedRotationMode = stimulus.RotationMode,
        };

        return AlsFrameInput.CreateDefault(new AlsFrameIdentity(0, 0, 1), FixedDeltaSeconds) with
        {
            CharacterTransform = characterTransform,
            CharacterYaw = stimulus.CharacterYaw,
            ActualVelocity = -Vector3.UnitZ * stimulus.Speed,
            ActualAcceleration = -Vector3.UnitZ * stimulus.Acceleration,
            ViewRotation = Quaternion.CreateFromYawPitchRoll(stimulus.ViewYaw, stimulus.ViewPitch, 0f),
            AimRotation = Quaternion.CreateFromYawPitchRoll(stimulus.AimYaw, stimulus.AimPitch, 0f),
            Floor = new AlsFloorSample(
                stimulus.Grounded,
                stimulus.FloorNormal,
                stimulus.PlatformId,
                platformTransform,
                stimulus.PlatformAngularVelocity,
                stimulus.PlatformColliderId),
            LeftFootHit = stimulus.LeftFootHit,
            RightFootHit = stimulus.RightFootHit,
            Stance = stimulus.Stance,
            RotationMode = stimulus.RotationMode,
            Command = command,
            CurrentDriveMode = AlsDriveMode.MotorDriven,
        };
    }

    private static AlsPoseTraceCase[] ReadCases(
        JsonElement element,
        string name,
        bool requireExpectedDescriptors)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("cases must be an array.");
        }

        var expectedIds = ExpectedCases[name];
        if (element.GetArrayLength() != expectedIds.Length)
        {
            throw new FormatException($"Trace '{name}' must contain exactly {expectedIds.Length} cases.");
        }

        var result = new AlsPoseTraceCase[expectedIds.Length];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            result[index++] = ReadCase(item, seen, requireExpectedDescriptors);
        }

        if (!seen.SetEquals(expectedIds))
        {
            throw new FormatException($"Trace '{name}' does not contain the exact locked case set.");
        }

        return result;
    }

    private static AlsPoseTraceCase ReadCase(
        JsonElement element,
        HashSet<string> seen,
        bool requireExpectedDescriptors)
    {
        ValidateObject(element,
        [
            "caseId", "category", "stance", "direction", "phase", "provenance",
            "source", "stimulus", "nativeActual", "portExpected",
        ], "case");
        var caseId = ReadString(element, "caseId");
        if (!seen.Add(caseId))
        {
            throw new FormatException($"Duplicate caseId '{caseId}'.");
        }

        var category = ReadOneOf(element, "category", "Aim", "Turn", "Rotate", "Feet", "Platform");
        var stance = ReadEnum<AlsStance>(element, "stance");
        var direction = ReadOneOf(element, "direction", "Center", "Up", "Down", "Left", "Right", "None");
        var phase = ReadOneOf(element, "phase", "steady", "playing", "hold", "release");
        RequireString(element, "provenance", "port_oracle_v1");
        var source = ReadCaseSource(element.GetProperty("source"));
        var stimulus = ReadStimulus(element.GetProperty("stimulus"), stance);
        var expectedPlatformRemovedSignal = caseId == "platform_release" ? (byte)1 : (byte)0;
        var signals = stimulus.PlatformRemovalSignals;
        if (signals.LeftPlatformRemoved != expectedPlatformRemovedSignal ||
            signals.RightPlatformRemoved != expectedPlatformRemovedSignal ||
            (expectedPlatformRemovedSignal == 1 &&
             (signals.LeftPlatformId != stimulus.PreviousPlatformId ||
              signals.LeftColliderId != stimulus.PreviousPlatformColliderId ||
              signals.RightPlatformId != stimulus.PreviousPlatformId ||
              signals.RightColliderId != stimulus.PreviousPlatformColliderId)) ||
            (expectedPlatformRemovedSignal == 0 &&
             (signals.LeftPlatformId != -1 || signals.LeftColliderId != -1 ||
              signals.RightPlatformId != -1 || signals.RightColliderId != -1)))
        {
            throw new FormatException(
                $"Case '{caseId}' platform removal signal does not match the locked scenario.");
        }
        var native = ReadNativeActual(element.GetProperty("nativeActual"), stimulus, category, source.AnimationObjectPath);
        var expected = ReadExpected(element.GetProperty("portExpected"));
        if (!CaseDescriptors.TryGetValue(caseId, out var descriptor) ||
            descriptor.Category != category || descriptor.Stance != stance ||
            descriptor.Direction != direction || descriptor.Phase != phase ||
            descriptor.SourceAnimationObjectPath != source.AnimationObjectPath ||
            descriptor.SourceCurveNamesKey != string.Join(',', source.CurveNames) ||
            (requireExpectedDescriptors &&
             (descriptor.NominalDegrees != expected.TurnNominalDegrees ||
              descriptor.LeftReleaseReason != expected.LeftReleaseReason ||
              descriptor.RightReleaseReason != expected.RightReleaseReason)))
        {
            throw new FormatException($"Case '{caseId}' does not match its locked descriptor tuple.");
        }

        return new AlsPoseTraceCase(caseId, category, stance, direction, phase,
            source.AnimationObjectPath, source.CurveNames, stimulus, native, expected);
    }

    private static AlsPoseCaseSource ReadCaseSource(JsonElement element)
    {
        ValidateObject(element, ["animationObjectPath", "curveSourceObjectPath", "curveNames"], "source");
        var animation = ReadString(element, "animationObjectPath");
        var curveSource = ReadString(element, "curveSourceObjectPath");
        if (!animation.StartsWith("/ALS/ALS/Animations/", StringComparison.Ordinal) ||
            !animation.Equals(curveSource, StringComparison.Ordinal))
        {
            throw new FormatException("source animation and curve object paths must name the same locked ALS animation asset.");
        }

        var curveNamesElement = element.GetProperty("curveNames");
        if (curveNamesElement.ValueKind != JsonValueKind.Array || curveNamesElement.GetArrayLength() == 0)
        {
            throw new FormatException("source.curveNames must contain at least one ALS curve.");
        }
        var curveNames = curveNamesElement.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
        if (curveNames.Any(value => value is not ("Layering_Head" or "RotationYawSpeed" or "FootLock_L" or "FootLock_R")) ||
            curveNames.Distinct(StringComparer.Ordinal).Count() != curveNames.Length)
        {
            throw new FormatException("source.curveNames contains an unsupported or duplicate ALS curve.");
        }

        return new AlsPoseCaseSource(animation, curveNames);
    }

    private static AlsPoseStimulus ReadStimulus(JsonElement element, AlsStance declaredStance)
    {
        ValidateObject(element,
        [
            "characterPosition", "characterYaw", "viewYaw", "viewPitch", "aimYaw", "aimPitch",
            "rotationMode", "stance", "grounded", "speed", "acceleration", "floorNormal",
            "platformId", "platformColliderId", "platformPosition", "platformRotation", "platformAngularVelocity",
            "previousPlatformId", "previousPlatformColliderId", "previousPlatformPosition", "previousPlatformRotation",
            "initialLeftLock", "initialRightLock",
            "leftFootOrigin", "rightFootOrigin", "leftFootHit", "rightFootHit", "leftIkWeight",
            "rightIkWeight", "leftLockCurve", "rightLockCurve", "previousYawCurve", "currentYawCurve",
            "previousYawPhase", "yawPhasePlayRate", "transitionFrameDelta", "platformRemovalSignals",
        ], "stimulus");
        var stance = ReadEnum<AlsStance>(element, "stance");
        if (stance != declaredStance)
        {
            throw new FormatException("case stance and stimulus stance disagree.");
        }

        var floorNormal = ReadUnitVector(element, "floorNormal");
        var platformRotation = ReadQuaternion(element, "platformRotation");
        return new AlsPoseStimulus(
            ReadVector3(element, "characterPosition"), ReadFloat(element, "characterYaw"),
            ReadFloat(element, "viewYaw"), ReadFloat(element, "viewPitch"),
            ReadFloat(element, "aimYaw"), ReadFloat(element, "aimPitch"),
            ReadEnum<AlsRotationMode>(element, "rotationMode"), stance,
            ReadByte(element, "grounded"), ReadFloat(element, "speed", 0f),
            ReadFloat(element, "acceleration", 0f), floorNormal,
            ReadInt(element, "platformId", -1), ReadLong(element, "platformColliderId", -1),
            ReadVector3(element, "platformPosition"), platformRotation,
            ReadVector3(element, "platformAngularVelocity"),
            ReadInt(element, "previousPlatformId", -1), ReadLong(element, "previousPlatformColliderId", -1),
            ReadVector3(element, "previousPlatformPosition"), ReadQuaternion(element, "previousPlatformRotation"),
            ReadInitialLock(element, "initialLeftLock"), ReadInitialLock(element, "initialRightLock"),
            ReadVector3(element, "leftFootOrigin"), ReadVector3(element, "rightFootOrigin"),
            ReadFootHit(element, "leftFootHit"), ReadFootHit(element, "rightFootHit"),
            ReadFloat(element, "leftIkWeight", 0f, 1f), ReadFloat(element, "rightIkWeight", 0f, 1f),
            ReadFloat(element, "leftLockCurve", 0f, 1f), ReadFloat(element, "rightLockCurve", 0f, 1f),
            ReadFloat(element, "previousYawCurve"), ReadFloat(element, "currentYawCurve"),
            ReadFloat(element, "previousYawPhase", 0f, 1f), ReadFloat(element, "yawPhasePlayRate", 0f),
            ReadInt(element, "transitionFrameDelta", 0, 1),
            ReadPlatformRemovalSignals(element));
    }

    private static AlsFootPlacementReleaseSignals ReadPlatformRemovalSignals(JsonElement parent)
    {
        var element = parent.GetProperty("platformRemovalSignals");
        ValidateObject(element,
        [
            "leftRemoved", "leftPlatformId", "leftColliderId",
            "rightRemoved", "rightPlatformId", "rightColliderId",
        ], "platformRemovalSignals");
        return new AlsFootPlacementReleaseSignals(
            ReadByte(element, "leftRemoved"),
            ReadInt(element, "leftPlatformId", -1),
            ReadLong(element, "leftColliderId", -1),
            ReadByte(element, "rightRemoved"),
            ReadInt(element, "rightPlatformId", -1),
            ReadLong(element, "rightColliderId", -1));
    }

    private static AlsPoseInitialLock ReadInitialLock(JsonElement parent, string name)
    {
        var element = parent.GetProperty(name);
        ValidateObject(element, ["localPosition", "localRotation", "amount", "locked"], name);
        var locked = ReadByte(element, "locked");
        var amount = ReadFloat(element, "amount", 0f, 1f);
        if ((locked == 0 && amount != 0f) || (locked == 1 && amount <= 0f))
        {
            throw new FormatException($"{name} locked and amount are inconsistent.");
        }

        return new AlsPoseInitialLock(
            ReadVector3(element, "localPosition"),
            ReadQuaternion(element, "localRotation"),
            amount,
            locked);
    }

    private static AlsPoseNativeActual ReadNativeActual(
        JsonElement element,
        AlsPoseStimulus stimulus,
        string category,
        string sourceAnimationObjectPath)
    {
        ValidateObject(element,
        [
            "viewYaw", "viewPitch", "spineYaw", "headBlendAmount", "turnUpdated", "turnAnimationId", "turnCurveId",
            "turnNominalDegrees", "turnActive", "turnDirection", "rotateAnimationId", "rotateCurveId",
            "rotateDirection", "rotateActive", "rotateLeft", "rotateRight", "playRate", "turnPhase", "rotatePhase",
            "turnAccumulatedYaw", "rotateAccumulatedYaw", "runtimeSourceAnimationObjectPath",
            "runtimeInstanceClassPath", "runtimeNodePropertyName", "runtimeNodePropertyOrdinal", "runtimeSourceWeight",
            "runtimeSourceTimeSeconds", "runtimePreviousSourceTimeSeconds", "runtimePreviousYawCurve",
            "runtimeCurrentYawCurve", "sourceSelectionVerified", "rotationYawSpeed", "feetValid",
            "movementBaseChanged", "hasRelativeBaseLocation", "hasRelativeBaseRotation", "movementBaseDeltaYaw",
            "previousLeftFootLockAmount", "previousRightFootLockAmount", "leftFootLockAmount",
            "rightFootLockAmount", "leftFootPlatformId", "rightFootPlatformId", "leftFootReleaseReason",
            "rightFootReleaseReason", "transitionFrameDelta", "animationUpdateCount",
            "footHitProvenance", "releaseReasonProvenance",
            "animationEvaluationCount", "animationPostUpdateCount", "publicAnimationTickCount",
            "movementBaseId", "movementBaseColliderId", "movementBasePosition", "movementBaseRotation",
            "previousMovementBaseId", "previousMovementBaseColliderId", "previousMovementBasePosition",
            "previousMovementBaseRotation", "actualFloorNormal", "actualLeftFootHit", "actualRightFootHit",
            "previousLeftLockWorldPosition", "previousRightLockWorldPosition",
            "previousLeftLockBasePosition", "previousRightLockBasePosition", "leftLockWorldPosition",
            "rightLockWorldPosition", "leftLockBasePosition", "rightLockBasePosition", "pelvis", "leftFoot", "rightFoot",
            "pelvisComponent", "leftFootComponent", "rightFootComponent", "head", "spine", "upperBody",
            "headComponent", "spineComponent", "upperBodyComponent",
        ], "nativeActual");
        var viewYaw = ReadFloat(element, "viewYaw");
        var viewPitch = ReadFloat(element, "viewPitch");
        _ = ReadFloat(element, "spineYaw");
        _ = ReadFloat(element, "headBlendAmount");
        _ = ReadByte(element, "turnUpdated");
        var turnAnimationId = ReadInt(element, "turnAnimationId", -1);
        var turnCurveId = ReadInt(element, "turnCurveId", -1);
        var turnNominalDegrees = (short)ReadInt(element, "turnNominalDegrees", 0, 180);
        var turnActive = ReadByte(element, "turnActive");
        var turnDirection = (sbyte)ReadInt(element, "turnDirection", -1, 1);
        var rotateAnimationId = ReadInt(element, "rotateAnimationId", -1);
        var rotateCurveId = ReadInt(element, "rotateCurveId", -1);
        var rotateDirection = (sbyte)ReadInt(element, "rotateDirection", -1, 1);
        var rotateActive = ReadByte(element, "rotateActive");
        _ = ReadByte(element, "rotateLeft");
        _ = ReadByte(element, "rotateRight");
        _ = ReadFloat(element, "playRate");
        var turnPhase = ReadFloat(element, "turnPhase", 0f, 1f);
        var rotatePhase = ReadFloat(element, "rotatePhase", 0f, 1f);
        _ = ReadFloat(element, "turnAccumulatedYaw");
        _ = ReadFloat(element, "rotateAccumulatedYaw");
        var runtimeSourceElement = element.GetProperty("runtimeSourceAnimationObjectPath");
        if (runtimeSourceElement.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("runtimeSourceAnimationObjectPath must be a string.");
        }
        var runtimeSourceAnimationObjectPath = runtimeSourceElement.GetString() ?? string.Empty;
        var runtimeInstanceClassElement = element.GetProperty("runtimeInstanceClassPath");
        var runtimeNodePropertyElement = element.GetProperty("runtimeNodePropertyName");
        if (runtimeInstanceClassElement.ValueKind != JsonValueKind.String ||
            runtimeNodePropertyElement.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("native runtime player identity fields must be strings.");
        }
        var runtimeInstanceClassPath = runtimeInstanceClassElement.GetString() ?? string.Empty;
        var runtimeNodePropertyName = runtimeNodePropertyElement.GetString() ?? string.Empty;
        var runtimeNodePropertyOrdinal = ReadInt(element, "runtimeNodePropertyOrdinal", -1);
        var runtimeSourceWeight = ReadFloat(element, "runtimeSourceWeight", 0f, 1f);
        _ = ReadFloat(element, "runtimeSourceTimeSeconds", 0f);
        _ = ReadFloat(element, "runtimePreviousSourceTimeSeconds", 0f);
        var runtimePreviousYawCurve = ReadFloat(element, "runtimePreviousYawCurve");
        var runtimeCurrentYawCurve = ReadFloat(element, "runtimeCurrentYawCurve");
        _ = ReadByte(element, "sourceSelectionVerified");
        _ = ReadFloat(element, "rotationYawSpeed");
        _ = ReadByte(element, "feetValid");
        _ = ReadByte(element, "movementBaseChanged");
        _ = ReadByte(element, "hasRelativeBaseLocation");
        _ = ReadByte(element, "hasRelativeBaseRotation");
        _ = ReadFloat(element, "movementBaseDeltaYaw");
        _ = ReadFloat(element, "previousLeftFootLockAmount");
        _ = ReadFloat(element, "previousRightFootLockAmount");
        _ = ReadFloat(element, "leftFootLockAmount");
        _ = ReadFloat(element, "rightFootLockAmount");
        var leftFootPlatformId = ReadInt(element, "leftFootPlatformId", -1);
        var rightFootPlatformId = ReadInt(element, "rightFootPlatformId", -1);
        var leftReleaseReason = ReadEnum<AlsFootReleaseReason>(element, "leftFootReleaseReason");
        var rightReleaseReason = ReadEnum<AlsFootReleaseReason>(element, "rightFootReleaseReason");
        if (ReadString(element, "footHitProvenance") != "derived_transition" ||
            ReadString(element, "releaseReasonProvenance") != "derived_transition")
        {
            throw new FormatException("native derived transition provenance is invalid.");
        }
        var transitionFrameDelta = ReadInt(element, "transitionFrameDelta", 0, 1);
        if (ReadInt(element, "animationUpdateCount", 1, 1) != 1 ||
            ReadInt(element, "animationEvaluationCount", 1, 1) != 1 ||
            ReadInt(element, "animationPostUpdateCount", 1, 1) != 1 ||
            ReadInt(element, "publicAnimationTickCount", 1, 1) != 1)
        {
            throw new FormatException("native animation pipeline stages must each execute exactly once.");
        }
        var movementBaseId = ReadInt(element, "movementBaseId", -1);
        var movementBaseColliderId = ReadLong(element, "movementBaseColliderId", -1);
        var movementBasePosition = ReadVector3(element, "movementBasePosition");
        var movementBaseRotation = ReadQuaternion(element, "movementBaseRotation");
        var previousMovementBaseId = ReadInt(element, "previousMovementBaseId", -1);
        var previousMovementBaseColliderId = ReadLong(element, "previousMovementBaseColliderId", -1);
        var previousMovementBasePosition = ReadVector3(element, "previousMovementBasePosition");
        var previousMovementBaseRotation = ReadQuaternion(element, "previousMovementBaseRotation");
        var actualFloorNormal = ReadUnitVector(element, "actualFloorNormal");
        var actualLeftFootHit = ReadFootHit(element, "actualLeftFootHit");
        var actualRightFootHit = ReadFootHit(element, "actualRightFootHit");
        var previousLeftLockWorldPosition = ReadVector3(element, "previousLeftLockWorldPosition");
        var previousRightLockWorldPosition = ReadVector3(element, "previousRightLockWorldPosition");
        var previousLeftLockBasePosition = ReadVector3(element, "previousLeftLockBasePosition");
        var previousRightLockBasePosition = ReadVector3(element, "previousRightLockBasePosition");
        _ = ReadVector3(element, "leftLockWorldPosition");
        _ = ReadVector3(element, "rightLockWorldPosition");
        _ = ReadVector3(element, "leftLockBasePosition");
        _ = ReadVector3(element, "rightLockBasePosition");
        ReadTransform(element, "pelvis");
        ReadTransform(element, "leftFoot");
        ReadTransform(element, "rightFoot");
        ReadTransform(element, "pelvisComponent");
        ReadTransform(element, "leftFootComponent");
        ReadTransform(element, "rightFootComponent");
        ReadTransform(element, "head");
        ReadTransform(element, "spine");
        ReadTransform(element, "upperBody");
        ReadTransform(element, "headComponent");
        ReadTransform(element, "spineComponent");
        ReadTransform(element, "upperBodyComponent");
        if (movementBaseId != stimulus.PlatformId || movementBaseColliderId != stimulus.PlatformColliderId ||
            movementBasePosition != stimulus.PlatformPosition || movementBaseRotation != stimulus.PlatformRotation ||
            previousMovementBaseId != stimulus.PreviousPlatformId ||
            previousMovementBaseColliderId != stimulus.PreviousPlatformColliderId ||
            previousMovementBasePosition != stimulus.PreviousPlatformPosition ||
            previousMovementBaseRotation != stimulus.PreviousPlatformRotation ||
            actualFloorNormal != stimulus.FloorNormal || actualLeftFootHit != stimulus.LeftFootHit ||
            actualRightFootHit != stimulus.RightFootHit ||
            (stimulus.PreviousPlatformId >= 0 && stimulus.InitialLeftLock.Locked != 0 &&
             previousLeftLockBasePosition != stimulus.InitialLeftLock.LocalPosition) ||
            (stimulus.PreviousPlatformId >= 0 && stimulus.InitialRightLock.Locked != 0 &&
             previousRightLockBasePosition != stimulus.InitialRightLock.LocalPosition) ||
            (stimulus.PreviousPlatformId < 0 && stimulus.InitialLeftLock.Locked != 0 &&
             previousLeftLockWorldPosition != stimulus.InitialLeftLock.LocalPosition) ||
            (stimulus.PreviousPlatformId < 0 && stimulus.InitialRightLock.Locked != 0 &&
             previousRightLockWorldPosition != stimulus.InitialRightLock.LocalPosition))
        {
            throw new FormatException("nativeActual and stimulus do not describe the same captured frame.");
        }

        if (transitionFrameDelta != stimulus.TransitionFrameDelta ||
            runtimePreviousYawCurve != stimulus.PreviousYawCurve ||
            runtimeCurrentYawCurve != stimulus.CurrentYawCurve ||
            (category is "Aim" or "Turn" or "Rotate") &&
            !runtimeSourceAnimationObjectPath.Equals(sourceAnimationObjectPath, StringComparison.Ordinal))
        {
            throw new FormatException("nativeActual runtime source and transition evidence disagree with the stimulus.");
        }
        if (category is "Aim" or "Turn" or "Rotate" &&
            (runtimeInstanceClassPath.Length == 0 || runtimeNodePropertyName.Length == 0 ||
             runtimeNodePropertyOrdinal < 0 || runtimeSourceWeight <= 0f))
        {
            throw new FormatException("nativeActual runtime player identity is incomplete.");
        }

        return new AlsPoseNativeActual(
            viewYaw, viewPitch,
            turnAnimationId, turnCurveId, turnPhase, turnNominalDegrees, turnDirection, turnActive,
            rotateAnimationId, rotateCurveId, rotatePhase, rotateDirection, rotateActive,
            leftFootPlatformId, rightFootPlatformId, leftReleaseReason, rightReleaseReason);
    }

    private static AlsPoseExpected ReadExpected(JsonElement element)
    {
        ValidateObject(element,
        [
            "aimRelativeYaw", "aimRelativePitch", "headWeight", "spineWeight", "upperBodyWeight", "spineResidualYaw",
            "turnAnimationId", "turnCurveId", "turnPhase", "turnPlayRate", "turnNominalDegrees", "turnDirection", "turnActive", "turnYawDelta",
            "rotateAnimationId", "rotateCurveId", "rotatePhase", "rotatePlayRate", "rotateDirection", "rotateActive", "rotateYawDelta",
            "pelvisOffset", "leftFoot", "rightFoot", "leftReleaseReason", "rightReleaseReason",
        ], "portExpected");
        return new AlsPoseExpected(
            ReadFloat(element, "aimRelativeYaw"), ReadFloat(element, "aimRelativePitch"),
            ReadFloat(element, "headWeight", 0f, 1f), ReadFloat(element, "spineWeight", 0f, 1f),
            ReadFloat(element, "upperBodyWeight", 0f, 1f), ReadFloat(element, "spineResidualYaw"),
            ReadInt(element, "turnAnimationId", -1), ReadInt(element, "turnCurveId", -1),
            ReadFloat(element, "turnPhase", 0f, 1f), ReadFloat(element, "turnPlayRate", 0f),
            checked((short)ReadInt(element, "turnNominalDegrees", 0)), checked((sbyte)ReadInt(element, "turnDirection", -1, 1)),
            ReadByte(element, "turnActive"), ReadFloat(element, "turnYawDelta"),
            ReadInt(element, "rotateAnimationId", -1), ReadInt(element, "rotateCurveId", -1),
            ReadFloat(element, "rotatePhase", 0f, 1f), ReadFloat(element, "rotatePlayRate", 0f),
            checked((sbyte)ReadInt(element, "rotateDirection", -1, 1)), ReadByte(element, "rotateActive"),
            ReadFloat(element, "rotateYawDelta"), ReadVector3(element, "pelvisOffset"),
            ReadFootOutput(element, "leftFoot"), ReadFootOutput(element, "rightFoot"),
            ReadEnum<AlsFootReleaseReason>(element, "leftReleaseReason"),
            ReadEnum<AlsFootReleaseReason>(element, "rightReleaseReason"));
    }

    private static AlsFootHit ReadFootHit(JsonElement parent, string propertyName)
    {
        var element = parent.GetProperty(propertyName);
        ValidateObject(element,
        [
            "valid", "walkable", "position", "normal", "platformId", "platformPosition",
            "platformRotation", "colliderId", "pointVelocity",
        ], propertyName);
        return new AlsFootHit(
            ReadByte(element, "valid"), ReadByte(element, "walkable"),
            ReadVector3(element, "position"), ReadUnitVector(element, "normal"),
            ReadInt(element, "platformId", -1), ReadVector3(element, "platformPosition"),
            ReadQuaternion(element, "platformRotation"), ReadLong(element, "colliderId", -1),
            ReadVector3(element, "pointVelocity"));
    }

    private static AlsFootPoseOutput ReadFootOutput(JsonElement parent, string propertyName)
    {
        var element = parent.GetProperty(propertyName);
        ValidateObject(element, ["position", "rotation", "lockAmount", "platformId"], propertyName);
        return new AlsFootPoseOutput(
            ReadVector3(element, "position"), ReadQuaternion(element, "rotation"),
            ReadFloat(element, "lockAmount", 0f, 1f), ReadInt(element, "platformId", -1));
    }

    private static void ReadReference(JsonElement element)
    {
        ValidateObject(element, ["repository", "commit", "targetEngine", "patchHashes"], "reference");
        RequireString(element, "repository", "https://github.com/Sixze/ALS-Refactored.git");
        RequireString(element, "commit", ReferenceCommit);
        RequireString(element, "targetEngine", "5.9.0");
        var hashes = element.GetProperty("patchHashes");
        if (hashes.ValueKind != JsonValueKind.Array || hashes.GetArrayLength() != 1 ||
            hashes[0].ValueKind != JsonValueKind.String || hashes[0].GetString() != PatchHash)
        {
            throw new FormatException("reference.patchHashes does not match the locked compatibility patch.");
        }
    }

    private static void ReadCoordinateSystem(JsonElement element)
    {
        ValidateObject(element, ["source", "target", "distanceUnit", "angleUnit", "quaternionSign"], "coordinateSystem");
        RequireString(element, "source", "UE5_XForward_YRight_ZUp");
        RequireString(element, "target", "Godot_XRight_YUp_ZBack");
        RequireString(element, "distanceUnit", "meter");
        RequireString(element, "angleUnit", "radian");
        RequireString(element, "quaternionSign", "canonical_nonnegative_w");
    }

    private static void ReadSources(JsonElement element)
    {
        ValidateObject(element,
            ["characterSettings", "movementSettings", "animationSettings", "animationBlueprint", "aimOverlayAnimationBlueprint", "skeletalMesh", "controlRig"],
            "sources");
        RequireString(element, "characterSettings", "/ALS/ALS/Data/Character/CS_Als_Default.CS_Als_Default");
        RequireString(element, "movementSettings", "/ALS/ALS/Data/Character/Movement/MS_Als_Normal.MS_Als_Normal");
        RequireString(element, "animationSettings", "/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default");
        RequireString(element, "animationBlueprint", "/ALS/ALS/Character/AB_Als.AB_Als_C");
        RequireString(element, "aimOverlayAnimationBlueprint",
            "/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_Rifle.AB_Als_Rifle_C");
        RequireString(element, "skeletalMesh", "/ALS/ALS/Character/SKM_Als.SKM_Als");
        RequireString(element, "controlRig", "/ALS/ALS/Character/CR_Als.CR_Als_C");
    }

    private static (float Position, float Rotation) ReadTolerances(JsonElement element)
    {
        ValidateObject(element, ["positionMeters", "rotationRadians"], "tolerances");
        return (
            ReadFloat(element, "positionMeters", float.Epsilon, 0.01f),
            ReadFloat(element, "rotationRadians", float.Epsilon, 0.02f));
    }

    private static string ReadName(JsonElement root, string path)
    {
        var name = ReadString(root, "name");
        if (!ExpectedCases.ContainsKey(name))
        {
            throw new FormatException("name is not a supported P4 trace.");
        }

        var expectedFile = $"trace_p4_{name}.json";
        if (!string.Equals(Path.GetFileName(path), expectedFile, StringComparison.Ordinal))
        {
            throw new FormatException("trace name does not match its file name.");
        }

        return name;
    }

    private static void ReadTransform(JsonElement parent, string name)
    {
        var transform = parent.GetProperty(name);
        ValidateObject(transform, ["position", "rotation"], name);
        _ = ReadVector3(transform, "position");
        _ = ReadQuaternion(transform, "rotation");
    }

    private static Vector3 ReadUnitVector(JsonElement parent, string name)
    {
        var vector = ReadVector3(parent, name);
        if (MathF.Abs(vector.Length() - 1f) > UnitTolerance)
        {
            throw new FormatException($"{name} must be a unit vector.");
        }

        return Vector3.Normalize(vector);
    }

    private static Vector3 ReadVector3(JsonElement parent, string name)
    {
        var element = parent.GetProperty(name);
        ValidateObject(element, ["x", "y", "z"], name);
        return new Vector3(ReadFloat(element, "x"), ReadFloat(element, "y"), ReadFloat(element, "z"));
    }

    private static Quaternion ReadQuaternion(JsonElement parent, string name)
    {
        var element = parent.GetProperty(name);
        ValidateObject(element, ["x", "y", "z", "w"], name);
        var value = new Quaternion(
            ReadFloat(element, "x"), ReadFloat(element, "y"),
            ReadFloat(element, "z"), ReadFloat(element, "w"));
        var length = value.Length();
        if (MathF.Abs(length - 1f) > UnitTolerance)
        {
            throw new FormatException($"{name} must be a unit quaternion.");
        }

        value = Quaternion.Normalize(value);
        if (value.W < 0f)
        {
            throw new FormatException($"{name} must use the canonical quaternion sign.");
        }

        return value;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement parent, string name)
        where TEnum : struct, Enum
    {
        var text = ReadString(parent, name);
        if (!Enum.TryParse<TEnum>(text, ignoreCase: false, out var value) || !Enum.IsDefined(value))
        {
            throw new FormatException($"{name} is not a defined {typeof(TEnum).Name}.");
        }

        return value;
    }

    private static string ReadOneOf(JsonElement parent, string name, params string[] values)
    {
        var value = ReadString(parent, name);
        if (Array.IndexOf(values, value) < 0)
        {
            throw new FormatException($"{name} has an unsupported value.");
        }

        return value;
    }

    private static string ReadString(JsonElement parent, string name)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } value || value.Length == 0)
        {
            throw new FormatException($"{name} must be a non-empty string.");
        }

        return value;
    }

    private static float ReadFloat(
        JsonElement parent,
        string name,
        float minimum = -float.MaxValue,
        float maximum = float.MaxValue)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetSingle(out var value) ||
            !float.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new FormatException($"{name} must be a finite number in range.");
        }

        return value;
    }

    private static byte ReadByte(JsonElement parent, string name) =>
        checked((byte)ReadInt(parent, name, 0, 1));

    private static int ReadInt(JsonElement parent, string name, int minimum, int maximum = int.MaxValue)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) ||
            value < minimum || value > maximum)
        {
            throw new FormatException($"{name} must be an integer in range.");
        }

        return value;
    }

    private static long ReadLong(JsonElement parent, string name, long minimum)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value) || value < minimum)
        {
            throw new FormatException($"{name} must be an integer in range.");
        }

        return value;
    }

    private static void ValidateObject(JsonElement element, string[] expected, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{path} must be an object.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new FormatException($"Duplicate property '{path}.{property.Name}'.");
            }

            if (Array.IndexOf(expected, property.Name) < 0)
            {
                throw new FormatException($"Unknown property '{path}.{property.Name}'.");
            }
        }

        foreach (var property in expected)
        {
            if (!seen.Contains(property))
            {
                throw new FormatException($"Missing property '{path}.{property}'.");
            }
        }
    }

    private static void RequireString(JsonElement parent, string name, string expected)
    {
        if (ReadString(parent, name) != expected)
        {
            throw new FormatException($"{name} does not match the locked value.");
        }
    }

    private static void RequireInteger(JsonElement parent, string name, int expected)
    {
        if (ReadInt(parent, name, int.MinValue) != expected)
        {
            throw new FormatException($"{name} does not match the locked value.");
        }
    }

    private static void RequireNumber(JsonElement parent, string name, double expected, double tolerance)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) ||
            !double.IsFinite(value) || System.Math.Abs(value - expected) > tolerance)
        {
            throw new FormatException($"{name} does not match the locked value.");
        }
    }

    private static void CompareFoot(
        List<AlsPoseTraceIssue> issues,
        string caseId,
        string field,
        in AlsFootPoseOutput expected,
        in AlsFootPoseOutput actual,
        AlsPoseTrace trace)
    {
        ComparePosition(issues, caseId, $"{field}.position", expected.Position, actual.Position, trace.PositionToleranceMeters);
        if (QuaternionAngle(expected.Rotation, actual.Rotation) > trace.RotationToleranceRadians)
        {
            issues.Add(new AlsPoseTraceIssue(caseId, $"{field}.rotation", expected.Rotation.ToString(), actual.Rotation.ToString()));
        }
        CompareFloatExact(issues, caseId, $"{field}.lockAmount", expected.LockAmount, actual.LockAmount);
        CompareExact(issues, caseId, $"{field}.platformId", expected.PlatformId, actual.PlatformId);
    }

    private static void ComparePosition(
        List<AlsPoseTraceIssue> issues,
        string caseId,
        string field,
        in Vector3 expected,
        in Vector3 actual,
        float tolerance)
    {
        if (Vector3.Distance(expected, actual) > tolerance)
        {
            issues.Add(new AlsPoseTraceIssue(caseId, field, expected.ToString(), actual.ToString()));
        }
    }

    private static void CompareFloatExact(
        List<AlsPoseTraceIssue> issues,
        string caseId,
        string field,
        float expected,
        float actual)
    {
        if (!expected.Equals(actual))
        {
            issues.Add(new AlsPoseTraceIssue(caseId, field, expected.ToString("R"), actual.ToString("R")));
        }
    }

    private static void CompareExact<T>(
        List<AlsPoseTraceIssue> issues,
        string caseId,
        string field,
        T expected,
        T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            var expectedText = expected is null ? string.Empty : expected.ToString() ?? string.Empty;
            var actualText = actual is null ? string.Empty : actual.ToString() ?? string.Empty;
            issues.Add(new AlsPoseTraceIssue(caseId, field, expectedText, actualText));
        }
    }

    private static float QuaternionAngle(in Quaternion left, in Quaternion right)
    {
        var dot = MathF.Abs(Quaternion.Dot(left, right));
        return 2f * MathF.Acos(System.Math.Clamp(dot, -1f, 1f));
    }

    private static float Normalize(float angle) => MathF.Atan2(MathF.Sin(angle), MathF.Cos(angle));

    private static IReadOnlyDictionary<string, AlsPoseCaseDescriptor> CreateCaseDescriptors()
    {
        var result = new Dictionary<string, AlsPoseCaseDescriptor>(StringComparer.Ordinal);
        const string idle = "/ALS/ALS/Animations/Base/A_Als_Idle.A_Als_Idle";
        void Add(
            string id, string category, AlsStance stance, string direction, string phase,
            short nominal = 0, AlsFootReleaseReason release = AlsFootReleaseReason.None,
            string sourceAnimationObjectPath = idle, string sourceCurveNamesKey = "FootLock_L,FootLock_R") =>
            result.Add(id, new AlsPoseCaseDescriptor(
                category, stance, direction, phase, nominal, release, release,
                sourceAnimationObjectPath, sourceCurveNamesKey));

        const string look = "/ALS/ALS/Animations/View/BS_Als_Look.BS_Als_Look";
        Add("aim_center", "Aim", AlsStance.Standing, "Center", "steady", sourceAnimationObjectPath: look, sourceCurveNamesKey: "Layering_Head");
        Add("aim_up", "Aim", AlsStance.Standing, "Up", "steady", sourceAnimationObjectPath: look, sourceCurveNamesKey: "Layering_Head");
        Add("aim_down", "Aim", AlsStance.Standing, "Down", "steady", sourceAnimationObjectPath: look, sourceCurveNamesKey: "Layering_Head");
        Add("aim_left", "Aim", AlsStance.Standing, "Left", "steady", sourceAnimationObjectPath: look, sourceCurveNamesKey: "Layering_Head");
        Add("aim_right", "Aim", AlsStance.Standing, "Right", "steady", sourceAnimationObjectPath: look, sourceCurveNamesKey: "Layering_Head");
        foreach (var stance in new[] { AlsStance.Standing, AlsStance.Crouching })
        {
            var stanceName = stance.ToString().ToLowerInvariant();
            foreach (var direction in new[] { "Left", "Right" })
            {
                var stancePrefix = stance == AlsStance.Crouching ? "Crouch_" : string.Empty;
                var turn90 = $"/ALS/ALS/Animations/TurnInPlace/A_Als_{stancePrefix}Turn_90_{direction}.A_Als_{stancePrefix}Turn_90_{direction}";
                var turn180 = $"/ALS/ALS/Animations/TurnInPlace/A_Als_{stancePrefix}Turn_180_{direction}.A_Als_{stancePrefix}Turn_180_{direction}";
                var rotate = $"/ALS/ALS/Animations/RotateInPlace/A_Als_{stancePrefix}Rotate_90_{direction}.A_Als_{stancePrefix}Rotate_90_{direction}";
                Add($"turn_{stanceName}_{direction.ToLowerInvariant()}_90", "Turn", stance, direction, "playing", 90,
                    sourceAnimationObjectPath: turn90, sourceCurveNamesKey: "RotationYawSpeed");
                Add($"turn_{stanceName}_{direction.ToLowerInvariant()}_180", "Turn", stance, direction, "playing", 180,
                    sourceAnimationObjectPath: turn180, sourceCurveNamesKey: "RotationYawSpeed");
                Add($"rotate_{stanceName}_{direction.ToLowerInvariant()}", "Rotate", stance, direction, "playing",
                    sourceAnimationObjectPath: rotate, sourceCurveNamesKey: "RotationYawSpeed");
            }
        }
        Add("feet_flat", "Feet", AlsStance.Standing, "None", "steady");
        Add("feet_slope", "Feet", AlsStance.Standing, "None", "steady");
        Add("feet_stairs", "Feet", AlsStance.Standing, "None", "steady");
        Add("platform_translate", "Platform", AlsStance.Standing, "None", "hold");
        Add("platform_rotate", "Platform", AlsStance.Standing, "None", "hold");
        Add("platform_base_change", "Platform", AlsStance.Standing, "None", "release", release: AlsFootReleaseReason.BaseChanged);
        Add("platform_teleport", "Platform", AlsStance.Standing, "None", "release", release: AlsFootReleaseReason.Teleported);
        Add("platform_release", "Platform", AlsStance.Standing, "None", "release", release: AlsFootReleaseReason.PlatformRemoved);
        return result;
    }
}

public sealed class AlsPoseTraceCase
{
    internal AlsPoseTraceCase(
        string caseId,
        string category,
        AlsStance stance,
        string direction,
        string phase,
        string sourceAnimationObjectPath,
        string[] sourceCurveNames,
        AlsPoseStimulus stimulus,
        AlsPoseNativeActual native,
        AlsPoseExpected expected)
    {
        CaseId = caseId;
        Category = category;
        Stance = stance;
        Direction = direction;
        Phase = phase;
        SourceAnimationObjectPath = sourceAnimationObjectPath;
        SourceCurveNames = sourceCurveNames;
        Stimulus = stimulus;
        Native = native;
        Expected = expected;
    }

    public string CaseId { get; }

    public string Category { get; }

    public AlsStance Stance { get; }

    public string Direction { get; }

    public string Phase { get; }

    public string SourceAnimationObjectPath { get; }

    public IReadOnlyList<string> SourceCurveNames { get; }

    internal AlsPoseStimulus Stimulus { get; }

    internal AlsPoseNativeActual Native { get; }

    internal AlsPoseExpected Expected { get; }
}

public readonly record struct AlsPoseTraceIssue(
    string CaseId,
    string Field,
    string Expected,
    string Actual);

internal readonly record struct AlsPoseStimulus(
    Vector3 CharacterPosition,
    float CharacterYaw,
    float ViewYaw,
    float ViewPitch,
    float AimYaw,
    float AimPitch,
    AlsRotationMode RotationMode,
    AlsStance Stance,
    byte Grounded,
    float Speed,
    float Acceleration,
    Vector3 FloorNormal,
    int PlatformId,
    long PlatformColliderId,
    Vector3 PlatformPosition,
    Quaternion PlatformRotation,
    Vector3 PlatformAngularVelocity,
    int PreviousPlatformId,
    long PreviousPlatformColliderId,
    Vector3 PreviousPlatformPosition,
    Quaternion PreviousPlatformRotation,
    AlsPoseInitialLock InitialLeftLock,
    AlsPoseInitialLock InitialRightLock,
    Vector3 LeftFootOrigin,
    Vector3 RightFootOrigin,
    AlsFootHit LeftFootHit,
    AlsFootHit RightFootHit,
    float LeftIkWeight,
    float RightIkWeight,
    float LeftLockCurve,
    float RightLockCurve,
    float PreviousYawCurve,
    float CurrentYawCurve,
    float PreviousYawPhase,
    float YawPhasePlayRate,
    int TransitionFrameDelta,
    AlsFootPlacementReleaseSignals PlatformRemovalSignals);

internal readonly record struct AlsPoseInitialLock(
    Vector3 LocalPosition,
    Quaternion LocalRotation,
    float Amount,
    byte Locked);

internal readonly record struct AlsPoseNativeActual(
    float ViewYaw,
    float ViewPitch,
    int TurnAnimationId,
    int TurnCurveId,
    float TurnPhase,
    short TurnNominalDegrees,
    sbyte TurnDirection,
    byte TurnActive,
    int RotateAnimationId,
    int RotateCurveId,
    float RotatePhase,
    sbyte RotateDirection,
    byte RotateActive,
    int LeftFootPlatformId,
    int RightFootPlatformId,
    AlsFootReleaseReason LeftReleaseReason,
    AlsFootReleaseReason RightReleaseReason);

internal readonly record struct AlsPoseExpected(
    float AimRelativeYaw,
    float AimRelativePitch,
    float HeadWeight,
    float SpineWeight,
    float UpperBodyWeight,
    float SpineResidualYaw,
    int TurnAnimationId,
    int TurnCurveId,
    float TurnPhase,
    float TurnPlayRate,
    short TurnNominalDegrees,
    sbyte TurnDirection,
    byte TurnActive,
    float TurnYawDelta,
    int RotateAnimationId,
    int RotateCurveId,
    float RotatePhase,
    float RotatePlayRate,
    sbyte RotateDirection,
    byte RotateActive,
    float RotateYawDelta,
    Vector3 PelvisOffset,
    AlsFootPoseOutput LeftFoot,
    AlsFootPoseOutput RightFoot,
    AlsFootReleaseReason LeftReleaseReason,
    AlsFootReleaseReason RightReleaseReason);

internal readonly record struct AlsPoseReplayResult(
    AlsViewPoseOutput View,
    AlsTurnRotateSelection Selection,
    AlsTurnRotateOutput Yaw,
    AlsFootPlacementOutput Feet);

internal readonly record struct AlsPoseCaseDescriptor(
    string Category,
    AlsStance Stance,
    string Direction,
    string Phase,
    short NominalDegrees,
    AlsFootReleaseReason LeftReleaseReason,
    AlsFootReleaseReason RightReleaseReason,
    string SourceAnimationObjectPath,
    string SourceCurveNamesKey);

internal readonly record struct AlsPoseCaseSource(
    string AnimationObjectPath,
    string[] CurveNames);
