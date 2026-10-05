$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$buildScriptPath = Join-Path $repositoryRoot 'scripts\build-als-exporter.ps1'
$buildScriptSource = [System.IO.File]::ReadAllText($buildScriptPath)
$verifyScriptPath = Join-Path $repositoryRoot 'scripts\verify-p2a.ps1'
$verifyScriptSource = [System.IO.File]::ReadAllText($verifyScriptPath)
$manifestWriterPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private\AlsManifestWriter.cpp'
$manifestWriterSource = [System.IO.File]::ReadAllText($manifestWriterPath)
$commandletPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private\AlsGodotExportCommandlet.cpp'
$commandletSource = [System.IO.File]::ReadAllText($commandletPath)
$gaspCommandletPath = Join-Path $repositoryRoot 'tools\unreal\AlsV4AssetExporter\Source\AlsV4AssetExporter\Private\AlsV4AssetExportCommandlet.cpp'
$gaspCommandletSource = [System.IO.File]::ReadAllText($gaspCommandletPath)
$registryPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private\AlsNotifyClassRegistry.cpp'
$registrySource = [System.IO.File]::ReadAllText($registryPath)
$animationReaderPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private\AlsAnimationMetadataReader.cpp'
$animationReaderSource = [System.IO.File]::ReadAllText($animationReaderPath)
$descriptorPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\AlsGodotExporter.uplugin'
$descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json
Describe 'ALS exporter build readiness gating' {
    It 'requires the native curve export self-test marker before the ready marker' {
        $selfTestMarkerIndex = $buildScriptSource.IndexOf('GODOT_ALS_CURVE_EXPORT_SELF_TEST_OK cases=16')
        $selfTestCheckIndex = $buildScriptSource.IndexOf('Contains($curveSelfTestMarker')
        $readyMarkerIndex = $buildScriptSource.IndexOf('GODOT_ALS_EXPORTER_READY')
        $readyCheckIndex = $buildScriptSource.IndexOf('Contains($marker')

        $selfTestMarkerIndex | Should BeGreaterThan -1
        $selfTestCheckIndex | Should BeGreaterThan $selfTestMarkerIndex
        $readyMarkerIndex | Should BeGreaterThan $selfTestCheckIndex
        $readyCheckIndex | Should BeGreaterThan $readyMarkerIndex
    }

    It 'requires the native timeline export self-test marker before the generic ready marker' {
        $selfTestMarkerIndex = $buildScriptSource.IndexOf('GODOT_ALS_TIMELINE_EXPORT_SELF_TEST_OK cases=26')
        $selfTestCheckIndex = $buildScriptSource.IndexOf('Contains($timelineSelfTestMarker')
        $readyMarkerIndex = $buildScriptSource.IndexOf("`$marker = 'GODOT_ALS_EXPORTER_READY'")
        $readyCheckIndex = $buildScriptSource.IndexOf('Contains($marker')

        $selfTestMarkerIndex | Should BeGreaterThan -1
        $selfTestCheckIndex | Should BeGreaterThan $selfTestMarkerIndex
        $readyMarkerIndex | Should BeGreaterThan $selfTestCheckIndex
        $readyCheckIndex | Should BeGreaterThan $readyMarkerIndex
    }

    It 'requires the native composite production-entry self-test marker before the generic ready marker' {
        $selfTestMarkerIndex = $buildScriptSource.IndexOf('GODOT_ALS_COMPOSITE_EXPORT_SELF_TEST_OK cases=1')
        $selfTestCheckIndex = $buildScriptSource.IndexOf('Contains($compositeSelfTestMarker')
        $readyMarkerIndex = $buildScriptSource.IndexOf("`$marker = 'GODOT_ALS_EXPORTER_READY'")
        $readyCheckIndex = $buildScriptSource.IndexOf('Contains($marker')

        $selfTestMarkerIndex | Should BeGreaterThan -1
        $selfTestCheckIndex | Should BeGreaterThan $selfTestMarkerIndex
        $readyMarkerIndex | Should BeGreaterThan $selfTestCheckIndex
        $readyCheckIndex | Should BeGreaterThan $readyMarkerIndex
    }

    It 'does not gate exporter readiness on a plugin version string' {
        $buildScriptSource | Should Match ([regex]::Escape("`$marker = 'GODOT_ALS_EXPORTER_READY'"))
        $verifyScriptSource | Should Match ([regex]::Escape("`$readyMarker = 'GODOT_ALS_EXPORTER_READY'"))
        $buildScriptSource | Should Not Match 'GODOT_ALS_EXPORTER_READY.*plugin='
        $verifyScriptSource | Should Not Match '\[string\]\$ReadyMarker|plugin=2\.0\.0|engine=\$engineVersion plugin='
    }

    It 'keeps producer metadata informational and readiness independent of the exporter version' {

        $descriptor.Version | Should Be 2
        $descriptor.VersionName | Should Be '2.0.0'
        $manifestWriterSource | Should Match ([regex]::Escape('WriteValue(TEXT("exporterVersion"), TEXT("2.0.0"))'))
        $buildScriptSource | Should Match ([regex]::Escape("`$marker = 'GODOT_ALS_EXPORTER_READY'"))
        $verifyScriptSource | Should Match ([regex]::Escape("`$readyMarker = 'GODOT_ALS_EXPORTER_READY'"))
        $buildScriptSource | Should Not Match 'GODOT_ALS_EXPORTER_READY.*plugin='
        $verifyScriptSource | Should Not Match '\[string\]\$ReadyMarker|plugin=2\.0\.0|engine=\$engineVersion plugin='
        $buildScriptSource | Should Match 'Get-AlsSupportedEngineVersion'
        $verifyScriptSource | Should Match 'Get-AlsSupportedEngineVersion'
        $verifyScriptSource | Should Match ([regex]::Escape("exporterVersion is missing."))
        $verifyScriptSource | Should Not Match ([regex]::Escape("exporterVersion -cne '2.0.0'"))
    }

    It 'audits animation semantics through the v2 timeline field' {
        $verifyScriptSource | Should Match ([regex]::Escape('@($_.metadata.timeline).Count'))
        $verifyScriptSource | Should Not Match ([regex]::Escape('@($_.metadata.notifies).Count'))
    }

    It 'validates exact GameplayTag domains and exercises production timeline entry points' {
        foreach ($prefix in @(
            'Als.LocomotionMode.',
            'Als.RotationMode.',
            'Als.Stance.',
            'Als.LocomotionAction.',
            'Als.GroundedEntryMode.'
        )) {
            $registrySource | Should Match ([regex]::Escape($prefix))
        }
        $registrySource | Should Match ([regex]::Escape('FAlsNotifyClassRegistry::Export(ActionNotifyEvent'))
        $registrySource | Should Match ([regex]::Escape('5bae929b17872885ecc5246f3d1e6a8a11ca1184'))
        $registrySource | Should Match ([regex]::Escape('Als.LocomotionAction.Mantling'))
        $animationReaderSource | Should Match ([regex]::Escape('NewObject<UAnimSequence>'))
        $animationReaderSource | Should Match ([regex]::Escape('NewObject<UAnimMontage>'))
        $animationReaderSource | Should Match ([regex]::Escape('ReadTimeline(*SequenceSelfTest'))
        $animationReaderSource | Should Match ([regex]::Escape('ReadTimeline(*MontageSelfTest'))
        $animationReaderSource | Should Match ([regex]::Escape('ExpectedCaseCount = 26'))
    }

    It 'locks exact final JSON IDs and permits NotifyName only for audit and fixture setup' {
        foreach ($expectedId in @(
            '256a3728fbc2e0cf1a311ccbc7f69a47b050d6e9',
            'edf169cea14fa4fc2033af2e2d96038491d734cf',
            '2db1fa5b4dfe5ca638c57f2c019f8e555467d352',
            'f3691e8a18d1bdf4b71ec03f5969a6252d1390da',
            'e15714690eca6dedcd52a9adbbf5ba5cb5211df5',
            'fbef40037b5759a889aeb69d70869d10cabf4bc4'
        )) {
            $animationReaderSource | Should Match ([regex]::Escape($expectedId))
        }

        $auditRead = 'NotifyEvent.GetNotifyEventName()'
        $fixtureWrite = 'SelfTestEvent.NotifyName = FName(FixtureLabel);'
        ([regex]::Matches($registrySource, [regex]::Escape($auditRead))).Count | Should Be 1
        $registrySource | Should Match ([regex]::Escape('void SetSelfTestEventDisplayName('))
        ([regex]::Matches($registrySource, [regex]::Escape($fixtureWrite))).Count | Should Be 1
        $registryWithoutAllowedDisplayNameAccess = $registrySource.Replace($auditRead, '').Replace($fixtureWrite, '')
        $registryWithoutAllowedDisplayNameAccess | Should Not Match 'NotifyName'
    }

    It 'requires Sequence markers plus Sequence Montage typed timeline evidence in both v2 manifests' {
        $verifyScriptSource | Should Match 'function Assert-P2AV2Manifest'
        $verifyScriptSource | Should Match ([regex]::Escape('Assert-P2AV2Manifest -Manifest $manifest -Label ''Partial'''))
        $verifyScriptSource | Should Match ([regex]::Escape('Assert-P2AV2Manifest -Manifest $formalManifest -Label ''Formal'''))
        $verifyScriptSource | Should Match ([regex]::Escape('contains no Sequence timeline entries'))
        $verifyScriptSource | Should Match ([regex]::Escape('contains no Montage timeline entries'))
        $verifyScriptSource | Should Match ([regex]::Escape('contains no sync markers'))
        $verifyScriptSource | Should Match ([regex]::Escape('contains no typed timeline events or actions'))
        $verifyScriptSource | Should Match ([regex]::Escape('foreach ($animationAsset in @($Manifest.animations) + @($Manifest.montages))'))
        $verifyScriptSource | Should Match ([regex]::Escape('foreach ($animationAsset in @($Manifest.animations))'))
        $verifyScriptSource | Should Match ([regex]::Escape("schemaVersion -ne 2"))
        $verifyScriptSource | Should Match ([regex]::Escape('exporterVersion is missing.'))
        $verifyScriptSource | Should Not Match ([regex]::Escape("exporterVersion -cne '2.0.0'"))
        $verifyScriptSource | Should Not Match ([regex]::Escape('curves or timeline entries'))
    }

    It 'preserves the original Unreal asset names and folders in exported file paths' {
        $discoveryPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private\AlsAssetDiscovery.cpp'
        $discoverySource = [System.IO.File]::ReadAllText($discoveryPath)

        $discoverySource | Should Match ([regex]::Escape('const FString AssetName = AssetData.AssetName.ToString();'))
        $discoverySource | Should Match ([regex]::Escape('PackagePath.Mid(ContentRoot.Len() + 1)'))
        $discoverySource | Should Match ([regex]::Escape('AssetName + TEXT(".") + Extension'))
        $commandletSource | Should Match ([regex]::Escape('FParse::Value(*Params, TEXT("ContentRoot="), ContentRoot)'))
        $commandletSource | Should Match ([regex]::Escape('Discover(ContentRoot, Assets, Error)'))
        $gaspCommandletSource | Should Match ([regex]::Escape('FString ContentRoot = TEXT("/Game/AdvancedLocomotionV4");'))
        $gaspCommandletSource | Should Match ([regex]::Escape('FParse::Value(*Params, TEXT("ContentRoot="), ContentRoot)'))
        $gaspCommandletSource | Should Match ([regex]::Escape('Discover(ContentRoot, Assets, Error)'))
        $gaspCommandletSource | Should Match ([regex]::Escape('WritePlanned(OutputDirectory, ContentRoot, Assets, Error)'))
        $gaspCommandletSource | Should Match ([regex]::Escape('WriteComplete(OutputDirectory, ContentRoot, Assets, Files, Error)'))
    }
}
