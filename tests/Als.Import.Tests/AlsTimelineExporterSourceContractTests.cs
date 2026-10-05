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
    private static readonly string VerifyP2A = File.ReadAllText(Path.Combine(
        Repository, "scripts", "verify-p2a.ps1"));

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
        Assert.DoesNotContain("GetDisplayNameTextByValue", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOneOf(", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("FStructProperty", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("FNameProperty", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("/Script/GameplayTags.GameplayTag", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"LocomotionAction\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("Action == TEXT(\"HighMantle\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("Action == TEXT(\"LowMantle\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("Action = TEXT(\"Mantling\")", RegistrySource, StringComparison.Ordinal);

        Assert.Contains("static bool Export(", RegistryHeader, StringComparison.Ordinal);
        Assert.Contains("bool FAlsNotifyClassRegistry::Export(", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("const FString& AssetStableId", RegistryHeader, StringComparison.Ordinal);
        Assert.Contains("int32 SourceIndex", RegistryHeader, StringComparison.Ordinal);
        Assert.Contains("FAlsExportedTimelineEntry& OutEntry", RegistryHeader, StringComparison.Ordinal);
        const string auditDisplayNameRead = "NotifyEvent.GetNotifyEventName()";
        const string fixtureDisplayNameWrite = "SelfTestEvent.NotifyName = FName(FixtureLabel);";
        Assert.Equal(1, RegistrySource.Split(auditDisplayNameRead, StringSplitOptions.None).Length - 1);
        Assert.Contains("void SetSelfTestEventDisplayName(", RegistrySource, StringComparison.Ordinal);
        Assert.Equal(1, RegistrySource.Split(fixtureDisplayNameWrite, StringSplitOptions.None).Length - 1);
        string registryWithoutAllowedDisplayNameAccess = RegistrySource
            .Replace(auditDisplayNameRead, string.Empty, StringComparison.Ordinal)
            .Replace(fixtureDisplayNameWrite, string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("NotifyName", registryWithoutAllowedDisplayNameAccess, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceClassPath.Contains", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"Unspecified\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"ViewDirection\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"LookingDirection\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"Recovering\")", RegistrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void SequenceAndMontageBothEmitTimeline()
    {
        Assert.Contains("ReadTimeline(*Sequence, Asset.Id, OutMetadata, OutError)", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("ReadTimeline(*Montage, Asset.Id, OutMetadata, OutError)", CompositeReader, StringComparison.Ordinal);
        Assert.Contains("SetArrayField(TEXT(\"timeline\")", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("SetArrayField(TEXT(\"syncMarkers\")", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("Sequence.GetPlayLength()", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("ValidateTimelineBounds", AnimationReader, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionMontageMetadataOmitsSequenceOnlySyncMarkers()
    {
        const string readTimeline =
            "if (!FAlsAnimationMetadataReader::ReadTimeline(*Montage, Asset.Id, OutMetadata, OutError))";
        const string removeSyncMarkers = "OutMetadata->RemoveField(TEXT(\"syncMarkers\"));";

        Assert.Contains(readTimeline, CompositeReader, StringComparison.Ordinal);
        Assert.Contains("return false;", CompositeReader[CompositeReader.IndexOf(readTimeline, StringComparison.Ordinal)..],
            StringComparison.Ordinal);
        Assert.Contains(removeSyncMarkers, CompositeReader, StringComparison.Ordinal);
        Assert.True(
            CompositeReader.IndexOf(removeSyncMarkers, StringComparison.Ordinal) >
            CompositeReader.IndexOf(readTimeline, StringComparison.Ordinal),
            "Montage sync markers must be removed only after timeline extraction succeeds.");
        Assert.DoesNotContain(
            "return FAlsAnimationMetadataReader::ReadTimeline(*Montage, Asset.Id, OutMetadata, OutError);",
            CompositeReader,
            StringComparison.Ordinal);

        Assert.Contains("MontageMetadata->TryGetArrayField(TEXT(\"syncMarkers\"), MontageMarkers)",
            AnimationReader, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeCompositeSelfTestCallsTheProductionReadEntryAndRejectsMontageSyncMarkers()
    {
        Assert.Contains("FAlsCompositeAssetReader::RunSelfTest", CompositeReader, StringComparison.Ordinal);
        Assert.Contains("FAssetData(MontageSelfTest)", CompositeReader, StringComparison.Ordinal);
        Assert.Contains("Read(Asset, Metadata, ReadError)", CompositeReader, StringComparison.Ordinal);
        Assert.Contains("Metadata->HasField(TEXT(\"syncMarkers\"))", CompositeReader, StringComparison.Ordinal);
        Assert.Contains("GODOT_ALS_COMPOSITE_EXPORT_SELF_TEST_OK cases=%d", CommandletSource,
            StringComparison.Ordinal);
        Assert.Contains("GODOT_ALS_COMPOSITE_EXPORT_SELF_TEST_OK cases=1", BuildScript,
            StringComparison.Ordinal);
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
        Assert.Contains("GODOT_ALS_TIMELINE_EXPORT_SELF_TEST_OK cases=26", BuildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void BlueprintEnumsUseExactPathAndRawTokens()
    {
        const string movementActionEnum =
            "/Game/AdvancedLocomotionV4/Data/Enums/ALS_MovementAction.ALS_MovementAction";

        Assert.Contains(movementActionEnum, RegistrySource, StringComparison.Ordinal);
        Assert.Contains("{TEXT(\"NewEnumerator0\"), TEXT(\"Mantling\")}", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("{TEXT(\"NewEnumerator1\"), TEXT(\"Mantling\")}", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("{TEXT(\"NewEnumerator2\"), TEXT(\"Rolling\")}", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("{TEXT(\"NewEnumerator3\"), TEXT(\"GettingUp\")}", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("{TEXT(\"NewEnumerator4\"), TEXT(\"None\")}", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("TEXT(\"NewEnumerator5\")", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("/Script/ALS.EAlsFootBone", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("/Script/Unknown.Future", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDisplayNameTextByValue", RegistrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeSelfTestCoversReviewBoundaries()
    {
        Assert.Contains("ExpectedCaseCount = 26", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("5bae929b17872885ecc5246f3d1e6a8a11ca1184", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("FAlsNotifyClassRegistry::Export(ActionNotifyEvent", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("Als.LocomotionAction.Mantling", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("NewObject<UAnimSequence>", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("NewObject<UAnimMontage>", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("ReadTimeline(*SequenceSelfTest", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("ReadTimeline(*MontageSelfTest", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("GetStringField(TEXT(\"stableEventId\")) != TEXT(\"256a3728fbc2e0cf1a311ccbc7f69a47b050d6e9\")",
            AnimationReader, StringComparison.Ordinal);
        Assert.Contains("GetStringField(TEXT(\"stableEventId\")) != TEXT(\"edf169cea14fa4fc2033af2e2d96038491d734cf\")",
            AnimationReader, StringComparison.Ordinal);
        Assert.Contains("GetStringField(TEXT(\"stableMarkerId\")) != TEXT(\"2db1fa5b4dfe5ca638c57f2c019f8e555467d352\")",
            AnimationReader, StringComparison.Ordinal);
        Assert.Contains("GetStringField(TEXT(\"stableEventId\")) != TEXT(\"f3691e8a18d1bdf4b71ec03f5969a6252d1390da\")",
            AnimationReader, StringComparison.Ordinal);
        Assert.Contains("GetStringField(TEXT(\"stableEventId\")) != TEXT(\"e15714690eca6dedcd52a9adbbf5ba5cb5211df5\")",
            AnimationReader, StringComparison.Ordinal);
        Assert.Contains("GetStringField(TEXT(\"stableMarkerId\")) != TEXT(\"fbef40037b5759a889aeb69d70869d10cabf4bc4\")",
            AnimationReader, StringComparison.Ordinal);
        Assert.Contains("ValidateTimelineBounds", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("TimelineEntryLess", AnimationReader, StringComparison.Ordinal);
        Assert.Contains("SyncMarkerLess", AnimationReader, StringComparison.Ordinal);
    }

    [Fact]
    public void GameplayTagsRequireExactFrozenDomains()
    {
        string[] exactPrefixes =
        [
            "Als.LocomotionMode.",
            "Als.RotationMode.",
            "Als.Stance.",
            "Als.LocomotionAction.",
            "Als.GroundedEntryMode.",
        ];
        foreach (var prefix in exactPrefixes)
        {
            Assert.Contains(prefix, RegistrySource, StringComparison.Ordinal);
        }

        Assert.Contains("ExpectedTagPrefix", RegistrySource, StringComparison.Ordinal);
        Assert.Contains("Als.LocomotionAction.Mantling", RegistrySource, StringComparison.Ordinal);
        Assert.DoesNotContain("FindLastChar(TEXT('.'), Separator)", RegistrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void P2AVerifierRequiresBothV2ManifestsAndIndependentTimelineEvidence()
    {
        Assert.Contains("function Assert-P2AV2Manifest", VerifyP2A, StringComparison.Ordinal);
        Assert.Contains("Assert-P2AV2Manifest -Manifest $manifest -Label 'Partial'", VerifyP2A, StringComparison.Ordinal);
        Assert.Contains("Assert-P2AV2Manifest -Manifest $formalManifest -Label 'Formal'", VerifyP2A, StringComparison.Ordinal);
        Assert.Contains("contains no Sequence timeline entries", VerifyP2A, StringComparison.Ordinal);
        Assert.Contains("contains no Montage timeline entries", VerifyP2A, StringComparison.Ordinal);
        Assert.Contains("contains no sync markers", VerifyP2A, StringComparison.Ordinal);
        Assert.Contains("contains no typed timeline events or actions", VerifyP2A, StringComparison.Ordinal);
        Assert.Contains("schemaVersion -ne 2", VerifyP2A, StringComparison.Ordinal);
        Assert.DoesNotContain("exporterVersion -cne '2.0.0'", VerifyP2A, StringComparison.Ordinal);
        Assert.Contains("exporterVersion is missing.", VerifyP2A, StringComparison.Ordinal);
        Assert.DoesNotContain("curves or timeline entries", VerifyP2A, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportPreservesOriginalUnrealAssetNamesAndContentRootFolders()
    {
        var discovery = ReadPrivateSource("AlsAssetDiscovery.cpp");

        Assert.Contains("const FString AssetName = AssetData.AssetName.ToString();", discovery, StringComparison.Ordinal);
        Assert.Contains("PackagePath.Mid(ContentRoot.Len() + 1)", discovery, StringComparison.Ordinal);
        Assert.Contains("AssetName + TEXT(\".\") + Extension", discovery, StringComparison.Ordinal);
        Assert.Contains("FParse::Value(*Params, TEXT(\"ContentRoot=\"), ContentRoot)", CommandletSource, StringComparison.Ordinal);
        Assert.Contains("Discover(ContentRoot, Assets, Error)", CommandletSource, StringComparison.Ordinal);
        Assert.Contains("WriteComplete(OutputDirectory, ContentRoot", CommandletSource, StringComparison.Ordinal);
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
    public void ReadyMarkerDoesNotPinExporterVersion()
    {
        Assert.Contains("GODOT_ALS_EXPORTER_READY", CommandletSource, StringComparison.Ordinal);
        Assert.Contains("GODOT_ALS_EXPORTER_READY", BuildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("engine=$engineVersion plugin=2.0.0", BuildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin=2.0.0", VerifyP2A, StringComparison.Ordinal);
        Assert.Contains("Get-AlsSupportedEngineVersion", BuildScript, StringComparison.Ordinal);
        Assert.Contains("Get-AlsSupportedEngineVersion", VerifyP2A, StringComparison.Ordinal);
    }

    private static string ReadPrivateSource(string fileName)
    {
        var path = Path.Combine(PrivateSource, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
