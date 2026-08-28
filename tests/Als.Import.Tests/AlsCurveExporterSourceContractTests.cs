namespace GodotAls.Import.Tests;

public sealed class AlsCurveExporterSourceContractTests
{
    private static readonly string CommandletSource = File.ReadAllText(Path.Combine(
        RepositoryRoot.Find(),
        "tools", "unreal", "AlsGodotExporter", "Source", "AlsGodotExporter", "Private",
        "AlsGodotExportCommandlet.cpp"));
    private static readonly string BuildScript = File.ReadAllText(Path.Combine(
        RepositoryRoot.Find(), "scripts", "build-als-exporter.ps1"));

    [Fact]
    public void ReadyCheckRunsTheNativeCurveExportSelfTestBeforePublishingItsReadyMarker()
    {
        var selfTestIndex = CommandletSource.IndexOf("RunCurveKeySelfTest", StringComparison.Ordinal);
        var markerIndex = CommandletSource.IndexOf("GODOT_ALS_CURVE_EXPORT_SELF_TEST_OK cases=%d", StringComparison.Ordinal);
        var readyIndex = CommandletSource.IndexOf("GODOT_ALS_EXPORTER_READY", StringComparison.Ordinal);

        Assert.True(selfTestIndex >= 0);
        Assert.True(markerIndex > selfTestIndex);
        Assert.True(readyIndex > markerIndex);
    }

    [Fact]
    public void BuildScriptRequiresTheNativeCurveExportSelfTestMarker()
    {
        var selfTestMarkerIndex = BuildScript.IndexOf("GODOT_ALS_CURVE_EXPORT_SELF_TEST_OK cases=16", StringComparison.Ordinal);
        var selfTestCheckIndex = BuildScript.IndexOf("Contains($curveSelfTestMarker", StringComparison.Ordinal);
        var readyMarkerIndex = BuildScript.IndexOf("GODOT_ALS_EXPORTER_READY", StringComparison.Ordinal);
        var readyCheckIndex = BuildScript.IndexOf("Contains($marker", StringComparison.Ordinal);

        Assert.True(selfTestMarkerIndex >= 0);
        Assert.True(selfTestCheckIndex > selfTestMarkerIndex);
        Assert.True(readyMarkerIndex > selfTestCheckIndex);
        Assert.True(readyCheckIndex > readyMarkerIndex);
    }

    [Fact]
    public void NativeCanonicalYawDerivationUsesDoublePrecisionRadiansAndReportsActualCaseCount()
    {
        var readerSource = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(),
            "tools", "unreal", "AlsGodotExporter", "Source", "AlsGodotExporter", "Private",
            "AlsAnimationMetadataReader.cpp"));

        Assert.Contains("UE_DOUBLE_PI / 180.0", readerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("* (PI / 180.0)", readerSource, StringComparison.Ordinal);
        Assert.Contains("RunCurveKeySelfTest(int32& OutCaseCount, FString& OutError)", readerSource, StringComparison.Ordinal);
        Assert.Contains("++OutCaseCount", readerSource, StringComparison.Ordinal);
    }
}
