param(
    [Parameter(Mandatory)][string]$EngineRoot,
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string]$ReferenceRoot,
    [Parameter(Mandatory)][string]$TracePlan,
    [Parameter(Mandatory)][string]$ArtifactDirectory
)

BeforeAll {
    $script:runDirectory = Join-Path $ArtifactDirectory ('discovery-behavior-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:runDirectory | Out-Null
    $script:editor = Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
    $script:commonArguments = @(
        $Project, '-run=AlsLocomotionTrace', "-ReferenceRoot=$ReferenceRoot",
        '-ReferenceCommit=b754d6f0f2bb03741d301f8fb88077ebfe561e17',
        '-PatchHashes=3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f',
        "-P5ATracePlan=$TracePlan",
        "-P5ATracePlanSha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $TracePlan).Hash.ToLowerInvariant())",
        '-stdout', '-FullStdOutLogOutput', '-unattended', '-nosplash', '-nullrhi', '-nosound', '-Multiprocess'
    )
}

Describe 'P5A native diagnostic boundary' {
    It 'adds actual curve offsets to the default and treats missing curves as zero contribution' -Tag CurveSemantics {
        $output = Join-Path $script:runDirectory 'curve-ready-output.json'
        $log = Join-Path $script:runDirectory 'curve-ready.log'
        & $script:editor @script:commonArguments '-TraceKind=P5A' '-ReadyCheck' "-Output=$output" "-abslog=$log" | Out-Null
        $LASTEXITCODE | Should -Be 0
        Test-Path -LiteralPath $output | Should -BeFalse
        $text = Get-Content -LiteralPath $log -Raw
        $text | Should -Match 'P5A native curve semantics self-test passed checks=12'
        $text | Should -Match 'P5A native asset closure self-test passed rejected=6 positive=1'
        $markers = [regex]::Matches($text, 'P5A_[A-Z_]+')
        $markers.Count | Should -Be 1
        $markers[0].Value | Should -Be 'P5A_TRACE_READY_OK'
        $text | Should -Match 'P5A_TRACE_READY_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17'
    }

    It 'rejects unknown transient assets while accepting an audited dynamic montage wrapper' -Tag Closure {
        $output = Join-Path $script:runDirectory 'closure-ready-output.json'
        $log = Join-Path $script:runDirectory 'closure-ready.log'
        & $script:editor @script:commonArguments '-TraceKind=P5A' '-ReadyCheck' "-Output=$output" "-abslog=$log" | Out-Null
        $LASTEXITCODE | Should -Be 0
        Test-Path -LiteralPath $output | Should -BeFalse
        $text = Get-Content -LiteralPath $log -Raw
        $text | Should -Match 'P5A native asset closure self-test passed rejected=6 positive=1'
        $text | Should -Match 'P5A native curve semantics self-test passed checks=12'
        $markers = [regex]::Matches($text, 'P5A_[A-Z_]+')
        $markers.Count | Should -Be 1
        $markers[0].Value | Should -Be 'P5A_TRACE_READY_OK'
        $text | Should -Match 'P5A_TRACE_READY_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17'
    }

    It 'discovers all measured frames and auxiliary assets without emitting formal trace data' {
        $output = Join-Path $script:runDirectory 'discovery.json'
        $log = Join-Path $script:runDirectory 'discovery.log'
        & $script:editor @script:commonArguments '-TraceKind=P5ADiscovery' "-Output=$output" "-abslog=$log" | Out-Null
        $LASTEXITCODE | Should -Be 0
        Test-Path -LiteralPath $output | Should -BeTrue
        $data = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        $data.kind | Should -Be 'p5a_native_discovery_v1'
        $data.diagnosticOnly | Should -BeTrue
        $data.measuredFrameCount | Should -Be 374
        @($data.cases).Count | Should -Be 8
        @($data.cases.frames).Count | Should -Be 374
        $data.PSObject.Properties.Name | Should -Not -Contain 'nativeReferenceAudit'
        $data.PSObject.Properties.Name | Should -Not -Contain 'native_raw'
        $auxiliary = @($data.assets | Where-Object assetObjectPath -EQ '/ALS/ALS/Animations/Transitions/A_Als_Stand_Transition_Right.A_Als_Stand_Transition_Right')
        $auxiliary.Count | Should -Be 1
        $auxiliary[0].assetPackageSha256 | Should -Match '^[0-9a-f]{64}$'
        $auxiliary[0].inFrozenNativeInventory | Should -BeFalse
        @($auxiliary[0].notifies).Count | Should -Be 2
        @($auxiliary[0].notifies | Where-Object sourceIndex -EQ 0)[0].eventGuid | Should -Not -BeNullOrEmpty
        $roll = @($data.cases | Where-Object caseId -EQ 'roll_default_section')[0]
        $roll.frames[0].callbacks[0].callback | Should -Be 'MontageStarted'
        $ended = @($roll.frames.callbacks | Where-Object callback -EQ 'MontageEnded')
        $ended.Count | Should -Be 1
        $ended[0].interrupted | Should -BeFalse
        $ended[0].snapshot.blendComplete | Should -BeTrue
        $ended[0].snapshot.stopped | Should -BeTrue
        $ended[0].snapshot.desiredWeight | Should -Be 0
        $ended[0].snapshot.blendTimeRemainingSeconds | Should -Not -BeNullOrEmpty
        foreach ($frame in $data.cases.frames) {
            $frame.frameUpdateAudit.animationUpdates | Should -Be 1
            $frame.frameUpdateAudit.evaluations | Should -Be 1
            $frame.frameUpdateAudit.postUpdates | Should -Be 1
            $frame.frameUpdateAudit.meshTicks | Should -Be 1
            $frame.PSObject.Properties.Name | Should -Not -Contain 'nativeActual'
        }
        $text = Get-Content -LiteralPath $log -Raw
        $text | Should -Match 'P5A_NATIVE_DISCOVERY_COMPLETE cases=8 frames=374'
        $text | Should -Not -Match 'P5A_TRACE_(READY|GENERATION)_OK'
        Write-Host "DISCOVERY_BEHAVIOR_ARTIFACT=$output"
    }

    It 'captures the auxiliary footstep and real closing snapshot in two identical formal runs' -Tag Formal {
        foreach ($run in 1..2) {
            $output = Join-Path $script:runDirectory "formal-$run.json"
            $log = Join-Path $script:runDirectory "formal-$run.log"
            & $script:editor @script:commonArguments '-TraceKind=P5A' "-Output=$output" "-abslog=$log" | Out-Null
            $LASTEXITCODE | Should -Be 0
            $data = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
            $data.schemaVersion | Should -Be 2
            $data.representation | Should -Be 'native_raw'
            $data.snapshot.bindings.version | Should -Be 2
            $data.snapshot.bindings.digest | Should -Be 'e458fef4df7a854d'
            @($data.cases).Count | Should -Be 8
            @($data.cases.frames).Count | Should -Be 374
            @($data.nativeReferenceAudit.assets).Count | Should -Be 11
            @($data.nativeReferenceAudit.events).Count | Should -Be 19
            @($data.nativeReferenceAudit.auxiliaryAssets).Count | Should -Be 9
            @($data.nativeReferenceAudit.auxiliaryAssets.events).Count | Should -Be 8
            $transition = $data.cases[2].frames[1].nativeActual.canonicalAssetOracle
            ([single]$transition.graphCurveWeights.transition) | Should -Be ([single]0.083333336)
            ([single]$transition.curves.allowTransitions) | Should -Be ([single]0.9166667)
            ([single]$transition.compressedCurves.allowTransitions) | Should -Be ([single]0.9166667)
            $roll = @($data.cases | Where-Object caseId -EQ 'roll_default_section')[0]
            $footstep = @($roll.frames[101].nativeActual.nativeRuntimeTimeline | Where-Object observedEventStableId -EQ '31009e422b1cc9903e0d851dd26d730acd39e802')
            $footstep.Count | Should -Be 1
            $footstep[0].phase | Should -Be 'Trigger'
            $footstep[0].source.observedAssetObjectPath | Should -Be '/ALS/ALS/Animations/Transitions/A_Als_Stand_Transition_Right.A_Als_Stand_Transition_Right'
            $roll.frames[90].nativeActual.actionOutcomes[0].nativeReason | Should -Be 'Finished'
            ([single]$roll.frames[90].nativeActual.actionPlayback.currentMontageTimeSeconds) | Should -Be ([single]1.4999992847442627)
            $roll.frames[90].nativeActual.stateAfter.actionPlaying | Should -BeFalse
            foreach ($frame in $data.cases.frames) {
                $frame.nativeActual.frameUpdateAudit.animationUpdates | Should -Be 1
                $frame.nativeActual.frameUpdateAudit.evaluations | Should -Be 1
                $frame.nativeActual.frameUpdateAudit.postUpdates | Should -Be 1
                $frame.nativeActual.frameUpdateAudit.meshTicks | Should -Be 1
            }
            Write-Host "FORMAL_NATIVE_ARTIFACT=$output"
        }
        (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $script:runDirectory 'formal-1.json')).Hash |
            Should -Be (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $script:runDirectory 'formal-2.json')).Hash
    }

    It 'rejects ready-check requests in diagnostic mode without writing a document' {
        $output = Join-Path $script:runDirectory 'ready-must-not-exist.json'
        $log = Join-Path $script:runDirectory 'diagnostic-ready-rejection.log'
        & $script:editor @script:commonArguments '-TraceKind=P5ADiscovery' '-ReadyCheck' "-Output=$output" "-abslog=$log" | Out-Null
        $LASTEXITCODE | Should -Be 2
        Test-Path -LiteralPath $output | Should -BeFalse
        (Get-Content -LiteralPath $log -Raw) | Should -Not -Match 'P5A_TRACE_(READY|GENERATION)_OK'
    }

    It 'rejects invalid formal inventory before simulation: <Mutation>' -Tag Mutation -ForEach @(
        @{Mutation='missing-field'}, @{Mutation='missing-asset'}, @{Mutation='extra-asset'},
        @{Mutation='package-hash'}, @{Mutation='event-identity'}, @{Mutation='reordered'},
        @{Mutation='duplicate'}, @{Mutation='auxiliary-role'}, @{Mutation='mapped-action-impostor'},
        @{Mutation='schema-v1'}
    ) {
        $plan = Get-Content -LiteralPath $TracePlan -Raw | ConvertFrom-Json -AsHashtable
        switch ($Mutation) {
            'missing-field' { $plan.Remove('nativeAuditDependencies') }
            'missing-asset' { $plan.nativeAuditDependencies = @($plan.nativeAuditDependencies | Select-Object -Skip 1) }
            'extra-asset' { $plan.nativeAuditDependencies += $plan.nativeAuditDependencies[0].Clone() }
            'package-hash' { $plan.nativeAuditDependencies[0].assetPackageSha256 = '0' * 64 }
            'event-identity' { $plan.nativeAuditDependencies[4].events[0].stableEventId = '0' * 40 }
            'reordered' { $first=$plan.nativeAuditDependencies[0]; $plan.nativeAuditDependencies[0]=$plan.nativeAuditDependencies[1]; $plan.nativeAuditDependencies[1]=$first }
            'duplicate' { $plan.nativeAuditDependencies[1]=$plan.nativeAuditDependencies[0].Clone() }
            'auxiliary-role' { $plan.nativeAuditDependencies[0].nativeRole='action' }
            'mapped-action-impostor' {
                $aux = $plan.nativeAuditDependencies[0]
                foreach ($key in 'assetObjectPath','assetStableId','assetPackageSha256','assetClassPath') { $plan.sources[7].nativeVariants[0][$key]=$aux[$key] }
            }
            'schema-v1' { $plan.schemaVersion=1 }
        }
        $planPath = Join-Path $script:runDirectory "mutation-$Mutation.json"
        $serialized = ($plan | ConvertTo-Json -Depth 100).Replace("`r`n", "`n").TrimEnd("`r", "`n") + "`n"
        [IO.File]::WriteAllText($planPath, $serialized, [Text.UTF8Encoding]::new($false))
        $planHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $planPath).Hash.ToLowerInvariant()
        $arguments = @($script:commonArguments | Where-Object { $_ -notlike '-P5ATracePlan=*' -and $_ -notlike '-P5ATracePlanSha256=*' })
        $output = Join-Path $script:runDirectory "rejected-$Mutation-output.json"
        $log = Join-Path $script:runDirectory "mutation-$Mutation.log"
        & $script:editor @arguments '-TraceKind=P5A' '-ReadyCheck' "-P5ATracePlan=$planPath" "-P5ATracePlanSha256=$planHash" "-Output=$output" "-abslog=$log" | Out-Null
        $LASTEXITCODE | Should -Be 4
        Test-Path -LiteralPath $output | Should -BeFalse
        (Get-Content -LiteralPath $log -Raw) | Should -Not -Match 'P5A_TRACE_(READY|GENERATION)_OK'
    }
}
