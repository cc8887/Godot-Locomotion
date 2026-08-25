using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

public sealed class AlsSkeletonCompilerTests
{
    [Fact]
    public void SeparatesLogicalVirtualAndPhysicalBoneContracts()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());

        var definition = AlsSkeletonCompiler.Compile(manifest.Skeletons[0]);

        Assert.Equal(5, definition.LogicalBones.Length);
        Assert.Equal(4, definition.PhysicalBones.Length);
        Assert.Single(definition.VirtualBones);
        Assert.Equal([0, 1, 2, 3, -1], definition.LogicalToPhysical);
        Assert.Equal([0, 1, 2, 3], definition.PhysicalToLogical);
        Assert.Equal(0, definition.GetLogicalBoneId("ROOT"));
        Assert.Equal(1, definition.GetPhysicalBoneId("pelvis"));
        Assert.Equal(2, definition.RequiredBones.FootLeft);
        Assert.Equal(3, definition.RequiredBones.FootRight);
        Assert.Equal(0, definition.VirtualBones[0].SourceLogicalBoneId);
        Assert.Equal(2, definition.VirtualBones[0].TargetLogicalBoneId);
        Assert.Equal(new System.Numerics.Vector3(2f, 3f, -1f), definition.PhysicalBones[1].Translation);
    }

    [Fact]
    public void RejectsVirtualBoneReferencesThatDoNotResolve()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var json = manifest.Skeletons[0].Metadata.GetRawText().Replace(
            "\"target\": \"foot_l\"",
            "\"target\": \"missing\"",
            StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var asset = manifest.Skeletons[0] with { Metadata = document.RootElement.Clone() };

        var exception = Assert.Throws<AlsCompilationException>(() => AlsSkeletonCompiler.Compile(asset));

        Assert.Contains(exception.Issues, issue =>
            issue.Code == "ALSRIG008" && issue.FieldPath == "$.metadata.virtualBones[0].target");
    }

    [Fact]
    public void RejectsAnIncompleteHumanoidRequiredBoneSet()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var json = manifest.Skeletons[0].Metadata.GetRawText().Replace(
            "\"name\": \"foot_r\"",
            "\"name\": \"toe_r\"",
            StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var asset = manifest.Skeletons[0] with { Metadata = document.RootElement.Clone() };

        var exception = Assert.Throws<AlsCompilationException>(() => AlsSkeletonCompiler.Compile(asset));

        Assert.Contains(exception.Issues, issue =>
            issue.Code == "ALSRIG010" && issue.FieldPath == "$.metadata.bones");
    }
}
