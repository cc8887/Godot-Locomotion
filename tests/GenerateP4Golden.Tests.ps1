$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$schemaPath = Join-Path $repositoryRoot 'tools/schemas/als_pose_trace.schema.json'
$generatorPath = Join-Path $repositoryRoot 'scripts/generate-p4-golden.ps1'
$fixtureDirectory = Join-Path $repositoryRoot 'tests/Als.Core.Tests/Fixtures/P4'
$lockedCommit = 'b754d6f0f2bb03741d301f8fb88077ebfe561e17'
$lockedPatchHash = '3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f'

Describe 'generate-p4-golden.ps1 contract' {
    BeforeAll {
        . $generatorPath
        $script:realInvokeP4Commandlet = (Get-Command Invoke-P4Commandlet).ScriptBlock
        $script:realWriteP4PortOracle = (Get-Command Write-P4PortOracle).ScriptBlock
        $script:realAssertP4Schema = (Get-Command Assert-P4Schema).ScriptBlock
        $script:realAssertP4RunsIdentical = (Get-Command Assert-P4RunsIdentical).ScriptBlock
        $script:realPublishP4FixtureDirectory = (Get-Command Publish-P4FixtureDirectory).ScriptBlock
        function Copy-P4ValidatedSource([string]$Destination) {
            Copy-Item -LiteralPath $fixtureDirectory -Destination $Destination -Recurse
        }
    }

    It 'exposes production orchestration without public test bypass parameters' {
        Get-Command Invoke-P4Generation -ErrorAction Stop | Should Not BeNullOrEmpty
        $parameters = (Get-Command $generatorPath).Parameters.Keys
        ($parameters -contains 'ValidatedOutputForTests') | Should Be $false
        ($parameters -contains 'FixtureDirectoryForTests') | Should Be $false
    }

    It 'publishes the strict locked schema and exact case matrix' {
        Test-Path -LiteralPath $schemaPath -PathType Leaf | Should Be $true
        Test-Path -LiteralPath $generatorPath -PathType Leaf | Should Be $true
        Test-Path -LiteralPath $fixtureDirectory -PathType Container | Should Be $true

        $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
        $schema.'$schema' | Should Be 'https://json-schema.org/draft/2020-12/schema'
        $schema.additionalProperties | Should Be $false

        $files = @(Get-ChildItem -LiteralPath $fixtureDirectory -Filter '*.json' -File)
        @($files.Name | Sort-Object) | Should Be @(
            'trace_p4_aim.json',
            'trace_p4_feet.json',
            'trace_p4_platform.json',
            'trace_p4_rotate.json',
            'trace_p4_turn.json')

        & $script:realAssertP4Schema $fixtureDirectory $schemaPath

        $cases = @($files | ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).cases })
        $cases.Count | Should Be 25
        @($cases | Where-Object category -ceq 'Aim').Count | Should Be 5
        @($cases | Where-Object category -ceq 'Turn').Count | Should Be 8
        @($cases | Where-Object category -ceq 'Rotate').Count | Should Be 4
        @($cases | Where-Object category -ceq 'Feet').Count | Should Be 3
        @($cases | Where-Object category -ceq 'Platform').Count | Should Be 5
        @($cases | Where-Object provenance -cne 'port_oracle_v1').Count | Should Be 0
        @($cases | Where-Object {
            $_.stimulus.platformRemovalSignals.leftRemoved -eq 1 -or
            $_.stimulus.platformRemovalSignals.rightRemoved -eq 1 }).caseId |
            Should Be @('platform_release')
    }

    It 'rejects unknown missing discrete and source path mutations through the schema' {
        $mutations = @(
            @{ Name = 'unknown'; Apply = { param($document) Add-Member -InputObject $document -NotePropertyName unexpected -NotePropertyValue $true } },
            @{ Name = 'missing'; Apply = { param($document) $document.cases[0].PSObject.Properties.Remove('phase') } },
            @{ Name = 'discrete'; Apply = { param($document) $document.cases[0].portExpected.leftReleaseReason = 'InventedReason' } },
            @{ Name = 'source'; Apply = { param($document) $document.sources.animationBlueprint = '/Wrong/AB' } },
            @{ Name = 'duplicate-id'; Apply = { param($document) $document.cases[1].caseId = $document.cases[0].caseId } },
            @{ Name = 'replacement-id'; Apply = { param($document) $document.cases[0].caseId = 'aim_replacement' } })

        foreach ($mutation in $mutations) {
            $directory = Join-Path $TestDrive "schema-$($mutation.Name)"
            Copy-Item -LiteralPath $fixtureDirectory -Destination $directory -Recurse
            $path = Join-Path $directory 'trace_p4_aim.json'
            $document = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            & $mutation.Apply $document
            Set-Content -LiteralPath $path -Encoding UTF8 -Value ($document | ConvertTo-Json -Depth 50)

            $failure = $null
            try { & $script:realAssertP4Schema $directory $schemaPath }
            catch { $failure = $_.Exception.Message }
            $failure | Should Not BeNullOrEmpty
        }
    }

    It 'rejects platform removal signals that do not match the transition case' {
        $mutations = @(
            @{ Name = 'release-without-signal'; File = 'trace_p4_platform.json'; CaseId = 'platform_release'; Value = 0 },
            @{ Name = 'non-release-with-signal'; File = 'trace_p4_platform.json'; CaseId = 'platform_translate'; Value = 1 })

        foreach ($mutation in $mutations) {
            $directory = Join-Path $TestDrive "signal-$($mutation.Name)"
            Copy-Item -LiteralPath $fixtureDirectory -Destination $directory -Recurse
            $path = Join-Path $directory $mutation.File
            $document = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            $traceCase = @($document.cases | Where-Object caseId -CEQ $mutation.CaseId)
            $traceCase.Count | Should Be 1
            $traceCase[0].stimulus.platformRemovalSignals.leftRemoved = $mutation.Value
            Set-Content -LiteralPath $path -Encoding UTF8 -Value ($document | ConvertTo-Json -Depth 50)

            $mutated = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            (@($mutated.cases | Where-Object caseId -CEQ $mutation.CaseId)[0].stimulus.platformRemovalSignals.leftRemoved) |
                Should Be $mutation.Value
            $failure = $null
            try { & $script:realAssertP4Schema $directory $schemaPath }
            catch { $failure = $_.Exception.Message }
            $failure | Should Not BeNullOrEmpty
        }
    }

    It 'rejects missing raw output and byte drift without changing official fixtures' {
        $officialHashBefore = @(Get-ChildItem $fixtureDirectory -File | Sort-Object Name |
            ForEach-Object { (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash }) -join '|'

        $raw = Join-Path $TestDrive 'raw-missing'
        Copy-Item -LiteralPath $fixtureDirectory -Destination $raw -Recurse
        Remove-Item -LiteralPath (Join-Path $raw 'trace_p4_turn.json')
        { & $script:realWriteP4PortOracle $repositoryRoot $raw (Join-Path $TestDrive 'oracle-missing') } |
            Should Throw "P4 commandlet output is missing 'trace_p4_turn.json'."

        $runA = Join-Path $TestDrive 'drift-a'
        $runB = Join-Path $TestDrive 'drift-b'
        Copy-Item -LiteralPath $fixtureDirectory -Destination $runA -Recurse
        Copy-Item -LiteralPath $fixtureDirectory -Destination $runB -Recurse
        Add-Content -LiteralPath (Join-Path $runB 'trace_p4_aim.json') -Value ' '
        { & $script:realAssertP4RunsIdentical $runA $runB } |
            Should Throw 'P4 consecutive runs are not byte-for-byte identical: trace_p4_aim.json'

        $officialHashAfter = @(Get-ChildItem $fixtureDirectory -File | Sort-Object Name |
            ForEach-Object { (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash }) -join '|'
        $officialHashAfter | Should Be $officialHashBefore
    }

    It 'locks reference provenance and keeps native observations separate from the port oracle' {
        $documents = @(Get-ChildItem -LiteralPath $fixtureDirectory -Filter '*.json' -File |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })

        foreach ($document in $documents) {
            $document.reference.commit | Should Be $lockedCommit
            $document.reference.targetEngine | Should Be '5.9.0'
            @($document.reference.patchHashes) | Should Be @($lockedPatchHash)
            $document.nativeProvenance | Should Be 'als_runtime'
            $document.portProvenance | Should Be 'port_oracle_v1'
            foreach ($case in $document.cases) {
                $case.nativeActual | Should Not BeNullOrEmpty
                $case.portExpected | Should Not BeNullOrEmpty
            }
        }
    }

    It 'uses reference preparation unique runs schema validation byte comparison and atomic publication' {
        $source = Get-Content -LiteralPath $generatorPath -Raw
        $source | Should Match 'prepare-p3-reference\.ps1'
        $source | Should Match '\[guid\]::NewGuid'
        $source | Should Match 'Assert-P4Schema'
        $source | Should Match 'Assert-P4RunsIdentical'
        $source | Should Match 'Publish-P4FixtureDirectory'
        $source | Should Match 'P4_GOLDEN_OK'
    }

    It 'orchestrates preparation two isolated native runs two schema checks one comparison and publication last' {
        $script:p4Calls = [System.Collections.Generic.List[string]]::new()
        Mock Assert-P4ReferenceLock { $script:p4Calls.Add('lock') }
        Mock Invoke-ReferencePreparation { $script:p4Calls.Add('prepare') }
        Mock Invoke-P3ReadyCheck { $script:p4Calls.Add('ready') }
        Mock Initialize-P4SchemaValidator { $script:p4Calls.Add('validator') }
        Mock Invoke-P4Commandlet {
            param($Editor, $Project, $Reference, $OutputDirectory, $LogPath)
            $script:p4Calls.Add("native:$([System.IO.Path]::GetFileName($OutputDirectory))")
        }
        Mock Write-P4PortOracle {
            param($Root, $RawDirectory, $OutputDirectory)
            $script:p4Calls.Add("oracle:$([System.IO.Path]::GetFileName($RawDirectory))")
        }
        Mock Assert-P4Schema {
            param($Directory, $SchemaPath)
            $script:p4Calls.Add("schema:$([System.IO.Path]::GetFileName($Directory))")
        }
        Mock Assert-P4RunsIdentical { $script:p4Calls.Add('compare') }
        Mock Publish-P4FixtureDirectory { $script:p4Calls.Add('publish') }

        Invoke-P4Generation $TestDrive $TestDrive 'fake-editor' (Join-Path $TestDrive 'Fake.uproject') | Should Be 'P4_GOLDEN_OK'
        @($script:p4Calls) | Should Be @(
            'lock', 'prepare', 'ready', 'validator', 'native:raw-a', 'native:raw-b',
            'oracle:raw-a', 'oracle:raw-b', 'schema:run-a', 'schema:run-b', 'compare', 'publish')
        Assert-MockCalled Invoke-P4Commandlet -Times 2 -Exactly
        Assert-MockCalled Assert-P4Schema -Times 2 -Exactly
        Assert-MockCalled Assert-P4RunsIdentical -Times 1 -Exactly
        Assert-MockCalled Publish-P4FixtureDirectory -Times 1 -Exactly
        Assert-MockCalled Initialize-P4SchemaValidator -Times 1 -Exactly
    }

    It 'never publishes when preparation native schema or byte comparison fails' {
        Mock Assert-P4ReferenceLock {}
        Mock Invoke-ReferencePreparation {
            if ($script:p4FailureStage -eq 'prepare') { throw 'injected prepare failure' }
        }
        Mock Invoke-P3ReadyCheck {}
        Mock Invoke-P4Commandlet {
            if ($script:p4FailureStage -eq 'native') { throw 'injected native failure' }
        }
        Mock Write-P4PortOracle {}
        Mock Assert-P4Schema {
            if ($script:p4FailureStage -eq 'schema') { throw 'injected schema failure' }
        }
        Mock Assert-P4RunsIdentical {
            if ($script:p4FailureStage -eq 'compare') { throw 'injected compare failure' }
        }
        Mock Publish-P4FixtureDirectory {
            param($ValidatedOutput, $FixtureDirectory)
            Set-Content -LiteralPath (Join-Path $FixtureDirectory 'published') -Value 'unexpected'
        }

        foreach ($stage in @('prepare', 'native', 'schema', 'compare')) {
            $script:p4FailureStage = $stage
            $root = Join-Path $TestDrive "orchestration-$stage"
            $official = Join-Path $root 'tests\Als.Core.Tests\Fixtures\P4'
            New-Item -ItemType Directory -Path $official -Force | Out-Null
            $sentinel = Join-Path $official 'official.json'
            Set-Content -LiteralPath $sentinel -NoNewline -Value 'official'
            $before = (Get-FileHash -Algorithm SHA256 -LiteralPath $sentinel).Hash

            $failure = $null
            try { Invoke-P4Generation $root $root 'fake-editor' (Join-Path $root 'fake.uproject') }
            catch { $failure = $_.Exception.Message }

            $failure | Should Be "injected $stage failure"
            (Get-FileHash -Algorithm SHA256 -LiteralPath $sentinel).Hash | Should Be $before
            Test-Path -LiteralPath (Join-Path $official 'published') | Should Be $false
        }
        $script:p4FailureStage = $null
    }

    It 'requires exactly one ordinal whole-line commandlet marker' {
        $fakeRoot = Join-Path $TestDrive 'fake-editor'
        New-Item -ItemType Directory -Path $fakeRoot -Force | Out-Null
        $shim = Join-Path $fakeRoot 'fake.cmd'
        $body = Join-Path $fakeRoot 'fake.ps1'
        Set-Content -LiteralPath $shim -Encoding Ascii -Value @(
            '@echo off',
            '@powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake.ps1" %*',
            '@exit /b %errorlevel%')
        Set-Content -LiteralPath $body -Encoding UTF8 -Value @'
param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
$log = ($Arguments | Where-Object { $_ -like '-abslog=*' } | Select-Object -First 1) -replace '^-abslog=', ''
if ($env:P4_FAKE_EXIT) { exit [int]$env:P4_FAKE_EXIT }
$markers = if ($env:P4_FAKE_MARKER) { $env:P4_FAKE_MARKER -split '\|' } else { @('wrong marker') }
Set-Content -LiteralPath $log -Value $markers
'@
        $previousExit = $env:P4_FAKE_EXIT
        $previousMarker = $env:P4_FAKE_MARKER
        try {
            $env:P4_FAKE_EXIT = '7'
            $failure = $null
            try {
                & $script:realInvokeP4Commandlet $shim 'fake.uproject' 'reference' (Join-Path $fakeRoot 'raw') (Join-Path $fakeRoot 'nonzero.log')
            }
            catch { $failure = $_.Exception.Message }
            $failure | Should Match 'failed with exit code'
            $env:P4_FAKE_EXIT = $null
            $exact = "P4_TRACE_GENERATION_OK cases=25 commit=$lockedCommit"
            foreach ($invalid in @(
                'wrong marker',
                "prefix $exact",
                "$exact suffix",
                "$exact|$exact")) {
                $env:P4_FAKE_MARKER = $invalid
                $failure = $null
                try {
                    & $script:realInvokeP4Commandlet $shim 'fake.uproject' 'reference' (Join-Path $fakeRoot 'raw') (Join-Path $fakeRoot 'marker.log')
                }
                catch { $failure = $_.Exception.Message }
                $failure | Should Match 'did not emit its exact success marker'
            }
            $env:P4_FAKE_MARKER = $exact
            & $script:realInvokeP4Commandlet $shim 'fake.uproject' 'reference' (Join-Path $fakeRoot 'raw') (Join-Path $fakeRoot 'marker.log')

            $uePrefix = '[2026.08.29-12.13.50:123][  0]LogTemp: Display: '
            $env:P4_FAKE_MARKER = "$uePrefix$exact"
            & $script:realInvokeP4Commandlet $shim 'fake.uproject' 'reference' (Join-Path $fakeRoot 'raw') (Join-Path $fakeRoot 'ue-prefix.log')

            foreach ($invalidUeMarker in @(
                "$uePrefix$exact suffix",
                "$uePrefix$exact|$uePrefix$exact")) {
                $env:P4_FAKE_MARKER = $invalidUeMarker
                $failure = $null
                try {
                    & $script:realInvokeP4Commandlet $shim 'fake.uproject' 'reference' (Join-Path $fakeRoot 'raw') (Join-Path $fakeRoot 'ue-invalid.log')
                }
                catch { $failure = $_.Exception.Message }
                $failure | Should Match 'did not emit its exact success marker'
            }
        }
        finally {
            $env:P4_FAKE_EXIT = $previousExit
            $env:P4_FAKE_MARKER = $previousMarker
        }
    }

    It 'preserves the previous fixture directory when atomic publication fails' {
        $testRoot = Join-Path $TestDrive 'publication'
        $source = Join-Path $testRoot 'source'
        $destination = Join-Path $testRoot 'official'
        Copy-P4ValidatedSource $source
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $destination 'official.json') -Value 'official' -NoNewline

        $previous = $env:GODOTALS_P4_GENERATOR_FAIL_STAGE
        try {
            $env:GODOTALS_P4_GENERATOR_FAIL_STAGE = 'after-backup'
            { & $script:realPublishP4FixtureDirectory $source $destination } | Should Throw 'Injected P4 publication failure after backup.'
        }
        finally {
            $env:GODOTALS_P4_GENERATOR_FAIL_STAGE = $previous
        }

        Test-Path -LiteralPath (Join-Path $destination 'official.json') -PathType Leaf | Should Be $true
        (Get-Content -LiteralPath (Join-Path $destination 'official.json') -Raw) | Should Be 'official'
        Test-Path -LiteralPath (Join-Path $destination 'trace_p4_aim.json') | Should Be $false
    }

    It 'publishes or rolls back for destination existence and every transaction stage without residue' {
        foreach ($destinationExists in @($false, $true)) {
            foreach ($stage in @('success', 'after-backup', 'after-publish')) {
                $caseRoot = Join-Path $TestDrive "atomic-$destinationExists-$stage"
                $source = Join-Path $caseRoot 'source'
                $destination = Join-Path $caseRoot 'official'
                Copy-P4ValidatedSource $source
                if ($destinationExists) {
                    New-Item -ItemType Directory -Path $destination -Force | Out-Null
                    Set-Content -LiteralPath (Join-Path $destination 'old.json') -Value 'old' -NoNewline
                }
                $previous = $env:GODOTALS_P4_GENERATOR_FAIL_STAGE
                try {
                    $env:GODOTALS_P4_GENERATOR_FAIL_STAGE = if ($stage -eq 'success') { $null } else { $stage }
                    if ($stage -eq 'success') {
                        & $script:realPublishP4FixtureDirectory $source $destination
                    }
                    else {
                        $expectedMessage = if ($stage -eq 'after-backup') {
                            'Injected P4 publication failure after backup.'
                        }
                        else {
                            'Injected P4 publication failure after publish.'
                        }
                        { & $script:realPublishP4FixtureDirectory $source $destination } | Should Throw $expectedMessage
                    }
                }
                finally { $env:GODOTALS_P4_GENERATOR_FAIL_STAGE = $previous }

                if ($stage -eq 'success') {
                    @(Get-ChildItem -LiteralPath $destination -File).Count | Should Be 5
                    Test-Path -LiteralPath (Join-Path $destination 'trace_p4_aim.json') | Should Be $true
                    Test-Path -LiteralPath (Join-Path $destination 'old.json') | Should Be $false
                }
                elseif ($destinationExists) {
                    (Get-Content -LiteralPath (Join-Path $destination 'old.json') -Raw) | Should Be 'old'
                    Test-Path -LiteralPath (Join-Path $destination 'trace_p4_aim.json') | Should Be $false
                }
                else {
                    Test-Path -LiteralPath $destination | Should Be $false
                }
                @(Get-ChildItem -LiteralPath $caseRoot -Directory -Filter '.official.p4-*').Count | Should Be 0
            }
        }
    }

    It 'recovers a destination backup left between the two publication moves' {
        $caseRoot = Join-Path $TestDrive 'crash-recovery'
        $destination = Join-Path $caseRoot 'official'
        $backup = Join-Path $caseRoot '.official.p4-backup'
        $staging = Join-Path $caseRoot '.official.p4-staging.0123456789abcdef0123456789abcdef'
        New-Item -ItemType Directory -Path $backup, $staging -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $backup 'old.json') -Value 'old' -NoNewline
        Set-Content -LiteralPath (Join-Path $staging 'partial.json') -Value 'partial' -NoNewline

        Repair-P4FixturePublication $destination

        (Get-Content -LiteralPath (Join-Path $destination 'old.json') -Raw) | Should Be 'old'
        Test-Path -LiteralPath $backup | Should Be $false
        Test-Path -LiteralPath $staging | Should Be $false
    }

    It 'rejects a reparse-point publication parent without following it' {
        $caseRoot = Join-Path $TestDrive 'reparse-publication'
        $outside = Join-Path $caseRoot 'outside'
        $source = Join-Path $caseRoot 'source'
        $linkedParent = Join-Path $caseRoot 'linked-parent'
        New-Item -ItemType Directory -Path $outside -Force | Out-Null
        Copy-P4ValidatedSource $source
        Set-Content -LiteralPath (Join-Path $outside 'sentinel.txt') -Value 'outside' -NoNewline
        New-Item -ItemType Junction -Path $linkedParent -Target $outside | Out-Null

        $failure = $null
        try { & $script:realPublishP4FixtureDirectory $source (Join-Path $linkedParent 'official') }
        catch { $failure = $_.Exception.Message }
        $failure | Should Match 'reparse point'

        (Get-Content -LiteralPath (Join-Path $outside 'sentinel.txt') -Raw) | Should Be 'outside'
        Test-Path -LiteralPath (Join-Path $outside 'official') | Should Be $false
    }

    It 'rejects a symbolic-link publication source without following it' {
        $caseRoot = Join-Path $TestDrive 'symlink-publication'
        $outside = Join-Path $caseRoot 'outside'
        $linkedSource = Join-Path $caseRoot 'linked-source'
        $destination = Join-Path $caseRoot 'official'
        New-Item -ItemType Directory -Path $outside -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $outside 'sentinel.txt') -Value 'outside' -NoNewline
        New-Item -ItemType SymbolicLink -Path $linkedSource -Target $outside -ErrorAction Stop | Out-Null

        $failure = $null
        try { & $script:realPublishP4FixtureDirectory $linkedSource $destination }
        catch { $failure = $_.Exception.Message }
        $failure | Should Match 'reparse point'

        (Get-Content -LiteralPath (Join-Path $outside 'sentinel.txt') -Raw) | Should Be 'outside'
        Test-Path -LiteralPath $destination | Should Be $false
    }

    It 'rejects extra files directories and nested reparse points before publication' {
        foreach ($kind in @('extra-file', 'extra-directory', 'nested-junction')) {
            $caseRoot = Join-Path $TestDrive "exact-tree-$kind"
            $source = Join-Path $caseRoot 'source'
            $destination = Join-Path $caseRoot 'official'
            Copy-P4ValidatedSource $source
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $destination 'official.txt') -NoNewline -Value 'official'
            if ($kind -eq 'extra-file') {
                Set-Content -LiteralPath (Join-Path $source 'extra.txt') -NoNewline -Value 'extra'
            }
            elseif ($kind -eq 'extra-directory') {
                New-Item -ItemType Directory -Path (Join-Path $source 'extra') | Out-Null
            }
            else {
                $outside = Join-Path $caseRoot 'outside'
                New-Item -ItemType Directory -Path $outside | Out-Null
                Set-Content -LiteralPath (Join-Path $outside 'sentinel.txt') -NoNewline -Value 'outside'
                New-Item -ItemType Junction -Path (Join-Path $source 'nested') -Target $outside | Out-Null
            }

            $failure = $null
            try { & $script:realPublishP4FixtureDirectory $source $destination }
            catch { $failure = $_.Exception.Message }
            $failure | Should Match 'exact five-file|reparse point'
            (Get-Content -LiteralPath (Join-Path $destination 'official.txt') -Raw) | Should Be 'official'
        }
    }

    It 'revalidates the copied staging tree before replacing official fixtures' {
        $caseRoot = Join-Path $TestDrive 'copy-revalidation'
        $source = Join-Path $caseRoot 'source'
        $destination = Join-Path $caseRoot 'official'
        Copy-P4ValidatedSource $source
        New-Item -ItemType Directory -Path $destination | Out-Null
        Set-Content -LiteralPath (Join-Path $destination 'official.txt') -NoNewline -Value 'official'
        $previous = $env:GODOTALS_P4_GENERATOR_FAIL_STAGE
        try {
            foreach ($stage in @('corrupt-after-copy', 'drift-after-schema')) {
                $env:GODOTALS_P4_GENERATOR_FAIL_STAGE = $stage
                $failure = $null
                try { & $script:realPublishP4FixtureDirectory $source $destination }
                catch { $failure = $_.Exception.Message }
                $failure | Should Not BeNullOrEmpty
                (Get-Content -LiteralPath (Join-Path $destination 'official.txt') -Raw) | Should Be 'official'
            }
        }
        finally { $env:GODOTALS_P4_GENERATOR_FAIL_STAGE = $previous }
    }

    It 'rejects a concurrent publisher through a cross-process destination lock' {
        $caseRoot = Join-Path $TestDrive 'concurrent-publication'
        $source = Join-Path $caseRoot 'source'
        $destination = Join-Path $caseRoot 'official'
        $ready = Join-Path $caseRoot 'lock-ready'
        Copy-P4ValidatedSource $source
        New-Item -ItemType Directory -Path $destination | Out-Null
        Set-Content -LiteralPath (Join-Path $destination 'official.txt') -NoNewline -Value 'official'
        $job = Start-Job -ArgumentList $generatorPath, $destination, $ready -ScriptBlock {
            param($scriptPath, $fixturePath, $readyPath)
            . $scriptPath
            $lease = Enter-P4PublicationLock $fixturePath
            try {
                Set-Content -LiteralPath $readyPath -NoNewline -Value 'ready'
                Start-Sleep -Seconds 15
            }
            finally { Exit-P4PublicationLock $lease }
        }
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(8)
            while (-not (Test-Path -LiteralPath $ready) -and [DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 50
            }
            Test-Path -LiteralPath $ready | Should Be $true
            $failure = $null
            try { & $script:realPublishP4FixtureDirectory $source $destination }
            catch { $failure = $_.Exception.Message }
            $failure | Should Match 'already active'
            (Get-Content -LiteralPath (Join-Path $destination 'official.txt') -Raw) | Should Be 'official'
        }
        finally {
            Stop-Job $job -ErrorAction SilentlyContinue
            Remove-Job $job -Force -ErrorAction SilentlyContinue
        }
    }
}
