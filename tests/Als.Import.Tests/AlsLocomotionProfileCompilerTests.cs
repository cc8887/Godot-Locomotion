using System.Text.Json;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLocomotionProfileCompilerTests
{
    private static readonly string[] ExactTopLevelProperties =
    [
        "schemaVersion",
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
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
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
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1,\n  \"schemaVersion\": 1,",
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

    private static AlsCompilationException CompileFailure(string json) =>
        Assert.Throws<AlsCompilationException>(() =>
            AlsLocomotionProfileCompiler.Compile(json, P3RepositoryFixtures.LoadAnimationSet()));
}
