using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsActualHistoryReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("v4_physics_actual_history_reference.json", 573, 51, 1007, 410, 3, 57, 24)]
    [InlineData("v4_physics_window_history_reference.json", 848, 40, 2055, 804, 0, 14, 198)]
    public void ActualAnchorsMatchingScatterAndAdjacentPublishedHistoryMatchNative(string file, int pairCount,
        int frameCount, int pointCount, int adjacentCount, int emptyCount, int freshCount, int slidingCount)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + file)));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var previous = new Dictionary<string, (long Epoch, AlsSavedContact[] Saved, float Minimum)>();
        var rows = doc.RootElement.GetProperty("cases").EnumerateArray().OrderBy(r => r.GetProperty("scenario").GetString())
            .ThenBy(r => r.GetProperty("mesh").GetString()).ThenBy(r => r.GetProperty("input").GetProperty("epoch").GetInt64()).ToArray();
        var frames = new HashSet<string>(); int points = 0, adjacent = 0, empty = 0, fresh = 0, sliding = 0; float maximum = 0;
        foreach (var row in rows)
        {
            var h = row.GetProperty("input"); var epoch = h.GetProperty("epoch").GetInt64(); var initial = B(h, "initialManifold");
            var id = $"{row.GetProperty("scenario")}/{row.GetProperty("mesh")}/{h.GetProperty("key").GetRawText()}";
            frames.Add($"{row.GetProperty("scenario")}/{row.GetProperty("mesh")}/{row.GetProperty("frame")}");
            var prior = h.GetProperty("saved").EnumerateArray().Select(Saved).ToArray();
            if (previous.TryGetValue(id, out var old) && old.Epoch + 1 == epoch)
            {
                Assert.Equal(old.Saved.Length, prior.Length);
                for (var i = 0; i < prior.Length; i++) CheckSaved(old.Saved[i], prior[i]);
                Assert.Equal(initial ? 0 : old.Minimum, F(h, "priorMinInitialPhi")); adjacent++;
            }
            var history = new AlsContactHistory(8); var key = new AlsContactPairKey(new(1, 1, 0, 1), new(2, 1, 0, 1));
            var m = h.GetProperty("matching"); var matching = new AlsContactMatchSettings(B(m, "quadratic0"), B(m, "quadratic1"),
                B(m, "simpleAssignment"), B(m, "restoreFriction"), F(m, "exactTolerance"), F(m, "nearTolerance"));
            if (!initial)
            {
                Assert.NotEmpty(prior); // Fixture has no noninitial, anchorless 8-point manifolds.
                history.Prepare(key, 0, prior.Select(s => new AlsDetectedContact(s.Anchor0, s.Anchor1, Vector3.UnitZ)).ToArray(), matching);
                history.Commit(prior.Select(s => new AlsContactHistoryResult(1, s.InitialPhi)).ToArray());
            }
            var seed = row.GetProperty("nativeSeed"); Assert.Equal(history.SavedCount, seed.GetArrayLength());
            for (var i = 0; i < seed.GetArrayLength(); i++) CheckSaved(history.SavedAt(i), Saved(seed[i]));
            var input = h.GetProperty("detected").EnumerateArray().Select(e => new AlsDetectedContact(Vec(e, "point0"), Vec(e, "point1"), Vec(e, "normal1"), B(e, "disabled"))).ToArray();
            history.Prepare(key, 1, input, matching); Assert.Equal(initial, history.PreparedInitialManifold);
            Assert.Equal(F(h, "priorMinInitialPhi"), history.PreparedMinInitialPhi);
            var native = row.GetProperty("nativeAssigned"); var captured = h.GetProperty("assigned");
            Assert.Equal(input.Length, native.GetArrayLength()); Assert.Equal(input.Length, captured.GetArrayLength());
            for (var i = 0; i < input.Length; i++)
            {
                var actual = history.PreparedAt(i); CheckAssigned(actual.Geometry, native[i]); CheckAssigned(actual.Geometry, captured[i]);
                Assert.Equal(captured[i].GetProperty("savedIndex").GetInt32(), actual.SavedIndex);
                if (!actual.Geometry.HasAnchor) fresh++; points++;
            }
            if (input.Length == 0) empty++;
            var results = row.GetProperty("results").EnumerateArray().Select(e => new AlsContactHistoryResult(F(e, "ratio"), F(e, "initialPhi"))).ToArray();
            sliding += results.Count(r => r.FrictionRatio >= 1e-4f && r.FrictionRatio < 1 - 1e-4f);
            history.Commit(results); var saved = row.GetProperty("nativeSaved"); Assert.Equal(saved.GetArrayLength(), history.SavedCount);
            Assert.Equal(F(row, "nativeMinInitialPhi"), history.MinInitialPhi);
            var actualSaved = Enumerable.Range(0, history.SavedCount).Select(history.SavedAt).ToArray();
            for (var i = 0; i < actualSaved.Length; i++) CheckSaved(actualSaved[i], Saved(saved[i]));
            previous[id] = (epoch, actualSaved, history.MinInitialPhi);
        }
        output.WriteLine($"ACTUAL_HISTORY runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} pairs={rows.Length} frames={frames.Count} points={points} adjacent={adjacent} empty={empty} fresh={fresh} sliding={sliding} maxAnchor={maximum:R}");
        Assert.Equal(pairCount, rows.Length); Assert.Equal(frameCount, frames.Count); Assert.Equal(pointCount, points);
        Assert.Equal(adjacentCount, adjacent); Assert.Equal(emptyCount, empty); Assert.Equal(freshCount, fresh); Assert.Equal(slidingCount, sliding);
        Assert.Equal(0, maximum);
        void Check(Vector3 a, Vector3 b) => maximum = MathF.Max(maximum, Vector3.Distance(a, b));
        void CheckSaved(AlsSavedContact a, AlsSavedContact b) { Check(a.Anchor0, b.Anchor0); Check(a.Anchor1, b.Anchor1); Assert.Equal(b.InitialPhi, a.InitialPhi); }
        void CheckAssigned(AlsContactGeometry a, JsonElement b)
        { Check(a.Anchor0, Vec(b, "anchor0")); Check(a.Anchor1, Vec(b, "anchor1")); Assert.Equal(F(b, "initialPhi"), a.InitialPhi); Assert.Equal(B(b, "hasAnchor"), a.HasAnchor); Assert.Equal(B(b, "initialContact"), a.InitialContact); }
    }
    private static AlsSavedContact Saved(JsonElement e) => new(Vec(e, "anchor0"), Vec(e, "anchor1"), F(e, "initialPhi"));
    private static Vector3 Vec(JsonElement e, string name) => V(e, name).ToSingle();
    private static float F(JsonElement e, string name) => (float)D(e, name);
}
