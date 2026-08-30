$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$buildScriptPath = Join-Path $repositoryRoot 'scripts\build-als-exporter.ps1'
$buildScriptSource = [System.IO.File]::ReadAllText($buildScriptPath)

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
        $selfTestMarkerIndex = $buildScriptSource.IndexOf('GODOT_ALS_TIMELINE_EXPORT_SELF_TEST_OK cases=')
        $selfTestCheckIndex = $buildScriptSource.IndexOf('Contains($timelineSelfTestMarker')
        $readyMarkerIndex = $buildScriptSource.IndexOf('GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=2.0.0')
        $readyCheckIndex = $buildScriptSource.IndexOf('Contains($marker')

        $selfTestMarkerIndex | Should BeGreaterThan -1
        $selfTestCheckIndex | Should BeGreaterThan $selfTestMarkerIndex
        $readyMarkerIndex | Should BeGreaterThan $selfTestCheckIndex
        $readyCheckIndex | Should BeGreaterThan $readyMarkerIndex
    }
}
