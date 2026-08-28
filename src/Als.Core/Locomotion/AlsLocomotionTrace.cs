using System.Globalization;
using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Math;

namespace GodotAls.Core.Locomotion;

public sealed class AlsLocomotionTrace
{
    private const double FixedDeltaSeconds = 1d / 60d;
    private const double TimeTolerance = 1e-12d;
    private const float MetricTolerance = 0.001f;
    private const float ParameterTolerance = 0.0001f;
    private const float YawTolerance = MathF.PI / 1800f;

    private static readonly string[] RootProperties =
    [
        "fixedDeltaSeconds", "frames", "kind", "name", "patchHashes",
        "referenceCommit", "schemaVersion",
    ];

    private static readonly string[] FrameProperties =
        ["command", "index", "nativeActual", "physicalActual", "portExpected", "tick", "time"];

    private static readonly string[] CommandProperties =
    [
        "aimYaw", "jumpPressed", "movementAxes", "requestedGait", "requestedRotationMode",
        "requestedStance", "standBlocked", "viewYaw",
    ];

    private static readonly string[] PhysicalActualProperties =
    [
        "acceleration", "grounded", "jumpTransition", "maxAcceleration", "maxBrakingDeceleration",
        "position", "rotationMode", "stance", "velocity", "yaw",
    ];

    private static readonly string[] NativeActualProperties =
        ["blendCoordinates", "gait", "lean", "locomotionState", "observedAnimationState", "playRate",
         "rotationMode", "stance", "stride", "synthesizedAnimationPhase", "targetYaw"];

    private static readonly string[] PortExpectedProperties =
        ["animationPhase", "animationState", "blendCoordinates", "gait", "lean", "locomotionState",
         "playRate", "rotationMode", "stance", "stride", "targetYaw"];

    private static readonly string[] Vector2Properties = ["x", "y"];
    private static readonly string[] Vector3Properties = ["x", "y", "z"];

    private static readonly IReadOnlyDictionary<string, int> SequenceFrameCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["idle_gaits"] = 240,
        ["directions"] = 240,
        ["crouch_clearance"] = 210,
        ["rotation_modes"] = 240,
        ["jump_land"] = 240,
    };

    private AlsLocomotionTrace(
        string name,
        string referenceCommit,
        string[] patchHashes,
        AlsLocomotionTraceFrame[] frames)
    {
        Name = name;
        ReferenceCommit = referenceCommit;
        PatchHashes = patchHashes;
        Frames = frames;
    }

    public string Name { get; }

    public string ReferenceCommit { get; }

    public IReadOnlyList<string> PatchHashes { get; }

    public AlsLocomotionTraceFrame[] Frames { get; }

    public static AlsLocomotionTrace Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            ValidateObject(root, RootProperties, "root");
            RequireInt32(root, "schemaVersion", 1);
            RequireString(root, "kind", "trace");
            RequireString(root, "referenceCommit", AlsLocomotionSettings.ReferenceCommit);
            RequireDouble(root, "fixedDeltaSeconds", FixedDeltaSeconds);
            var patchHashes = ReadPatchHashes(root.GetProperty("patchHashes"));
            var name = ReadSequenceName(root.GetProperty("name"), path);
            var frames = ReadFrames(root.GetProperty("frames"), name);

            return new AlsLocomotionTrace(
                name,
                AlsLocomotionSettings.ReferenceCommit,
                patchHashes,
                frames);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or InvalidOperationException or OverflowException)
        {
            throw new FormatException("Invalid P3 locomotion trace JSON.", exception);
        }
    }

    public static AlsLocomotionTraceIssue[] Compare(
        AlsLocomotionTrace trace,
        AlsLocomotionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(settings);

        var issues = new List<AlsLocomotionTraceIssue>();
        var state = default(AlsRuntimeState);
        var result = default(AlsFrameResult);

        foreach (var frame in trace.Frames)
        {
            AlsLocomotionModel.Evaluate(frame.Input, ref state, ref result, settings);

            CompareExact(issues, frame.Index, "locomotionState", frame.ExpectedLocomotionState, result.ResolvedLocomotionState);
            CompareExact(issues, frame.Index, "actualGait", frame.ExpectedActualGait, result.ActualGait);
            CompareExact(issues, frame.Index, "actualStance", frame.ExpectedActualStance, result.ActualStance);
            CompareExact(issues, frame.Index, "actualRotationMode", frame.ExpectedActualRotationMode, result.ActualRotationMode);
            CompareExact(issues, frame.Index, "animationState", frame.ExpectedAnimationState, result.AnimationState);
            CompareNumber(issues, frame.Index, "blendCoordinates.x", frame.ExpectedBlendCoordinates.X, result.BlendCoordinates.X, MetricTolerance);
            CompareNumber(issues, frame.Index, "blendCoordinates.y", frame.ExpectedBlendCoordinates.Y, result.BlendCoordinates.Y, MetricTolerance);
            CompareNumber(issues, frame.Index, "stride", frame.ExpectedStride, result.Stride, ParameterTolerance);
            CompareNumber(issues, frame.Index, "playRate", frame.ExpectedPlayRate, result.PlayRate, ParameterTolerance);
            CompareNumber(issues, frame.Index, "lean.x", frame.ExpectedLean.X, result.Lean.X, ParameterTolerance);
            CompareNumber(issues, frame.Index, "lean.y", frame.ExpectedLean.Y, result.Lean.Y, ParameterTolerance);
            ComparePhase(issues, frame.Index, frame.ExpectedAnimationPhase, result.AnimationPhase);
            CompareYaw(issues, frame.Index, frame.ExpectedTargetYaw, result.TargetYaw);
        }

        return [.. issues];
    }

    private static AlsLocomotionTraceFrame[] ReadFrames(JsonElement element, string sequenceName)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("frames must be an array.");
        }

        var expectedFrameCount = SequenceFrameCounts[sequenceName];
        if (element.GetArrayLength() != expectedFrameCount)
        {
            throw new FormatException(
                $"Sequence '{sequenceName}' frame count must be exactly {expectedFrameCount}, " +
                $"actual {element.GetArrayLength()}.");
        }

        var frames = new AlsLocomotionTraceFrame[element.GetArrayLength()];
        var frameIndex = 0;
        foreach (var frameElement in element.EnumerateArray())
        {
            var path = $"frames[{frameIndex}]";
            ValidateObject(frameElement, FrameProperties, path);
            var index = ReadInt32(frameElement, "index", 0, int.MaxValue);
            var tick = ReadInt64(frameElement, "tick", 0, long.MaxValue);
            if (index != frameIndex || tick != frameIndex)
            {
                throw new FormatException($"{path} index and tick must be sequential from zero.");
            }

            var time = ReadFiniteDouble(frameElement, "time", 0d, double.MaxValue);
            var expectedTime = frameIndex * FixedDeltaSeconds;
            if (System.Math.Abs(time - expectedTime) > TimeTolerance)
            {
                throw new FormatException($"{path}.time must equal index * fixedDeltaSeconds.");
            }

            frames[frameIndex] = ReadFrame(frameElement, frameIndex, tick, time);
            frameIndex++;
        }

        return frames;
    }

    private static AlsLocomotionTraceFrame ReadFrame(
        JsonElement frameElement,
        int index,
        long tick,
        double time)
    {
        var commandElement = frameElement.GetProperty("command");
        var physicalElement = frameElement.GetProperty("physicalActual");
        var nativeElement = frameElement.GetProperty("nativeActual");
        var expectedElement = frameElement.GetProperty("portExpected");
        ValidateObject(commandElement, CommandProperties, $"frames[{index}].command");
        ValidateObject(physicalElement, PhysicalActualProperties, $"frames[{index}].physicalActual");
        ValidateObject(nativeElement, NativeActualProperties, $"frames[{index}].nativeActual");
        ValidateObject(expectedElement, PortExpectedProperties, $"frames[{index}].portExpected");

        var commandAxes = ReadVector2(commandElement, "movementAxes", -1f, 1f);
        var requestedGait = ReadEnum<AlsGait>(commandElement, "requestedGait");
        var requestedStance = ReadEnum<AlsStance>(commandElement, "requestedStance");
        var requestedRotationMode = ReadEnum<AlsRotationMode>(commandElement, "requestedRotationMode");
        var jumpPressed = ReadBooleanByte(commandElement, "jumpPressed");
        _ = ReadBooleanByte(commandElement, "standBlocked");
        var viewYaw = ConvertYaw(ReadFiniteFloat(commandElement, "viewYaw"));
        var aimYaw = ConvertYaw(ReadFiniteFloat(commandElement, "aimYaw"));

        var command = new AlsLocomotionCommand(
            commandAxes,
            viewYaw,
            0f,
            aimYaw,
            0f,
            requestedGait,
            requestedStance,
            requestedRotationMode,
            jumpPressed);

        var uePosition = ReadVector3(physicalElement, "position");
        var ueVelocity = ReadVector3(physicalElement, "velocity");
        var ueAcceleration = ReadVector3(physicalElement, "acceleration");
        var actualPosition = ConvertVector(uePosition);
        var actualVelocity = ConvertVector(ueVelocity);
        var actualAcceleration = ConvertVector(ueAcceleration);
        var actualYaw = ConvertYaw(ReadFiniteFloat(physicalElement, "yaw"));
        var grounded = ReadBooleanByte(physicalElement, "grounded");
        var jumpTransition = ReadBooleanByte(physicalElement, "jumpTransition");
        var actualStance = ReadEnum<AlsStance>(physicalElement, "stance");
        var actualRotationMode = ReadEnum<AlsRotationMode>(physicalElement, "rotationMode");
        var locomotionState = ReadEnum<AlsLocomotionState>(expectedElement, "locomotionState");
        var actualGait = ReadEnum<AlsGait>(expectedElement, "gait");
        var expectedStance = ReadEnum<AlsStance>(expectedElement, "stance");
        var expectedRotationMode = ReadEnum<AlsRotationMode>(expectedElement, "rotationMode");
        if (locomotionState is not (AlsLocomotionState.Grounded or AlsLocomotionState.InAir))
        {
            throw new FormatException($"frames[{index}].portExpected.locomotionState is not valid for P3.");
        }

        if ((grounded == 1) != (locomotionState == AlsLocomotionState.Grounded))
        {
            throw new FormatException($"frames[{index}] grounded and locomotionState disagree.");
        }

        if (jumpTransition == 1 && (jumpPressed == 0 || grounded == 1))
        {
            throw new FormatException($"frames[{index}] contains an inconsistent jump transition.");
        }

        var animationState = ReadEnum<AlsAnimationState>(expectedElement, "animationState");
        var maxAcceleration = ReadFiniteFloat(physicalElement, "maxAcceleration", 0f, float.MaxValue);
        var maxBraking = ReadFiniteFloat(physicalElement, "maxBrakingDeceleration", 0f, float.MaxValue);
        var blendCoordinates = ReadVector2(expectedElement, "blendCoordinates");
        var stride = ReadFiniteFloat(expectedElement, "stride", 0f, 1f);
        var playRate = ReadFiniteFloat(expectedElement, "playRate", 0f, 3f);
        var lean = ReadVector2(expectedElement, "lean", -1f, 1f);
        var phase = ReadFiniteFloat(expectedElement, "animationPhase", 0f, 1f, maximumExclusive: true);
        var targetYaw = ConvertYaw(ReadFiniteFloat(expectedElement, "targetYaw"));
        var nativeActual = ReadNativeActual(nativeElement);

        var transform = Matrix4x4.CreateRotationY(actualYaw);
        transform.Translation = actualPosition;
        var input = AlsFrameInput.CreateDefault(
            new AlsFrameIdentity(index, 0, 1),
            (float)FixedDeltaSeconds) with
        {
            CharacterTransform = transform,
            ActualVelocity = actualVelocity,
            ActualAcceleration = actualAcceleration,
            InputDirection = AlsLocomotionCommandResolver.Resolve(command, actualStance).WorldDirection,
            DesiredSpeed = MathF.Sqrt(
                (actualVelocity.X * actualVelocity.X) +
                (actualVelocity.Z * actualVelocity.Z)),
            ViewRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, viewYaw),
            AimRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, aimYaw),
            Floor = new AlsFloorSample(grounded, Vector3.UnitY, -1, Matrix4x4.Identity, Vector3.Zero),
            RequestedGait = requestedGait,
            Stance = actualStance,
            RotationMode = actualRotationMode,
            Command = command,
            CharacterYaw = actualYaw,
            MaxAcceleration = maxAcceleration,
            MaxBrakingDeceleration = maxBraking,
            JumpAccepted = (byte)(jumpTransition == 1 && jumpPressed == 1 ? 1 : 0),
        };

        return new AlsLocomotionTraceFrame(
            index,
            tick,
            time,
            input,
            nativeActual,
            locomotionState,
            actualGait,
            expectedStance,
            expectedRotationMode,
            animationState,
            blendCoordinates,
            stride,
            playRate,
            lean,
            phase,
            targetYaw,
            grounded,
            jumpTransition);
    }

    private static AlsNativeLocomotionObservation ReadNativeActual(JsonElement element)
    {
        var locomotionState = ReadEnum<AlsLocomotionState>(element, "locomotionState");
        if (locomotionState is not (AlsLocomotionState.Grounded or AlsLocomotionState.InAir))
        {
            throw new FormatException("nativeActual.locomotionState is not valid for P3.");
        }

        return new AlsNativeLocomotionObservation(
            locomotionState,
            ReadEnum<AlsAnimationState>(element, "observedAnimationState"),
            ReadEnum<AlsGait>(element, "gait"),
            ReadEnum<AlsStance>(element, "stance"),
            ReadEnum<AlsRotationMode>(element, "rotationMode"),
            ReadVector2(element, "blendCoordinates"),
            ReadFiniteFloat(element, "stride", 0f, 1f),
            ReadFiniteFloat(element, "playRate", 0f, 3f),
            ReadVector2(element, "lean", -1f, 1f),
            ReadFiniteFloat(element, "synthesizedAnimationPhase", 0f, 1f, maximumExclusive: true),
            ConvertYaw(ReadFiniteFloat(element, "targetYaw")));
    }

    private static void ValidateObject(JsonElement element, string[] expectedProperties, string path)
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

            if (Array.IndexOf(expectedProperties, property.Name) < 0)
            {
                throw new FormatException($"Unknown property '{path}.{property.Name}'.");
            }
        }

        foreach (var expectedProperty in expectedProperties)
        {
            if (!seen.Contains(expectedProperty))
            {
                throw new FormatException($"Missing property '{path}.{expectedProperty}'.");
            }
        }
    }

    private static string[] ReadPatchHashes(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 1)
        {
            throw new FormatException("patchHashes must contain exactly one entry.");
        }

        var patch = element[0];
        if (patch.ValueKind != JsonValueKind.String || patch.GetString() != AlsLocomotionSettings.PatchHash)
        {
            throw new FormatException("patchHashes does not match the locked compatibility patch.");
        }

        return [AlsLocomotionSettings.PatchHash];
    }

    private static string ReadSequenceName(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String ||
            element.GetString() is not { } name ||
            !SequenceFrameCounts.ContainsKey(name))
        {
            throw new FormatException("name is not a supported P3 sequence.");
        }

        var expectedFileName = $"trace_{name}.json";
        if (!string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.Ordinal))
        {
            throw new FormatException($"Sequence name '{name}' does not match file name '{Path.GetFileName(path)}'.");
        }

        return name;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement parent, string propertyName)
        where TEnum : struct, Enum
    {
        var element = parent.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<TEnum>(element.GetString(), ignoreCase: false, out var value) ||
            !Enum.IsDefined(value))
        {
            throw new FormatException($"{propertyName} is not a defined {typeof(TEnum).Name} value.");
        }

        return value;
    }

    private static Vector2 ReadVector2(
        JsonElement parent,
        string propertyName,
        float minimum = -float.MaxValue,
        float maximum = float.MaxValue)
    {
        var element = parent.GetProperty(propertyName);
        ValidateObject(element, Vector2Properties, propertyName);
        return new Vector2(
            ReadFiniteFloat(element, "x", minimum, maximum),
            ReadFiniteFloat(element, "y", minimum, maximum));
    }

    private static Vector3 ReadVector3(JsonElement parent, string propertyName)
    {
        var element = parent.GetProperty(propertyName);
        ValidateObject(element, Vector3Properties, propertyName);
        return new Vector3(
            ReadFiniteFloat(element, "x"),
            ReadFiniteFloat(element, "y"),
            ReadFiniteFloat(element, "z"));
    }

    private static float ReadFiniteFloat(
        JsonElement parent,
        string propertyName,
        float minimum = -float.MaxValue,
        float maximum = float.MaxValue,
        bool maximumExclusive = false)
    {
        var value = ReadFiniteDouble(parent, propertyName, minimum, maximum, maximumExclusive);
        var result = (float)value;
        if (!float.IsFinite(result))
        {
            throw new FormatException($"{propertyName} must fit in a finite Single.");
        }

        return result;
    }

    private static double ReadFiniteDouble(
        JsonElement parent,
        string propertyName,
        double minimum,
        double maximum,
        bool maximumExclusive = false)
    {
        var element = parent.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value) ||
            value < minimum ||
            (maximumExclusive ? value >= maximum : value > maximum))
        {
            throw new FormatException($"{propertyName} must be a finite number in the documented range.");
        }

        return value;
    }

    private static int ReadInt32(JsonElement parent, string propertyName, int minimum, int maximum)
    {
        var element = parent.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value) ||
            value < minimum ||
            value > maximum)
        {
            throw new FormatException($"{propertyName} must be an integer in the documented range.");
        }

        return value;
    }

    private static long ReadInt64(JsonElement parent, string propertyName, long minimum, long maximum)
    {
        var element = parent.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt64(out var value) ||
            value < minimum ||
            value > maximum)
        {
            throw new FormatException($"{propertyName} must be an integer in the documented range.");
        }

        return value;
    }

    private static byte ReadBooleanByte(JsonElement parent, string propertyName) =>
        parent.GetProperty(propertyName).ValueKind switch
        {
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => throw new FormatException($"{propertyName} must be a boolean."),
        };

    private static void RequireInt32(JsonElement parent, string propertyName, int expected)
    {
        if (ReadInt32(parent, propertyName, int.MinValue, int.MaxValue) != expected)
        {
            throw new FormatException($"{propertyName} must equal {expected}.");
        }
    }

    private static void RequireString(JsonElement parent, string propertyName, string expected)
    {
        var element = parent.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.String || element.GetString() != expected)
        {
            throw new FormatException($"{propertyName} does not match the locked value.");
        }
    }

    private static void RequireDouble(JsonElement parent, string propertyName, double expected)
    {
        var element = parent.GetProperty(propertyName);
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value) ||
            value != expected)
        {
            throw new FormatException($"{propertyName} does not match the locked value {expected:R}.");
        }
    }

    private static Vector3 ConvertVector(in Vector3 unreal) => new(unreal.Y, unreal.Z, -unreal.X);

    private static float ConvertYaw(float unrealYaw) => AlsMath.NormalizeAngleRadians(-unrealYaw);

    private static void CompareExact<T>(
        List<AlsLocomotionTraceIssue> issues,
        int frame,
        string field,
        T expected,
        T actual)
        where T : struct, Enum
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            issues.Add(new AlsLocomotionTraceIssue(frame, field, expected.ToString(), actual.ToString()));
        }
    }

    private static void CompareNumber(
        List<AlsLocomotionTraceIssue> issues,
        int frame,
        string field,
        float expected,
        float actual,
        float tolerance)
    {
        if (!float.IsFinite(expected) || !float.IsFinite(actual) || MathF.Abs(expected - actual) > tolerance)
        {
            issues.Add(new AlsLocomotionTraceIssue(frame, field, Format(expected), Format(actual)));
        }
    }

    private static void ComparePhase(
        List<AlsLocomotionTraceIssue> issues,
        int frame,
        float expected,
        float actual)
    {
        var direct = MathF.Abs(expected - actual);
        var distance = MathF.Min(direct, 1f - direct);
        if (!float.IsFinite(expected) || !float.IsFinite(actual) || distance > ParameterTolerance)
        {
            issues.Add(new AlsLocomotionTraceIssue(
                frame,
                "animationPhase",
                Format(expected),
                Format(actual)));
        }
    }

    private static void CompareYaw(
        List<AlsLocomotionTraceIssue> issues,
        int frame,
        float expected,
        float actual)
    {
        var distance = MathF.Abs(AlsMath.NormalizeAngleRadians(actual - expected));
        if (!float.IsFinite(expected) || !float.IsFinite(actual) || distance > YawTolerance)
        {
            issues.Add(new AlsLocomotionTraceIssue(frame, "targetYaw", Format(expected), Format(actual)));
        }
    }

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}

public readonly record struct AlsLocomotionTraceFrame(
    int Index,
    long Tick,
    double Time,
    AlsFrameInput Input,
    AlsNativeLocomotionObservation NativeActual,
    AlsLocomotionState ExpectedLocomotionState,
    AlsGait ExpectedActualGait,
    AlsStance ExpectedActualStance,
    AlsRotationMode ExpectedActualRotationMode,
    AlsAnimationState ExpectedAnimationState,
    Vector2 ExpectedBlendCoordinates,
    float ExpectedStride,
    float ExpectedPlayRate,
    Vector2 ExpectedLean,
    float ExpectedAnimationPhase,
    float ExpectedTargetYaw,
    byte ExpectedGrounded,
    byte ExpectedJumpTransition);

public readonly record struct AlsNativeLocomotionObservation(
    AlsLocomotionState LocomotionState,
    AlsAnimationState ObservedAnimationState,
    AlsGait Gait,
    AlsStance Stance,
    AlsRotationMode RotationMode,
    Vector2 BlendCoordinates,
    float Stride,
    float PlayRate,
    Vector2 Lean,
    float SynthesizedAnimationPhase,
    float TargetYaw);

public readonly record struct AlsLocomotionTraceIssue(
    int Frame,
    string Field,
    string Expected,
    string Actual);
