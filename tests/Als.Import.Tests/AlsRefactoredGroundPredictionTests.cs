using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredGroundPredictionTests(ITestOutputHelper output)
{
    private static string Source() => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/refactored_ground_prediction_inputs.json"));
    private static AlsRefactoredGroundPredictionProfile Profile() => AlsRefactoredGroundPredictionCompiler.Compile(Source());
    private static AlsGroundPredictionInput Input(double velocity = -200, float block = 0) =>
        new(new(1, 2, 1), new(0, 0, 100), new(0, 0, velocity), 1, 35, 90, .7f, block);

    [Fact]
    public void AuthoredResponseMatchesOneThousandAndOneNativeCurveSamples()
    {
        var profile = Profile();
        Assert.Equal(1, profile.AmountCurve.Sample(0)); Assert.Equal(0, profile.AmountCurve.Sample(1));
        Assert.NotEqual(.5f, profile.AmountCurve.Sample(.5f));
        Assert.Equal(3, profile.BlockingObjectChannels.Length);
        var changed = JsonNode.Parse(Source())!;
        changed["curve"]!["keys"]![0]!["leaveTangent"] = -1;
        Assert.Throws<InvalidDataException>(() => AlsRefactoredGroundPredictionCompiler.Compile(changed.ToJsonString()));
        changed = JsonNode.Parse(Source())!; changed["sweepChannel"] = "MotorMask";
        Assert.Throws<InvalidDataException>(() => AlsRefactoredGroundPredictionCompiler.Compile(changed.ToJsonString()));
    }

    [Fact]
    public void NativeThresholdDistanceScaleAndMaskAreDistinctFromV4()
    {
        var model = Profile().Model;
        Assert.False(model.Prepare(Input(-199.99), 1).Enabled);
        var minimum = model.Prepare(Input(), 2);
        Assert.True(minimum.Enabled); Assert.Equal(-150, minimum.End.Z - minimum.Start.Z);
        var maximum = model.Prepare(Input(-5000) with { Scale = 1.5f }, 3);
        Assert.Equal(-3000, maximum.End.Z - maximum.Start.Z);
        Assert.True(model.Prepare(Input(block: -.5f), 4).Enabled);
        Assert.Equal(1, model.Prepare(Input(block: -.5f), 5).Allowance);
        Assert.False(model.Prepare(Input(block: 1.5f), 6).Enabled);
        Assert.False(model.Prepare(Input(block: 1 - .00005f), 7).Enabled);
        Assert.True(model.Prepare(Input(block: 1 - .0002f), 8).Enabled);
        var lateral = model.Prepare(Input(-500) with { Velocity = new(300, 400, -500), Scale = .75f }, 9);
        Assert.InRange((lateral.End.X - lateral.Start.X) / (lateral.End.Y - lateral.Start.Y), .74999999, .75000001);
        Assert.Equal(35, lateral.Radius); Assert.Equal(90, lateral.HalfHeight); // already world-scaled capsule sizes
    }

    [Fact]
    public void PenetratingWalkableHitIsAcceptedAndForeignResponseRejected()
    {
        var profile = Profile(); var model = profile.Model;
        var query = model.Prepare(Input(-500, .25f), 10);
        var hit = new AlsGroundPredictionObservation(query, true, true, 0, new(0, 0, 1));
        Assert.Equal(.75f, model.Evaluate(query, hit));
        Assert.Equal(0, model.Evaluate(query, hit with { Normal = new(1, 0, 0) }));
        Assert.Equal(0, model.Evaluate(query, hit with { Blocking = false }));
        Assert.Equal(profile.AmountCurve.Sample(.3f) * .75f, model.Evaluate(query, hit with { Time = .3f }));
        Assert.Throws<ArgumentException>(() => model.Evaluate(query, hit with { Query = query with { Serial = 11 } }));
        Assert.Throws<ArgumentException>(() => model.Evaluate(query, hit with { Time = float.NaN }));
        var skip = model.Prepare(Input(-100), 11);
        Assert.Equal(0, model.Evaluate(skip, new(skip, false, false, 0, default)));
    }

    [Fact]
    public void ActualNativeInAirUpdateAndSweepMatchAcrossScaleMaskAndPenetration()
    {
        var model = Profile().Model;
        using var document = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "tests/Als.Core.Tests/Fixtures/FootIk/native_ground_prediction.json")));
        var rows = document.RootElement.GetProperty("rows");
        Assert.Equal(1260, rows.GetArrayLength());
        var maximum = 0f; var maximumSweep = 0d; var positive = 0; var zero = 0; var penetration = 0;
        ulong serial = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var height = row.GetProperty("height").GetSingle(); var vertical = row.GetProperty("vertical").GetSingle();
            var input = Input(vertical, row.GetProperty("block").GetSingle()) with { Location = new(0, 0, height),
                Velocity = new(row.GetProperty("horizontal").GetSingle(), 0, vertical), Scale = row.GetProperty("scale").GetSingle() };
            var query = model.Prepare(input, ++serial);
            Assert.Equal(row.GetProperty("enabled").GetBoolean(), query.Enabled);
            Assert.Equal(row.GetProperty("allowance").GetSingle(), query.Allowance);
            var sweep = query.End - query.Start - Vector(row.GetProperty("sweep"));
            maximumSweep = Math.Max(maximumSweep, Math.Sqrt(sweep.LengthSquared));
            Assert.True(Math.Sqrt(sweep.LengthSquared) < .000001,
                $"Native sweep differs: {row.GetRawText()}, C#={query.End - query.Start}, errorCm={Math.Sqrt(sweep.LengthSquared):R}.");
            var hit = new AlsGroundPredictionObservation(query, row.GetProperty("blocking").GetBoolean(),
                row.GetProperty("penetrating").GetBoolean(), row.GetProperty("time").GetSingle(), Vector(row.GetProperty("normal")));
            var actual = model.Evaluate(query, hit); var expected = row.GetProperty("result").GetSingle();
            maximum = Math.Max(maximum, Math.Abs(actual - expected));
            Assert.True(Math.Abs(actual - expected) <= .00002f,
                $"Native prediction differs: {row.GetRawText()}, C#={actual:R}.");
            if (expected > 0) positive++; else zero++;
            if (expected > 0 && hit.StartedPenetrating) penetration++;
        }
        Assert.True(positive > 0 && zero > 0 && penetration > 0);
        output.WriteLine($"nativeFrames=1260 positive={positive} zero={zero} penetration={penetration} maximumError={maximum:R} maximumSweepCm={maximumSweep:R}");
        static AlsDoubleVector Vector(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    }
}
