using System.Text.Json;

namespace GodotAls.Import.Tests;

public sealed class AlsTimelineExporterSourceContractTests
{
    private static readonly string Repository = RepositoryRoot.Find();
    private static readonly string PrivateSource = Path.Combine(
        Repository, "tools", "unreal", "AlsGodotExporter", "Source", "AlsGodotExporter", "Private");
    private static readonly string ExportTypes = ReadPrivateSource("AlsExportTypes.h");
    private static readonly string RegistryHeader = ReadPrivateSource("AlsNotifyClassRegistry.h");
    private static readonly string RegistrySource = ReadPrivateSource("AlsNotifyClassRegistry.cpp");
    private static readonly string AnimationReader = ReadPrivateSource("AlsAnimationMetadataReader.cpp");
    private static readonly string CompositeReader = ReadPrivateSource("AlsCompositeAssetReader.cpp");
    private static readonly string ManifestWriter = ReadPrivateSource("AlsManifestWriter.cpp");
    private static readonly string CommandletSource = ReadPrivateSource("AlsGodotExportCommandlet.cpp");
    private static readonly string BuildScript = File.ReadAllText(Path.Combine(
        Repository, "scripts", "build-als-exporter.ps1"));

    [Fact]
    public void ExporterDeclaresEveryTypedTimelineField()
    {
        Assert.Contains("struct FAlsExportedTimelineEntry", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("FString StableEventId;", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("FString Kind;", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("FString SourceClassPath;", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("FString DisplayName;", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("double TimeSeconds{0.0};", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("double DurationSeconds{0.0};", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("double TriggerWeightThreshold{0.0};", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("FString TickMode;", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("int32 SourceIndex{INDEX_NONE};", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("int32 TrackIndex{INDEX_NONE};", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("TSharedPtr<FJsonObject> Payload;", ExportTypes, StringComparison.Ordinal);

        Assert.Contains("struct FAlsExportedSyncMarker", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("FString StableMarkerId;", ExportTypes, StringComparison.Ordinal);
        Assert.Contains("FString Name;", ExportTypes, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryUsesExplicitClassAliasesWithoutDisplayNameInference()
    {
        string[] approvedKinds =
        [
            "Generic",
            "Footstep",
            "SetAction",
            "SetGroundedEntry",
            "EarlyBlendOut",
            "RootMotionScale",
        ];

        foreach (var kind in approvedKinds)
        {
            Assert.Contains($"TEXT(\"{kind}\")", RegistrySource, StringComparison.Ordinal);
        }

        string[] payloadFields =
        [
            "foot",
            "action",
            "mode",
            "blendOutSeconds",
            "checkInput",
            "checkLocomotionMode",
            "locomotionMode",
            "checkRotationMode",
            "rotationMode",
            "checkStance",
            "stance",
            "translationScale",
        ];
        foreach (var field in payloadFields)
        {
            Assert.Contains($"TEXT(\"{field}\")", RegistrySource, StringComparison.Ordinal);
        }

        Assert.Contains(
            "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/MovementAction_NotifyState.MovementAction_NotifyState_C",
            RegistrySource,
            StringComparison.Ordinal);
        Assert.Contains(
            "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/Footstep_AnimNotify.Footstep_AnimNotify_C",
            RegistrySource,
            StringComparison.Ordinal);
        Assert.Contains(
            "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/GroundedEntryState_AnimNotify.GroundedEntryState_AnimNotify_C",
            RegistrySource,
            StringComparison.Ordinal);
        Assert.Contains(
            "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/EarlyBlendOut_NotifyState.EarlyBlendOut_NotifyState_C",
            RegistrySource,
            StringComparison.Ordinal);
        Assert.Contains("/Script/ALS.AlsAnimNotify_FootstepEffects", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("/Script/ALS.AlsAnimNotifyState_SetLocomotionAction", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("/Script/ALS.AlsAnimNotify_SetGroundedEntryMode", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("/Script/ALS.AlsAnimNotifyState_EarlyBlendOut", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("/Script/ALS.AlsAnimNotifyState_SetRootMotionScale", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("/Script/ALS.AlsAnimNotifyState_SetAction", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("/Script/ALS.AlsAnimNotifyState_MovementAction", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("/Script/ALS.AlsAnimNotifyState_RootMotionScale", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("CameraShake_Notify.CameraShake_Notify_C", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("OverlayOverride_NotifyState.OverlayOverride_NotifyState_C", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("GetDisplayNameTextByValue", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOneOf(", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("FStructProperty", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("FNameProperty", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"LocomotionAction\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("Action.StartsWith(TEXT(\"NewEnumerator\"))", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("Action == TEXT(\"HighMantle\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("Action == TEXT(\"LowMantle\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("Action = TEXT(\"Mantling\")", RegistrySource, StringComparison.Ordinal);

        Assert.Contains("static bool Export(", RegistryHeader, StringComparison.Ordinal);
        Assert.Contains("bool FAlsNotifyClassRegistry::Export(", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("const FString& AssetStableId", RegistryHeader, StringComparison.Ordinal);
        Assert.Contains("int32 SourceIndex", RegistryHeader, StringComparison.Ordinal);
        Assert.Contains("FAlsExportedTimelineEntry& OutEntry", RegistryHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("NotifyName", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceClassPath.Contains", RegistrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void SequenceAndMontageBothEmitTimeline()
    {
        Assert.Contains("ReadTimeline(*Sequence, Asset.Id, OutMetadata, OutError)", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("ReadTimeline(*Montage, Asset.Id, OutMetadata, OutError)", CompositeReader, StringComparison.Ordinal);
        Assert.Contains("SetArrayField(TEXT(\"timeline\")", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("SetArrayField(TEXT(\"syncMarkers\")", AnimationReader, StringComparison.Ordinal);
    }

    [Fact]
    public void StableIdsIncludeSourceIndexAndClassPath()
    {
        Assert.Contains("TEXT(\"%s|timeline|%d|%s\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("*AssetStableId, SourceIndex, *OutEntry.SourceClassPath", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"%s|marker|%d|%s\")", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("*AssetStableId, SourceIndex, *Marker.Name", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("FSHA1", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("ToLowerInline", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("IsAssetReferenceField", ManifestWriter, StringComparison.Ordinal);
        Assert.Contains("FieldName != TEXT(\"stableEventId\")", ManifestWriter, StringComparison.Ordinal);
        Assert.Contains("FieldName != TEXT(\"stableMarkerId\")", ManifestWriter, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeTickModesMapExplicitly()
    {
        Assert.Contains("EMontageNotifyTickType::Queued", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("EMontageNotifyTickType::BranchingPoint", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"Queued\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"BranchingPoint\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("RunTimelineSelfTest", CommandletSource, StringComparison.Ordinal);
        Assert.Contains("GODOT_ALS_TIMELINE_EXPORT_SELF_TEST_OK cases=%d", CommandletSource, StringComparison.Ordinal);
        Assert.Contains("GODOT_ALS_TIMELINE_EXPORT_SELF_TEST_OK cases=", BuildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void NameNoneSectionNormalizesToEmptyString()
    {
        Assert.Contains("Section.NextSectionName.IsNone() ? FString() : Section.NextSectionName.ToString()",
            CompositeReader, StringComparison.Ordinal);
        Assert.DoesNotContain("SetStringField(TEXT(\"nextSection\"), Section.NextSectionName.ToString())",
            CompositeReader, StringComparison.Ordinal);
    }

    [Fact]
    public void DescriptorDeclaresExporterVersionTwo()
    {
        using var descriptor = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Repository, "tools", "unreal", "AlsGodotExporter", "AlsGodotExporter.uplugin")));

        Assert.Equal(2, descriptor.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal("2.0.0", descriptor.RootElement.GetProperty("VersionName").GetString());
        Assert.Contains("WriteValue(TEXT(\"schemaVersion\"), 2)", ManifestWriter, StringComparison.Ordinal);
        Assert.Contains("WriteValue(TEXT(\"exporterVersion\"), TEXT(\"2.0.0\"))", ManifestWriter, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadyMarkerDeclaresExporterVersionTwo()
    {
        const string marker = "GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=2.0.0";

        Assert.Contains("plugin=2.0.0", CommandletSource, StringComparison.Ordinal);
        Assert.Contains(marker, BuildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin=1.0.0", CommandletSource, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin=1.0.0", BuildScript, StringComparison.Ordinal);
    }

    private static string ReadPrivateSource(string fileName)
    {
        var path = Path.Combine(PrivateSource, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
