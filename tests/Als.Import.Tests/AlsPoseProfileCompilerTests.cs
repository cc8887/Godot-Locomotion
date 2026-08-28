using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPoseProfileCompilerTests
{
    [Fact]
    public void RepositoryProfileCompilesExactAimTurnRotateMasksAndFeet()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();

        var profile = AlsPoseProfileCompiler.Compile(ReadProfile(), set);

        Assert.Equal(1, profile.SchemaVersion);
        Assert.Equal(set.AssetIndex.GetSkeletonId("b5b52715012cad50bf7a625ddf01e4335bb4fcf0"), profile.SkeletonId);
        Assert.Equal(set.AssetIndex.GetAimOffsetId("b4bf2befd979de45f53f300dc0e60c702fc3a686"), profile.Aim.AimOffsetId);
        Assert.Equal(8, profile.Turns.Length);
        Assert.Equal(4, profile.Rotates.Length);
        Assert.All(profile.Turns, turn =>
        {
            Assert.Equal(1.2f, turn.BasePlayRate);
            Assert.Equal(0.2f, turn.BlendSeconds);
            Assert.Equal((byte)1, turn.ScaleAngle);
            var curve = set.Animations[turn.AnimationId].Curves.Single(value =>
                value.CanonicalKind == AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond);
            Assert.Equal(curve.CurveId, turn.CurveId);
        });
        Assert.Equal(8, profile.Turns.Select(value => (value.Stance, value.Direction, value.NominalDegrees)).Distinct().Count());
        Assert.Equal(4, profile.Rotates.Select(value => (value.Stance, value.Direction)).Distinct().Count());
        Assert.Equal(11, profile.Masks.Entries.Length);
        Assert.All(profile.Masks.Entries, entry => Assert.NotEmpty(entry.BoneIds));
        Assert.DoesNotContain(profile.Masks.Entries.SelectMany(value => value.BoneIds)
            .GroupBy(value => value), value => value.Count() > 1);
        Assert.True(profile.Feet.TraceUpMeters > 0f);
        Assert.True(profile.Feet.TraceDownMeters > 0f);
    }

    [Fact]
    public void RuntimeArraysAreDefensivelyCopied()
    {
        var profile = AlsPoseProfileCompiler.Compile(ReadProfile(), P3RepositoryFixtures.LoadAnimationSet());
        var turns = profile.Turns;
        var masks = profile.Masks.Entries;
        var bones = masks[0].BoneIds;

        turns[0] = default;
        masks[0] = masks[1];
        bones[0] = -100;

        Assert.NotEqual(default, profile.Turns[0]);
        Assert.NotEqual(profile.Masks.Entries[1], profile.Masks.Entries[0]);
        Assert.DoesNotContain(-100, profile.Masks.Entries[0].BoneIds);
    }

    [Theory]
    [InlineData("unknown", "$.unknown")]
    [InlineData("missingStableId", "$.aim.down")]
    [InlineData("missingBone", "$.masks[0].root")]
    [InlineData("nonCanonicalRoots", "$.masks[9].root")]
    [InlineData("asymmetricRoots", "$.masks")]
    [InlineData("duplicateMaskBone", "$.masks")]
    [InlineData("boundaryEscape", "$.masks[0].boundaries[0]")]
    public void InvalidProfileRelationshipsAreRejectedWithStablePaths(string mutation, string expectedPath)
    {
        var json = Mutate(root =>
        {
            switch (mutation)
            {
                case "unknown": root["unknown"] = true; break;
                case "missingStableId": root["aim"]!["down"] = new string('f', 40); break;
                case "missingBone": root["masks"]![0]!["root"] = "missing_bone"; break;
                case "nonCanonicalRoots":
                    root["masks"]![9]!["root"] = "ball_l";
                    root["masks"]![10]!["root"] = "ball_r";
                    root["feet"]!["leftFootRoot"] = "ball_l";
                    root["feet"]!["rightFootRoot"] = "ball_r";
                    break;
                case "asymmetricRoots": root["masks"]![10]!["root"] = "Foot_L"; break;
                case "duplicateMaskBone": root["masks"]![0]!["boundaries"] = new JsonArray("neck_01", "clavicle_r"); break;
                case "boundaryEscape": root["masks"]![0]!["boundaries"]![0] = "Foot_L"; break;
            }
        });

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsPoseProfileCompiler.Compile(json, P3RepositoryFixtures.LoadAnimationSet()));

        Assert.Contains(exception.Issues, issue => issue.FieldPath.StartsWith(expectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateJsonPropertyIsRejected()
    {
        var json = ReadProfile().Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1,\n  \"schemaVersion\": 1,",
            StringComparison.Ordinal);

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsPoseProfileCompiler.Compile(json, P3RepositoryFixtures.LoadAnimationSet()));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.schemaVersion" && issue.Message.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void WrongSkeletonIsRejectedWithAssetStableId()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var downId = set.AssetIndex.GetAnimationId("c4bba06be47ce74153c2048d0de7b30a6465a84f");
        var animations = set.Animations.ToArray();
        animations[downId] = animations[downId] with { SkeletonId = 0 };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsPoseProfileCompiler.Compile(ReadProfile(), set with { Animations = animations }));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.aim.down" && issue.AssetId == animations[downId].StableId);
    }

    [Fact]
    public void IncompatibleAimAdditiveBaseIsRejected()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var downId = set.AssetIndex.GetAnimationId("c4bba06be47ce74153c2048d0de7b30a6465a84f");
        var animations = set.Animations.ToArray();
        animations[downId] = animations[downId] with { AdditiveBasePoseAnimationId = downId };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsPoseProfileCompiler.Compile(ReadProfile(), set with { Animations = animations }));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.aim.down" && issue.AssetId == animations[downId].StableId);
    }

    [Fact]
    public void MissingCanonicalCurveIsRejectedWithAnimationStableId()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var turnStableId = JsonNode.Parse(ReadProfile())!["turns"]![0]!["animation"]!.GetValue<string>();
        var animationId = set.AssetIndex.GetAnimationId(turnStableId);
        var animations = set.Animations.ToArray();
        animations[animationId] = animations[animationId] with { Curves = [] };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsPoseProfileCompiler.Compile(ReadProfile(), set with { Animations = animations }));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.turns[0].animation" && issue.AssetId == turnStableId);
    }

    private static string ReadProfile() => File.ReadAllText(Path.Combine(
        RepositoryRoot.Find(), "assets", "config", "p4_pose_profile.json"));

    private static string Mutate(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }
}
