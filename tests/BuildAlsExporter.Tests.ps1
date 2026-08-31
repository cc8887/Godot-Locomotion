$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$buildScriptPath = Join-Path $repositoryRoot 'scripts\build-als-exporter.ps1'
$buildScriptSource = [System.IO.File]::ReadAllText($buildScriptPath)
$verifyScriptPath = Join-Path $repositoryRoot 'scripts\verify-p2a.ps1'
$verifyScriptSource = [System.IO.File]::ReadAllText($verifyScriptPath)
$manifestWriterPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private\AlsManifestWriter.cpp'
$manifestWriterSource = [System.IO.File]::ReadAllText($manifestWriterPath)
$commandletPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private\AlsGodotExportCommandlet.cpp'
$commandletSource = [System.IO.File]::ReadAllText($commandletPath)
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

    It 'requires the native timeline export self-test marker before the v2 ready marker' {
        $selfTestMarkerIndex = $buildScriptSource.IndexOf('GODOT_ALS_TIMELINE_EXPORT_SELF_TEST_OK cases=26')
        $selfTestCheckIndex = $buildScriptSource.IndexOf('Contains($timelineSelfTestMarker')
        $readyMarkerIndex = $buildScriptSource.IndexOf('GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=2.0.0')
        $readyCheckIndex = $buildScriptSource.IndexOf('Contains($marker')

        $selfTestMarkerIndex | Should BeGreaterThan -1
        $selfTestCheckIndex | Should BeGreaterThan $selfTestMarkerIndex
        $readyMarkerIndex | Should BeGreaterThan $selfTestCheckIndex
        $readyCheckIndex | Should BeGreaterThan $readyMarkerIndex
    }

    It 'keeps every v2 producer and consumer version declaration consistent' {
        $marker = 'GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=2.0.0'

        $descriptor.Version | Should Be 2
        $descriptor.VersionName | Should Be '2.0.0'
        $manifestWriterSource | Should Match ([regex]::Escape('WriteValue(TEXT("exporterVersion"), TEXT("2.0.0"))'))
        $commandletSource | Should Match ([regex]::Escape('plugin=2.0.0'))
        $buildScriptSource | Should Match ([regex]::Escape($marker))
        $verifyScriptSource | Should Match ([regex]::Escape($marker))
        $verifyScriptSource | Should Not Match ([regex]::Escape('plugin=1.0.0'))
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

    It 'requires independent Sequence Montage marker and typed timeline evidence in both v2 manifests' {
        $verifyScriptSource | Should Match 'function Assert-P2AV2Manifest'
        $verifyScriptSource | Should Match ([regex]::Escape('Assert-P2AV2Manifest -Manifest $manifest -Label ''Partial'''))
        $verifyScriptSource | Should Match ([regex]::Escape('Assert-P2AV2Manifest -Manifest $formalManifest -Label ''Formal'''))
        $verifyScriptSource | Should Match ([regex]::Escape('contains no Sequence timeline entries'))
        $verifyScriptSource | Should Match ([regex]::Escape('contains no Montage timeline entries'))
        $verifyScriptSource | Should Match ([regex]::Escape('contains no sync markers'))
        $verifyScriptSource | Should Match ([regex]::Escape('contains no typed timeline events or actions'))
        $verifyScriptSource | Should Match ([regex]::Escape("schemaVersion -ne 2"))
        $verifyScriptSource | Should Match ([regex]::Escape("exporterVersion -cne '2.0.0'"))
        $verifyScriptSource | Should Not Match ([regex]::Escape('curves or timeline entries'))
    }
}
