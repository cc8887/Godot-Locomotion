using System.Text.Json;
using GodotAls.Core.Locomotion;
using Xunit.Abstractions;
using M = System.Math;

namespace GodotAls.Core.Tests;

public sealed class AlsRefactoredFootControlsTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(30, 0)] [InlineData(30, .5f)] [InlineData(30, 1)] [InlineData(30, 2)]
    [InlineData(60, 0)] [InlineData(60, .5f)] [InlineData(60, 1)] [InlineData(60, 2)]
    [InlineData(120, 0)] [InlineData(120, .5f)] [InlineData(120, 1)] [InlineData(120, 2)]
    public void ActualNativeRigUnitsMatchContinuousCandidateStateAndRetries(int hz, float damping)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "FootIk", "native_refactored_foot_controls.json")));
        var fixture = document.RootElement;
        Assert.Equal(1, fixture.GetProperty("schemaVersion").GetInt32());
        var sample = fixture.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("hz").GetInt32() == hz && F(c, "damping") == damping);
        var initialCalf = Q(sample, "initialCalf"); var initialFoot = Q(sample, "initialFoot");
        var location = default(AlsFootOffsetLocationState);
        var rotation = default(AlsFootOffsetRotationState);
        var pole = AlsFootPoleState.Initial; var smooth = default(AlsRigVectorDamperState);
        double maxLocation = 0, maxOffset = 0, maxVelocity = 0, maxRotation = 0, maxNormal = 0, maxPole = 0;
        var frames = 0; var failures = 0; var resets = 0;
        foreach (var row in sample.GetProperty("frames").EnumerateArray())
        {
            if (row.GetProperty("reset").GetBoolean())
            { location = default; rotation = default; pole = AlsFootPoleState.Initial; smooth = default; resets++; }
            var dt = F(row, "dt"); var moving = row.GetProperty("moving").GetDouble();
            var l = new AlsFootOffsetLocationInput(dt, 100, V(row, "thigh"), V(row, "targetLocation"),
                F(row, "offsetZ"), -20, F(row, "legLength"), AlsRefactoredFootGraphSettings.MinPelvisToFootDistance(moving),
                .99f, F(row, "frequency"), damping, F(row, "targetVelocityAmount"));
            var r = new AlsFootOffsetRotationInput(dt, initialCalf, initialFoot,
                Q(row, "calfRotation"), Q(row, "footRotation"), Q(row, "targetRotation"), V(row, "normal"),
                new(-20, 40), AlsRefactoredFootGraphSettings.Swing2(moving), new(0, 0), F(row, "halfLife"));
            var nextLocation = AlsFootOffsetLocationModel.Evaluate(location, l);
            var nextRotation = AlsFootOffsetRotationModel.Evaluate(rotation, r);
            var nextPole = AlsFootPoleModel.Evaluate(pole, V(row, "thigh"), V(row, "calf"), V(row, "foot"));
            var targetPole = nextPole.ItemBLocation + nextPole.Direction * 40;
            var nextSmooth = AlsFootPoleModel.Smooth(smooth, targetPole, dt, .05f);
            // Simulate discarded complete candidates, then retry from committed history.
            Assert.Equal(nextLocation, AlsFootOffsetLocationModel.Evaluate(location, l));
            Assert.Equal(nextRotation, AlsFootOffsetRotationModel.Evaluate(rotation, r));
            Assert.Equal(nextPole, AlsFootPoleModel.Evaluate(pole, V(row, "thigh"), V(row, "calf"), V(row, "foot")));
            Assert.Equal(nextSmooth, AlsFootPoleModel.Smooth(smooth, targetPole, dt, .05f));
            location = nextLocation; rotation = nextRotation; pole = nextPole; smooth = nextSmooth;
            maxLocation = M.Max(maxLocation, Distance(location.FootLocation, V(row, "resultLocation")));
            maxOffset = M.Max(maxOffset, M.Abs(location.OffsetZ - F(row, "resultOffsetZ")));
            maxVelocity = M.Max(maxVelocity, M.Abs(location.Spring.Velocity - F(row, "springVelocity")));
            maxRotation = M.Max(maxRotation, 1 - M.Abs(AlsQuaternion.Dot(rotation.FootRotation.Normalized(), Q(row, "resultRotation").Normalized())));
            maxNormal = M.Max(maxNormal, Distance(rotation.OffsetNormal, V(row, "resultNormal")));
            maxPole = M.Max(maxPole, M.Max(Distance(smooth.Current, V(row, "smoothedPole")),
                M.Max(Distance(pole.ItemBLocation, V(row, "poleB")), M.Max(Distance(pole.Projection, V(row, "poleProjection")), Distance(pole.Direction, V(row, "poleDirection"))))));
            Assert.Equal(row.GetProperty("springValid").GetBoolean(), location.Spring.Valid);
            Assert.Equal(F(row, "springPreviousTarget"), location.Spring.PreviousTarget);
            Assert.Equal(row.GetProperty("poleSuccess").GetBoolean(), pole.Success);
            if (!pole.Success) failures++;
            frames++;
        }
        output.WriteLine($"hz={hz} damping={damping} frames={frames} retries={frames} maxLocationCm={maxLocation:R} maxOffsetCm={maxOffset:R} maxVelocity={maxVelocity:R} rotationError={maxRotation:R} normalError={maxNormal:R} poleErrorCm={maxPole:R}");
        Assert.Equal(120, frames); Assert.Equal(1, resets); Assert.True(failures >= 4);
        Assert.InRange(maxLocation, 0, .0002); Assert.InRange(maxOffset, 0, .0002);
        Assert.InRange(maxVelocity, 0, .003); Assert.InRange(maxRotation, 0, 1e-12);
        Assert.InRange(maxNormal, 0, 1e-12); Assert.InRange(maxPole, 0, 1e-10);
    }

    private static float F(JsonElement row, string name) => row.GetProperty(name).GetSingle();
    private static AlsDoubleVector V(JsonElement row, string name)
    { var a = row.GetProperty(name); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
    private static AlsQuaternion Q(JsonElement row, string name)
    { var a = row.GetProperty(name); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble()); }
    private static double Distance(AlsDoubleVector a, AlsDoubleVector b) => M.Sqrt((a - b).LengthSquared);

    [Fact]
    public void FailedPoleRetainsPositionAndDirectionAndColdFailureUsesNativeForwardDefault()
    {
        var cold = AlsFootPoleModel.Evaluate(AlsFootPoleState.Initial, default, default, new(0, 0, 10));
        Assert.Equal(AlsFootPoleState.Initial, cold);
        var good = AlsFootPoleModel.Evaluate(cold, default, new(5, 0, 5), new(0, 0, 10));
        var bad = AlsFootPoleModel.Evaluate(good, new(100, 0, 0), new(100, 0, 5), new(100, 0, 10));
        Assert.False(bad.Success);
        Assert.Equal(good with { Success = false }, bad);
    }

    [Fact]
    public void LegClampIsImmediateAfterInterpolationAndDoesNotOverwriteSpringOffset()
    {
        var input = new AlsFootOffsetLocationInput(1f / 60, 100, new(0, 0, 90), new(200, 0, 0), 15, -20, 95, 20, .99f, 12, 2, 0);
        var first = AlsFootOffsetLocationModel.Evaluate(default, input);
        Assert.Equal(15, first.OffsetZ);
        Assert.InRange(Distance(first.FootLocation, input.ThighLocation), 94.04999, 94.05001);
        Assert.False(first.Spring.Valid);
        // The rig unit's first call resets the spring. Its second nonzero-delta
        // call initializes UAlsMath's own spring and snaps to that call's target.
        var second = AlsFootOffsetLocationModel.Evaluate(first, input with { OffsetZ = -10 });
        Assert.Equal(-10, second.OffsetZ); Assert.True(second.Spring.Valid);
        var pause = AlsFootOffsetLocationModel.Evaluate(second, input with { OffsetZ = 30, DeltaTime = 0 });
        Assert.Equal(second.Spring, pause.Spring); Assert.Equal(second.OffsetZ, pause.OffsetZ);
    }
}
