using System.Text.Json;
using GodotAls.Core.Locomotion;
using Xunit.Abstractions;
using M = System.Math;

namespace GodotAls.Core.Tests;

public sealed class AlsRigTwoBoneIkTests(ITestOutputHelper output)
{
    [Fact]
    public void ActualPerItemNodeMatchesAxesWeightsScalesAndAllDescendantWrites()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "FootIk", "native_rig_two_bone_ik.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var parents = root.GetProperty("parents").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var scratch = new AlsPrecisePose[parents.Length]; var changed = new bool[parents.Length];
        double maxPosition = 0, maxRotation = 0, maxScale = 0;
        var worstPosition = ""; var worstRotation = ""; var count = 0; var skipped = 0; var partial = 0;
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var initial = row.GetProperty("initial").EnumerateArray().Select(P).ToArray();
            var before = row.GetProperty("before").EnumerateArray().Select(P).ToArray();
            var after = row.GetProperty("after").EnumerateArray().Select(P).ToArray();
            var input = new AlsRigTwoBoneIkInput(before[0], before[1], before[2], initial[0], initial[1], initial[2],
                P(row.GetProperty("target")), V(row.GetProperty("primary")), V(row.GetProperty("secondary")),
                F(row, "secondaryWeight"), V(row.GetProperty("pole")), B(row, "hasSpace"), B(row, "direction"), P(row.GetProperty("space")),
                B(row, "stretch"), .75f, 1.25f, F(row, "weight"), F(row, "lengthA"), F(row, "lengthB"));
            var result = AlsRigTwoBoneIk.Solve(input);
            Assert.Equal(result, AlsRigTwoBoneIk.Solve(input));
            var candidate = before.ToArray(); var retry = before.ToArray();
            AlsRigTwoBoneIk.Apply(result, candidate, parents, 0, 1, 2, B(row, "propagate"), scratch, changed);
            AlsRigTwoBoneIk.Apply(result, retry, parents, 0, 1, 2, B(row, "propagate"), scratch, changed);
            Assert.Equal(candidate, retry);
            Assert.Equal(input.Root, before[0]); // caller's committed input was not modified
            if (!result.Applied) { Assert.Equal(before, candidate); skipped++; }
            else if (input.Weight < 1) partial++;
            for (var bone = 0; bone < parents.Length; bone++)
            {
                var position = M.Sqrt((candidate[bone].Position - after[bone].Position).LengthSquared);
                var rotation = 1 - M.Abs(AlsQuaternion.Dot(candidate[bone].Rotation.Normalized(), after[bone].Rotation.Normalized()));
                var scale = M.Sqrt((candidate[bone].Scale - after[bone].Scale).LengthSquared);
                if (position > maxPosition) { maxPosition = position; worstPosition = $"row={count} bone={bone} weight={input.Weight} secondary={input.SecondaryAxisWeight} propagate={B(row, "propagate")}"; }
                if (rotation > maxRotation) { maxRotation = rotation; worstRotation = $"row={count} bone={bone} weight={input.Weight} secondary={input.SecondaryAxisWeight}"; }
                maxScale = M.Max(maxScale, scale);
            }
            count++;
        }
        output.WriteLine($"cases={count} bones={count * parents.Length} retries={count} skipped={skipped} partial={partial} maxPositionCm={maxPosition:R} ({worstPosition}) rotationError={maxRotation:R} ({worstRotation}) maxScale={maxScale:R}");
        Assert.Equal(504, count); Assert.Equal(144, skipped); Assert.Equal(216, partial);
        Assert.InRange(maxPosition, 0, 1e-5); Assert.InRange(maxRotation, 0, 1e-12); Assert.InRange(maxScale, 0, 1e-12);
    }

    [Fact]
    public void InvalidReferenceLengthsSkipWithoutWritingHierarchy()
    {
        var pose = AlsPrecisePose.Identity;
        var input = new AlsRigTwoBoneIkInput(pose, pose, pose, pose, pose, pose, pose,
            new(1, 0, 0), new(0, 1, 0), 1, new(0, 1, 0), false, false, pose, false, .75f, 1.25f, 1, 0, 0);
        Assert.False(AlsRigTwoBoneIk.Solve(input).Applied);
        Assert.Throws<ArgumentException>(() => AlsRigTwoBoneIk.Solve(input with { Weight = float.NaN }));
    }

    private static AlsPrecisePose P(JsonElement value)
    {
        var q = value.GetProperty("q");
        return new(V(value.GetProperty("p")), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()), V(value.GetProperty("s")));
    }
    private static AlsDoubleVector V(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static float F(JsonElement row, string name) => row.GetProperty(name).GetSingle();
    private static bool B(JsonElement row, string name) => row.GetProperty(name).GetBoolean();
}
