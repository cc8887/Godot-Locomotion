using GodotAls.Import.Manifest;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Tests;

public sealed class AlsExportPlanValidatorTests
{
    [Fact]
    public void ValidatorAcceptsCanonicalPlan()
    {
        var mesh = Asset("/Game/AdvancedLocomotionV4/Props/Meshes/Box.Box", AlsAssetKind.StaticMesh,
            "meshes/static/Props/Meshes/Box.fbx");
        var plan = new AlsExportPlan(1, [mesh], new AlsExportPlanSummary(1, 1));

        Assert.Empty(AlsExportPlanValidator.Validate(plan));
    }

    [Fact]
    public void ValidatorRejectsUnsortedAssetsAndExcludedAudio()
    {
        var audio = Asset("/Game/AdvancedLocomotionV4/Audio/Step.Step", AlsAssetKind.OtherConfig);
        var mesh = Asset("/Game/AdvancedLocomotionV4/Props/Meshes/Box.Box", AlsAssetKind.StaticMesh,
            "meshes/static/Props/Meshes/Box.fbx");
        var plan = new AlsExportPlan(1, [mesh, audio], new AlsExportPlanSummary(2, 1));

        var issues = AlsExportPlanValidator.Validate(plan);

        Assert.Contains(issues, issue => issue.Code == "ALSPLAN004");
        Assert.Contains(issues, issue => issue.Code == "ALSPLAN007");
    }

    [Fact]
    public void ValidatorAcceptsNestedOriginalEngineNamesWithoutPinningAssetIds()
    {
        var path = "/Game/ReplacementPack/Characters/Prototype/Body.Body";
        var mesh = Asset(path, AlsAssetKind.SkeletalMesh,
            "meshes/skeletal/Characters/Prototype/Body.fbx");
        var plan = new AlsExportPlan(1, [mesh], new AlsExportPlanSummary(1, 1));

        Assert.Empty(AlsExportPlanValidator.Validate(plan));
    }

    [Fact]
    public void ValidatorRejectsStableIdBasedNamesAndUnsafeNestedPaths()
    {
        var objectPath = "/Game/ReplacementPack/Characters/Body.Body";
        var id = AlsStableAssetId.Create(objectPath);
        foreach (var outputPath in new[]
        {
            $"meshes/skeletal/{id}.fbx",
            "meshes/skeletal/../Body.fbx",
            "meshes/skeletal/Characters\\Body.fbx",
        })
        {
            var mesh = Asset(objectPath, AlsAssetKind.SkeletalMesh, outputPath);
            var plan = new AlsExportPlan(1, [mesh], new AlsExportPlanSummary(1, 1));

            Assert.Contains(AlsExportPlanValidator.Validate(plan), issue => issue.Code == "ALSPLAN009");
        }
    }

    [Fact]
    public void ValidatorRejectsMismatchedIdMissingDependencyAndInvalidOutput()
    {
        var asset = Asset("/Game/AdvancedLocomotionV4/Props/Meshes/Box.Box", AlsAssetKind.StaticMesh,
            "animations/not-an-id.fbx") with
        {
            Id = new string('0', 40),
            Dependencies = [new string('1', 40)],
        };
        var plan = new AlsExportPlan(1, [asset], new AlsExportPlanSummary(2, 0));

        var issues = AlsExportPlanValidator.Validate(plan);

        Assert.Contains(issues, issue => issue.Code == "ALSPLAN003");
        Assert.Contains(issues, issue => issue.Code == "ALSPLAN008");
        Assert.Contains(issues, issue => issue.Code == "ALSPLAN009");
        Assert.Contains(issues, issue => issue.Code == "ALSPLAN010");
    }

    private static AlsExportPlanAsset Asset(string objectPath, AlsAssetKind kind, string? outputPath = null)
    {
        return new AlsExportPlanAsset(
            AlsStableAssetId.Create(objectPath),
            objectPath,
            objectPath[..objectPath.LastIndexOf('.')],
            $"/Script/Engine.{kind}",
            kind,
            outputPath,
            [],
            []);
    }
}
