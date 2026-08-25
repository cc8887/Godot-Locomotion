using System.Text.Json;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

public sealed class AlsAnimationSetCompilerTests
{
    [Fact]
    public void CompilesStableIndicesAndTypedClipSemantics()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());

        var definition = AlsAnimationSetCompiler.Compile(manifest);

        Assert.Single(definition.Skeletons);
        var clip = Assert.Single(definition.Animations);
        Assert.Equal(0, clip.Id);
        Assert.Equal("67aa33bdcab9e580ed7bec894c9858bb5cf30764", clip.StableId);
        Assert.Equal(0, clip.SkeletonId);
        Assert.Equal(1f, clip.PlayLength);
        Assert.Equal(30, clip.FrameRateNumerator);
        Assert.True(clip.Loop);
        Assert.Equal(0, definition.AssetIndex.GetAnimationId(clip.StableId));
        Assert.Equal(0, definition.AssetIndex.GetSkeletonId(definition.Skeletons[0].AssetId));
        Assert.Equal(64, definition.DefinitionDigest.Length);
    }

    [Fact]
    public void CompilationDigestIsDeterministicAndChangesWithClipMetadata()
    {
        var json = File.ReadAllText(AlsManifestSerializerTests.FixturePath());
        var original = AlsManifestSerializer.Deserialize(json);
        var changed = AlsManifestSerializer.Deserialize(json.Replace(
            "\"playLength\": 1.0",
            "\"playLength\": 2.0",
            StringComparison.Ordinal));

        var first = AlsAnimationSetCompiler.Compile(original);
        var second = AlsAnimationSetCompiler.Compile(original);
        var modified = AlsAnimationSetCompiler.Compile(changed);

        Assert.Equal(first.DefinitionDigest, second.DefinitionDigest);
        Assert.NotEqual(first.DefinitionDigest, modified.DefinitionDigest);
    }

    [Fact]
    public void RejectsAnUnresolvedTypedAnimationReference()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var metadataJson = manifest.Animations[0].Metadata.GetRawText().Replace(
            "09ee83c5ee0df9c7343e9ebc943d4902498af3e5",
            new string('f', 40),
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(metadataJson);
        manifest = manifest with
        {
            Animations = [manifest.Animations[0] with { Metadata = document.RootElement.Clone() }],
        };

        var exception = Assert.Throws<AlsCompilationException>(() => AlsAnimationSetCompiler.Compile(manifest));

        Assert.Contains(exception.Issues, issue =>
            issue.Code == "ALSMANIFEST012" &&
            issue.FieldPath == "$.animations[0].metadata.skeletonId");
    }

    [Fact]
    public void CompilesCompositeAssetMetadataAndReferences()
    {
        var fixture = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var animation = fixture.Animations[0];
        var skeleton = fixture.Skeletons[0];
        var texture = Asset("/Game/Test/T_Test.T_Test", "/Script/Engine.Texture2D", "textures/test.png", [], new
        {
            overlay = false, prop = true, width = 64, height = 32, pixelFormatSource = "TSF_BGRA8",
        });
        var parentMaterial = Asset("/Game/Test/M_Parent.M_Parent", "/Script/Engine.Material", null, [texture.Id], new
        {
            overlay = false,
            prop = true,
            referencedTextures = new[] { new { id = texture.Id, objectPath = texture.ObjectPath } },
        });
        var material = Asset("/Game/Test/MI_Test.MI_Test", "/Script/Engine.MaterialInstanceConstant", null, [parentMaterial.Id, texture.Id], new
        {
            overlay = true,
            prop = true,
            referencedTextures = new[] { new { id = texture.Id, objectPath = texture.ObjectPath } },
            parentId = parentMaterial.Id,
            parentObjectPath = parentMaterial.ObjectPath,
            scalarParameterOverrides = new[] { new { name = "Roughness", association = 0, index = -1, value = 0.25f } },
            vectorParameterOverrides = new[] { new { name = "Color", association = 0, index = -1, value = new[] { 1f, 0.5f, 0.25f, 1f } } },
            textureParameterOverrides = new[] { new { name = "Albedo", association = 0, index = -1, textureId = texture.Id, textureObjectPath = texture.ObjectPath } },
        });
        var skeletalMesh = Asset("/Game/Test/SK_Test.SK_Test", "/Script/Engine.SkeletalMesh", "meshes/skeletal/test.fbx", [skeleton.Id, material.Id], new
        {
            overlay = false, prop = false, skeletonId = skeleton.Id, skeletonObjectPath = skeleton.ObjectPath, materialSlotCount = 2,
        });
        var staticMesh = Asset("/Game/Test/SM_Test.SM_Test", "/Script/Engine.StaticMesh", "meshes/static/test.fbx", [material.Id], new
        {
            overlay = false, prop = true, materialSlotCount = 1,
        });
        var montage = Asset("/Game/Test/AM_Test.AM_Test", "/Script/Engine.AnimMontage", null, [animation.Id], new
        {
            overlay = false,
            prop = false,
            sections = new[] { new { name = "Default", nextSection = "None", startTime = 0f } },
            slots = new[] { new { slotName = "BaseLayer", segments = new[] { new { animationId = animation.Id, animationObjectPath = animation.ObjectPath, startPosition = 0f, animationStartTime = 0f, animationEndTime = 1f, playRate = 1f, loopCount = 1 } } } },
            playLength = 1f,
            blendInTime = 0.1f,
            blendInOption = 2,
            blendOutTime = 0.2f,
            blendOutOption = 2,
            blendOutTriggerTime = -1f,
            enableAutoBlendOut = true,
        });
        var blend = BlendAsset("/Game/Test/BS_Test.BS_Test", "/Script/Engine.BlendSpace", animation);
        var aim = BlendAsset("/Game/Test/AO_Test.AO_Test", "/Script/Engine.AimOffsetBlendSpace", animation);
        var physics = Asset("/Game/Test/PHYS_Test.PHYS_Test", "/Script/Engine.PhysicsAsset", null, [skeletalMesh.Id], new
        {
            overlay = false,
            prop = false,
            bodies = new[] { new { bone = "pelvis", primitiveCount = 2 } },
            constraints = new[] { new { childBone = "pelvis", parentBone = "root" } },
            constraintCount = 1,
        });
        var curve = GenericAsset("/Game/Test/Curve_Test.Curve_Test", "/Script/Engine.CurveFloat");
        var config = GenericAsset("/Game/Test/Config_Test.Config_Test", "/Script/Engine.DataAsset");
        var materials = new[] { parentMaterial, material }.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
        var manifest = fixture with
        {
            SkeletalMeshes = [skeletalMesh],
            StaticMeshes = [staticMesh],
            Montages = [montage],
            BlendSpaces = [blend],
            AimOffsets = [aim],
            Materials = materials,
            Textures = [texture],
            PhysicsAssets = [physics],
            Curves = [curve],
            ConfigAssets = [config],
            AuditSummary = fixture.AuditSummary with { AssetCount = 13 },
        };

        var definition = AlsAnimationSetCompiler.Compile(manifest);

        Assert.Equal(0, definition.AssetIndex.GetSkeletalMeshId(skeletalMesh.Id));
        Assert.Equal(0, definition.AssetIndex.GetStaticMeshId(staticMesh.Id));
        Assert.Equal(0, definition.SkeletalMeshes[0].SkeletonId);
        Assert.Equal(2, definition.SkeletalMeshes[0].MaterialSlotCount);
        Assert.Equal(1, definition.StaticMeshes[0].MaterialSlotCount);
        Assert.Equal(0, definition.Montages[0].Slots[0].Segments[0].AnimationId);
        Assert.Equal("Default", definition.Montages[0].Sections[0].Name);
        Assert.Equal(0, definition.BlendSpaces[0].Samples[0].AnimationId);
        Assert.Equal(30f, definition.BlendSpaces[0].Parameters[0].Maximum);
        Assert.Equal(0, definition.AimOffsets[0].Samples[0].AnimationId);
        Assert.Equal(64, definition.Textures[0].Width);
        var compiledMaterial = definition.Materials[definition.AssetIndex.GetMaterialId(material.Id)];
        Assert.Equal(definition.AssetIndex.GetMaterialId(parentMaterial.Id), compiledMaterial.ParentMaterialId);
        Assert.Equal(0, compiledMaterial.ReferencedTextureIds[0]);
        Assert.Equal(0, compiledMaterial.TextureParameterOverrides[0].TextureId);
        Assert.Equal(0.25f, compiledMaterial.ScalarParameterOverrides[0].Value);
        Assert.Equal(2, definition.PhysicsAssets[0].Bodies[0].PrimitiveCount);
        Assert.Equal("root", definition.PhysicsAssets[0].Constraints[0].ParentBone);
        Assert.Equal(0, definition.AssetIndex.GetCurveId(curve.Id));
        Assert.Equal(0, definition.AssetIndex.GetConfigAssetId(config.Id));

        var payload = AlsAnimationSetPayload.Serialize(definition);
        Assert.Equal(64, AlsAnimationSetPayload.ComputeSha256(payload).Length);
        var restored = AlsAnimationSetPayload.Deserialize(payload);
        Assert.Equal(definition.DefinitionDigest, restored.DefinitionDigest);
        Assert.Equal(definition.Montages[0].Slots[0].Segments[0], restored.Montages[0].Slots[0].Segments[0]);
        Assert.Equal(definition.BlendSpaces[0].Samples[0].AnimationId, restored.BlendSpaces[0].Samples[0].AnimationId);
        Assert.Equal(definition.BlendSpaces[0].Samples[0].SampleValue, restored.BlendSpaces[0].Samples[0].SampleValue);
        Assert.Equal(definition.BlendSpaces[0].Samples[0].RateScale, restored.BlendSpaces[0].Samples[0].RateScale);
        Assert.Equal(definition.Materials[1].TextureParameterOverrides[0], restored.Materials[1].TextureParameterOverrides[0]);
        Assert.Equal(definition.PhysicsAssets[0].Constraints[0], restored.PhysicsAssets[0].Constraints[0]);
        Assert.Equal(0, restored.AssetIndex.GetAnimationId(animation.Id));
    }

    [Fact]
    public void RejectsPhysicsBonesThatDoNotResolveAgainstTheDependentMeshSkeleton()
    {
        var fixture = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var skeleton = fixture.Skeletons[0];
        var skeletalMesh = Asset(
            "/Game/Test/SK_Test.SK_Test",
            "/Script/Engine.SkeletalMesh",
            "meshes/skeletal/test.fbx",
            [skeleton.Id],
            new
            {
                overlay = false,
                prop = false,
                skeletonId = skeleton.Id,
                skeletonObjectPath = skeleton.ObjectPath,
                materialSlotCount = 0,
            });
        var physics = Asset(
            "/Game/Test/PHYS_Test.PHYS_Test",
            "/Script/Engine.PhysicsAsset",
            null,
            [skeletalMesh.Id],
            new
            {
                overlay = false,
                prop = false,
                bodies = new[] { new { bone = "missing", primitiveCount = 1 } },
                constraints = new[] { new { childBone = "pelvis", parentBone = "missing" } },
                constraintCount = 1,
            });
        var manifest = fixture with
        {
            SkeletalMeshes = [skeletalMesh],
            PhysicsAssets = [physics],
            AuditSummary = fixture.AuditSummary with { AssetCount = 4 },
        };

        var exception = Assert.Throws<AlsCompilationException>(() => AlsAnimationSetCompiler.Compile(manifest));

        Assert.Contains(exception.Issues, issue =>
            issue.Code == "ALSPHYSICS003" && issue.FieldPath == "$.physicsAssets[0].metadata.bodies[0].bone");
    }

    private static AlsManifestAsset BlendAsset(string objectPath, string classPath, AlsManifestAsset animation) =>
        Asset(objectPath, classPath, null, [animation.Id], new
        {
            overlay = false,
            prop = false,
            parameters = new[] { new { name = "Direction", minimum = -30f, maximum = 30f, gridDivisions = 4 } },
            samples = new[] { new { animationId = animation.Id, animationObjectPath = animation.ObjectPath, sampleValue = new[] { 10f, 0f, 0f }, rateScale = 1f } },
        });

    private static AlsManifestAsset GenericAsset(string objectPath, string classPath) =>
        Asset(objectPath, classPath, null, [], new { overlay = false, prop = false, assetClass = classPath, assetRegistryTagCount = 3 });

    private static AlsManifestAsset Asset(
        string objectPath,
        string classPath,
        string? outputPath,
        string[] dependencies,
        object metadata)
    {
        var packagePath = objectPath[..objectPath.LastIndexOf('.')];
        var assetName = objectPath[(objectPath.LastIndexOf('/') + 1)..objectPath.LastIndexOf('.')];
        return new AlsManifestAsset(
            AlsStableAssetId.Create(objectPath),
            objectPath,
            packagePath,
            assetName,
            classPath,
            outputPath,
            dependencies,
            JsonSerializer.SerializeToElement(metadata, AlsManifestSerializer.JsonOptions));
    }
}
