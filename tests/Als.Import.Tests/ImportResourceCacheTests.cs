using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using GodotAls.Import.Resources;

namespace GodotAls.Import.Tests;

public sealed class ImportResourceCacheTests
{
    private static string Index => MantlingHostFixture.Read("refactored_animation_sources");
    private static byte[] Bytes(string path) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", path));

    [Fact]
    public void RepeatedCatalogsShareValidatedPayloadsWithinOneOperationOnly()
    {
        var reads = 0;
        var catalog = new AlsRefactoredAnimationCatalog(Index, path => { reads++; return Bytes(path); });
        var source = catalog.Assets.Values.First(entry => entry.Class == "AnimBlueprint").Source;
        var scope = new ImportResourceScope("first");
        var value = catalog.Read(source);
        for (var i = 0; i < 8; i++) Assert.Equal(value, catalog.Read(source));
        var second = new AlsRefactoredAnimationCatalog(Index, _ => throw new InvalidOperationException("Duplicate physical read"));
        Assert.Equal(value, second.Read(source));
        Assert.Equal(1, reads);
        scope.Dispose();
        Assert.Equal(0, scope.Resources.GetStatistics().LiveVersions);
        using var fresh = new ImportResourceScope("next");
        Assert.NotEqual(value, catalog.Read(source));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void IndividualPayloadReplacementAndFailedValidationPreserveOtherResources()
    {
        using var scope = new ImportResourceScope("replace");
        var catalog = new AlsRefactoredAnimationCatalog(Index, Bytes);
        var entry = catalog.Assets.Values.First(asset => asset.Class == "AnimBlueprint");
        var independent = catalog.Assets.Values.First(asset => asset.Class == "CurveFloat");
        var old = catalog.Read(entry.Source);
        var untouched = catalog.Read(independent.Source);
        var changed = JsonNode.Parse(Bytes(entry.File))!;
        changed["nativeText"] = old.GetProperty("nativeText").GetString() + "\n;test replacement";
        var payload = Encoding.UTF8.GetBytes(changed.ToJsonString());
        var index = JsonNode.Parse(Index)!;
        var row = index["assets"]!.AsArray().Single(asset => asset!["source"]!.GetValue<string>() == entry.Source)!;
        row["sha256"] = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var replacement = new AlsRefactoredAnimationCatalog(index.ToJsonString(), path => path == entry.File ? payload : Bytes(path));
        Assert.EndsWith(";test replacement", replacement.Read(entry.Source).GetProperty("nativeText").GetString());
        Assert.Equal(untouched, replacement.Read(independent.Source));
        Assert.DoesNotContain(";test replacement", old.GetProperty("nativeText").GetString());
        row["sha256"] = new string('0', 64);
        Assert.Throws<ArgumentException>(() => new AlsRefactoredAnimationCatalog(index.ToJsonString(), Bytes));
        Assert.EndsWith(";test replacement", replacement.Read(entry.Source).GetProperty("nativeText").GetString());
    }

    [Fact]
    public void CompiledPoseResourcesAreSharedButSamplersRemainIndependent()
    {
        using var scope = new ImportResourceScope("poses");
        var catalog = new AlsRefactoredAnimationCatalog(Index, Bytes);
        const string source = "/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose";
        var first = catalog.CompileAbsolutePoseWithCurves(source);
        var second = catalog.CompileAbsolutePoseWithCurves(source);
        Assert.Same(first.Pose, second.Pose); Assert.Same(first.Curves, second.Curves);
        Assert.NotSame(first.Pose.CreateSampler(), second.Pose.CreateSampler());
        var additive = catalog.CompileAdditivePose(AlsRefactoredDefaultOverlayProfile.IdleSource);
        Assert.Same(additive, catalog.CompileAdditivePose(AlsRefactoredDefaultOverlayProfile.IdleSource));
        Assert.NotSame(additive.CreateSampler(), additive.CreateSampler());
        Assert.Throws<ArgumentException>(() => catalog.CompileAbsolutePose(AlsRefactoredDefaultOverlayProfile.IdleSource));
        var inPlace = new AlsRefactoredAnimationCatalog(Index, Bytes, sampleRootMotion: false).CompileAbsolutePoseWithCurves(source);
        Assert.NotSame(first.Pose, inPlace.Pose);
        Assert.Equal(first.Pose.BoneNames.ToArray(), inPlace.Pose.BoneNames.ToArray());
    }

    [Fact]
    public void ReplacingOnePayloadInvalidatesItsPoseWithoutDiscardingUnrelatedPose()
    {
        using var scope = new ImportResourceScope("pose-replace");
        var catalog = new AlsRefactoredAnimationCatalog(Index, Bytes);
        const string source = "/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose";
        var entry = catalog.Assets[source];
        var independentSource = catalog.Assets.Values.Where(asset => asset.Class == "AnimSequence" && asset.Source != source)
            .First(asset => catalog.Read(asset.Source).GetProperty("evaluation").GetProperty("additiveType").GetString() == "AAT_None").Source;
        var old = catalog.CompileAbsolutePoseWithCurves(source);
        var independent = catalog.CompileAbsolutePoseWithCurves(independentSource);
        var changed = JsonNode.Parse(Bytes(entry.File))!;
        changed["nativeText"] = changed["nativeText"]!.GetValue<string>() + "\n;replacement";
        var bytes = Encoding.UTF8.GetBytes(changed.ToJsonString());
        var index = JsonNode.Parse(Index)!;
        index["assets"]!.AsArray().Single(asset => asset!["source"]!.GetValue<string>() == source)!["sha256"]
            = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var updated = new AlsRefactoredAnimationCatalog(index.ToJsonString(), path => path == entry.File ? bytes : Bytes(path));
        var current = updated.CompileAbsolutePoseWithCurves(source);
        Assert.NotSame(old.Pose, current.Pose);
        Assert.Equal(old.Pose.ReferencePose.ToArray(), current.Pose.ReferencePose.ToArray());
        Assert.Same(independent.Pose, updated.CompileAbsolutePoseWithCurves(independentSource).Pose);
    }
}
