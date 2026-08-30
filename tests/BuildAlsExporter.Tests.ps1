$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$buildScriptPath = Join-Path $repositoryRoot 'scripts\build-als-exporter.ps1'
$buildScriptSource = [System.IO.File]::ReadAllText($buildScriptPath)
$verifyScriptPath = Join-Path $repositoryRoot 'scripts\verify-p2a.ps1'
$verifyScriptSource = [System.IO.File]::ReadAllText($verifyScriptPath)
$manifestWriterPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private\AlsManifestWriter.cpp'
$manifestWriterSource = [System.IO.File]::ReadAllText($manifestWriterPath)
$commandletPath = Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private\AlsGodotExportCommandlet.cpp'
$commandletSource = [System.IO.File]::ReadAllText($commandletPath)
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
        $selfTestMarkerIndex = $buildScriptSource.IndexOf('GODOT_ALS_TIMELINE_EXPORT_SELF_TEST_OK cases=22')
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
}
