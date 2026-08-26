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

    private static AlsCompilationException CompileFailure(string json) =>
        Assert.Throws<AlsCompilationException>(() =>
            AlsLocomotionProfileCompiler.Compile(json, P3RepositoryFixtures.LoadAnimationSet()));
}
