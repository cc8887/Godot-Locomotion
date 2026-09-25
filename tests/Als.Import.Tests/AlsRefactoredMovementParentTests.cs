using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementParentTests(ITestOutputHelper output)
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    private static AlsRefactoredMovementInput Read(JsonElement r)
    {
        float F(string n) => r.GetProperty(n).GetSingle();
        AlsDoubleVector V(string n) { var v = r.GetProperty(n); return new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble()); }
        var q = r.GetProperty("rotation");
        return new(V("velocity"), V("acceleration"), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()),
            F("speed"), F("scale"), F("velocityYaw"), r.GetProperty("viewYaw").GetDouble(), F("maxAcceleration"), F("maxBraking"),
            r.GetProperty("gait").GetString()!, r.GetProperty("velocityMode").GetBoolean(), r.GetProperty("pending").GetBoolean(),
            F("delta"), F("running"), F("sprinting"), F("hipsLock"), F("sprintBlock"));
    }
    private static float[] Values(AlsRefactoredMovementParentRuntime parent)
    {
        var s = parent.MovementCandidate;
        return [s.VelocityInitialized ? 1 : 0, s.VelocityBlend.X, s.VelocityBlend.Y, s.VelocityBlend.Z, s.VelocityBlend.W,
            s.Lean.X, s.Lean.Y, (float)s.Direction, s.HipsLock, s.YawOffsets.X, s.YawOffsets.Y, s.YawOffsets.Z, s.YawOffsets.W,
            s.StandingStride, s.WalkRun, s.StandingRate, s.SprintBlock, s.SprintTime, s.SprintAcceleration,
            s.CrouchingStride, s.CrouchingRate, parent.Candidate.PivotActive ? 1 : 0, (float)parent.Candidate.HipsDirection];
    }
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ActualNativeParentTrajectoryMatchesWithAtomicRetry(int hz)
    {
        var catalog = Catalog(); var settings = new AlsRefactoredMovementSettings(MantlingHostFixture.Read("refactored_movement_settings"), catalog);
        var standing = new AlsRefactoredStanceCallbacks(catalog, false); var crouching = new AlsRefactoredStanceCallbacks(catalog, true);
        var parent = new AlsRefactoredMovementParentRuntime(standing, settings, crouching);
        var callbacks = standing.Nodes.ToArray().Concat(crouching.Nodes.ToArray()).GroupBy(n => n.Function).ToDictionary(g => g.Key, g => g.First());
        using var document = JsonDocument.Parse(MantlingHostFixture.Read("refactored_movement_parent_native"));
        var root = document.RootElement; Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        foreach (var hash in root.GetProperty("resourceHashes").EnumerateObject())
            Assert.Equal(hash.Value.GetString()!.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(hash.Name)))));
        var trace = root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == $"movement-{hz}");
        var frame = 0; var max = 0f; var nonzeroLean = 0; var sprintAcceleration = 0; var pivots = 0; var directions = new HashSet<AlsRefactoredMovementDirection>();
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var request = row.GetProperty("request"); var input = Read(request.GetProperty("input"));
            var committed = parent.CommittedMovement; var latch = parent.Committed;
            void Prepare()
            {
                parent.Prepare(new(frame, 7, 1), input);
                foreach (var op in request.GetProperty("operations").EnumerateArray())
                {
                    switch (op.GetString())
                    {
                        case "InitializeGrounded": parent.InitializeGrounded(frame); break;
                        case "InitializeLean": parent.InitializeLean(frame); break;
                        case "RefreshGrounded": parent.RefreshGrounded(frame); break;
                        case "ActivatePivot": parent.ActivatePivot(frame); break;
                        default: parent.Apply(new(frame, 7, 1), catalog.IndexDigest, callbacks[Enum.Parse<AlsRefactoredStanceFunction>(op.GetString()!)]); break;
                    }
                }
            }
            Prepare(); var actual = Values(parent); var native = row.GetProperty("state"); Assert.Equal(actual.Length, native.GetArrayLength());
            for (var i = 0; i < actual.Length; i++)
            {
                var error = MathF.Abs(actual[i] - native[i].GetSingle());
                Assert.True(error <= 2e-6f, $"hz={hz} frame={frame} field={i} actual={actual[i]:R} native={native[i].GetSingle():R} error={error:R}");
                max = MathF.Max(max, error);
            }
            Assert.Equal(committed, parent.CommittedMovement); Assert.Equal(latch, parent.Committed);
            nonzeroLean += parent.MovementCandidate.Lean.LengthSquared() > 0 ? 1 : 0;
            sprintAcceleration += parent.MovementCandidate.SprintAcceleration != 0 ? 1 : 0;
            pivots += parent.Candidate.PivotActive ? 1 : 0; directions.Add(parent.MovementCandidate.Direction);
            parent.Cancel(); Prepare(); Assert.Equal(actual, Values(parent)); parent.ValidateCommit(frame); parent.Commit(frame); frame++;
        }
        Assert.Equal(hz * 5, frame); Assert.Equal(4, directions.Count);
        Assert.True(nonzeroLean > 0 && sprintAcceleration > 0 && pivots > 0);
        output.WriteLine($"{hz} Hz frames={frame} max={max:R} lean={nonzeroLean} sprintAcceleration={sprintAcceleration} pivot={pivots}");
    }

    [Fact]
    public void LateRefreshFailureAndReinitializationDoNotPublishPartialState()
    {
        var catalog = Catalog(); var callbacks = new AlsRefactoredStanceCallbacks(catalog, false);
        var settings = new AlsRefactoredMovementSettings(MantlingHostFixture.Read("refactored_movement_settings"), catalog);
        var parent = new AlsRefactoredMovementParentRuntime(callbacks, settings);
        var refresh = callbacks.Nodes.ToArray().First(n => n.Function == AlsRefactoredStanceFunction.RefreshStandingMovement);
        var input = new AlsRefactoredMovementInput(new(100, 0, 0), new(500, 0, 0), AlsQuaternion.Identity,
            100, 1, 0, 0, 1000, 800, "Als.Gait.Sprinting", false, false, .1f, 1, 0, 0, 0);
        parent.Prepare(new(0, 1, 1), input); parent.RefreshGrounded(0); parent.Apply(new(0, 1, 1), catalog.IndexDigest, refresh); parent.Commit(0);
        var committed = parent.CommittedMovement;
        parent.Prepare(new(1, 1, 1), input with { Speed = float.MaxValue, Scale = float.Epsilon }); parent.InitializeLean(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => parent.Apply(new(1, 1, 1), catalog.IndexDigest, refresh));
        Assert.Throws<InvalidOperationException>(() => parent.Commit(1)); Assert.Equal(committed, parent.CommittedMovement); parent.Cancel();
        parent.Prepare(new(1, 1, 1), input, true); Assert.Equal(AlsRefactoredMovementState.Initial, parent.MovementCandidate); parent.Cancel();
        parent.Prepare(new(1, 1, 1), input); Assert.Equal(committed, parent.MovementCandidate); parent.Cancel();
        parent.Prepare(new(1, 1, 1));
        Assert.Throws<InvalidOperationException>(() => parent.Apply(new(1, 1, 1), catalog.IndexDigest, refresh));
        Assert.Throws<InvalidOperationException>(() => parent.Commit(1)); parent.Cancel();
        Assert.Throws<ArgumentException>(() => parent.Prepare(new(1, 1, 1), input with { Scale = 0 }));
        Assert.Equal(committed, parent.CommittedMovement);
    }
}
