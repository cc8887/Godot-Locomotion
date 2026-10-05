$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:Generator = Join-Path $script:RepositoryRoot 'scripts\generate-p5a-profile.ps1'
$script:Manifest = Join-Path $script:RepositoryRoot 'assets\generated\als_v4\als_manifest.json'
$script:TrackedProfile = Join-Path $script:RepositoryRoot 'assets\config\p5a_animation_runtime.json'

function Invoke-P5aGenerator([string]$ManifestPath, [string]$OutputPath) {
    $output = @(& pwsh -NoProfile -File $script:Generator `
        -ManifestPath $ManifestPath -OutputPath $OutputPath 2>&1)
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join [Environment]::NewLine) }
}

function Write-P5aJsonFixture([object]$Value, [string]$Path) {
    [IO.File]::WriteAllText(
        $Path, ($Value | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
}

function Write-P5aRawJsonFixture([string]$Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function Get-P5aTestAssetId([string]$ObjectPath) {
    $sha1 = [Security.Cryptography.SHA1]::Create()
    try {
        return [Convert]::ToHexString($sha1.ComputeHash([Text.Encoding]::UTF8.GetBytes($ObjectPath))).ToLowerInvariant()
    }
    finally { $sha1.Dispose() }
}

function Set-P5aTestSourceContentRoot([object]$Manifest, [string]$ReplacementRoot) {
    $oldRoot = [string]$Manifest.sourceContentRoot
    $idByPath = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($asset in @($Manifest.animations) + @($Manifest.montages)) {
        $oldPath = [string]$asset.objectPath
        if ($oldPath.StartsWith("$oldRoot/", [StringComparison]::Ordinal)) {
            $asset.objectPath = $ReplacementRoot + $oldPath.Substring($oldRoot.Length)
            $asset.id = Get-P5aTestAssetId ([string]$asset.objectPath)
            $idByPath.Add([string]$asset.objectPath, [string]$asset.id)
        }
    }
    foreach ($montage in @($Manifest.montages)) {
        foreach ($slot in @($montage.metadata.slots)) {
            foreach ($segment in @($slot.segments)) {
                $path = [string]$segment.animationObjectPath
                $replacementPath = $ReplacementRoot + $path.Substring($oldRoot.Length)
                if ($idByPath.ContainsKey($replacementPath)) {
                    $segment.animationObjectPath = $replacementPath
                    $segment.animationId = $idByPath[$replacementPath]
                }
            }
        }
    }
    $Manifest.sourceContentRoot = $ReplacementRoot
}

Describe 'generate-p5a-profile strict mapping and publication' {
    It 'generates the exact deterministic runtime profile from canonical object paths' {
        $first = Join-Path $TestDrive 'first.json'
        $second = Join-Path $TestDrive 'second.json'

        $one = Invoke-P5aGenerator $script:Manifest $first
        $two = Invoke-P5aGenerator $script:Manifest $second

        $one.ExitCode | Should Be 0
        $two.ExitCode | Should Be 0
        $one.Output | Should Be 'P5A_PROFILE_GENERATION_OK groups=1 members=17 transitions=4 actions=1 segments=1'
        $two.Output | Should Be 'P5A_PROFILE_GENERATION_OK groups=1 members=17 transitions=4 actions=1 segments=1'
        (Get-FileHash $first -Algorithm SHA256).Hash | Should Be (Get-FileHash $second -Algorithm SHA256).Hash
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($first)) | Should Be `
            ([Convert]::ToBase64String([IO.File]::ReadAllBytes($script:TrackedProfile)))

        $profile = Get-Content -Raw $first | ConvertFrom-Json
        (@($profile.PSObject.Properties.Name) -join ',') | Should Be `
            'schemaVersion,eventSemantics,curveSemantics,syncGroups,dynamicTransition,actions,demoCases'
        $profile.schemaVersion | Should Be 1
        (@($profile.eventSemantics.kind) -join ',') | Should Be `
            'Generic,Footstep,SetAction,SetGroundedEntry,EarlyBlendOut,RootMotionScale'
        (@($profile.eventSemantics.semanticId) -join ',') | Should Be '0,1,2,3,4,5'
        $allow = $profile.curveSemantics.allowTransitions
        "$($allow.sourceName)|$($allow.missingValue)|$($allow.blendMode)|$($allow.clampMinimum)|$($allow.clampMaximum)" |
            Should Be 'Enable_Transition|1|AdditiveToDefault|0|1'
        @($profile.syncGroups).Count | Should Be 1
        @($profile.syncGroups[0].members).Count | Should Be 17
        (@($profile.syncGroups[0].members.animation) -join ',') | Should Be `
            '21c24bd7df5192db2e2a860457f2b7b0681de41d,245ea51e30449a60b7d2b783ec0f6d24ec3bacc0,32fe18c71ccb860fe35c01d6b2b10fa2e4d98297,44a7f89b2c1dac832ca63753c131a037420f9d7e,572c3c83c9007964c233db4c7288ae38e20c3dec,6124eafdcbeaaf04bca366add34c821faa0e4963,859f8a49c55747e7382a1ae15970b23cc12f3f85,8ae1b9703a7d0144e570247d88629530b376885f,8bd6ad52ad04a2ad23b47187886c630701df5260,8eb8837c9f32973628b83ed2b98f7d68a0e82aa9,945bdda63e8a379694c792c3545fe11dd166b724,a4c6e0e455e7be7355cdd7c3ce49272d07773b18,b07a51bbab122c81679ac30d3f2f78f45ac14dc8,db60b2c35ce5ef5216c782fc1f33549cbcf8278d,eb84a748fee4615754ce3cbcd3c259b33918b935,f9ec8804e251f03c57e04dde4db9bd921457bae4,fc2d3a4142a1bd82d20877d806c783ff56dfe688'
        @($profile.syncGroups[0].members | Where-Object { $_.loopPolicy -ceq 'Loop' -and $_.canLead }).Count |
            Should Be 17
        (@($profile.dynamicTransition.slots | ForEach-Object { "$($_.stance)/$($_.foot)/$($_.animation)" }) -join ',') |
            Should Be 'Standing/Left/3e23712571d6bbea8744fc94dad0904a6a0a0b5d,Standing/Right/97d46bf9858376893c1c34a128c27044b4467d82,Crouching/Left/3e23712571d6bbea8744fc94dad0904a6a0a0b5d,Crouching/Right/97d46bf9858376893c1c34a128c27044b4467d82'
        @($profile.actions).Count | Should Be 1
        "$($profile.actions[0].name)|$($profile.actions[0].montage)|$($profile.actions[0].slot)|$($profile.actions[0].startSection)" |
            Should Be 'Roll|2d9341182885d90ad666fff32c025937438b1827|BaseLayer|Default'
        "$($profile.demoCases.transitionStance)|$($profile.demoCases.transitionFoot)|$($profile.demoCases.rollAction)" |
            Should Be 'Standing|Left|Roll'
    }

    It 'resolves semantic source roles relative to the manifest ContentRoot' {
        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $replacementRoot = '/Game/ReplacementLocomotionPack'
        Set-P5aTestSourceContentRoot $manifest $replacementRoot
        $fixture = Join-Path $TestDrive 'replacement-content-root.json'
        $output = Join-Path $TestDrive 'replacement-content-root-profile.json'
        Write-P5aJsonFixture $manifest $fixture

        $result = Invoke-P5aGenerator $fixture $output

        $result.ExitCode | Should Be 0
        $profile = Get-Content -Raw $output | ConvertFrom-Json
        @($profile.syncGroups[0].members).Count | Should Be 17
        $profile.actions[0].montage | Should Be (Get-P5aTestAssetId `
            "$replacementRoot/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions/ALS_N_LandRoll_F_Montage_Default.ALS_N_LandRoll_F_Montage_Default")
        $originalProfile = Get-Content -Raw $script:TrackedProfile | ConvertFrom-Json
        @($profile.syncGroups[0].members.animation | Where-Object { $_ -in $originalProfile.syncGroups[0].members.animation }).Count |
            Should Be 0
    }

    It 'rejects a malformed sourceContentRoot instead of resolving outside the selected package' {
        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $manifest.sourceContentRoot = '/Game/../ReplacementPack'
        $fixture = Join-Path $TestDrive 'invalid-content-root.json'
        Write-P5aJsonFixture $manifest $fixture

        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'invalid-content-root-profile.json')

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'sourceContentRoot.*canonical Unreal content path'
    }

    It 'rejects zero and multiple exact ordinal object path matches without basename fallback' {
        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $target = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Transitions/ALS_N_Transition_L.ALS_N_Transition_L'
        $asset = @($manifest.animations | Where-Object { $_.objectPath -ceq $target })[0]
        $asset.objectPath = '/Game/Other/ALS_N_Transition_L.ALS_N_Transition_L'
        $missing = Join-Path $TestDrive 'missing.json'
        Write-P5aJsonFixture $manifest $missing
        $result = Invoke-P5aGenerator $missing (Join-Path $TestDrive 'missing-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'zero.*exact.*object path'

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $manifest.animations = @($manifest.animations) + @($manifest.animations | Where-Object { $_.objectPath -ceq $target } | Select-Object -First 1)
        $duplicate = Join-Path $TestDrive 'duplicate.json'
        Write-P5aJsonFixture $manifest $duplicate
        $result = Invoke-P5aGenerator $duplicate (Join-Path $TestDrive 'duplicate-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'multiple.*exact.*object path'
    }

    It 'uses ordinal case-sensitive paths and rejects a case-only replacement' {
        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $target = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions/ALS_N_LandRoll_F_Montage_Default.ALS_N_LandRoll_F_Montage_Default'
        (@($manifest.montages | Where-Object { $_.objectPath -ceq $target }))[0].objectPath = $target.ToUpperInvariant()
        $fixture = Join-Path $TestDrive 'case.json'
        Write-P5aJsonFixture $manifest $fixture

        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'case-output.json')

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'zero.*exact.*object path'
    }

    It 'requires the exact Roll Sequence path and one BaseLayer segment that references it' {
        $sequencePath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions/ALS_N_LandRoll_F.ALS_N_LandRoll_F'
        $montagePath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions/ALS_N_LandRoll_F_Montage_Default.ALS_N_LandRoll_F_Montage_Default'

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        (@($manifest.animations | Where-Object { $_.objectPath -ceq $sequencePath }))[0].objectPath = '/Game/Other/ALS_N_LandRoll_F.ALS_N_LandRoll_F'
        $fixture = Join-Path $TestDrive 'missing-sequence.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'missing-sequence-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'zero.*exact.*object path'

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $montage = (@($manifest.montages | Where-Object { $_.objectPath -ceq $montagePath }))[0]
        $montage.metadata.slots[0].segments[0].animationId = '0000000000000000000000000000000000000000'
        $fixture = Join-Path $TestDrive 'wrong-segment.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'wrong-segment-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'BaseLayer.*segment.*Roll Sequence'

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $montage = (@($manifest.montages | Where-Object { $_.objectPath -ceq $montagePath }))[0]
        $montage.metadata.slots[0].segments = @($montage.metadata.slots[0].segments) + @($montage.metadata.slots[0].segments[0])
        $fixture = Join-Path $TestDrive 'extra-segment.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'extra-segment-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'exactly one BaseLayer segment'

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $montage = (@($manifest.montages | Where-Object { $_.objectPath -ceq $montagePath }))[0]
        $montage.metadata.slots = @($montage.metadata.slots) + @([pscustomobject]@{
            slotName = 'OtherLayer'; segments = @()
        })
        $fixture = Join-Path $TestDrive 'extra-slot.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'extra-slot-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'exactly one.*slot'

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $montage = (@($manifest.montages | Where-Object { $_.objectPath -ceq $montagePath }))[0]
        $montage.metadata.sections[0].name = 'Other'
        $fixture = Join-Path $TestDrive 'missing-default.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'missing-default-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'exactly one Default section'

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $montage = (@($manifest.montages | Where-Object { $_.objectPath -ceq $montagePath }))[0]
        $montage.metadata.slots[0].segments[0].animationObjectPath = '/Game/Other/ALS_N_LandRoll_F.ALS_N_LandRoll_F'
        $fixture = Join-Path $TestDrive 'wrong-segment-path.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'wrong-segment-path-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'segment.*object path.*Roll Sequence'
    }

    It 'rejects incompatible schema and noncanonical selected asset IDs but accepts exporter metadata changes' {
        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $manifest.schemaVersion = 1
        $fixture = Join-Path $TestDrive 'schema-one.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'schema-one-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'schemaVersion.*2'

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $manifest.exporterVersion = '9.4-preview'
        $fixture = Join-Path $TestDrive 'replacement-exporter.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'replacement-exporter-output.json')
        $result.ExitCode | Should Be 0

        $manifest.exporterVersion = ' '
        $fixture = Join-Path $TestDrive 'empty-exporter.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'empty-exporter-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'exporterVersion.*missing'

        $transitionPath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Transitions/ALS_N_Transition_L.ALS_N_Transition_L'
        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        (@($manifest.animations | Where-Object { $_.objectPath -ceq $transitionPath }))[0].id =
            '3E23712571D6BBEA8744FC94DAD0904A6A0A0B5D'
        $fixture = Join-Path $TestDrive 'uppercase-id.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'uppercase-id-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'lowercase 40-hex'

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        (@($manifest.animations | Where-Object { $_.objectPath -ceq $transitionPath }))[0].id =
            '0000000000000000000000000000000000000000'
        $fixture = Join-Path $TestDrive 'invalid-id.json'
        Write-P5aJsonFixture $manifest $fixture
        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive 'invalid-id-output.json')
        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'stable ID.*object path'
    }

    It 'rejects malformed and recursively duplicated raw JSON before conversion or publication' {
        $raw = [IO.File]::ReadAllText($script:Manifest)
        $cases = @(
            [pscustomobject]@{
                Name = 'duplicate-root'
                Raw = $raw.Replace('"schemaVersion": 2,', '"schemaVersion": 1, "schemaVersion": 2,')
                Pattern = 'duplicate property.*schemaVersion'
            }
            [pscustomobject]@{
                Name = 'duplicate-selected-id'
                Raw = $raw.Replace(
                    '"id": "3e23712571d6bbea8744fc94dad0904a6a0a0b5d"',
                    '"id": "0000000000000000000000000000000000000000", "id": "3e23712571d6bbea8744fc94dad0904a6a0a0b5d"')
                Pattern = 'duplicate property.*id'
            }
            [pscustomobject]@{
                Name = 'duplicate-selected-object-path'
                Raw = $raw.Replace(
                    '"objectPath": "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Transitions/ALS_N_Transition_L.ALS_N_Transition_L"',
                    '"objectPath": "/Game/Other/ALS_N_Transition_L.ALS_N_Transition_L", "objectPath": "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Transitions/ALS_N_Transition_L.ALS_N_Transition_L"')
                Pattern = 'duplicate property.*objectPath'
            }
            [pscustomobject]@{
                Name = 'malformed'
                Raw = '{"schemaVersion":2'
                Pattern = 'not valid JSON'
            }
        )

        foreach ($case in $cases) {
            $fixture = Join-Path $TestDrive "$($case.Name).json"
            $output = Join-Path $TestDrive "$($case.Name)-output.json"
            Write-P5aRawJsonFixture $case.Raw $fixture

            $result = Invoke-P5aGenerator $fixture $output

            $result.ExitCode | Should Not Be 0
            $result.Output | Should Match $case.Pattern
            Test-Path -LiteralPath $output | Should Be $false
        }
    }

    It 'rejects the manifest as output before changing its bytes' {
        $fixture = Join-Path $TestDrive 'same.json'
        [IO.File]::Copy($script:Manifest, $fixture)
        $before = [Convert]::ToBase64String([IO.File]::ReadAllBytes($fixture))

        $result = Invoke-P5aGenerator $fixture (Join-Path $TestDrive '.\same.json')

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'manifest.*output.*same file'
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($fixture)) | Should Be $before
    }

    It 'preserves old output and removes temporary residue when replacement fails' {
        $output = Join-Path $TestDrive 'locked.json'
        [IO.File]::WriteAllText($output, '{"old":true}', [Text.UTF8Encoding]::new($false))
        $before = [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))
        $lock = [IO.FileStream]::new($output, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try { $result = Invoke-P5aGenerator $script:Manifest $output }
        finally { $lock.Dispose() }

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'publish.*atomically'
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($output)) | Should Be $before
        @(Get-ChildItem $TestDrive -Filter '.locked.json.*.tmp').Count | Should Be 0
    }

    It 'uses same-directory WriteThrough Flush true Replace Move and no production failure hook' {
        $source = [IO.File]::ReadAllText($script:Generator)
        $source | Should Match 'Resolve-ExactManifestAsset'
        $source | Should Not Match '\.assetName|AssetName|Split-Path.*-Leaf|GetFileNameWithoutExtension'
        $source | Should Match 'WriteThrough'
        $source | Should Match '\.Flush\(\$true\)'
        $source | Should Match '\[IO\.File\]::Replace|\[System\.IO\.File\]::Replace'
        $source | Should Match '\[IO\.File\]::Move|\[System\.IO\.File\]::Move'
        $source | Should Not Match 'Inject|TestHook|FailureHook'
    }
}
