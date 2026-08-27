using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLocomotionProfileCompilerTests
{
    private static readonly string[] ExactTopLevelProperties =
    [
        "schemaVersion",
        "presentation",
        "mannequin",
        "standingIdle",
        "crouchingIdle",
        "standingSamples",
        "crouchingSamples",
        "jumpStart",
        "fallLoop",
        "land",
        "leanAdditive",
    ];

    [Fact]
    public void RepositoryProfileCompilesAgainstTheP2AnimationSet()
    {
        var definition = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), definition);

        Assert.Equal(13, profile.StandingSamples.Length);
        Assert.Equal(4, profile.CrouchingSamples.Length);
        Assert.Equal(5, profile.LeanAdditiveSamples.Length);
        Assert.True(profile.MannequinMeshId >= 0);
        Assert.True(profile.StandingIdleAnimationId >= 0);
        Assert.True(profile.CrouchingIdleAnimationId >= 0);
        Assert.True(profile.JumpStartAnimationId >= 0);
        Assert.True(profile.FallLoopAnimationId >= 0);
        Assert.True(profile.LandAnimationId >= 0);
        Assert.True(profile.LeanAdditiveBasePoseAnimationId >= 0);
        Assert.All(profile.AllAnimationIds, id => Assert.True(id >= 0));
    }

    [Fact]
    public void RepositoryPresentationCompilesToTheLockedGodotTransform()
    {
        var profile = AlsLocomotionProfileCompiler.Compile(
            P3RepositoryFixtures.ReadProfile(), P3RepositoryFixtures.LoadAnimationSet());
        Assert.Equal(new System.Numerics.Vector3(0f, -0.92f, 0f),
            profile.Presentation.TranslationMeters);
        Assert.InRange(MathF.Abs(profile.Presentation.YawRadians - MathF.PI / 2f), 0f, 1e-6f);
    }

    [Fact]
    public void SchemaVersionOneIsRejectedBeforePresentationCompilation()
    {
        var json = P3RepositoryFixtures.WithProfileMutation(root => root["schemaVersion"] = 1);
        var exception = CompileFailure(json);
        Assert.Contains(exception.Issues, issue =>
            issue.Code == "ALSPROFILE003" && issue.FieldPath == "$.schemaVersion" &&
            issue.Expected == "2" && issue.Actual == "1");
    }

    [Theory]
    [MemberData(nameof(InvalidPresentationCases))]
    public void InvalidPresentationIsRejectedWithSpecificDiagnostic(
        string json,
        string expectedCode,
        string expectedPath,
        string? expected,
        string? actual)
    {
        var exception = CompileFailure(json);

        var issue = Assert.Single(exception.Issues);
        Assert.Equal(expectedCode, issue.Code);
        Assert.Equal(expectedPath, issue.FieldPath);
        if (expected is not null)
        {
            Assert.Equal(expected, issue.Expected);
        }
        if (actual is not null)
        {
            Assert.Equal(actual, issue.Actual);
        }
    }

    [Fact]
    public void AllAnimationIdsAreTheExactSortedUniqueRuntimeUnion()
    {
        var definition = P3RepositoryFixtures.LoadAnimationSet();
        var first = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), definition);
        var second = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), definition);
        var expected = new[]
            {
                first.StandingIdleAnimationId,
                first.CrouchingIdleAnimationId,
            }
            .Concat(first.StandingSamples.Select(sample => sample.AnimationId))
            .Concat(first.CrouchingSamples.Select(sample => sample.AnimationId))
            .Concat([first.JumpStartAnimationId, first.FallLoopAnimationId, first.LandAnimationId])
            .Concat(first.LeanAdditiveSamples.Select(sample => sample.AnimationId))
            .Append(first.LeanAdditiveBasePoseAnimationId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(expected, first.AllAnimationIds);
        Assert.Equal(first.AllAnimationIds.Length, first.AllAnimationIds.Distinct().Count());
        Assert.Equal(first.AllAnimationIds.OrderBy(id => id), first.AllAnimationIds);
        Assert.Equal(first.AllAnimationIds, second.AllAnimationIds);
    }

    [Fact]
    public void RepositoryProfileHasTheExactTopLevelContract()
    {
        using var document = JsonDocument.Parse(P3RepositoryFixtures.ReadProfile());
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(ExactTopLevelProperties, names);
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void CompiledRuntimeProfileContainsNoStableIdStrings()
    {
        var profileType = typeof(AlsLocomotionAnimationProfile);
        var sampleType = typeof(AlsLocomotionAnimationSample);

        Assert.DoesNotContain(profileType.GetProperties(), property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(sampleType.GetProperties(), property => property.PropertyType == typeof(string));
        Assert.All(sampleType.GetProperties(), property =>
            Assert.Contains(property.PropertyType, new[] { typeof(int), typeof(float) }));
    }

    [Fact]
    public void MissingStableIdFailsWithoutFallback()
    {
        var exception = CompileFailure(P3RepositoryFixtures.WithMissingJumpClip());

        Assert.Contains("missing stable ID", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == "$.jumpStart" &&
            issue.Message.Contains("missing stable ID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownTopLevelPropertyIsRejected()
    {
        var exception = CompileFailure(P3RepositoryFixtures.WithUnknownProperty());

        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == "$.fallback" &&
            issue.Message.Contains("unknown property", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownSamplePropertyIsRejected()
    {
        var exception = CompileFailure(P3RepositoryFixtures.WithUnknownSampleProperty());

        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == "$.standingSamples[0].fallback" &&
            issue.Message.Contains("unknown property", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DuplicateJsonPropertyIsRejected()
    {
        var json = P3RepositoryFixtures.ReadProfile().Replace(
            "\"schemaVersion\": 2,",
            "\"schemaVersion\": 2,\n  \"schemaVersion\": 2,",
            StringComparison.Ordinal);

        var exception = CompileFailure(json);

        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == "$.schemaVersion" &&
            issue.Message.Contains("duplicate JSON property", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NonFiniteSampleCoordinateIsRejected()
    {
        var exception = CompileFailure(P3RepositoryFixtures.WithInvalidSampleNumber());

        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == "$.standingSamples[0].x" &&
            issue.Message.Contains("finite", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("mannequin")]
    [InlineData("standingIdle")]
    [InlineData("leanAdditive")]
    public void MissingRequiredPropertyIsRejected(string propertyName)
    {
        var exception = CompileFailure(P3RepositoryFixtures.WithMissingProperty(propertyName));

        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == $"$.{propertyName}" &&
            issue.Message.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DuplicateStableIdIsRejected()
    {
        var exception = CompileFailure(P3RepositoryFixtures.WithDuplicateStableId());

        Assert.Contains(exception.Issues, issue =>
            issue.Message.Contains("duplicate stable ID", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("standingSamples")]
    [InlineData("crouchingSamples")]
    public void EmptyLocomotionGridIsRejected(string propertyName)
    {
        var exception = CompileFailure(P3RepositoryFixtures.WithEmptyGrid(propertyName));

        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == $"$.{propertyName}" &&
            issue.Message.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CrossSkeletonAnimationIsRejected()
    {
        var definition = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), definition);
        var animations = definition.Animations.ToArray();
        var id = profile.StandingSamples[0].AnimationId;
        animations[id] = animations[id] with { SkeletonId = profile.SkeletonId + 1 };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsLocomotionProfileCompiler.Compile(
                P3RepositoryFixtures.ReadProfile(), definition with { Animations = animations }));

        Assert.Contains(exception.Issues, issue =>
            issue.Message.Contains("cross-skeleton", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnsupportedLeanAdditiveTypeIsRejected()
    {
        var definition = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), definition);
        var animations = definition.Animations.ToArray();
        var id = profile.LeanAdditiveSamples[0].AnimationId;
        animations[id] = animations[id] with { AdditiveType = 2 };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsLocomotionProfileCompiler.Compile(
                P3RepositoryFixtures.ReadProfile(), definition with { Animations = animations }));

        Assert.Contains(exception.Issues, issue =>
            issue.Message.Contains("unsupported additive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MismatchedLeanBasePoseIsRejected()
    {
        var definition = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), definition);
        var animations = definition.Animations.ToArray();
        var id = profile.LeanAdditiveSamples[0].AnimationId;
        animations[id] = animations[id] with
        {
            AdditiveBasePoseAnimationId = profile.StandingIdleAnimationId,
        };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsLocomotionProfileCompiler.Compile(
                P3RepositoryFixtures.ReadProfile(), definition with { Animations = animations }));

        Assert.Contains(exception.Issues, issue =>
            issue.Message.Contains("base pose mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MismatchedLeanBasePoseFrameIsRejectedWithSpecificDiagnostic()
    {
        var definition = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), definition);
        var animations = definition.Animations.ToArray();
        var id = profile.LeanAdditiveSamples[0].AnimationId;
        animations[id] = animations[id] with { AdditiveBasePoseFrame = 7 };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsLocomotionProfileCompiler.Compile(
                P3RepositoryFixtures.ReadProfile(), definition with { Animations = animations }));

        Assert.Contains(exception.Issues, issue =>
            issue.Code == "ALSPROFILE022" &&
            issue.FieldPath == "$.leanAdditive.samples[1]" &&
            issue.Message.Contains("base pose frame mismatch", StringComparison.OrdinalIgnoreCase) &&
            issue.Expected == "7" &&
            issue.Actual == "0");
    }

    public static IEnumerable<object[]> InvalidPresentationCases()
    {
        yield return Case(
            P3RepositoryFixtures.WithProfileMutation(root => root.Remove("presentation")),
            "ALSPROFILE007", "$.presentation");
        yield return Case(
            P3RepositoryFixtures.WithProfileMutation(root => root["presentation"] = 0),
            "ALSPROFILE004", "$.presentation");
        yield return Case(
            WithDuplicatePresentationProperty(),
            "ALSPROFILE006", "$.presentation");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(
                presentation => presentation["fallback"] = 0),
            "ALSPROFILE005", "$.presentation.fallback");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(
                presentation => presentation.Remove("translationMeters")),
            "ALSPROFILE007", "$.presentation.translationMeters");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(
                presentation => presentation.Remove("yawRadians")),
            "ALSPROFILE007", "$.presentation.yawRadians");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(presentation =>
            {
                presentation["YawRadians"] = presentation["yawRadians"]!.DeepClone();
                presentation.Remove("yawRadians");
            }),
            "ALSPROFILE005", "$.presentation.YawRadians");
        yield return Case(
            WithDuplicateYawRadiansProperty(),
            "ALSPROFILE006", "$.presentation.yawRadians");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(
                presentation => presentation["translationMeters"] = 0),
            "ALSPROFILE025", "$.presentation.translationMeters");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(presentation =>
                presentation["translationMeters"] = JsonNode.Parse("[0,-0.92]")),
            "ALSPROFILE026", "$.presentation.translationMeters", "3", "2");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(presentation =>
                presentation["translationMeters"] = JsonNode.Parse("[0,-0.92,0,0]")),
            "ALSPROFILE026", "$.presentation.translationMeters", "3", "4");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(presentation =>
                presentation["translationMeters"] = JsonNode.Parse("[0,\"bad\",0]")),
            "ALSPROFILE013", "$.presentation.translationMeters[1]");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(presentation =>
                presentation["translationMeters"] = JsonNode.Parse("[0,1e100,0]")),
            "ALSPROFILE013", "$.presentation.translationMeters[1]");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(
                presentation => presentation["yawRadians"] = "bad"),
            "ALSPROFILE013", "$.presentation.yawRadians");
        yield return Case(
            P3RepositoryFixtures.WithPresentationMutation(
                presentation => presentation["yawRadians"] = 1e100),
            "ALSPROFILE013", "$.presentation.yawRadians");
    }

    private static object[] Case(
        string json,
        string code,
        string path,
        string? expected = null,
        string? actual = null) =>
        [json, code, path, expected!, actual!];

    private static string WithDuplicatePresentationProperty() =>
        P3RepositoryFixtures.ReadProfile().Replace(
            "\"presentation\": {",
            $"\"presentation\": {{}},{Environment.NewLine}  \"presentation\": {{",
            StringComparison.Ordinal);

    private static string WithDuplicateYawRadiansProperty() =>
        P3RepositoryFixtures.ReadProfile().Replace(
            "\"yawRadians\": 1.5707963267948966",
            $"\"yawRadians\": 1.5707963267948966,{Environment.NewLine}" +
            "    \"yawRadians\": 1.5707963267948966",
            StringComparison.Ordinal);

    private static AlsCompilationException CompileFailure(string json) =>
        Assert.Throws<AlsCompilationException>(() =>
            AlsLocomotionProfileCompiler.Compile(json, P3RepositoryFixtures.LoadAnimationSet()));
}
