using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsFootIkInputTests
{
    [Fact]
    public void CompilesOriginalFootFunctions()
    {
        var d = Compile();
        Assert.Equal(.99, d.Lock.CaptureThreshold);
        Assert.Equal(13.5, d.Offset.FootHeight); Assert.Equal(50, d.Offset.TraceAbove); Assert.Equal(45, d.Offset.TraceBelow);
        Assert.Equal(30, d.Offset.DownSpeed); Assert.Equal(15, d.Offset.UpSpeed); Assert.Equal(30, d.Offset.RotationSpeed);
        Assert.Equal(15, d.Reset.Speed);
        Assert.Equal(default, d.InitialState);
    }
    [Theory]
    [InlineData("SetFootLocking", "BooleanOR", "BooleanAND")]
    [InlineData("SetFootLocking", "Less_DoubleDouble", "Greater_DoubleDouble")]
    [InlineData("SetFootLocking", "RTS_Component", "RTS_World")]
    [InlineData("SetFootLockOffsets", "GetWorldDeltaSeconds", "GetDeltaSeconds")]
    [InlineData("SetFootLockOffsets", "LessLess_VectorRotator", "GreaterGreater_VectorRotator")]
    [InlineData("SetFootOffsets", "IsWalkable", "IsFalling")]
    [InlineData("SetFootOffsets", "DegAtan2", "Atan2")]
    [InlineData("SetFootOffsets", "TraceTypeQuery1", "TraceTypeQuery2")]
    [InlineData("ResetIKOffsets", "MemberName=\"FootLock_R_Location\"", "MemberName=\"FootOffset_R_Location\"")]
    [InlineData("UpdateFootIK", "DefaultValue=\"ik_foot_l\"", "DefaultValue=\"foot_l\"")]
    public void RejectsChangedNativeInputSemantics(string graphName, string before, string after)
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == graphName)!;
        var text = graph["nativeText"]!.GetValue<string>(); Assert.Contains(before, text);
        graph["nativeText"] = text.Replace(before, after, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AlsFootIkInputCompiler.Compile(root.ToJsonString()));
    }
    [Fact]
    public void LockRequiresFullCaptureOrFallingCurveAndRecapturesWhileFull()
    {
        var model = Compile().Lock;
        var state = model.Evaluate(default, Observation(.6f)); Assert.Equal(0, state.Alpha);
        state = model.Evaluate(state, Observation(1) with { FootComponentLocation = new(10, 20, 30) });
        Assert.Equal(1, state.Alpha); Assert.Equal(new AlsDoubleVector(10, 20, 30), state.Location);
        state = model.Evaluate(state, Observation(1) with { FootComponentLocation = new(40, 50, 60) });
        Assert.Equal(new AlsDoubleVector(40, 50, 60), state.Location);
        state = model.Evaluate(state, Observation(.4f)); Assert.Equal((double).4f, state.Alpha);
        var previous = state;
        state = model.Evaluate(state, Observation(.6f)); Assert.Equal(previous, state);
        state = model.Evaluate(state, Observation(0)); Assert.Equal(0, state.Alpha); Assert.Equal(previous.Location, state.Location);
    }
    [Fact]
    public void DisabledIkPreservesAllLockReferences()
    {
        var previous = new AlsFootLockInputState(.8, new(10, 20, -30), new(1, 2, 3));
        var observation = Observation(0) with { EnableCurve = 0, WorldVelocity = new(300, 0, 0), ActorRotation = new(0, 90, 0) };
        Assert.Equal(previous, Compile().Lock.Evaluate(previous, observation));
    }
    [Fact]
    public void MatchesNativeLockCompensationMath()
    {
        var model = Compile().Lock;
        var rows = Fixture("native_lock_math.json")["rows"]!.AsArray(); Assert.Equal(18, rows.Count);
        foreach (var r in rows)
        {
            var previous = new AlsFootLockInputState(.4, Vector(r!["location"]!), Rotation(r["rotation"]!));
            var input = new AlsFootLockObservation(1, .4f, default, default, Vector(r["velocity"]!), r["delta"]!.GetValue<float>(),
                Rotation(r["component"]!), Rotation(r["actor"]!), Rotation(r["last"]!), r["grounded"]!.GetValue<bool>());
            var result = model.Evaluate(previous, input);
            Near(Vector(r["resultLocation"]!), result.Location, 1e-8);
            Near(Rotation(r["resultRotation"]!), result.Rotation);
        }
    }
    [Fact]
    public void TraceUsesFootHorizontalLocationAndRootWorldHeight()
    {
        var trace = Compile().Offset.Trace(new(20, 30, 200), new(100, 200, 80));
        Assert.Equal(new AlsDoubleVector(20, 30, 130), trace.Start);
        Assert.Equal(new AlsDoubleVector(20, 30, 35), trace.End);
    }
    [Fact]
    public void WalkableSurfaceProducesUnclampedThreeAxisOffsetAndSlopeRotation()
    {
        var model = Compile().Offset;
        var input = new AlsFootOffsetObservation(1, new(10, 20, 200), new(0, 0, 100), true, new(10, 20, 90), new(.6, 0, .8));
        var result = model.Evaluate(default, default, input, 1);
        Near(new(8.1, 0, -12.7), result.LocationTarget, 1e-12); Assert.Equal(result.LocationTarget, result.State.Location);
        Assert.Equal((double)(float)(-System.Math.Atan2(.6, .8) * 180 / System.Math.PI), result.State.Rotation.Pitch);
        Assert.Equal(0, result.State.Rotation.Yaw); Assert.Equal(0, result.State.Rotation.Roll);
    }
    [Fact]
    public void NoHitRetainsFunctionTargetAndDisabledIkClearsOffsets()
    {
        var model = Compile().Offset;
        var previous = new AlsFootOffsetInputState(new(0, 0, -20), new(10, 20, 30));
        var target = new AlsDoubleVector(2, 3, -4);
        var input = new AlsFootOffsetObservation(1, default, default, false, default, default);
        var missing = model.Evaluate(previous, target, input, 1);
        Assert.Equal(target, missing.LocationTarget); Assert.Equal(target, missing.State.Location); Assert.Equal(default, missing.State.Rotation);
        var disabled = model.Evaluate(previous, target, input with { EnableCurve = 0 }, 0);
        Assert.Equal(target, disabled.LocationTarget); Assert.Equal(default, disabled.State);
    }
    [Fact]
    public void ResetMatchesActualBlueprintExecutionAcrossMultipleFrames()
    {
        var fixture = Fixture("native_reset_trace.json"); Assert.True(fixture["defaultsRestored"]!.GetValue<bool>());
        var rows = fixture["rows"]!.AsArray(); Assert.Equal(12, rows.Count);
        var model = Compile().Reset;
        foreach (var r in rows)
        {
            var before = State(r!["before"]!); var expected = State(r["after"]!);
            var actual = model.Evaluate(before, r["delta"]!.GetValue<double>());
            Near(expected.LeftOffset.Location, actual.LeftOffset.Location, 1e-10);
            Near(expected.LeftOffset.Rotation, actual.LeftOffset.Rotation);
            Near(expected.RightLock.Location, actual.RightLock.Location, 1e-10);
            Assert.Equal(expected.LeftLock, actual.LeftLock); Assert.Equal(expected.RightOffset, actual.RightOffset);
            Assert.Equal(expected.RightLock.Alpha, actual.RightLock.Alpha); Assert.Equal(expected.RightLock.Rotation, actual.RightLock.Rotation);
        }
    }
    [Fact]
    public void RejectedInputDoesNotAdvanceLockHistory()
    {
        var model = Compile().Lock; var previous = new AlsFootLockInputState(.4, new(10, 20, 30), default);
        var input = Observation(.4f) with { WorldVelocity = new(300, 0, 0) };
        var first = model.Evaluate(previous, input);
        Assert.Throws<ArgumentException>(() => model.Evaluate(previous, input with { WorldDelta = float.NaN }));
        Assert.Equal(first, model.Evaluate(previous, input)); Assert.Equal(new AlsDoubleVector(10, 20, 30), previous.Location);
    }
    [Fact]
    public void GlobalUpdateResetsLocalTargetsAndKeepsRagdollOffsetProperties()
    {
        var model = Compile();
        var hit = new AlsFootOffsetObservation(1, new(10, 20, 200), new(0, 0, 100), true, new(10, 20, 90), new(0, 0, 1));
        var input = new AlsFootIkObservation(AlsMovementStateInput.Grounded, 1.0 / 60, Observation(0), Observation(0), hit, hit);
        var first = model.Evaluate(model.InitialState, input);
        Assert.Equal(new AlsDoubleVector(0, 0, -10), first.LeftTarget);
        var missing = model.Evaluate(first.State, input with { LeftOffset = hit with { Walkable = false }, RightOffset = hit with { Walkable = false } });
        Assert.Equal(default, missing.LeftTarget); Assert.Equal(default, missing.RightTarget);
        Assert.True(missing.State.LeftOffset.Location.Z > first.State.LeftOffset.Location.Z);
        var ragdoll = model.Evaluate(first.State, input with { MovementState = AlsMovementStateInput.Ragdoll });
        Assert.Equal(first.State.LeftOffset, ragdoll.State.LeftOffset); Assert.Equal(first.State.RightOffset, ragdoll.State.RightOffset);
        Assert.Equal(first.State.Pelvis, ragdoll.State.Pelvis);
        var inAir = model.Evaluate(first.State, input with { MovementState = AlsMovementStateInput.InAir });
        Assert.True(inAir.State.Pelvis.Offset.Z > first.State.Pelvis.Offset.Z);
        Assert.Equal(first.State.RightOffset, inAir.State.RightOffset);
        Assert.True(inAir.State.LeftOffset.Location.Z > first.State.LeftOffset.Location.Z);
    }
    [Fact]
    public void GroundNoneAndMantlingUseTheSameAuthoredOffsetBranch()
    {
        var model = Compile();
        var hit = new AlsFootOffsetObservation(1, default, default, true, new(0, 0, -20), new(0, 0, 1));
        var input = new AlsFootIkObservation(AlsMovementStateInput.Grounded, 1.0 / 60, Observation(.4f), Observation(.4f), hit, hit);
        var grounded = model.Evaluate(default, input);
        Assert.Equal(grounded, model.Evaluate(default, input with { MovementState = AlsMovementStateInput.None }));
        Assert.Equal(grounded, model.Evaluate(default, input with { MovementState = AlsMovementStateInput.Mantling }));
        Assert.Throws<ArgumentException>(() => model.Evaluate(default, input with { LeftOffset = hit with { EnableCurve = 0 } }));
    }
    [Fact]
    public void LockCompensationUsesWorldDeltaIndependentlyOfAnimationInterpolation()
    {
        var model = Compile();
        var previous = model.InitialState with { LeftLock = new(.4, new(10, 0, -80), default) };
        var hit = new AlsFootOffsetObservation(1, default, default, true, new(0, 0, -20), new(0, 0, 1));
        var input = new AlsFootIkObservation(AlsMovementStateInput.Grounded, 0,
            Observation(.4f) with { WorldDelta = .02f, WorldVelocity = new(100, 0, 0) }, Observation(0), hit, hit);
        var result = model.Evaluate(previous, input);
        Assert.Equal(10 - 100.0 * .02f, result.State.LeftLock.Location.X);
        Assert.Equal(previous.LeftOffset, result.State.LeftOffset); Assert.Equal(previous.Pelvis.Offset, result.State.Pelvis.Offset);
        Assert.Equal(result, model.Evaluate(previous, input));
    }
    private static AlsFootLockObservation Observation(float curve) => new(1, curve, default, default, default, 1f / 60, default, default, default, true);
    private static AlsFootIkPropertyState State(JsonNode n) => new(
        new(n["FootLock_L_Alpha"]!.GetValue<double>(), Vector(n["FootLock_L_Location"]!), Rotation(n["FootLock_L_Rotation"]!)),
        new(n["FootLock_R_Alpha"]!.GetValue<double>(), Vector(n["FootLock_R_Location"]!), Rotation(n["FootLock_R_Rotation"]!)),
        new(Vector(n["FootOffset_L_Location"]!), Rotation(n["FootOffset_L_Rotation"]!)),
        new(Vector(n["FootOffset_R_Location"]!), Rotation(n["FootOffset_R_Rotation"]!)), default);
    private static AlsDoubleVector Vector(JsonNode n) => new(n[0]!.GetValue<double>(), n[1]!.GetValue<double>(), n[2]!.GetValue<double>());
    private static AlsAimingRotation Rotation(JsonNode n) => new(n[0]!.GetValue<double>(), n[1]!.GetValue<double>(), n[2]!.GetValue<double>());
    private static void Near(AlsDoubleVector expected, AlsDoubleVector actual, double tolerance)
    { for (var axis = 0; axis < 3; axis++) Assert.True(System.Math.Abs(expected[axis] - actual[axis]) <= tolerance, $"Expected {expected}; got {actual}."); }
    private static void Near(AlsAimingRotation expected, AlsAimingRotation actual)
    { Assert.Equal(expected.Pitch, actual.Pitch, 10); Assert.Equal(expected.Yaw, actual.Yaw, 10); Assert.Equal(expected.Roll, actual.Roll, 10); }
    private static JsonNode Fixture(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "tests/Als.Import.Tests/Fixtures/FootIk", name)))!;
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_foot_ik_inputs.json"));
    private static AlsFootIkInputModel Compile() => AlsFootIkInputCompiler.Compile(Read());
}
