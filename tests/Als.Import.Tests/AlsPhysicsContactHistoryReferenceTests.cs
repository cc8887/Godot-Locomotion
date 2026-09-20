using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsContactHistoryReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void NativeSixFrameMatchingAndFrictionAnchorPersistenceMatch()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_contact_history_reference.json")));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var cases = doc.RootElement.GetProperty("cases"); Assert.Equal(192, cases.GetArrayLength());
        var exact = (float)D(doc.RootElement, "exactTolerance"); var near = (float)D(doc.RootElement, "nearTolerance");
        Assert.Equal(.2f, exact); Assert.Equal(1, near);
        var ids = new HashSet<string>(); float maximum = 0; var frames = 0; var restored = 0; var fresh = 0;
        foreach (var row in cases.EnumerateArray())
        {
            var id = $"{row.GetProperty("simple")}/{row.GetProperty("restore")}/{row.GetProperty("quadratic0")}/{row.GetProperty("quadratic1")}/{row.GetProperty("count")}/{row.GetProperty("scenario")}";
            Assert.True(ids.Add(id)); var history = new AlsContactHistory(8);
            var key = new AlsContactPairKey(new(1, 1, 0, 1), new(2, 1, 0, 1));
            var settings = new AlsContactMatchSettings(B(row, "quadratic0"), B(row, "quadratic1"), B(row, "simple"), B(row, "restore"), exact, near);
            Assert.Equal(6, row.GetProperty("frames").GetArrayLength());
            foreach (var frame in row.GetProperty("frames").EnumerateArray())
            {
                var step = frame.GetProperty("frame").GetInt32(); frames++;
                var input = frame.GetProperty("input").EnumerateArray().Select(e => new AlsDetectedContact(V(e, "point0").ToSingle(),
                    V(e, "point1").ToSingle(), V(e, "normal1").ToSingle(), B(e, "disabled"))).ToArray();
                history.Prepare(key, step, input, settings);
                Assert.Equal(step is 0 or 4, history.PreparedInitialManifold);
                Assert.Equal((float)D(frame, "priorMinInitialPhi"), history.PreparedMinInitialPhi);
                var assigned = frame.GetProperty("assigned"); Assert.Equal(input.Length, assigned.GetArrayLength());
                for (var i = 0; i < input.Length; i++)
                {
                    var actual = history.PreparedAt(i).Geometry; var expected = assigned[i];
                    Check(actual.Anchor0, V(expected, "anchor0").ToSingle()); Check(actual.Anchor1, V(expected, "anchor1").ToSingle());
                    Assert.Equal((float)D(expected, "initialPhi"), actual.InitialPhi);
                    Assert.Equal(B(expected, "hasAnchor"), actual.HasAnchor); Assert.Equal(B(expected, "initialContact"), actual.InitialContact);
                    if (actual.HasAnchor) restored++; else fresh++;
                }
                // Supplied results are inputs to the persistence stage. No expected
                // saved anchors are fed back; every subsequent frame uses Core state.
                var results = frame.GetProperty("results").EnumerateArray().Select(e => new AlsContactHistoryResult((float)D(e, "ratio"), (float)D(e, "initialPhi"))).ToArray();
                history.Commit(results); var saved = frame.GetProperty("saved"); Assert.Equal(saved.GetArrayLength(), history.SavedCount);
                Assert.Equal((float)D(frame, "minInitialPhi"), history.MinInitialPhi);
                for (var i = 0; i < history.SavedCount; i++)
                {
                    var actual = history.SavedAt(i); Check(actual.Anchor0, V(saved[i], "anchor0").ToSingle()); Check(actual.Anchor1, V(saved[i], "anchor1").ToSingle());
                    Assert.Equal((float)D(saved[i], "initialPhi"), actual.InitialPhi);
                }
                void Check(Vector3 a, Vector3 b)
                {
                    var error = Vector3.Distance(a, b); maximum = MathF.Max(maximum, error);
                    Assert.True(error <= 1e-6f, $"{id} frame {step}: {a} != {b}, error={error:R}");
                }
            }
        }
        Assert.True(restored > 0 && fresh > 0); Assert.Equal(1152, frames);
        output.WriteLine($"NATIVE_CONTACT_HISTORY_OK cases={ids.Count} frames={frames} restored={restored} fresh={fresh} max_anchor={maximum:R}");
    }
}
