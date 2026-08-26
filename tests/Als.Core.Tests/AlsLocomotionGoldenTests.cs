using System.Globalization;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using Xunit.Abstractions;

namespace GodotAls.Core.Tests;

public sealed class AlsLocomotionGoldenTests
{
    private const string PinnedCommit = "b754d6f0f2bb03741d301f8fb88077ebfe561e17";
    private readonly ITestOutputHelper _output;

    public AlsLocomotionGoldenTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<string> ReferenceFixtures => new()
    {
        "trace_idle_gaits.json",
        "trace_directions.json",
        "trace_crouch_clearance.json",
        "trace_rotation_modes.json",
        "trace_jump_land.json",
    };

    [Theory]
    [MemberData(nameof(ReferenceFixtures))]
    public void ReferenceTraceMatchesTheProductionModel(string fixture)
    {
        var trace = AlsLocomotionTrace.Load(P3Fixture.Path(fixture));

        Assert.Equal(PinnedCommit, trace.ReferenceCommit);
        var issues = AlsLocomotionTrace.Compare(trace, P3TestSettings.Reference);
        if (issues.Length > 0)
        {
            _output.WriteLine(Summarize(issues));
        }

        Assert.Empty(issues);
    }

    [Fact]
    public void EnumMismatchIsNeverHiddenByNumericTolerance()
    {
        var trace = AlsLocomotionTrace.Load(P3Fixture.Path("trace_idle_gaits.json"));
        trace.Frames[0] = trace.Frames[0] with { ExpectedActualGait = AlsGait.Sprinting };

        var issues = AlsLocomotionTrace.Compare(trace, P3TestSettings.Reference);

        Assert.Contains(issues, issue => issue.Field == "actualGait" && issue.Frame == 0);
    }

    [Fact]
    public void NativeObservationCanDifferFromThePortExpectationOnFrameZero()
    {
        var trace = AlsLocomotionTrace.Load(P3Fixture.Path("trace_directions.json"));

        Assert.NotEqual(trace.Frames[0].NativeActual.Stride, trace.Frames[0].ExpectedStride);
        Assert.NotEqual(trace.Frames[0].NativeActual.PlayRate, trace.Frames[0].ExpectedPlayRate);
    }

    [Fact]
    public void ComparerIgnoresNativeObservations()
    {
        var trace = AlsLocomotionTrace.Load(P3Fixture.Path("trace_idle_gaits.json"));
        var expected = AlsLocomotionTrace.Compare(trace, P3TestSettings.Reference);
        trace.Frames[0] = trace.Frames[0] with
        {
            NativeActual = trace.Frames[0].NativeActual with
            {
                Gait = AlsGait.Sprinting,
                Stride = 1f,
                PlayRate = 3f,
                Lean = new System.Numerics.Vector2(1f, -1f),
                TargetYaw = MathF.PI,
            },
        };

        Assert.Equal(expected, AlsLocomotionTrace.Compare(trace, P3TestSettings.Reference));
    }

    [Fact]
    public void AllFiveTraceFixturesAreCopiedToTheTestOutput()
    {
        foreach (var fixture in ReferenceFixtures)
        {
            Assert.True(File.Exists(P3Fixture.Path(fixture)), fixture);
        }
    }

    [Theory]
    [InlineData("root")]
    [InlineData("frame")]
    [InlineData("command")]
    [InlineData("physicalActual")]
    [InlineData("nativeActual")]
    [InlineData("portExpected")]
    [InlineData("vector")]
    public void UnknownPropertiesAreRejectedAtEveryObjectLevel(string level)
    {
        AssertMutationRejected(root => ObjectAt(root, level)["unexpected"] = 1);
    }

    [Theory]
    [InlineData("root", "kind")]
    [InlineData("frame", "tick")]
    [InlineData("command", "viewYaw")]
    [InlineData("physicalActual", "grounded")]
    [InlineData("nativeActual", "gait")]
    [InlineData("portExpected", "gait")]
    [InlineData("vector", "x")]
    public void MissingPropertiesAreRejectedAtEveryObjectLevel(string level, string property)
    {
        AssertMutationRejected(root => ObjectAt(root, level).Remove(property));
    }

    [Fact]
    public void DuplicatePropertiesAreRejected()
    {
        var json = File.ReadAllText(P3Fixture.Path("trace_idle_gaits.json"));
        var duplicate = json.Replace(
            "\"schemaVersion\": 1",
            "\"schemaVersion\": 1, \"schemaVersion\": 1",
            StringComparison.Ordinal);

        AssertRawRejected(duplicate);
    }

    [Theory]
    [InlineData("\"grounded\": true", "\"grounded\": true, \"grounded\": true")]
    [InlineData("\"synthesizedAnimationPhase\": 0.01666666753590107", "\"synthesizedAnimationPhase\": 0.01666666753590107, \"synthesizedAnimationPhase\": 0.01666666753590107")]
    [InlineData("\"animationState\": \"Grounded\"", "\"animationState\": \"Grounded\", \"animationState\": \"Grounded\"")]
    public void DuplicatePropertiesAreRejectedInAllThreeTraceLayers(string marker, string replacement)
    {
        var json = File.ReadAllText(P3Fixture.Path("trace_idle_gaits.json"));
        AssertRawRejected(ReplaceFirst(json, marker, replacement));
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("kind", "\"settings\"")]
    [InlineData("name", "\"not_a_sequence\"")]
    [InlineData("referenceCommit", "\"0000000000000000000000000000000000000000\"")]
    [InlineData("patchHashes", "[]")]
    [InlineData("fixedDeltaSeconds", "0.02")]
    public void LockedMetadataMismatchesAreRejected(string property, string jsonValue)
    {
        AssertMutationRejected(root => root[property] = JsonNode.Parse(jsonValue));
    }

    [Fact]
    public void ASequenceNameMustMatchItsFixtureFileName()
    {
        AssertMutationRejected(root => root["name"] = "directions");
    }

    [Theory]
    [InlineData("requestedGait", "Flying")]
    [InlineData("requestedStance", "Prone")]
    [InlineData("requestedRotationMode", "Orbiting")]
    public void InvalidCommandEnumsAreRejected(string property, string value)
    {
        AssertMutationRejected(root => FirstCommand(root)[property] = value);
    }

    [Theory]
    [InlineData("gait", "Flying")]
    [InlineData("stance", "Prone")]
    [InlineData("rotationMode", "Orbiting")]
    [InlineData("locomotionState", "Swimming")]
    [InlineData("animationState", "Hovering")]
    public void InvalidActualEnumsAreRejected(string property, string value)
    {
        AssertMutationRejected(root => FirstActual(root)[property] = value);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void NonfiniteNumbersAreRejected(string token)
    {
        var json = File.ReadAllText(P3Fixture.Path("trace_idle_gaits.json"));
        var invalid = json.Replace("\"viewYaw\": 0", $"\"viewYaw\": {token}", StringComparison.Ordinal);

        AssertRawRejected(invalid);
    }

    [Theory]
    [InlineData("\"maxAcceleration\": 20", "\"maxAcceleration\": NaN")]
    [InlineData("\"synthesizedAnimationPhase\": 0.01666666753590107", "\"synthesizedAnimationPhase\": NaN")]
    [InlineData("\"animationPhase\": 0", "\"animationPhase\": NaN")]
    public void NonfiniteNumbersAreRejectedInAllThreeTraceLayers(string marker, string replacement)
    {
        var json = File.ReadAllText(P3Fixture.Path("trace_idle_gaits.json"));
        AssertRawRejected(ReplaceFirst(json, marker, replacement));
    }

    [Theory]
    [InlineData("index", 3)]
    [InlineData("tick", 3)]
    public void FrameCountersMustBeSequentialFromZero(string property, int value)
    {
        AssertMutationRejected(root => SecondFrame(root)[property] = value);
    }

    [Fact]
    public void FrameTimeMustMatchItsFixedStep()
    {
        AssertMutationRejected(root => SecondFrame(root)["time"] = 0.5);
    }

    [Fact]
    public void TraceMustContainFrames()
    {
        AssertMutationRejected(root => root["frames"] = new JsonArray());
    }

    [Theory]
    [InlineData("movementAxes", "x", 1.01)]
    [InlineData("movementAxes", "y", -1.01)]
    public void MovementAxesOutsideTheDocumentedRangeAreRejected(
        string vector,
        string component,
        double value)
    {
        AssertMutationRejected(root =>
            FirstCommand(root)[vector]![component] = value);
    }

    [Theory]
    [InlineData("stride", 1.01)]
    [InlineData("playRate", 3.01)]
    [InlineData("animationPhase", 1.0)]
    public void ActualParametersOutsideTheDocumentedRangeAreRejected(string property, double value)
    {
        AssertMutationRejected(root => FirstActual(root)[property] = value);
    }

    [Fact]
    public void LeanOutsideTheNormalizedRangeIsRejected()
    {
        AssertMutationRejected(root => FirstActual(root)["lean"]!["x"] = 1.01);
    }

    [Fact]
    public void SchemaPinsTraceMetadataAndParameterBounds()
    {
        var schema = JsonNode.Parse(File.ReadAllText(P3Fixture.RepositoryPath(
            "tools",
            "schemas",
            "als_locomotion_trace.schema.json")))!.AsObject();
        var definitions = schema["$defs"]!.AsObject();
        var metadataProperties = definitions["metadata"]!["properties"]!.AsObject();
        var actualProperties = definitions["portExpected"]!["properties"]!.AsObject();

        Assert.Equal(PinnedCommit, metadataProperties["referenceCommit"]!["const"]!.GetValue<string>());
        Assert.Equal(AlsLocomotionSettings.PatchHash,
            metadataProperties["patchHashes"]!["items"]!["const"]!.GetValue<string>());
        Assert.Equal(1, metadataProperties["patchHashes"]!["minItems"]!.GetValue<int>());
        Assert.Equal(1, metadataProperties["patchHashes"]!["maxItems"]!.GetValue<int>());
        Assert.Equal(0, actualProperties["stride"]!["minimum"]!.GetValue<int>());
        Assert.Equal(1, actualProperties["stride"]!["maximum"]!.GetValue<int>());
        Assert.Equal(0, actualProperties["playRate"]!["minimum"]!.GetValue<int>());
        Assert.Equal(3, actualProperties["playRate"]!["maximum"]!.GetValue<int>());
    }

    [Fact]
    public void BooleanFieldsAreConvertedToBytes()
    {
        var trace = AlsLocomotionTrace.Load(P3Fixture.Path("trace_jump_land.json"));

        Assert.All(trace.Frames, frame =>
        {
            Assert.InRange(frame.Input.Floor.IsGrounded, (byte)0, (byte)1);
            Assert.InRange(frame.Input.Command.JumpPressed, (byte)0, (byte)1);
            Assert.InRange(frame.Input.JumpAccepted, (byte)0, (byte)1);
        });
    }

    private static void AssertMutationRejected(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(File.ReadAllText(P3Fixture.Path("trace_idle_gaits.json")))!.AsObject();
        mutation(root);
        AssertRawRejected(root.ToJsonString());
    }

    private static void AssertRawRejected(string json)
    {
        var directory = Directory.CreateTempSubdirectory("godot-als-p3-");
        try
        {
            var path = System.IO.Path.Combine(directory.FullName, "trace_idle_gaits.json");
            File.WriteAllText(path, json);
            Assert.Throws<FormatException>(() => AlsLocomotionTrace.Load(path));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static JsonObject ObjectAt(JsonObject root, string level) => level switch
    {
        "root" => root,
        "frame" => FirstFrame(root),
        "command" => FirstCommand(root),
        "physicalActual" => FirstFrame(root)["physicalActual"]!.AsObject(),
        "nativeActual" => FirstFrame(root)["nativeActual"]!.AsObject(),
        "portExpected" => FirstFrame(root)["portExpected"]!.AsObject(),
        "vector" => FirstFrame(root)["physicalActual"]!["velocity"]!.AsObject(),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static JsonObject FirstFrame(JsonObject root) =>
        root["frames"]!.AsArray()[0]!.AsObject();

    private static JsonObject SecondFrame(JsonObject root) =>
        root["frames"]!.AsArray()[1]!.AsObject();

    private static JsonObject FirstCommand(JsonObject root) =>
        FirstFrame(root)["command"]!.AsObject();

    private static JsonObject FirstActual(JsonObject root) =>
        FirstFrame(root)["portExpected"]!.AsObject();

    private static string Summarize(AlsLocomotionTraceIssue[] issues) => string.Join(
        Environment.NewLine,
        issues
            .GroupBy(issue => issue.Field)
            .Select(group =>
            {
                var first = group.First();
                var numericErrors = group
                    .Select(issue => TryError(issue, out var error) ? error : (double?)null)
                    .Where(error => error.HasValue)
                    .Select(error => error!.Value)
                    .ToArray();
                var maximum = numericErrors.Length == 0
                    ? "n/a"
                    : numericErrors.Max().ToString("R", CultureInfo.InvariantCulture);
                return $"{group.Key}: count={group.Count()}, firstFrame={first.Frame}, " +
                       $"firstExpected={first.Expected}, firstActual={first.Actual}, maxAbsError={maximum}";
            }));

    private static bool TryError(AlsLocomotionTraceIssue issue, out double error)
    {
        if (double.TryParse(issue.Expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expected) &&
            double.TryParse(issue.Actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actual))
        {
            error = System.Math.Abs(expected - actual);
            return true;
        }

        error = 0d;
        return false;
    }

    private static string ReplaceFirst(string text, string marker, string replacement)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, marker);
        return string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + marker.Length));
    }
}
