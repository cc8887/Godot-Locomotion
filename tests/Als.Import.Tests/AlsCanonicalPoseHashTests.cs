using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

public sealed class AlsCanonicalPoseHashTests
{
    [Fact]
    public void PhysicalPoseHashIsDeterministicAndExcludesVirtualBones()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var definition = AlsSkeletonCompiler.Compile(manifest.Skeletons[0]);

        var first = AlsCanonicalPoseHash.Create(definition.PhysicalBones);
        var second = AlsCanonicalPoseHash.Create(definition.PhysicalBones);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(first, definition.TargetPhysicalRestPoseHash);
    }

    [Fact]
    public void PhysicalPoseHashChangesWhenACompiledTransformChanges()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var definition = AlsSkeletonCompiler.Compile(manifest.Skeletons[0]);
        var bones = definition.PhysicalBones.ToArray();
        bones[1] = bones[1] with { Translation = bones[1].Translation + System.Numerics.Vector3.UnitY };

        Assert.NotEqual(definition.TargetPhysicalRestPoseHash, AlsCanonicalPoseHash.Create(bones));
    }
}
