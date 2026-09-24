using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredDefaultOverlayTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));

    [Theory]
    [InlineData(AlsRefactoredBasicOverlayKind.Feminine, .5f)]
    [InlineData(AlsRefactoredBasicOverlayKind.Masculine, 1f)]
    public void ActualVariantsRequireTheirOwnPoseResourceAndIdleAlpha(AlsRefactoredBasicOverlayKind kind, float alpha)
    {
        var catalog = Catalog(); var profile = AlsRefactoredDefaultOverlayCompiler.Compile(catalog, kind);
        Assert.Equal(kind, profile.Kind); Assert.Equal(alpha, profile.IdleAlpha);
        var payload = catalog.Read(AlsRefactoredDefaultOverlayProfile.BlueprintFor(kind));
        Assert.Throws<ArgumentException>(() => AlsRefactoredDefaultOverlayCompiler.ValidateGraph(payload));
        foreach (var changeAlpha in new[] { false, true })
        {
            var changed = JsonNode.Parse(payload.GetRawText())!;
            var node = changed["compiled"]!["nodes"]!.AsArray().Single(n => (int)n!["propertyIndex"]! == (changeAlpha ? 1 : 5))!;
            if (changeAlpha) node["runtime"]!["alpha"] = .75f;
            else node["runtime"]!["sequence"] = AlsRefactoredDefaultOverlayProfile.PoseSource;
            using var doc = JsonDocument.Parse(changed.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredDefaultOverlayCompiler.ValidateGraph(doc.RootElement, kind));
        }
        var overlay = profile.CreateRuntime(0);
        overlay.Prepare(0, new(0, 1, 0, 0, 0), .01f, .4f);
        Assert.Equal(.4f * alpha, overlay.SourceInput.Weight);
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsRefactoredDefaultOverlayCompiler.Compile(catalog, (AlsRefactoredBasicOverlayKind)99));
    }

    [Fact]
    public void ActualGraphCompilesAndChangedGraphPoliciesAreRejected()
    {
        var catalog = Catalog(); var profile = AlsRefactoredDefaultOverlayCompiler.Compile(catalog);
        Assert.Equal(79, profile.BoneNames.Length);
        Assert.Equal(AlsRefactoredDefaultOverlayProfile.IdleSource, profile.PlayerDefinition(0, 3).Source);
        var payload = catalog.Read(AlsRefactoredDefaultOverlayProfile.Blueprint);
        void Reject(int id, string field, JsonNode value)
        {
            var root = JsonNode.Parse(payload.GetRawText())!;
            var n = root["compiled"]!["nodes"]!.AsArray().Single(n => (int)n!["propertyIndex"]! == id)!;
            n["runtime"]![field] = value;
            using var doc = JsonDocument.Parse(root.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredDefaultOverlayCompiler.ValidateGraph(doc.RootElement));
        }
        Reject(1, "alpha", JsonValue.Create(.5f)!);
        Reject(4, "bAlwaysUpdateChildren", JsonValue.Create(true)!);
        Reject(9, "explicitFrame", JsonValue.Create(2)!);
        Reject(11, "groupName", JsonValue.Create("Wrong group")!);
        Reject(10, "bNormalizeAlpha", JsonValue.Create(false)!);
        Reject(3, "a", JsonNode.Parse("{\"linkId\":4,\"sourceLinkId\":3}")!);
        var changed = JsonNode.Parse(payload.GetRawText())!;
        changed["nativeText"] = payload.GetProperty("nativeText").GetString()!.Replace("GaitWalkingAmount", "GaitRunningAmount", StringComparison.Ordinal);
        using var altered = JsonDocument.Parse(changed.ToJsonString());
        Assert.Throws<ArgumentException>(() => AlsRefactoredDefaultOverlayCompiler.ValidateGraph(altered.RootElement));
    }

    [Fact]
    public void PredictionInterpolationOnlyAdvancesWhileAirBranchIsRelevant()
    {
        var ground = new AlsRefactoredDefaultOverlayInput(0, 1, 0, 0, 1);
        Assert.Equal(default, AlsRefactoredDefaultOverlay.Advance(default, ground, 1f / 60));
        var air = ground with { InAir = 1, GroundPrediction = .2f };
        var state = AlsRefactoredDefaultOverlay.Advance(default, air, 1f / 60);
        Assert.Equal(new(true, .2f), state);
        var up = AlsRefactoredDefaultOverlay.Advance(state, air with { GroundPrediction = 1 }, .01f);
        Assert.Equal(.36f, up.Prediction, 6);
        var down = AlsRefactoredDefaultOverlay.Advance(up, air with { GroundPrediction = 0 }, .01f);
        Assert.Equal(.342f, down.Prediction, 6);
        Assert.Equal(down, AlsRefactoredDefaultOverlay.Advance(down, ground, 10));
        Assert.Equal(down, AlsRefactoredDefaultOverlay.Advance(down, air, 0));
        Assert.Throws<ArgumentException>(() => AlsRefactoredDefaultOverlay.Advance(down, air with { Walking = float.NaN }, .01f));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void RealResourcesComposeWithSharedPlayerAndRetryDoesNotAdvanceState(int hz)
    {
        var catalog = Catalog(); var profile = AlsRefactoredDefaultOverlayCompiler.Compile(catalog);
        var bank = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog, bank,
            new Dictionary<string, AlsRefactoredTriangulationProfile>(), [profile.PlayerDefinition(0, 0)]);
        var overlay = profile.CreateRuntime(0);
        var source = catalog.CompileAbsolutePoseWithCurves(AlsRefactoredDefaultOverlayProfile.PoseSource);
        var sampler = source.Pose.CreateSampler(source.Curves);
        var expected = new AlsPrecisePose[79]; var scratch = new AlsInertialCurve[source.Curves.Names.Length];
        for (var frame = 0; frame < hz * 2; frame++)
        {
            var branch = frame / (hz / 2) % 4;
            var input = branch switch
            {
                0 => new AlsRefactoredDefaultOverlayInput(0, 1, 0, 0, 1),
                1 => new AlsRefactoredDefaultOverlayInput(1, 1, 0, 0, 0),
                2 => new AlsRefactoredDefaultOverlayInput(0, 0, 1, 0, 1),
                _ => new AlsRefactoredDefaultOverlayInput(.35f, .6f, .4f, 1, frame % 7 / 6f)
            };
            var before = overlay.CommittedState; var time = players.CommittedTime(0);
            var reset = frame == hz + 1;
            overlay.Prepare(frame, input, 1f / hz, reinitialize: reset);
            players.Prepare(frame, [overlay.SourceInput], 1f / hz);
            overlay.Evaluate(frame, players);
            var pose = overlay.Pose.ToArray(); var curves = overlay.Curves.ToArray(); var candidate = overlay.CandidateState;
            overlay.Evaluate(frame, players); Assert.Equal(pose, overlay.Pose.ToArray());
            overlay.Cancel(); players.Cancel();
            Assert.Equal(before, overlay.CommittedState); Assert.Equal(time, players.CommittedTime(0));
            Assert.Throws<InvalidOperationException>(() => overlay.Pose.ToArray());
            overlay.Prepare(frame, input, 1f / hz, reinitialize: reset);
            players.Prepare(frame, [overlay.SourceInput], 1f / hz); overlay.Evaluate(frame, players);
            Assert.Equal(candidate, overlay.CandidateState); Assert.Equal(pose, overlay.Pose.ToArray()); Assert.Equal(curves, overlay.Curves.ToArray());
            Assert.Equal(.75f, players.Ticks[0].Weight);
            if (branch < 3)
            {
                var seconds = (float)((double)branch * source.Pose.Data.FrameRateDenominator / source.Pose.Data.FrameRateNumerator);
                sampler.Sample(seconds, true, false, false, expected, scratch);
                for (var bone = 0; bone < 79; bone++)
                {
                    var composed = AlsPrecisePoseBlender.LocalApply(expected[bone], players.Pose(0)[bone], .75f);
                    Assert.True((composed.Position - overlay.Pose[bone].Position).LengthSquared < 1e-20);
                    Assert.True(Math.Abs(AlsQuaternion.Dot(composed.Rotation, overlay.Pose[bone].Rotation)) > 1 - 1e-12);
                }
                var baseNames = source.Curves.Names.ToArray(); var idleNames = players.CurveNames(0).ToArray();
                for (var c = 0; c < profile.CurveNames.Length; c++)
                {
                    var baseIndex = Array.IndexOf(baseNames, profile.CurveNames[c]);
                    var idleIndex = Array.IndexOf(idleNames, profile.CurveNames[c]);
                    var value = baseIndex < 0 ? default : scratch[baseIndex];
                    if (idleIndex >= 0) value = AlsStandingCycleCurves.Accumulate(value, players.Curves(0)[idleIndex], .75f);
                    Assert.Equal(value, overlay.Curves[c]);
                }
            }
            overlay.ValidateCommit(frame); players.ValidateCommit(frame); players.Commit(frame); overlay.Commit(frame);
        }
    }

    [Fact]
    public void InteriorBlendAndZeroStanceWeightsPreserveCurvePresenceAndReferenceFallback()
    {
        AlsPrecisePose Pose(double x) => new(new(x, 0, 0), new(0, 0, 0, 1), new(1, 1, 1));
        AlsPrecisePose[] frames = [Pose(0), Pose(10), Pose(100)];
        AlsInertialCurve[] curves = [default, new(10), new(100)];
        var input = new AlsRefactoredDefaultOverlayInput(.25f, .6f, .4f, .5f, .2f);
        var state = new AlsRefactoredDefaultOverlayState(true, .2f);
        var bone = AlsRefactoredDefaultOverlay.Bone(frames, Pose(-10), input, state);
        var curve = AlsRefactoredDefaultOverlay.Curve(curves, input, state);
        Assert.Equal(24.75, bone.Position.X, 5); Assert.True(curve.Present); Assert.Equal(24.75f, curve.Value, 5);
        input = input with { Standing = 0, Crouching = 0, InAir = 0 };
        Assert.Equal(Pose(-10), AlsRefactoredDefaultOverlay.Bone(frames, Pose(-10), input, state));
        Assert.False(AlsRefactoredDefaultOverlay.Curve(curves, input, state).Present);
    }

    [Fact]
    public void ForeignSourceFaultAndUpdateOnlyFramesRespectCommitBoundary()
    {
        var catalog = Catalog(); var profile = AlsRefactoredDefaultOverlayCompiler.Compile(catalog);
        var bank = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        var foreign = new AlsRefactoredSourcePlayerRuntime(catalog, bank, new Dictionary<string, AlsRefactoredTriangulationProfile>(),
            [new(0, AlsRefactoredDefaultOverlayProfile.PoseSource, -1)]);
        var overlay = profile.CreateRuntime(0); var input = new AlsRefactoredDefaultOverlayInput(0, 1, 0, 1, .6f);
        overlay.Prepare(0, input, .01f); foreign.Prepare(0, [overlay.SourceInput], .01f);
        Assert.Throws<ArgumentException>(() => overlay.Evaluate(0, foreign));
        Assert.Throws<ArgumentException>(() => overlay.Commit(0));
        Assert.Throws<InvalidOperationException>(() => overlay.Pose.ToArray());
        Assert.Equal(default, overlay.CommittedState);
        overlay.Cancel(); foreign.Cancel();
        overlay.Prepare(0, input, .01f); overlay.Commit(0);
        Assert.Equal(new(true, .6f), overlay.CommittedState);
        Assert.Throws<ArgumentException>(() => overlay.Prepare(0, input, .01f));
        overlay.Prepare(1, input with { GroundPrediction = .1f }, .01f, reinitialize: true);
        Assert.Equal(.1f, overlay.CandidateState.Prediction); overlay.Cancel();
        Assert.Equal(.6f, overlay.CommittedState.Prediction);
    }
}
