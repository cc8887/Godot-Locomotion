$script:P5aRepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:P5aGeneratorPath = Join-Path $script:P5aRepositoryRoot 'scripts\generate-p5a-golden.ps1'
$script:P5aVerifierPath = Join-Path $script:P5aRepositoryRoot 'scripts\verify-p5a-golden.ps1'
$script:P5aOracleProjectPath = Join-Path $script:P5aRepositoryRoot 'tools\Als.P5aOracle\Als.P5aOracle.csproj'
$script:P5aOracleProgramPath = Join-Path $script:P5aRepositoryRoot 'tools\Als.P5aOracle\Program.cs'
$script:P5aLockedCommit = 'b754d6f0f2bb03741d301f8fb88077ebfe561e17'
$script:P5aLockedPatch = '3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f'
$script:P5aLayoutDigest = 'd6fef54173240d32'
$script:P5aBindingDigest = '2b4be600d531c734'
$script:P5aGraphDigest = '44403c2869d8f615'
$script:P5aGeneratorLoadError = $null
$script:P5aVerifierLoadError = $null
$script:P5aSelectedDotnetClosures = @{}
$script:P5aDefaultWorkflowToolchain = $null

function Write-TestP5aText
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrEmpty($parent))
    {
        [void][IO.Directory]::CreateDirectory($parent)
    }
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function Get-TestP5aFileHash
{
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-TestP5aTreeHash
{
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$RelativePaths
    )

    $ordered = [string[]]@($RelativePaths)
    [Array]::Sort($ordered, [StringComparer]::Ordinal)
    $stream = [IO.MemoryStream]::new()
    try
    {
        foreach ($relativePath in $ordered)
        {
            $normalized = $relativePath.Replace('\', '/')
            $pathBytes = [Text.Encoding]::UTF8.GetBytes($normalized)
            $hashBytes = [Text.Encoding]::ASCII.GetBytes(
                (Get-TestP5aFileHash (Join-Path $Root $relativePath)))
            $stream.Write($pathBytes, 0, $pathBytes.Length)
            $stream.WriteByte(0)
            $stream.Write($hashBytes, 0, $hashBytes.Length)
            $stream.WriteByte(10)
        }
        $sha = [Security.Cryptography.SHA256]::Create()
        try
        {
            return ([BitConverter]::ToString($sha.ComputeHash($stream.ToArray())) -replace '-', '').ToLowerInvariant()
        }
        finally
        {
            $sha.Dispose()
        }
    }
    finally
    {
        $stream.Dispose()
    }
}

function Set-TestP5aBuildManifestField
{
    param(
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value
    )

    $text = [IO.File]::ReadAllText($ManifestPath)
    $pattern = '(?m)^' + [regex]::Escape($Name) + '=[^\r\n]*$'
    ([regex]::Matches($text, $pattern)).Count | Should Be 1
    Write-TestP5aText $ManifestPath ([regex]::Replace($text, $pattern, "$Name=$Value"))
}

function Assert-TestP5aPathCapability
{
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Name)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        "Missing 13B capability: $Name" | Should BeNullOrEmpty
    }
}

function Assert-TestP5aCommandCapability
{
    param(
        [Parameter(Mandatory)][string]$Name,
        [ValidateSet('generator', 'verifier', 'either')][string]$Owner = 'either'
    )

    if ($Owner -eq 'generator')
    {
        Assert-TestP5aPathCapability $script:P5aGeneratorPath 'scripts/generate-p5a-golden.ps1'
        if ($null -ne $script:P5aGeneratorLoadError)
        {
            "Missing 13B capability: generator dot-source ($($script:P5aGeneratorLoadError))" |
                Should BeNullOrEmpty
        }
    }
    elseif ($Owner -eq 'verifier')
    {
        Assert-TestP5aPathCapability $script:P5aVerifierPath 'scripts/verify-p5a-golden.ps1'
        if ($null -ne $script:P5aVerifierLoadError)
        {
            "Missing 13B capability: verifier dot-source ($($script:P5aVerifierLoadError))" |
                Should BeNullOrEmpty
        }
    }

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue))
    {
        "Missing 13B capability: $Name" | Should BeNullOrEmpty
    }
}

function Test-TestP5aRejects
{
    param([Parameter(Mandatory)][scriptblock]$Action)

    try
    {
        & $Action
        return $false
    }
    catch
    {
        return $true
    }
}

function Invoke-TestP5aFailureCapture
{
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[object]]$Output
    )

    try
    {
        & $Action | ForEach-Object { [void]$Output.Add($_) }
        return $null
    }
    catch
    {
        return $_.Exception.Message
    }
}

function Get-TestP5aProcessFailureDiagnosticPattern
{
    param([Parameter(Mandatory)][string]$FailureKind)

    switch ($FailureKind)
    {
        'nonzero' { return '(?i)(non.?zero|exit)' }
        'timeout' { return '(?i)tim(?:e|ed).?out' }
        'warning' { return '(?i)warning' }
        'error' { return '(?i)(error|fatal)' }
        default { return '(?i)marker' }
    }
}

function Invoke-TestP5aWithAmbientStagingEnvironment
{
    param(
        [Parameter(Mandatory)][string]$AmbientValue,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $name = 'GODOTALS_P5A_STAGING_ROOT'
    $previous = [Environment]::GetEnvironmentVariable(
        $name, [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $name, $AmbientValue, [EnvironmentVariableTarget]::Process)
    try
    {
        $output = @(& $Action)
        return [pscustomobject]@{
            Output = @($output)
            ValueAfterAction = [Environment]::GetEnvironmentVariable(
                $name, [EnvironmentVariableTarget]::Process)
        }
    }
    finally
    {
        [Environment]::SetEnvironmentVariable(
            $name, $previous, [EnvironmentVariableTarget]::Process)
    }
}

function Assert-TestP5aExactParentMarker
{
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Output,
        [Parameter(Mandatory)][string]$ExpectedMarker
    )

    $parentMarkers = @($Output | ForEach-Object { [string]$_ } | Where-Object {
        $_ -match '^P5A_GOLDEN_'
    })
    $parentMarkers.Count | Should Be 1
    $parentMarkers[0] | Should Be $ExpectedMarker
}

function Assert-TestP5aParentMarkerMutationsRejected
{
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Output,
        [Parameter(Mandatory)][string]$ExpectedMarker,
        [Parameter(Mandatory)][ValidateSet('generation', 'fixture')][string]$Kind
    )

    $withoutParent = @($Output | Where-Object { [string]$_ -notmatch '^P5A_GOLDEN_' })
    $malformed = if ($Kind -ceq 'generation') {
        'P5A_GOLDEN_GENERATION_OK cases=7 commit=wrong'
    } else {
        'P5A_GOLDEN_FIXTURE_OK cases=7 commit=wrong'
    }
    $wrongFamily = if ($Kind -ceq 'generation') {
        "P5A_GOLDEN_FIXTURE_OK cases=8 commit=$script:P5aLockedCommit"
    } else {
        "P5A_GOLDEN_GENERATION_OK cases=8 commit=$script:P5aLockedCommit"
    }

    Test-TestP5aRejects {
        Assert-TestP5aExactParentMarker -Output @($Output + $ExpectedMarker) `
            -ExpectedMarker $ExpectedMarker
    } | Should Be $true
    Test-TestP5aRejects {
        Assert-TestP5aExactParentMarker -Output @($Output + $malformed) `
            -ExpectedMarker $ExpectedMarker
    } | Should Be $true
    Test-TestP5aRejects {
        Assert-TestP5aExactParentMarker -Output @($Output + $wrongFamily) `
            -ExpectedMarker $ExpectedMarker
    } | Should Be $true
    Test-TestP5aRejects {
        Assert-TestP5aExactParentMarker -Output @($withoutParent + $malformed) `
            -ExpectedMarker $ExpectedMarker
    } | Should Be $true
    Test-TestP5aRejects {
        Assert-TestP5aExactParentMarker -Output @($withoutParent + $wrongFamily) `
            -ExpectedMarker $ExpectedMarker
    } | Should Be $true
}

function Get-TestP5aDefaultRunnerDefinition
{
    param([Parameter(Mandatory)][Management.Automation.Language.Ast]$Ast)

    $definitions = @($Ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -match '^Invoke-P5a(?:Generator|Verifier)DefaultProcess$' -and
            $node.Extent.Text -match '(?i)CreateProcessW'
    }, $true))
    $definitions.Count | Should Be 1
    return $definitions[0]
}

function Assert-TestP5aNativeRunnerLexicalClosure
{
    param([Parameter(Mandatory)][string]$RunnerText)

    # PowerShell AST intentionally treats the Add-Type here-string as opaque.  Keep
    # this narrow lexical contract alongside the AST closure instead of requiring a
    # compiler or a network-restored C# parser in the Pester 3/4 harness.
    foreach ($token in @(
        'CreateProcessW', 'STARTUPINFOEX', 'EXTENDED_STARTUPINFO_PRESENT',
        'STARTF_USESTDHANDLES', 'CreatePipe', 'SetHandleInformation',
        'InitializeProcThreadAttributeList', 'UpdateProcThreadAttribute',
        'DeleteProcThreadAttributeList', 'PROC_THREAD_ATTRIBUTE_JOB_LIST',
        'PROC_THREAD_ATTRIBUTE_HANDLE_LIST', 'QueryInformationJobObject',
        'JOBOBJECT_BASIC_ACCOUNTING_INFORMATION', 'TotalProcesses',
        'ActiveProcesses', 'JobProcessIds', 'StdOutLines', 'StdErrLines',
        'StdOutBytes', 'StdErrBytes', 'OutputLimitExceeded',
        'GetQueuedCompletionStatus', 'RemainingMilliseconds', 'metadata'))
    {
        $RunnerText | Should Match ([regex]::Escape($token))
    }

    foreach ($forbiddenPattern in @(
        '(?i)ProcessStartInfo', '(?i)Diagnostics\.Process',
        '(?i)\$process\s*\.\s*Start\s*\(', '(?i)\bProcess\s*\.\s*Start\s*\(',
        '(?i)AssignProcessToJobObject', '(?i)\bAttach\s*\(',
        '(?i)ReadToEnd(?:Async)?', '(?i)WaitForExit\s*\(\s*\)',
        '(?i)WaitAll\s*\(\s*[^,\r\n]+\s*\)', '(?i)Start-Sleep',
        '(?i)\(\s*\$stdout\s*\+\s*\$stderr\s*\)'))
    {
        $RunnerText | Should Not Match $forbiddenPattern
    }

    foreach ($streamName in @('StdOut', 'StdErr'))
    {
        $matches = @([regex]::Matches(
            $RunnerText,
            '(?im)\bMax' + $streamName + 'Bytes\s*=\s*([0-9]+)'))
        $matches.Count | Should Be 1
        $cap = [int64]$matches[0].Groups[1].Value
        $cap | Should BeGreaterThan 0
        ($cap -le 8388608) | Should Be $true
    }

    foreach ($limit in @(
        @{ Name = 'MaxActiveProcesses'; Maximum = 128 }
        @{ Name = 'MaxTrackedProcesses'; Maximum = 4096 }
        @{ Name = 'MaxProcessDepth'; Maximum = 64 }
        @{ Name = 'MaxExecutablePathCharacters'; Maximum = 8388608 }
        @{ Name = 'MaxAncestorEdges'; Maximum = 262144 }
    ))
    {
        $matches = @([regex]::Matches(
            $RunnerText,
            '(?im)\b' + $limit.Name + '\s*=\s*([0-9]+)'))
        $matches.Count | Should Be 1
        $value = [int64]$matches[0].Groups[1].Value
        $value | Should BeGreaterThan 0
        ($value -le [int64]$limit.Maximum) | Should Be $true
        @([regex]::Matches($RunnerText, '\b' + $limit.Name + '\b')).Count |
            Should BeGreaterThan 1
    }
    $RunnerText | Should Match 'JOB_OBJECT_LIMIT_ACTIVE_PROCESS'
    $RunnerText | Should Match 'ActiveProcessLimit'

    $jobListIndex = $RunnerText.LastIndexOf('PROC_THREAD_ATTRIBUTE_JOB_LIST',
        [StringComparison]::Ordinal)
    $handleListIndex = $RunnerText.LastIndexOf('PROC_THREAD_ATTRIBUTE_HANDLE_LIST',
        [StringComparison]::Ordinal)
    $createIndex = $RunnerText.LastIndexOf('CreateProcessW', [StringComparison]::Ordinal)
    $jobListIndex | Should BeGreaterThan -1
    $handleListIndex | Should BeGreaterThan $jobListIndex
    $createIndex | Should BeGreaterThan $handleListIndex
    $attributeOrderPattern = '(?s)UpdateProcThreadAttribute.{0,4096}' +
        'PROC_THREAD_ATTRIBUTE_JOB_LIST.{0,4096}UpdateProcThreadAttribute.{0,4096}' +
        'PROC_THREAD_ATTRIBUTE_HANDLE_LIST.{0,4096}CreateProcessW\s*\('
    $RunnerText | Should Match $attributeOrderPattern
    $RunnerText | Should Match '(?i)(?:TotalProcesses\s*!=|JobTotalProcesses\s*-ne)'
}

function Assert-TestP5aProcessLaunchClosure
{
    param([Parameter(Mandatory)][string]$Path)

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $Path, [ref]$tokens, [ref]$errors)
    @($errors).Count | Should Be 0
    $runner = Get-TestP5aDefaultRunnerDefinition -Ast $ast
    $runnerStart = $runner.Extent.StartOffset
    $runnerEnd = $runner.Extent.EndOffset

    $forbiddenCommands = @(
        'cmd', 'cmd.exe', 'dotnet', 'dotnet.exe', 'powershell', 'powershell.exe',
        'pwsh', 'pwsh.exe', 'Invoke-Command', 'Invoke-Expression',
        'icm', 'iex', 'Invoke-Item', 'ii',
        'Start-Job', 'Start-Process', 'Start-ThreadJob',
        'sajb', 'saps', 'start',
        'UnrealEditor', 'UnrealEditor.exe', 'UnrealEditorCmd', 'UnrealEditorCmd.exe')
    $allowedInvocationVariables = @('ProcessInvoker', 'FileSystemInvoker', 'CheckpointInvoker')
    $processInvokerCommands = [Collections.Generic.List[object]]::new()
    $commands = @($ast.FindAll({
        param($node) $node -is [Management.Automation.Language.CommandAst]
    }, $true))
    foreach ($command in $commands)
    {
        $commandName = $command.GetCommandName()
        if (-not [string]::IsNullOrEmpty($commandName))
        {
            @($forbiddenCommands | Where-Object {
                $_.Equals($commandName, [StringComparison]::OrdinalIgnoreCase)
            }).Count | Should Be 0
            $commandName | Should Not Match '(?i)\.(exe|com|cmd|bat)$'
            if ($commandName -ieq 'New-Object')
            {
                $command.Extent.Text | Should Not Match '(?i)Diagnostics\.Process'
            }
        }

        if ($command.InvocationOperator -in @(
            [Management.Automation.Language.TokenKind]::Ampersand,
            [Management.Automation.Language.TokenKind]::Dot))
        {
            $first = $command.CommandElements[0]
            ($first -is [Management.Automation.Language.VariableExpressionAst]) | Should Be $true
            $variableName = [string]$first.VariablePath.UserPath
            @($allowedInvocationVariables | Where-Object { $_ -ceq $variableName }).Count |
                Should Be 1
            if ($variableName -ceq 'ProcessInvoker')
            {
                $processInvokerCommands.Add($command)
            }
        }
    }

    @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and
            $node.Member -is [Management.Automation.Language.StringConstantExpressionAst] -and
            $node.Member.Value -ceq 'Start'
    }, $true)).Count | Should Be 0

    $processInvokerCommands.Count | Should Be 1
    $ancestor = $processInvokerCommands[0].Parent
    while ($null -ne $ancestor -and
           $ancestor -isnot [Management.Automation.Language.FunctionDefinitionAst])
    {
        ($ancestor -is [Management.Automation.Language.IfStatementAst]) | Should Be $false
        ($ancestor -is [Management.Automation.Language.SwitchStatementAst]) | Should Be $false
        ($ancestor -is [Management.Automation.Language.LoopStatementAst]) | Should Be $false
        $ancestor = $ancestor.Parent
    }

    $processLikeVariables = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.VariablePath.UserPath -match '(?i)process.*invoker'
    }, $true) | ForEach-Object { $_.VariablePath.UserPath } | Sort-Object -Unique)
    @($processLikeVariables) | Should Be @('ProcessInvoker')

    $defaultedProcessInvokerParameters = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.ParameterAst] -and
            $node.Name.VariablePath.UserPath -ceq 'ProcessInvoker' -and
            $null -ne $node.DefaultValue
    }, $true))
    $defaultedProcessInvokerParameters.Count | Should Be 1
    ($defaultedProcessInvokerParameters[0].DefaultValue -is
        [Management.Automation.Language.VariableExpressionAst]) | Should Be $true
    $defaultedProcessInvokerParameters[0].DefaultValue.VariablePath.UserPath |
        Should Be ("function:$($runner.Name)")

    $runnerText = $runner.Extent.Text
    Assert-TestP5aNativeRunnerLexicalClosure -RunnerText $runnerText
    foreach ($pattern in @(
        'ProcessId', 'DescendantProcesses', 'parentProcessId',
        'ancestorProcessIds', 'imageName', 'executablePath'))
    {
        $runnerText | Should Match $pattern
    }
}

function Get-TestP5aOracleSourcePaths
{
    $paths = [string[]]@(
        '.editorconfig'
        'global.json'
        'Directory.Build.props'
        'tools/Als.P5aOracle/Als.P5aOracle.csproj'
        'tools/Als.P5aOracle/Program.cs'
        'src/Als.Import/Als.Import.csproj'
        'src/Als.Import/ImportOne.cs'
        'src/Als.Import/Nested/ImportTwo.cs'
        'src/Als.Core/Als.Core.csproj'
        'src/Als.Core/CoreOne.cs'
        'src/Als.Core/Nested/CoreTwo.cs'
    )
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    return $paths
}

function New-TestP5aOracleRepository
{
    param([Parameter(Mandatory)][string]$Name, [switch]$WithoutArtifacts)

    $root = Join-Path $TestDrive $Name
    [void][IO.Directory]::CreateDirectory($root)
    $files = [ordered]@{
        '.editorconfig' = "root = true`n"
        'global.json' = "{`n  `"sdk`": { `"version`": `"8.0.100`", `"rollForward`": `"latestPatch`" }`n}`n"
        'Directory.Build.props' = "<Project>`n  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>`n</Project>`n"
        'tools/Als.P5aOracle/Als.P5aOracle.csproj' = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <P5aRepositoryRoot>$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)\..\..'))</P5aRepositoryRoot>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Als.Import\Als.Import.csproj" />
    <ProjectReference Include="..\..\src\Als.Core\Als.Core.csproj" />
  </ItemGroup>
  <Target Name="WriteP5aOracleBuildManifest" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
    <Exec Command="&quot;$(TargetDir)Als.P5aOracle.exe&quot; --write-build-manifest --repository-root &quot;$(P5aRepositoryRoot)&quot; --output &quot;$(TargetDir)p5a-oracle-build.manifest&quot; --sdk-version &quot;$(NETCoreSdkVersion)&quot;" />
  </Target>
</Project>
'@
        'tools/Als.P5aOracle/Program.cs' = "namespace Als.P5aOracle; internal static class Program { static int Main(string[] args) => 0; }`n"
        'src/Als.Import/Als.Import.csproj' = "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>`n"
        'src/Als.Import/ImportOne.cs' = "namespace Als.Import; public sealed class ImportOne { }`n"
        'src/Als.Import/Nested/ImportTwo.cs' = "namespace Als.Import; public sealed class ImportTwo { }`n"
        'src/Als.Core/Als.Core.csproj' = "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>`n"
        'src/Als.Core/CoreOne.cs' = "namespace Als.Core; public sealed class CoreOne { }`n"
        'src/Als.Core/Nested/CoreTwo.cs' = "namespace Als.Core; public sealed class CoreTwo { }`n"
        'tests/Als.Core.Tests/Als.Core.Tests.csproj' = "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net8.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>`n"
        'tests/Als.Core.Tests/AlsP5aGoldenTests.cs' = "namespace Als.Core.Tests; public sealed class AlsP5aGoldenTests { }`n"
        'tests/Als.Core.Tests/Nested/P5aTestHelper.cs' = "namespace Als.Core.Tests; internal static class P5aTestHelper { }`n"
        'tests/Als.Core.Tests/bin/Generated.cs' = "this test file is excluded`n"
        'tests/Als.Core.Tests/OBJ/Generated.cs' = "this test file is excluded ordinal-ignore-case`n"
        'tests/Als.Core.Tests/TestResults/Generated.cs' = "this test result is excluded`n"
        'tools/schemas/als_p5a_trace.schema.json' = "{}`n"
        'tools/schemas/als_p5a_trace_plan.schema.json' = "{}`n"
        'assets/generated/als_v4/als_manifest.json' = "{}`n"
        'assets/config/p3_locomotion_profile.json' = "{}`n"
        'assets/config/p4_pose_profile.json' = "{}`n"
        'assets/config/p5a_animation_runtime.json' = "{}`n"
        'src/Als.Import/bin/Generated.cs' = "this file is excluded`n"
        'src/Als.Core/OBJ/Generated.cs' = "this file is excluded ordinal-ignore-case`n"
    }
    foreach ($entry in $files.GetEnumerator())
    {
        Write-TestP5aText (Join-Path $root $entry.Key) ([string]$entry.Value)
    }

    $sourcePaths = Get-TestP5aOracleSourcePaths
    $oldTime = [DateTime]::UtcNow.AddMinutes(-10)
    foreach ($relativePath in $sourcePaths)
    {
        [IO.File]::SetLastWriteTimeUtc((Join-Path $root $relativePath), $oldTime)
    }

    $output = Join-Path $root 'tools\Als.P5aOracle\bin\Release\net8.0'
    $artifactNames = @(
        'Als.P5aOracle.exe'
        'Als.P5aOracle.dll'
        'Als.Import.dll'
        'Als.Core.dll'
        'Als.P5aOracle.deps.json'
        'Als.P5aOracle.runtimeconfig.json'
    )
    $manifestPath = Join-Path $output 'p5a-oracle-build.manifest'
    if (-not $WithoutArtifacts)
    {
        [void][IO.Directory]::CreateDirectory($output)
        Write-TestP5aText (Join-Path $output 'Als.P5aOracle.exe') "synthetic apphost`n"
        Write-TestP5aText (Join-Path $output 'Als.P5aOracle.dll') "synthetic oracle assembly`n"
        Write-TestP5aText (Join-Path $output 'Als.Import.dll') "synthetic import assembly`n"
        Write-TestP5aText (Join-Path $output 'Als.Core.dll') "synthetic core assembly`n"
        Write-TestP5aText (Join-Path $output 'Als.P5aOracle.runtimeconfig.json') @'
{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}
'@
        Write-TestP5aText (Join-Path $output 'Als.P5aOracle.deps.json') @'
{"runtimeTarget":{"name":".NETCoreApp,Version=v8.0"},"targets":{".NETCoreApp,Version=v8.0":{"Als.P5aOracle/1.0.0":{"dependencies":{"Als.Import":"1.0.0","Als.Core":"1.0.0"},"runtime":{"Als.P5aOracle.dll":{}}},"Als.Import/1.0.0":{"runtime":{"Als.Import.dll":{}}},"Als.Core/1.0.0":{"runtime":{"Als.Core.dll":{}}}}},"libraries":{"Als.P5aOracle/1.0.0":{"type":"project","serviceable":false,"sha512":""},"Als.Import/1.0.0":{"type":"project","serviceable":false,"sha512":""},"Als.Core/1.0.0":{"type":"project","serviceable":false,"sha512":""}}}
'@
        $newTime = [DateTime]::UtcNow.AddMinutes(-1)
        foreach ($nameToTouch in $artifactNames)
        {
            [IO.File]::SetLastWriteTimeUtc((Join-Path $output $nameToTouch), $newTime)
        }

        $treeHash = Get-TestP5aTreeHash -Root $root -RelativePaths $sourcePaths
        $manifest = @(
            'p5a_oracle_build_v1'
            'configuration=Release'
            'targetFramework=net8.0'
            'sdkVersion=8.0.100'
            "sourceTreeSha256=$treeHash"
            "executableSha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.P5aOracle.exe'))"
            "oracleAssemblySha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.P5aOracle.dll'))"
            "importAssemblySha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.Import.dll'))"
            "coreAssemblySha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.Core.dll'))"
            "depsSha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.P5aOracle.deps.json'))"
            "runtimeConfigSha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.P5aOracle.runtimeconfig.json'))"
        ) -join "`n"
        Write-TestP5aText $manifestPath ($manifest + "`n")
    }

    return [pscustomobject]@{
        Root = $root
        ProjectPath = Join-Path $root 'tools\Als.P5aOracle\Als.P5aOracle.csproj'
        OutputPath = $output
        ManifestPath = $manifestPath
        SourceRelativePaths = $sourcePaths
        SourcePaths = @($sourcePaths | ForEach-Object { Join-Path $root $_ })
        ArtifactNames = $artifactNames
        ArtifactPaths = @($artifactNames | ForEach-Object { Join-Path $output $_ })
        AppHostPath = Join-Path $output 'Als.P5aOracle.exe'
        DepsPath = Join-Path $output 'Als.P5aOracle.deps.json'
    }
}

function New-TestP5aDeployment
{
    param(
        [Parameter(Mandatory)][string]$Name,
        [string]$RepositoryRoot
    )

    $root = if ([string]::IsNullOrEmpty($RepositoryRoot)) {
        Join-Path $TestDrive $Name
    } else {
        [IO.Path]::GetFullPath($RepositoryRoot)
    }
    $owned = Join-Path $root 'tools\unreal\AlsLocomotionTrace'
    $project = Join-Path $root 'SyntheticGame'
    $uproject = Join-Path $project 'SyntheticGame.uproject'
    $deployed = Join-Path $project 'Plugins\AlsLocomotionTrace'
    Write-TestP5aText $uproject "{}`n"
    foreach ($relative in @(
        'AlsLocomotionTrace.uplugin'
        'Source/AlsLocomotionTrace/AlsLocomotionTrace.Build.cs'
        'Source/AlsLocomotionTrace/Private/Trace.cpp'
        'Source/AlsLocomotionTrace/Public/Trace.h'
    ))
    {
        $content = "owned:$relative`n"
        Write-TestP5aText (Join-Path $owned $relative) $content
        Write-TestP5aText (Join-Path $deployed $relative) $content
    }
    Write-TestP5aText (Join-Path $owned 'Binaries\Win64\ignored.bin') "owned ignored`n"
    Write-TestP5aText (Join-Path $deployed 'Binaries\Win64\ignored.bin') "deployed ignored and different`n"
    Write-TestP5aText (Join-Path $owned 'Intermediate\ignored.txt') "owned intermediate`n"
    Write-TestP5aText (Join-Path $deployed 'Intermediate\ignored.txt') "deployed intermediate and different`n"

    $buildId = 'SYNTHETIC-BUILD-ID-13B'
    Write-TestP5aText (Join-Path $project 'Source\SyntheticGameEditor.Target.cs') @'
using UnrealBuildTool;
public sealed class SyntheticGameEditorTarget : TargetRules
{
    public SyntheticGameEditorTarget(TargetInfo Target) : base(Target) { Type = TargetType.Editor; }
}
'@
    $receipt = Join-Path $project 'Binaries\Win64\SyntheticGameEditor.target'
    Write-TestP5aText $receipt @"
{"TargetName":"SyntheticGameEditor","Platform":"Win64","Configuration":"Development","Version":{"MajorVersion":5,"MinorVersion":9,"PatchVersion":0,"BuildId":"$buildId"}}
"@
    $alsManifest = Join-Path $project 'Plugins\ALS\Binaries\Win64\UnrealEditor.modules'
    $traceManifest = Join-Path $deployed 'Binaries\Win64\UnrealEditor.modules'
    Write-TestP5aText $alsManifest @"
{"BuildId":"$buildId","Modules":{"ALS":"UnrealEditor-ALS.dll"}}
"@
    Write-TestP5aText $traceManifest @"
{"BuildId":"$buildId","Modules":{"AlsLocomotionTrace":"UnrealEditor-AlsLocomotionTrace.dll"}}
"@
    $alsDll = Join-Path (Split-Path -Parent $alsManifest) 'UnrealEditor-ALS.dll'
    $traceDll = Join-Path (Split-Path -Parent $traceManifest) 'UnrealEditor-AlsLocomotionTrace.dll'
    Write-TestP5aText $alsDll "synthetic ALS DLL`n"
    Write-TestP5aText $traceDll "synthetic trace DLL`n"

    $sourceTime = [DateTime]::UtcNow.AddMinutes(-10)
    foreach ($path in @(Get-ChildItem -LiteralPath $deployed -File -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/](Binaries|Intermediate)[\\/]'
    }))
    {
        $path.LastWriteTimeUtc = $sourceTime
    }
    [IO.File]::SetLastWriteTimeUtc($traceDll, [DateTime]::UtcNow.AddMinutes(-1))

    return [pscustomobject]@{
        Root = $root
        RepositoryRoot = $root
        OwnedRoot = $owned
        ProjectRoot = $project
        UProject = $uproject
        DeployedRoot = $deployed
        ReceiptPath = $receipt
        AlsManifestPath = $alsManifest
        TraceManifestPath = $traceManifest
        AlsDllPath = $alsDll
        TraceDllPath = $traceDll
    }
}

function New-TestP5aProcessShim
{
    param([Parameter(Mandatory)][string]$Name)

    $root = Join-Path $TestDrive $Name
    [void][IO.Directory]::CreateDirectory($root)
    $shimPath = Join-Path $root 'p5a-process-shim.ps1'
    $logPath = Join-Path $root 'direct-children.ndjson'
    $controlPath = Join-Path $root 'control.json'
    Write-TestP5aText $controlPath "{}`n"
    Write-TestP5aText $shimPath @'
param(
    [string]$ChildFilePath,
    [string[]]$ChildArguments,
    [string]$ChildWorkingDirectory,
    [string]$PhaseName,
    [string]$LogPath,
    [string]$ControlPath
)
$control = Get-Content -LiteralPath $ControlPath -Raw | ConvertFrom-Json
$call = if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
    @(Get-Content -LiteralPath $LogPath).Count + 1
} else {
    1
}
$leaseProbeWritable = @()
foreach ($probePath in @($control.ProbeLockedPaths)) {
    $writable = $false
    try {
        $probe = [IO.File]::Open(
            [string]$probePath, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $probe.Dispose()
        $writable = $true
    } catch {
        $writable = $false
    }
    $leaseProbeWritable += $writable
}
$directProcessId = 5000 + $call
$descendantCall = if ($null -ne $control.DescendantCall) { [int]$control.DescendantCall } else { 2 }
$descendantRoot = Join-Path (Split-Path -Parent $ControlPath) 'descendants'
$descendants = @()
if ($call -eq $descendantCall) {
    if ($null -ne $control.DescendantRecords) {
        $descendants = @($control.DescendantRecords | ForEach-Object {
            [pscustomobject]@{
                processId = [int]$_.processId
                parentProcessId = [int]$_.parentProcessId
                ancestorProcessIds = @($_.ancestorProcessIds | ForEach-Object { [int]$_ })
                imageName = [string]$_.imageName
                executablePath = [string]$_.executablePath
            }
        })
    } else {
        $descendantImages = @($control.DescendantImages)
        for ($index = 0; $index -lt $descendantImages.Count; $index++) {
            $image = [string]$descendantImages[$index]
            $configuredPath = $null
            if ($null -ne $control.DescendantExecutablePaths) {
                $pathProperty = @($control.DescendantExecutablePaths.PSObject.Properties | Where-Object {
                    $_.Name -ceq $image
                })
                if ($pathProperty.Count -eq 1) {
                    $configuredPaths = @($pathProperty[0].Value | ForEach-Object { [string]$_ })
                    $occurrence = 0
                    if ($index -gt 0) {
                        foreach ($priorImage in @($descendantImages[0..($index - 1)])) {
                            if ([string]$priorImage -ceq $image) { $occurrence++ }
                        }
                    }
                    if ($occurrence -lt $configuredPaths.Count) {
                        $configuredPath = $configuredPaths[$occurrence]
                    }
                }
            }
            if ([string]::IsNullOrEmpty($configuredPath)) {
                $executablePath = [IO.Path]::GetFullPath((Join-Path $descendantRoot $image))
                [void][IO.Directory]::CreateDirectory((Split-Path -Parent $executablePath))
                if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
                    [IO.File]::WriteAllText($executablePath, 'test descendant image')
                }
            } else {
                $executablePath = $configuredPath
            }
            $descendants += [pscustomobject]@{
                processId = 10000 + ($call * 100) + $index
                parentProcessId = $directProcessId
                ancestorProcessIds = @($directProcessId)
                imageName = $image
                executablePath = $executablePath
            }
        }
    }
}
$record = [ordered]@{
    filePath = $ChildFilePath
    arguments = @($ChildArguments)
    workingDirectory = $ChildWorkingDirectory
    phase = $PhaseName
    processId = $directProcessId
    descendantProcesses = @($descendants)
    p5aStagingRootEnvironment = [Environment]::GetEnvironmentVariable(
        'GODOTALS_P5A_STAGING_ROOT', [EnvironmentVariableTarget]::Process)
    p5aPrebuiltOracleApphostEnvironment = [Environment]::GetEnvironmentVariable(
        'GODOTALS_P5A_PREBUILT_ORACLE_APPHOST', [EnvironmentVariableTarget]::Process)
    leaseProbeWritable = @($leaseProbeWritable)
}
Add-Content -LiteralPath $LogPath -Encoding UTF8 -Value ($record | ConvertTo-Json -Compress -Depth 20)
function Get-SeparateValue([string]$Name) {
    $index = [Array]::IndexOf([string[]]$ChildArguments, $Name)
    if ($index -lt 0 -or $index + 1 -ge $ChildArguments.Count) { return $null }
    return $ChildArguments[$index + 1]
}
function Get-PrefixedValue([string]$Prefix) {
    $match = @($ChildArguments | Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) })
    if ($match.Count -ne 1) { return $null }
    return $match[0].Substring($Prefix.Length)
}
function Write-Utf8([string]$Path, [string]$Value) {
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}
$mode = if ($ChildArguments.Count -gt 0) { $ChildArguments[0] } else { '' }
$marker = ''
if ($mode -ceq '--write-native-plan') {
    $output = Get-SeparateValue '--output'
    $bytes = if ($call -eq 2 -and $control.PlanBDrift) {
        "{`"representation`":`"plan-b-drift`"}`n"
    } else {
        "{`"representation`":`"plan`"}`n"
    }
    Write-Utf8 $output $bytes
    $planHash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([int]$control.PlanMarkerMismatchCall -eq $call) { $planHash = 'f' * 64 }
    $marker = "P5A_ORACLE_DIGESTS layout=d6fef54173240d32 bindings=2b4be600d531c734 graph=44403c2869d8f615 plan=$planHash"
}
elseif ($mode -ceq '--write-canonical-pair') {
    $nativeBytes = if ($call -eq 7 -and $control.NativeBDrift) {
        "{`"representation`":`"native-b-drift`"}`n"
    } else {
        "{`"representation`":`"native`"}`n"
    }
    $portBytes = if ($call -eq 7 -and $control.PortBDrift) {
        "{`"representation`":`"port-b-drift`"}`n"
    } else {
        "{`"representation`":`"port`"}`n"
    }
    Write-Utf8 (Get-SeparateValue '--native-canonical') $nativeBytes
    Write-Utf8 (Get-SeparateValue '--port-canonical') $portBytes
    $plan = Get-SeparateValue '--trace-plan'
    $planHash = (Get-FileHash -LiteralPath $plan -Algorithm SHA256).Hash.ToLowerInvariant()
    $marker = "P5A_ORACLE_DIGESTS layout=d6fef54173240d32 bindings=2b4be600d531c734 graph=44403c2869d8f615 plan=$planHash"
}
elseif ($mode -ceq '--verify-fixture') {
    $marker = 'P5A_ORACLE_DIGESTS layout=d6fef54173240d32 bindings=2b4be600d531c734 graph=44403c2869d8f615 plan=' + ('a' * 64)
}
elseif ($mode -ceq 'test') {
    $loggerIndex = [Array]::IndexOf([string[]]$ChildArguments, '--logger')
    $trxPath = $ChildArguments[$loggerIndex + 1].Substring('trx;LogFileName='.Length)
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $trxPath))
    $values = [ordered]@{ total = 1; passed = 1; failed = 0; error = 0; notExecuted = 0; skipped = 0 }
    if ($control.TrxMode -ceq 'zero') { $values.total = 0; $values.passed = 0 }
    elseif ($control.TrxMode -ceq 'partial') { $values.total = 2; $values.passed = 1 }
    elseif ($control.TrxMode -ceq 'failed') { $values.passed = 0; $values.failed = 1 }
    elseif ($control.TrxMode -ceq 'error') { $values.passed = 0; $values.error = 1 }
    elseif ($control.TrxMode -ceq 'notExecuted') { $values.passed = 0; $values.notExecuted = 1 }
    elseif ($control.TrxMode -ceq 'skipped') { $values.passed = 0; $values.skipped = 1 }
    if ($control.TrxMode -cne 'missing') {
        $trx = '<TestRun><ResultSummary outcome="Completed"><Counters total="{0}" passed="{1}" failed="{2}" error="{3}" notExecuted="{4}" skipped="{5}" /></ResultSummary></TestRun>' -f $values.total,$values.passed,$values.failed,$values.error,$values.notExecuted,$values.skipped
        if ($control.TrxMode -ceq 'malformed') { $trx = '<TestRun>' }
        Write-Utf8 $trxPath $trx
    }
    if ($control.ExtraTrx) { Write-Utf8 (Join-Path (Split-Path -Parent $trxPath) 'extra.trx') '<TestRun />' }
}
elseif (@($ChildArguments | Where-Object { $_ -ceq '-ReadyCheck' }).Count -eq 1) {
    $marker = 'P5A_TRACE_READY_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17'
}
else {
    $output = Get-PrefixedValue '-Output='
    if ($null -ne $output) {
        $rawBytes = if ($call -eq 5 -and $control.RawBDrift) {
            "{`"representation`":`"raw-b-drift`"}`n"
        } else {
            "{`"representation`":`"raw`"}`n"
        }
        Write-Utf8 $output $rawBytes
    }
    $marker = 'P5A_TRACE_GENERATION_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17'
}
if ([int]$control.MutatePlanOnCall -eq $call) {
    $planArgument = Get-PrefixedValue '-P5ATracePlan='
    if ($null -eq $planArgument) { $planArgument = Get-SeparateValue '--trace-plan' }
    if ($null -ne $planArgument) { [IO.File]::AppendAllText($planArgument, 'drift') }
}
$stdOutLines = if ([string]::IsNullOrEmpty($marker)) { @('synthetic dotnet output') } else { @($marker) }
$stdErrLines = @()
if ([int]$control.FailCall -eq $call) {
    switch ([string]$control.FailureKind) {
        'missing' { $stdOutLines = @('ordinary output') }
        'duplicate' { $stdOutLines = @($marker, $marker) }
        'prefix' { $stdOutLines = @("prefix $marker") }
        'suffix' { $stdOutLines = @("$marker suffix") }
        'wrong' { $stdOutLines = @('P5A_TRACE_GENERATION_OK cases=7 commit=wrong') }
        'warning' { $stdErrLines = @('LogTemp: Warning: synthetic warning') }
        'error' { $stdErrLines = @('LogTemp: Error: synthetic error') }
        'stderr-marker' { $stdOutLines = @('ordinary output'); $stdErrLines = @($marker) }
    }
}
$exitCode = if ([int]$control.FailCall -eq $call -and $control.FailureKind -ceq 'nonzero') { 7 } else { 0 }
$timedOut = [int]$control.FailCall -eq $call -and $control.FailureKind -ceq 'timeout'
return [pscustomobject]@{
    ProcessId = $directProcessId
    ExitCode = $exitCode
    TimedOut = $timedOut
    OutputLimitExceeded = $false
    StdOutLines = @($stdOutLines)
    StdErrLines = @($stdErrLines)
    StdOutBytes = [Text.Encoding]::UTF8.GetByteCount(($stdOutLines -join "`n"))
    StdErrBytes = [Text.Encoding]::UTF8.GetByteCount(($stdErrLines -join "`n"))
    # Transitional only: protocol tests keep exercising the pre-split scripts until
    # their production gates consume StdOutLines and StdErrLines directly.
    OutputLines = @($stdOutLines) + @($stdErrLines)
    DescendantProcesses = @($descendants)
    JobTotalProcesses = 1 + @($descendants).Count
    JobActiveProcesses = 0
    JobProcessIds = @($directProcessId) + @($descendants | ForEach-Object { [int]$_.processId })
}
'@
    $capturedShimPath = $shimPath
    $capturedLogPath = $logPath
    $capturedControlPath = $controlPath
    $invoker = {
        param($FilePath, $Arguments, $WorkingDirectory, $TimeoutSeconds, $PhaseName)
        & $capturedShimPath `
            -ChildFilePath $FilePath `
            -ChildArguments @($Arguments) `
            -ChildWorkingDirectory $WorkingDirectory `
            -PhaseName $PhaseName `
            -LogPath $capturedLogPath `
            -ControlPath $capturedControlPath
    }.GetNewClosure()
    return [pscustomobject]@{
        Root = $root
        ShimPath = $shimPath
        LogPath = $logPath
        ControlPath = $controlPath
        Invoker = $invoker
    }
}

function Set-TestP5aShimControl
{
    param([Parameter(Mandatory)][object]$Shim, [Parameter(Mandatory)][hashtable]$Values)

    Write-TestP5aText $Shim.ControlPath (($Values | ConvertTo-Json -Depth 10 -Compress) + "`n")
}

function Get-TestP5aShimRecords
{
    param([Parameter(Mandatory)][object]$Shim)

    if (-not (Test-Path -LiteralPath $Shim.LogPath -PathType Leaf)) { return @() }
    return @(Get-Content -LiteralPath $Shim.LogPath | ForEach-Object { $_ | ConvertFrom-Json })
}

function New-TestP5aEnvironmentMutatingInvoker
{
    param(
        [Parameter(Mandatory)][object]$Shim,
        [AllowNull()][string]$Value,
        [switch]$Remove
    )

    $innerInvoker = $Shim.Invoker
    $state = [Collections.Generic.List[int]]::new()
    $capturedState = $state
    $capturedValue = $Value
    $capturedRemove = $Remove.IsPresent
    return {
        param($FilePath, $Arguments, $WorkingDirectory, $TimeoutSeconds, $PhaseName)

        $capturedState.Add($capturedState.Count + 1)
        if ($capturedState.Count -eq 1)
        {
            if ($capturedRemove)
            {
                Remove-Item Env:GODOTALS_P5A_STAGING_ROOT -ErrorAction SilentlyContinue
            }
            else
            {
                [Environment]::SetEnvironmentVariable(
                    'GODOTALS_P5A_STAGING_ROOT', $capturedValue,
                    [EnvironmentVariableTarget]::Process)
            }
        }
        & $innerInvoker `
            -FilePath $FilePath `
            -Arguments @($Arguments) `
            -WorkingDirectory $WorkingDirectory `
            -TimeoutSeconds $TimeoutSeconds `
            -PhaseName $PhaseName
    }.GetNewClosure()
}

function New-TestP5aProcessContext
{
    param([Parameter(Mandatory)][string]$Name)

    $oracle = New-TestP5aOracleRepository "$Name-repository"
    $root = $oracle.Root
    $deployment = New-TestP5aDeployment "$Name-deployment" -RepositoryRoot $root
    $editor = Join-Path $TestDrive "$Name-shims\UnrealEditorCmd.exe"
    $uproject = $deployment.UProject
    $reference = Join-Path $TestDrive "$Name-reference"
    $staging = Join-Path $TestDrive "$Name-staging"
    $fixture = Join-Path $TestDrive "$Name-fixture\trace_p5a.json"
    Write-TestP5aText $editor "synthetic editor path; never executed`n"
    Write-TestP5aText $uproject "{}`n"
    [void][IO.Directory]::CreateDirectory($reference)
    Write-TestP5aText $fixture "synthetic committed fixture`n"
    return [pscustomobject]@{
        Root = $root
        Oracle = $oracle
        Deployment = $deployment
        Editor = $editor
        UProject = $uproject
        Reference = $reference
        Staging = $staging
        Fixture = $fixture
    }
}

function Get-TestP5aOracleLeasePaths
{
    param([Parameter(Mandatory)][object]$Context)

    $paths = @(
        @($Context.Oracle.SourcePaths)
        $Context.Oracle.ManifestPath
        @($Context.Oracle.ArtifactPaths)
    ) | ForEach-Object { [IO.Path]::GetFullPath([string]$_) }
    @($paths | Sort-Object -Unique).Count | Should Be $paths.Count
    return [string[]]$paths
}

function Get-TestP5aVerifierLeasePaths
{
    param([Parameter(Mandatory)][object]$Context)

    $testRoot = [IO.Path]::GetFullPath((Join-Path `
        $Context.Root 'tests\Als.Core.Tests'))
    $testSources = [string[]]@(Get-ChildItem -LiteralPath $testRoot `
        -Filter '*.cs' -File -Recurse | Where-Object {
            $relative = [IO.Path]::GetRelativePath($testRoot, $_.FullName)
            -not @($relative.Split([IO.Path]::DirectorySeparatorChar) | Where-Object {
                $_.Equals('bin', [StringComparison]::OrdinalIgnoreCase) -or
                $_.Equals('obj', [StringComparison]::OrdinalIgnoreCase) -or
                $_.Equals('TestResults', [StringComparison]::OrdinalIgnoreCase)
            }).Count
        } | ForEach-Object { [IO.Path]::GetFullPath($_.FullName) })
    [Array]::Sort($testSources, [StringComparer]::Ordinal)
    return [string[]]@(
        @(Get-TestP5aOracleLeasePaths $Context)
        [IO.Path]::GetFullPath($Context.Fixture)
        [IO.Path]::GetFullPath((Join-Path `
            $Context.Root 'tools\schemas\als_p5a_trace.schema.json'))
        [IO.Path]::GetFullPath((Join-Path `
            $Context.Root 'tools\schemas\als_p5a_trace_plan.schema.json'))
        [IO.Path]::GetFullPath((Join-Path `
            $Context.Root 'assets\generated\als_v4\als_manifest.json'))
        [IO.Path]::GetFullPath((Join-Path `
            $Context.Root 'assets\config\p3_locomotion_profile.json'))
        [IO.Path]::GetFullPath((Join-Path `
            $Context.Root 'assets\config\p4_pose_profile.json'))
        [IO.Path]::GetFullPath((Join-Path `
            $Context.Root 'assets\config\p5a_animation_runtime.json'))
        [IO.Path]::GetFullPath((Join-Path `
            $testRoot 'Als.Core.Tests.csproj'))
        @($testSources)
    )
}

function Get-TestP5aDeploymentLeasePaths
{
    param([Parameter(Mandatory)][object]$Context)

    $ownedPaths = @(Get-ChildItem -LiteralPath $Context.Deployment.OwnedRoot -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](Binaries|Intermediate)([\\/]|$)' } |
        ForEach-Object { $_.FullName })
    $deployedPaths = @(Get-ChildItem -LiteralPath $Context.Deployment.DeployedRoot -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](Binaries|Intermediate)([\\/]|$)' } |
        ForEach-Object { $_.FullName })
    $evidencePaths = @(
        $Context.Deployment.ReceiptPath
        $Context.Deployment.AlsManifestPath
        $Context.Deployment.TraceManifestPath
        $Context.Deployment.AlsDllPath
        $Context.Deployment.TraceDllPath
    )
    $paths = @($ownedPaths) + @($deployedPaths) + @($evidencePaths) |
        ForEach-Object { [IO.Path]::GetFullPath([string]$_) }
    @($paths | Sort-Object -Unique).Count | Should Be $paths.Count
    return [string[]]$paths
}

function Get-TestP5aGeneratorLeasePaths
{
    param([Parameter(Mandatory)][object]$Context)

    $paths = @(Get-TestP5aOracleLeasePaths $Context) +
        @(Get-TestP5aDeploymentLeasePaths $Context)
    @($paths | Sort-Object -Unique).Count | Should Be $paths.Count
    return [string[]]$paths
}

function Assert-TestP5aLeaseProbeRecords
{
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Records,
        [Parameter(Mandatory)][string[]]$ExpectedPaths
    )

    $ExpectedPaths.Count | Should BeGreaterThan 0
    foreach ($record in $Records)
    {
        @($record.leaseProbeWritable).Count | Should Be $ExpectedPaths.Count
        @($record.leaseProbeWritable | Where-Object { $_ -eq $true }).Count | Should Be 0
    }
}

function Assert-TestP5aDescendantRecordShape
{
    param(
        [Parameter(Mandatory)][object]$DirectChildRecord,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$ExpectedImages,
        [string[]]$ExpectedExecutablePaths = @()
    )

    $directProcessId = [int]$DirectChildRecord.processId
    $directProcessId | Should BeGreaterThan 0
    $descendants = @($DirectChildRecord.descendantProcesses)
    $descendants.Count | Should Be $ExpectedImages.Count
    @($descendants | ForEach-Object { [int]$_.processId } | Sort-Object -Unique).Count |
        Should Be $descendants.Count
    @($descendants | ForEach-Object { [string]$_.imageName }) |
        Should Be @($ExpectedImages)
    if ($ExpectedExecutablePaths.Count -gt 0)
    {
        @($descendants | ForEach-Object {
            [IO.Path]::GetFullPath([string]$_.executablePath)
        }) | Should Be @($ExpectedExecutablePaths | ForEach-Object {
            [IO.Path]::GetFullPath($_)
        })
    }
    $knownProcessIds = @($directProcessId) +
        @($descendants | ForEach-Object { [int]$_.processId })
    foreach ($descendant in $descendants)
    {
        $processId = [int]$descendant.processId
        $parentProcessId = [int]$descendant.parentProcessId
        $processId | Should BeGreaterThan 0
        $parentProcessId | Should Not Be $processId
        $knownProcessIds | Should Contain $parentProcessId
        $ancestors = @($descendant.ancestorProcessIds | ForEach-Object { [int]$_ })
        $ancestors.Count | Should BeGreaterThan 0
        @($ancestors | Sort-Object -Unique).Count | Should Be $ancestors.Count
        $ancestors | Should Not Contain $processId
        $ancestors[0] | Should Be $directProcessId
        $ancestors[-1] | Should Be $parentProcessId
        if ($parentProcessId -eq $directProcessId)
        {
            $ancestors | Should Be @($directProcessId)
        }
        else
        {
            $parentRecords = @($descendants | Where-Object {
                [int]$_.processId -eq $parentProcessId
            })
            $parentRecords.Count | Should Be 1
            $expectedAncestors = @(
                @($parentRecords[0].ancestorProcessIds | ForEach-Object { [int]$_ }) +
                $parentProcessId)
            $ancestors | Should Be $expectedAncestors
        }
        [IO.Path]::IsPathFullyQualified([string]$descendant.executablePath) |
            Should Be $true
        Test-Path -LiteralPath ([string]$descendant.executablePath) -PathType Leaf |
            Should Be $true
        [IO.Path]::GetFileName([string]$descendant.executablePath) |
            Should Be ([string]$descendant.imageName)
    }
}

function Get-TestP5aExpectedProcessPaths
{
    param([Parameter(Mandatory)][object]$Context)

    return [pscustomobject]@{
        PlanA = Join-Path $Context.Staging 'plan-a.json'
        PlanB = Join-Path $Context.Staging 'plan-b.json'
        ReadyOutput = Join-Path $Context.Staging 'ready-check.json'
        RawA = Join-Path $Context.Staging 'raw-a.json'
        RawB = Join-Path $Context.Staging 'raw-b.json'
        NativeA = Join-Path $Context.Staging 'native-a.json'
        NativeB = Join-Path $Context.Staging 'native-b.json'
        PortA = Join-Path $Context.Staging 'port-a.json'
        PortB = Join-Path $Context.Staging 'port-b.json'
        ReadyLog = Join-Path $Context.Staging 'ready-check.log'
        CaptureALog = Join-Path $Context.Staging 'capture-a.log'
        CaptureBLog = Join-Path $Context.Staging 'capture-b.log'
        Trx = Join-Path $Context.Staging 'p5a-golden-tests.trx'
    }
}

function Assert-TestP5aPathUnderRoot
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    [IO.Path]::IsPathFullyQualified($Path) | Should Be $true
    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    [IO.Path]::GetFullPath($Path).StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) |
        Should Be $true
}

function Invoke-TestP5aExternalProcess
{
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 180
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    foreach ($entry in $Environment.GetEnumerator())
    {
        if ($null -eq $entry.Value)
        {
            [void]$startInfo.Environment.Remove([string]$entry.Key)
        }
        else
        {
            $startInfo.Environment[[string]$entry.Key] = [string]$entry.Value
        }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try
    {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000))
        {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Synthetic process timed out: $FilePath"
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = $stdout.GetAwaiter().GetResult()
            StdErr = $stderr.GetAwaiter().GetResult()
        }
    }
    finally
    {
        $process.Dispose()
    }
}

function Get-TestP5aDotnetApplicationPath
{
    $commands = @(Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue)
    if ($commands.Count -eq 0)
    {
        'Missing 13B capability: dotnet application selected by global.json' | Should BeNullOrEmpty
    }
    return [IO.Path]::GetFullPath([string]$commands[0].Source)
}

function Get-TestP5aSelectedDotnetDescendantClosure
{
    param(
        [string]$DotnetPath,
        [string]$WorkingDirectory = $script:P5aRepositoryRoot
    )

    if ([string]::IsNullOrEmpty($DotnetPath))
    {
        $DotnetPath = Get-TestP5aDotnetApplicationPath
    }
    $dotnetPath = [IO.Path]::GetFullPath($DotnetPath)
    $cacheKey = $dotnetPath.ToUpperInvariant()
    if ($script:P5aSelectedDotnetClosures.ContainsKey($cacheKey))
    {
        return $script:P5aSelectedDotnetClosures[$cacheKey]
    }
    $WorkingDirectory = [IO.Path]::GetFullPath($WorkingDirectory)
    $versionRun = Invoke-TestP5aExternalProcess `
        -FilePath $dotnetPath -Arguments @('--version') -WorkingDirectory $WorkingDirectory
    $versionRun.ExitCode | Should Be 0
    $version = $versionRun.StdOut.Trim()
    $version | Should Match '^8\.0\.[0-9]+$'
    $sdkListRun = Invoke-TestP5aExternalProcess `
        -FilePath $dotnetPath -Arguments @('--list-sdks') -WorkingDirectory $WorkingDirectory
    $sdkListRun.ExitCode | Should Be 0
    $sdkBaseMatches = @($sdkListRun.StdOut -split '\r?\n' | ForEach-Object {
        if ($_ -match ('^' + [regex]::Escape($version) + '\s+\[(.+)\]$'))
        {
            [IO.Path]::GetFullPath($Matches[1])
        }
    } | Where-Object { -not [string]::IsNullOrEmpty($_) })
    $sdkBaseMatches.Count | Should Be 1
    $sdkRoot = [IO.Path]::GetFullPath((Join-Path $sdkBaseMatches[0] $version))
    Test-Path -LiteralPath $sdkRoot -PathType Container | Should Be $true
    $pathsByImage = [ordered]@{}
    $dotnetImage = [IO.Path]::GetFileName($dotnetPath)
    $dotnetImage | Should Be 'dotnet.exe'
    $pathsByImage[$dotnetImage] = [string[]]@($dotnetPath)
    foreach ($candidateImage in @('MSBuild.exe', 'testhost.exe', 'vstest.console.exe'))
    {
        $matches = @(Get-ChildItem -LiteralPath $sdkRoot -File -Recurse | Where-Object {
            $_.Name -ceq $candidateImage
        } | Sort-Object -Property FullName)
        foreach ($match in $matches)
        {
            Assert-TestP5aPathUnderRoot -Path $match.FullName -Root $sdkRoot
        }
        if ($matches.Count -gt 0)
        {
            $pathsByImage[$candidateImage] = [string[]]@(
                $matches | ForEach-Object { [IO.Path]::GetFullPath($_.FullName) })
        }
    }
    $allowedRecords = @($pathsByImage.GetEnumerator() | ForEach-Object {
        $imageName = [string]$_.Key
        @($_.Value | ForEach-Object {
            [pscustomobject]@{
                ImageName = $imageName
                ExecutablePath = [IO.Path]::GetFullPath([string]$_)
            }
        })
    })
    $allowedRecords.Count | Should BeGreaterThan 0
    $closure = [pscustomobject]@{
        DotnetPath = $dotnetPath
        SdkRoot = $sdkRoot
        PathsByImage = $pathsByImage
        AllowedRecords = @($allowedRecords)
        ImageSequence = @($allowedRecords | ForEach-Object { [string]$_.ImageName })
        ExecutablePathSequence = @($allowedRecords | ForEach-Object {
            [string]$_.ExecutablePath
        })
    }
    $script:P5aSelectedDotnetClosures[$cacheKey] = $closure
    return $closure
}

function Invoke-TestP5aIsolatedEntrypoint
{
    param(
        [Parameter(Mandatory)][ValidateSet('generator', 'verifier')][string]$Kind,
        [Parameter(Mandatory)][object]$Context,
        [Parameter(Mandatory)][object]$Shim
    )

    $entrypointPath = if ($Kind -ceq 'generator') {
        $script:P5aGeneratorPath
    } else {
        $script:P5aVerifierPath
    }
    $source = if ($Kind -ceq 'generator') {
@'
param(
    $EntrypointPath, $RepositoryRoot, $UnrealEditorCmd, $UnrealProject,
    $ReferenceRoot, $StagingRoot, $DestinationPath,
    $ShimPath, $LogPath, $ControlPath
)
$capturedShimPath = $ShimPath
$capturedLogPath = $LogPath
$capturedControlPath = $ControlPath
$processInvoker = {
    param($FilePath, $Arguments, $WorkingDirectory, $TimeoutSeconds, $PhaseName)
    & $capturedShimPath `
        -ChildFilePath $FilePath `
        -ChildArguments @($Arguments) `
        -ChildWorkingDirectory $WorkingDirectory `
        -PhaseName $PhaseName `
        -LogPath $capturedLogPath `
        -ControlPath $capturedControlPath
}.GetNewClosure()
& $EntrypointPath `
    -RepositoryRoot $RepositoryRoot `
    -UnrealEditorCmd $UnrealEditorCmd `
    -UnrealProject $UnrealProject `
    -ReferenceRoot $ReferenceRoot `
    -StagingRoot $StagingRoot `
    -DestinationPath $DestinationPath `
    -ProcessInvoker $processInvoker `
    -TimeoutSeconds 10
'@
    } else {
@'
param(
    $EntrypointPath, $RepositoryRoot, $FixturePath, $StagingRoot,
    $ShimPath, $LogPath, $ControlPath
)
$capturedShimPath = $ShimPath
$capturedLogPath = $LogPath
$capturedControlPath = $ControlPath
$processInvoker = {
    param($FilePath, $Arguments, $WorkingDirectory, $TimeoutSeconds, $PhaseName)
    & $capturedShimPath `
        -ChildFilePath $FilePath `
        -ChildArguments @($Arguments) `
        -ChildWorkingDirectory $WorkingDirectory `
        -PhaseName $PhaseName `
        -LogPath $capturedLogPath `
        -ControlPath $capturedControlPath
}.GetNewClosure()
& $EntrypointPath `
    -RepositoryRoot $RepositoryRoot `
    -FixturePath $FixturePath `
    -StagingRoot $StagingRoot `
    -ProcessInvoker $processInvoker `
    -TimeoutSeconds 10
'@
    }

    $powerShell = [Management.Automation.PowerShell]::Create()
    try
    {
        [void]$powerShell.AddScript($source)
        [void]$powerShell.AddArgument($entrypointPath)
        [void]$powerShell.AddArgument($Context.Root)
        if ($Kind -ceq 'generator')
        {
            [void]$powerShell.AddArgument($Context.Editor)
            [void]$powerShell.AddArgument($Context.UProject)
            [void]$powerShell.AddArgument($Context.Reference)
            [void]$powerShell.AddArgument($Context.Staging)
            [void]$powerShell.AddArgument($Context.Fixture)
        }
        else
        {
            [void]$powerShell.AddArgument($Context.Fixture)
            [void]$powerShell.AddArgument($Context.Staging)
        }
        [void]$powerShell.AddArgument($Shim.ShimPath)
        [void]$powerShell.AddArgument($Shim.LogPath)
        [void]$powerShell.AddArgument($Shim.ControlPath)
        $output = @($powerShell.Invoke())
        $errors = @($powerShell.Streams.Error | ForEach-Object { $_.ToString() })
        if ($powerShell.HadErrors -or $errors.Count -ne 0)
        {
            throw "Isolated $Kind entrypoint failed: $($errors -join ' | ')"
        }
        return $output
    }
    finally
    {
        $powerShell.Dispose()
    }
}

function Invoke-TestP5aDefaultRunner
{
    param(
        [Parameter(Mandatory)][ValidateSet('generator', 'verifier')][string]$Kind,
        [Parameter(Mandatory)][string]$EntrypointPath,
        [Parameter(Mandatory)][string]$RunnerName,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$PhaseName
    )

    $source = @'
param(
    $Kind, $EntrypointPath, $RunnerName, $FilePath, $Arguments,
    $WorkingDirectory, $TimeoutSeconds, $PhaseName
)
$requestedTimeoutSeconds = $TimeoutSeconds
if ($Kind -ceq 'generator') {
    . $EntrypointPath `
        -UnrealEditorCmd '13b-default-runner-dot-source' `
        -UnrealProject '13b-default-runner-dot-source' `
        -ReferenceRoot '13b-default-runner-dot-source'
} else {
    . $EntrypointPath
}
$runner = Get-Command -Name $RunnerName -CommandType Function -ErrorAction Stop
& $runner `
    -FilePath $FilePath `
    -Arguments @($Arguments) `
    -WorkingDirectory $WorkingDirectory `
    -TimeoutSeconds $requestedTimeoutSeconds `
    -PhaseName $PhaseName
'@
    $powerShell = [Management.Automation.PowerShell]::Create()
    try
    {
        [void]$powerShell.AddScript($source)
        [void]$powerShell.AddArgument($Kind)
        [void]$powerShell.AddArgument($EntrypointPath)
        [void]$powerShell.AddArgument($RunnerName)
        [void]$powerShell.AddArgument($FilePath)
        [void]$powerShell.AddArgument([string[]]$Arguments)
        [void]$powerShell.AddArgument($WorkingDirectory)
        [void]$powerShell.AddArgument($TimeoutSeconds)
        [void]$powerShell.AddArgument($PhaseName)
        $output = @($powerShell.Invoke())
        $errors = @($powerShell.Streams.Error | ForEach-Object { $_.ToString() })
        if ($powerShell.HadErrors -or $errors.Count -ne 0)
        {
            throw "Default $Kind runner failed: $($errors -join ' | ')"
        }
        return $output
    }
    finally
    {
        $powerShell.Dispose()
    }
}

function Assert-TestP5aAtomicPublicationClosure
{
    param([Parameter(Mandatory)][string]$Path)

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $Path, [ref]$tokens, [ref]$errors)
    @($errors).Count | Should Be 0
    $publishDefinitions = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'Publish-P5aFixtureAtomically'
    }, $true))
    $publishDefinitions.Count | Should Be 1
    $publish = $publishDefinitions[0]

    $defaultParameters = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.ParameterAst] -and
            $node.Name.VariablePath.UserPath -ceq 'FileSystemInvoker' -and
            $null -ne $node.DefaultValue
    }, $true))
    $defaultParameters.Count | Should Be 1
    ($defaultParameters[0].DefaultValue -is
        [Management.Automation.Language.VariableExpressionAst]) | Should Be $true
    $defaultPath = [string]$defaultParameters[0].DefaultValue.VariablePath.UserPath
    $defaultPath | Should Match '^function:.+'
    $runnerName = $defaultPath.Substring('function:'.Length)
    $runnerDefinitions = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq $runnerName
    }, $true))
    $runnerDefinitions.Count | Should Be 1
    $runner = $runnerDefinitions[0]

    $fileOperations = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and
            $node.Expression -is [Management.Automation.Language.TypeExpressionAst] -and
            $node.Expression.TypeName.FullName -match '^(System\.)?IO\.File$' -and
            $node.Member.Value -in @('Copy', 'Replace', 'Move')
    }, $true))
    foreach ($operation in @('Copy', 'Replace', 'Move'))
    {
        $matches = @($fileOperations | Where-Object { $_.Member.Value -ceq $operation })
        $matches.Count | Should Be 1
        ($matches[0].Extent.StartOffset -ge $runner.Extent.StartOffset -and
         $matches[0].Extent.EndOffset -le $runner.Extent.EndOffset) | Should Be $true
    }
    $copyOperation = @($fileOperations | Where-Object { $_.Member.Value -ceq 'Copy' })[0]
    $copyOperation.Extent.Text | Should Not Match '(?i),\s*\$true\s*\)$'

    foreach ($commitOperation in @($fileOperations | Where-Object {
        $_.Member.Value -in @('Replace', 'Move')
    }))
    {
        $commitStatement = $commitOperation
        while ($null -ne $commitStatement.Parent -and
               $commitStatement.Parent -isnot [Management.Automation.Language.StatementBlockAst])
        {
            $commitStatement = $commitStatement.Parent
        }
        ($commitStatement.Parent -is [Management.Automation.Language.StatementBlockAst]) |
            Should Be $true
        $commitBlock = $commitStatement.Parent
        $statementIndex = [Array]::IndexOf(
            [Management.Automation.Language.StatementAst[]]@($commitBlock.Statements),
            $commitStatement)
        ($statementIndex -ge 0) | Should Be $true
        $runnerTail = @($commitBlock.Statements | Select-Object -Skip ($statementIndex + 1))
        $runnerTail.Count | Should Be 1
        ($runnerTail[0] -is [Management.Automation.Language.ReturnStatementAst]) |
            Should Be $true
        @($runnerTail[0].FindAll({
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -or
                $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -or
                $node -is [Management.Automation.Language.ThrowStatementAst] -or
                $node -is [Management.Automation.Language.ExitStatementAst]
        }, $true)).Count | Should Be 0
    }

    $fileSystemInvokerCalls = @($publish.FindAll({
        param($node)
        $node -is [Management.Automation.Language.CommandAst] -and
            $node.InvocationOperator -eq [Management.Automation.Language.TokenKind]::Ampersand -and
            $node.CommandElements.Count -gt 1 -and
            $node.CommandElements[0] -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.CommandElements[0].VariablePath.UserPath -ceq 'FileSystemInvoker'
    }, $true))
    $fileSystemInvokerCalls.Count | Should Be 3
    @($fileSystemInvokerCalls | ForEach-Object {
        $operationNode = $_.CommandElements[1]
        ($operationNode -is [Management.Automation.Language.StringConstantExpressionAst]) |
            Should Be $true
        [string]$operationNode.Value
    } | Sort-Object) | Should Be @('File.Copy', 'File.Move', 'File.Replace')

    @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.BinaryExpressionAst] -and
            $node.Extent.Text -match '(?i)\$FileSystemInvoker\b'
    }, $true)).Count | Should Be 0
    @($ast.FindAll({
        param($node)
        if ($node -isnot [Management.Automation.Language.IfStatementAst]) { return $false }
        foreach ($clause in $node.Clauses)
        {
            if ($clause.Item1.Extent.Text -match '(?i)\$FileSystemInvoker\b')
            {
                return $true
            }
        }
        return $false
    }, $true)).Count | Should Be 0

    return [pscustomobject]@{
        Ast = $ast
        Publish = $publish
        Runner = $runner
        RunnerCommitCalls = @($fileOperations | Where-Object {
            $_.Member.Value -in @('Replace', 'Move')
        })
        DefaultParameter = $defaultParameters[0]
        CommitCalls = @($fileSystemInvokerCalls | Where-Object {
            [string]$_.CommandElements[1].Value -in @('File.Replace', 'File.Move')
        })
    }
}

function Assert-TestP5aBestEffortFinally
{
    param([Parameter(Mandatory)][Management.Automation.Language.StatementBlockAst]$Finally)

    $fallibleNodes = @($Finally.FindAll({
        param($node)
        $node -is [Management.Automation.Language.CommandAst] -or
            $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -or
            $node -is [Management.Automation.Language.ThrowStatementAst] -or
            $node -is [Management.Automation.Language.ExitStatementAst]
    }, $true))
    foreach ($node in $fallibleNodes)
    {
        $guard = $node.Parent
        while ($null -ne $guard -and
               $guard -isnot [Management.Automation.Language.TryStatementAst] -and
               $guard -ne $Finally)
        {
            $guard = $guard.Parent
        }
        ($guard -is [Management.Automation.Language.TryStatementAst]) | Should Be $true
        $guard.CatchClauses.Count | Should BeGreaterThan 0
        foreach ($catch in $guard.CatchClauses)
        {
            @($catch.Body.FindAll({
                param($catchNode)
                $catchNode -is [Management.Automation.Language.CommandAst] -or
                    $catchNode -is [Management.Automation.Language.InvokeMemberExpressionAst] -or
                    $catchNode -is [Management.Automation.Language.ThrowStatementAst] -or
                    $catchNode -is [Management.Automation.Language.ExitStatementAst]
            }, $true)).Count | Should Be 0
        }
    }
}

function Invoke-TestP5aObservedDefaultAtomicPublication
{
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $destinationDirectory = Split-Path -Parent $DestinationPath
    [void][IO.Directory]::CreateDirectory($destinationDirectory)
    $destinationExisted = Test-Path -LiteralPath $DestinationPath -PathType Leaf
    $oldHandle = $null
    if ($destinationExisted)
    {
        $oldHandle = [IO.File]::Open(
            $DestinationPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    }
    $watcher = [IO.FileSystemWatcher]::new($destinationDirectory)
    $watcher.NotifyFilter = [IO.NotifyFilters]::FileName
    $eventPrefix = 'p5a-atomic-' + [Guid]::NewGuid().ToString('N')
    $renamedId = $eventPrefix + '-renamed'
    $deletedId = $eventPrefix + '-deleted'
    try
    {
        Register-ObjectEvent -InputObject $watcher -EventName Renamed `
            -SourceIdentifier $renamedId | Out-Null
        Register-ObjectEvent -InputObject $watcher -EventName Deleted `
            -SourceIdentifier $deletedId | Out-Null
        $watcher.EnableRaisingEvents = $true
        $output = @(Publish-P5aFixtureAtomically `
            -ValidatedFixturePath $SourcePath `
            -DestinationPath $DestinationPath)

        $expectedRenameCount = 1
        $renamedRecords = [Collections.Generic.List[object]]::new()
        $deadline = [DateTime]::UtcNow.AddSeconds(3)
        while ($renamedRecords.Count -lt $expectedRenameCount -and
               [DateTime]::UtcNow -lt $deadline)
        {
            $eventRecord = Wait-Event -SourceIdentifier $renamedId -Timeout 1
            if ($null -ne $eventRecord)
            {
                $renamedRecords.Add([pscustomobject]@{
                    OldFullPath = [IO.Path]::GetFullPath(
                        [string]$eventRecord.SourceEventArgs.OldFullPath)
                    FullPath = [IO.Path]::GetFullPath(
                        [string]$eventRecord.SourceEventArgs.FullPath)
                })
                Remove-Event -EventIdentifier $eventRecord.EventIdentifier
            }
        }
        $deletedPaths = @(Get-Event -SourceIdentifier $deletedId `
            -ErrorAction SilentlyContinue | ForEach-Object {
                [IO.Path]::GetFullPath([string]$_.SourceEventArgs.FullPath)
            })
        $oldHandleText = $null
        if ($null -ne $oldHandle)
        {
            $oldHandle.Position = 0
            $reader = [IO.StreamReader]::new(
                $oldHandle, [Text.Encoding]::UTF8, $true, 1024, $true)
            try { $oldHandleText = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        return [pscustomobject]@{
            Output = @($output)
            Renamed = @($renamedRecords.ToArray())
            DeletedPaths = @($deletedPaths)
            OldHandleText = $oldHandleText
        }
    }
    finally
    {
        $watcher.EnableRaisingEvents = $false
        foreach ($sourceIdentifier in @($renamedId, $deletedId))
        {
            Unregister-Event -SourceIdentifier $sourceIdentifier -ErrorAction SilentlyContinue
            Get-Event -SourceIdentifier $sourceIdentifier -ErrorAction SilentlyContinue |
                Remove-Event -ErrorAction SilentlyContinue
        }
        $watcher.Dispose()
        if ($null -ne $oldHandle) { $oldHandle.Dispose() }
    }
}

function New-TestP5aAtomicFileSystem
{
    param(
        [string]$FailOnOperation,
        [string[]]$ProbeLockedPaths = @()
    )

    $records = [Collections.Generic.List[object]]::new()
    $capturedRecords = $records
    $capturedFailure = $FailOnOperation
    $capturedProbePaths = @($ProbeLockedPaths)
    $invoker = {
        param($Operation, $SourcePath, $DestinationPath, $OracleLease, $DeploymentLease)

        $leaseProbeWritable = @()
        foreach ($probePath in $capturedProbePaths)
        {
            $writable = $false
            try
            {
                $probe = [IO.File]::Open(
                    $probePath, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::Read)
                $probe.Dispose()
                $writable = $true
            }
            catch
            {
                $writable = $false
            }
            $leaseProbeWritable += $writable
        }
        $capturedRecords.Add([pscustomobject]@{
            operation = [string]$Operation
            sourcePath = [string]$SourcePath
            destinationPath = [string]$DestinationPath
            leaseProbeWritable = @($leaseProbeWritable)
            oracleLeaseIdentity = if ($null -eq $OracleLease) { 0 } else {
                [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($OracleLease)
            }
            deploymentLeaseIdentity = if ($null -eq $DeploymentLease) { 0 } else {
                [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($DeploymentLease)
            }
            oracleHandleIdentities = if ($null -eq $OracleLease) { @() } else {
                @($OracleLease.Handles | ForEach-Object {
                    [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($_)
                })
            }
            deploymentHandleIdentities = if ($null -eq $DeploymentLease) { @() } else {
                @($DeploymentLease.Handles | ForEach-Object {
                    [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($_)
                })
            }
        })
        if ([string]$Operation -ceq $capturedFailure)
        {
            throw "Injected atomic filesystem failure: $Operation"
        }
        switch ([string]$Operation)
        {
            'File.Copy' { [IO.File]::Copy($SourcePath, $DestinationPath, $true) }
            'File.Replace' {
                $replaceMethod = [IO.File].GetMethod(
                    'Replace', [type[]]@([string], [string], [string]))
                $replaceArguments = [object[]]::new(3)
                $replaceArguments[0] = [string]$SourcePath
                $replaceArguments[1] = [string]$DestinationPath
                $replaceArguments[2] = $null
                try { $null = $replaceMethod.Invoke($null, $replaceArguments) }
                catch
                {
                    if ($null -ne $_.Exception.InnerException)
                    {
                        throw $_.Exception.InnerException
                    }
                    throw
                }
            }
            'File.Move' { [IO.File]::Move($SourcePath, $DestinationPath) }
            'File.Delete' { [IO.File]::Delete($SourcePath) }
            default { throw "Unexpected atomic filesystem operation: $Operation" }
        }
    }.GetNewClosure()
    return [pscustomobject]@{ Records = $records; Invoker = $invoker }
}

function New-TestP5aCheckpointInvoker
{
    param(
        [string]$FailAt,
        [string]$MutateAt,
        [string]$MutatePath,
        [scriptblock]$MutationAction
    )

    $records = [Collections.Generic.List[string]]::new()
    $observations = [Collections.Generic.List[object]]::new()
    $capturedRecords = $records
    $capturedObservations = $observations
    $capturedFailure = $FailAt
    $capturedMutation = $MutateAt
    $capturedMutationPath = $MutatePath
    $capturedMutationAction = $MutationAction
    $invoker = {
        param($Checkpoint, $OracleLease, $DeploymentLease)

        $name = [string]$Checkpoint
        $capturedRecords.Add($name)
        $oracleHandles = if ($null -eq $OracleLease) { @() } else { @($OracleLease.Handles) }
        $deploymentHandles = if ($null -eq $DeploymentLease) { @() } else { @($DeploymentLease.Handles) }
        $capturedObservations.Add([pscustomobject]@{
            checkpoint = $name
            oracleLease = $OracleLease
            deploymentLease = $DeploymentLease
            oracleLeaseIdentity = if ($null -eq $OracleLease) { 0 } else {
                [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($OracleLease)
            }
            deploymentLeaseIdentity = if ($null -eq $DeploymentLease) { 0 } else {
                [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($DeploymentLease)
            }
            oracleHandleIdentities = @($oracleHandles | ForEach-Object {
                [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($_)
            })
            deploymentHandleIdentities = @($deploymentHandles | ForEach-Object {
                [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($_)
            })
        })
        if ($name -ceq $capturedMutation -and -not [string]::IsNullOrEmpty($capturedMutationPath))
        {
            [IO.File]::AppendAllText($capturedMutationPath, 'checkpoint-drift')
        }
        if ($name -ceq $capturedMutation -and $null -ne $capturedMutationAction)
        {
            & $capturedMutationAction $name
        }
        if ($name -ceq $capturedFailure)
        {
            throw "Injected generator checkpoint failure: $name"
        }
    }.GetNewClosure()
    return [pscustomobject]@{
        Records = $records
        Observations = $observations
        Invoker = $invoker
    }
}

function Assert-TestP5aLeaseContinuity
{
    param(
        [Parameter(Mandatory)][object]$CheckpointObserver,
        [Parameter(Mandatory)][string[]]$ExpectedCheckpoints,
        [Parameter(Mandatory)][int]$OracleHandleCount,
        [int]$DeploymentHandleCount = 0
    )

    @($CheckpointObserver.Records) | Should Be @($ExpectedCheckpoints)
    $observations = @($CheckpointObserver.Observations)
    $observations.Count | Should Be $ExpectedCheckpoints.Count
    $oracleObservations = @($observations | Where-Object { $null -ne $_.oracleLease })
    $oracleObservations.Count | Should Be $observations.Count
    $oracleIdentities = @($oracleObservations | ForEach-Object {
        [int]$_.oracleLeaseIdentity
    } | Sort-Object -Unique)
    $oracleIdentities.Count | Should Be 1
    $oracleIdentities[0] | Should Not Be 0
    foreach ($observation in $oracleObservations)
    {
        @($observation.oracleHandleIdentities).Count | Should Be $OracleHandleCount
        @($observation.oracleHandleIdentities) |
            Should Be @($oracleObservations[0].oracleHandleIdentities)
    }

    if ($DeploymentHandleCount -gt 0)
    {
        $observations[0].deploymentLease | Should BeNullOrEmpty
        $deploymentObservations = @($observations | Select-Object -Skip 1)
        @($deploymentObservations | Where-Object { $null -eq $_.deploymentLease }).Count |
            Should Be 0
        @($deploymentObservations | ForEach-Object {
            [int]$_.deploymentLeaseIdentity
        } | Sort-Object -Unique).Count | Should Be 1
        foreach ($observation in $deploymentObservations)
        {
            @($observation.deploymentHandleIdentities).Count | Should Be $DeploymentHandleCount
            @($observation.deploymentHandleIdentities) |
                Should Be @($deploymentObservations[0].deploymentHandleIdentities)
        }
    }
    else
    {
        @($observations | Where-Object { $null -ne $_.deploymentLease }).Count | Should Be 0
    }

    $leaseObjects = @($oracleObservations[0].oracleLease)
    if ($DeploymentHandleCount -gt 0)
    {
        $leaseObjects += @($observations[1].deploymentLease)
    }
    foreach ($leaseObject in $leaseObjects)
    {
        foreach ($handle in @($leaseObject.Handles))
        {
            $handle.SafeFileHandle.IsClosed | Should Be $true
        }
    }
}

function Invoke-TestP5aGeneratorProtocol
{
    param([Parameter(Mandatory)][object]$Context, [Parameter(Mandatory)][object]$Shim)

    return @(Invoke-P5aGeneratorProcessProtocol `
        -RepositoryRoot $Context.Root `
        -OracleAppHost $Context.Oracle.AppHostPath `
        -UnrealEditorCmd $Context.Editor `
        -UnrealProject $Context.UProject `
        -ReferenceRoot $Context.Reference `
        -StagingRoot $Context.Staging `
        -ProcessInvoker $Shim.Invoker `
        -TimeoutSeconds 10)
}

function Invoke-TestP5aVerifierProtocol
{
    param([Parameter(Mandatory)][object]$Context, [Parameter(Mandatory)][object]$Shim)

    return @(Invoke-P5aVerifierProcessProtocol `
        -RepositoryRoot $Context.Root `
        -OracleAppHost $Context.Oracle.AppHostPath `
        -FixturePath $Context.Fixture `
        -StagingRoot $Context.Staging `
        -ProcessInvoker $Shim.Invoker `
        -TimeoutSeconds 10)
}

function Get-TestP5aEvidence
{
    return [pscustomobject][ordered]@{
        sdkVersion = '8.0.100'
        sourceTreeSha256 = '1' * 64
        buildManifestSha256 = '2' * 64
        executableSha256 = '3' * 64
        oracleAssemblySha256 = '4' * 64
        importAssemblySha256 = '5' * 64
        coreAssemblySha256 = '6' * 64
        depsSha256 = '7' * 64
        runtimeConfigSha256 = '8' * 64
    }
}

function Get-TestP5aBuildEvidence
{
    return [pscustomobject][ordered]@{
        ownedPluginTreeSha256 = '9' * 64
        targetReceiptSha256 = 'a' * 64
        alsModuleManifestSha256 = 'b' * 64
        traceModuleManifestSha256 = 'c' * 64
        alsModuleDllSha256 = 'd' * 64
        traceModuleDllSha256 = 'e' * 64
    }
}

function Invoke-TestP5aWithProcessEnvironment
{
    param(
        [Parameter(Mandatory)][hashtable]$Values,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $previous = @{}
    foreach ($name in $Values.Keys)
    {
        $previous[$name] = [Environment]::GetEnvironmentVariable(
            [string]$name, [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            [string]$name, [string]$Values[$name], [EnvironmentVariableTarget]::Process)
    }
    try
    {
        return @(& $Action)
    }
    finally
    {
        foreach ($name in $Values.Keys)
        {
            [Environment]::SetEnvironmentVariable(
                [string]$name, $previous[$name], [EnvironmentVariableTarget]::Process)
        }
    }
}

function New-TestP5aDefaultWorkflowToolchain
{
    param([Parameter(Mandatory)][string]$Name)

    $context = New-TestP5aProcessContext $Name
    $programPath = Join-Path $context.Root 'tools\Als.P5aOracle\Program.cs'
    Write-TestP5aText $programPath @'
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Text.Json;

namespace Als.P5aOracle;

internal static class Program
{
    private const string MarkerPrefix = "P5A_ORACLE_DIGESTS layout=d6fef54173240d32 bindings=2b4be600d531c734 graph=44403c2869d8f615 plan=";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) return 80;
            if (args[0] == "--run-short-lived-sdk-chain")
            {
                WriteEvidence("direct.pid", Environment.ProcessId.ToString());
                var result = SpawnAndAwait(
                    RequiredEnvironment("GODOTALS_P5A_SHORT_LIVED_MSBUILD_PATH"),
                    "--run-short-lived-msbuild", "msbuild");
                if (result == 0) WriteEvidence("chain.complete", "complete");
                return result;
            }
            if (args[0] == "--run-short-lived-msbuild")
            {
                return SpawnAndAwait(
                    RequiredEnvironment("GODOTALS_P5A_SHORT_LIVED_VSTEST_PATH"),
                    "--run-short-lived-vstest", "vstest");
            }
            if (args[0] == "--run-short-lived-vstest")
            {
                return SpawnAndAwait(
                    RequiredEnvironment("GODOTALS_P5A_SHORT_LIVED_TESTHOST_PATH"),
                    "--run-short-lived-testhost", "testhost");
            }
            if (args[0] == "--run-short-lived-testhost")
            {
                WriteEvidence("testhost.self.pid", Environment.ProcessId.ToString());
                return 0;
            }
            if (args[0] == "--wait-for-test-release")
            {
                if (args.Length != 2) return 87;
                return WaitForRelease(args[1]);
            }
            Log(args);
            SpawnConfiguredForbiddenDescendant(args[0]);
            if (args[0] == "--version")
            {
                Console.WriteLine("8.0.100");
                return 0;
            }
            if (args[0] == "--list-sdks")
            {
                Console.WriteLine("8.0.100 [" + RequiredEnvironment("GODOTALS_P5A_DEFAULT_DOTNET_SDK_BASE") + "]");
                return 0;
            }
            if (args[0] == "--write-build-manifest") return 0;
            if (args[0] == "--write-native-plan")
            {
                CopyEnvironmentFile("GODOTALS_P5A_DEFAULT_PLAN", Separate(args, "--output"));
                Console.WriteLine(MarkerPrefix + Hash(Separate(args, "--output")));
                return 0;
            }
            if (args[0] == "--write-canonical-pair")
            {
                CopyEnvironmentFile("GODOTALS_P5A_DEFAULT_NATIVE", Separate(args, "--native-canonical"));
                CopyEnvironmentFile("GODOTALS_P5A_DEFAULT_PORT", Separate(args, "--port-canonical"));
                Console.WriteLine(MarkerPrefix + Hash(Separate(args, "--trace-plan")));
                return 0;
            }
            if (args[0] == "--verify-fixture")
            {
                var staging = Environment.GetEnvironmentVariable("GODOTALS_P5A_STAGING_ROOT") ?? "";
                if (!Path.IsPathFullyQualified(staging) || !Directory.Exists(staging) ||
                    Directory.EnumerateFileSystemEntries(staging).Any()) return 81;
                var fixture = Separate(args, "--fixture");
                var expected = RequiredEnvironment("GODOTALS_P5A_DEFAULT_NATIVE");
                if (!File.ReadAllBytes(fixture).SequenceEqual(File.ReadAllBytes(expected))) return 82;
                Console.WriteLine(MarkerPrefix + Hash(RequiredEnvironment("GODOTALS_P5A_DEFAULT_PLAN")));
                return 0;
            }
            if (args[0] == "test")
            {
                var loggerIndex = Array.IndexOf(args, "--logger");
                if (loggerIndex < 0 || loggerIndex + 1 >= args.Length) return 83;
                const string prefix = "trx;LogFileName=";
                if (!args[loggerIndex + 1].StartsWith(prefix, StringComparison.Ordinal)) return 84;
                Write(args[loggerIndex + 1][prefix.Length..],
                    "<TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"1\" passed=\"1\" failed=\"0\" error=\"0\" notExecuted=\"0\" skipped=\"0\" /></ResultSummary></TestRun>");
                return 0;
            }
            if (args.Any(value => value == "-run=AlsLocomotionTrace"))
            {
                var ready = args.Any(value => value == "-ReadyCheck");
                if (!ready)
                {
                    CopyEnvironmentFile("GODOTALS_P5A_DEFAULT_RAW", Prefixed(args, "-Output="));
                }
                Console.WriteLine(ready
                    ? "P5A_TRACE_READY_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17"
                    : "P5A_TRACE_GENERATION_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17");
                return 0;
            }
            return 85;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.GetType().Name + ": " + exception.Message);
            return 86;
        }
    }

    private static void Log(string[] args)
    {
        var path = Environment.GetEnvironmentVariable("GODOTALS_P5A_DEFAULT_WORKFLOW_LOG");
        if (string.IsNullOrEmpty(path)) return;
        Write(path, JsonSerializer.Serialize(new
        {
            executablePath = Environment.ProcessPath,
            processId = Environment.ProcessId,
            arguments = args,
        }) + "\n", append: true);
    }

    private static void SpawnConfiguredForbiddenDescendant(string mode)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GODOTALS_P5A_DEFAULT_FORBIDDEN_DESCENDANT_MODE"),
                mode, StringComparison.Ordinal)) return;
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ??
                throw new InvalidOperationException("current apphost path is unavailable"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--wait-for-test-release");
        startInfo.ArgumentList.Add(RequiredEnvironment(
            "GODOTALS_P5A_DEFAULT_FORBIDDEN_DESCENDANT_RELEASE"));
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("forbidden descendant failed to start");
        Write(RequiredEnvironment("GODOTALS_P5A_DEFAULT_FORBIDDEN_DESCENDANT_PID"),
            process.Id.ToString());
    }

    private static int SpawnAndAwait(string executablePath, string mode, string role)
    {
        if (!Path.IsPathFullyQualified(executablePath) || !File.Exists(executablePath))
            return 91;
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(mode);
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException(role + " failed to start");
        WriteEvidence(role + ".pid", process.Id.ToString());
        if (!process.WaitForExit(10000)) return 92;
        WriteEvidence(role + ".exited", process.ExitCode.ToString());
        return process.ExitCode;
    }

    private static void WriteEvidence(string name, string value)
    {
        var root = RequiredEnvironment("GODOTALS_P5A_SHORT_LIVED_EVIDENCE_ROOT");
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("short-lived evidence root must be absolute");
        Write(Path.Combine(Path.GetFullPath(root), name), value);
    }

    private static int WaitForRelease(string releasePath)
    {
        if (!Path.IsPathFullyQualified(releasePath)) return 88;
        releasePath = Path.GetFullPath(releasePath);
        var directory = Path.GetDirectoryName(releasePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return 89;
        using var signal = new ManualResetEventSlim(false);
        using var watcher = new FileSystemWatcher(directory, Path.GetFileName(releasePath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler changed = (_, _) => signal.Set();
        RenamedEventHandler renamed = (_, _) => signal.Set();
        watcher.Created += changed;
        watcher.Changed += changed;
        watcher.Renamed += renamed;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(releasePath))
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !signal.Wait(remaining)) return 90;
            signal.Reset();
        }
        return 0;
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name);

    private static string Separate(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length) throw new InvalidOperationException(name);
        return args[index + 1];
    }

    private static string Prefixed(string[] args, string prefix) =>
        args.Single(value => value.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];

    private static void CopyEnvironmentFile(string environmentName, string destination) =>
        Copy(RequiredEnvironment(environmentName), destination);

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static void Write(string path, string value, bool append = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (append) File.AppendAllText(path, value);
        else File.WriteAllText(path, value);
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
'@

    $dotnetPath = Get-TestP5aDotnetApplicationPath
    $build = Invoke-TestP5aExternalProcess `
        -FilePath $dotnetPath `
        -Arguments @('build', $context.Oracle.ProjectPath, '-c', 'Release',
                     '--nologo', '--no-incremental') `
        -WorkingDirectory $context.Root
    if ($build.ExitCode -ne 0)
    {
        "Missing 13B capability: TestDrive default-workflow child build ($($build.StdErr)$($build.StdOut))" |
            Should BeNullOrEmpty
    }

    $oldTime = [DateTime]::UtcNow.AddMinutes(-10)
    foreach ($sourcePath in $context.Oracle.SourcePaths)
    {
        [IO.File]::SetLastWriteTimeUtc($sourcePath, $oldTime)
    }
    $artifactTime = [DateTime]::UtcNow.AddMinutes(-1)
    foreach ($artifactPath in $context.Oracle.ArtifactPaths)
    {
        Assert-TestP5aPathCapability $artifactPath 'default-workflow runtime artifact'
        [IO.File]::SetLastWriteTimeUtc($artifactPath, $artifactTime)
    }
    $treeHash = Get-TestP5aTreeHash `
        -Root $context.Root `
        -RelativePaths $context.Oracle.SourceRelativePaths
    $output = $context.Oracle.OutputPath
    $manifest = @(
        'p5a_oracle_build_v1'
        'configuration=Release'
        'targetFramework=net8.0'
        'sdkVersion=8.0.100'
        "sourceTreeSha256=$treeHash"
        "executableSha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.P5aOracle.exe'))"
        "oracleAssemblySha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.P5aOracle.dll'))"
        "importAssemblySha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.Import.dll'))"
        "coreAssemblySha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.Core.dll'))"
        "depsSha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.P5aOracle.deps.json'))"
        "runtimeConfigSha256=$(Get-TestP5aFileHash (Join-Path $output 'Als.P5aOracle.runtimeconfig.json'))"
    ) -join "`n"
    Write-TestP5aText $context.Oracle.ManifestPath ($manifest + "`n")

    $bundleRoot = Join-Path $TestDrive "$Name-default-workflow-bundle"
    $plan = Join-Path $bundleRoot 'plan.json'
    $raw = Join-Path $bundleRoot 'raw.json'
    $native = Join-Path $bundleRoot 'native.json'
    $protocolOnlyPort = Join-Path $bundleRoot 'protocol-only-port.json'
    Write-TestP5aText $plan "{`"representation`":`"default-plan`"}`n"
    Write-TestP5aText $raw "{`"representation`":`"default-raw`"}`n"
    Write-TestP5aText $native "{`"representation`":`"default-native`"}`n"
    Write-TestP5aText $protocolOnlyPort "{`"representation`":`"protocol-only-port`"}`n"
    $logPath = Join-Path $bundleRoot 'children.ndjson'
    $forbiddenDescendantPidPath = Join-Path $bundleRoot 'forbidden-descendant.pid'
    $forbiddenDescendantReleasePath = Join-Path $bundleRoot 'forbidden-descendant.release'

    $dotnetDirectory = Join-Path $TestDrive "$Name-selected-dotnet"
    [void][IO.Directory]::CreateDirectory($dotnetDirectory)
    foreach ($artifactName in @(
        'Als.P5aOracle.exe', 'Als.P5aOracle.dll', 'Als.Import.dll', 'Als.Core.dll',
        'Als.P5aOracle.deps.json', 'Als.P5aOracle.runtimeconfig.json'))
    {
        [IO.File]::Copy(
            (Join-Path $output $artifactName),
            (Join-Path $dotnetDirectory $artifactName), $true)
    }
    $selectedDotnet = Join-Path $dotnetDirectory 'dotnet.exe'
    [IO.File]::Copy((Join-Path $output 'Als.P5aOracle.exe'), $selectedDotnet, $true)
    $selectedSdkBase = Join-Path $dotnetDirectory 'sdk'
    $selectedSdkRoot = Join-Path $selectedSdkBase '8.0.100'
    $selectedMsBuild = Join-Path $selectedSdkRoot 'MSBuild.exe'
    $selectedVstest = Join-Path $selectedSdkRoot 'vstest.console.exe'
    $selectedTestHost = Join-Path $selectedSdkRoot 'TestHostNetFramework\testhost.exe'
    foreach ($selectedSdkExecutable in @(
        $selectedMsBuild, $selectedVstest, $selectedTestHost))
    {
        $selectedSdkDirectory = Split-Path -Parent $selectedSdkExecutable
        [void][IO.Directory]::CreateDirectory($selectedSdkDirectory)
        [IO.File]::Copy(
            (Join-Path $output 'Als.P5aOracle.exe'), $selectedSdkExecutable, $true)
        foreach ($companionName in @(
            'Als.P5aOracle.dll', 'Als.Import.dll', 'Als.Core.dll',
            'Als.P5aOracle.deps.json', 'Als.P5aOracle.runtimeconfig.json'))
        {
            [IO.File]::Copy(
                (Join-Path $output $companionName),
                (Join-Path $selectedSdkDirectory $companionName), $true)
        }
    }
    $shortLivedEvidenceRoot = Join-Path $bundleRoot 'short-lived-evidence'
    $context.Editor = $context.Oracle.AppHostPath

    return [pscustomobject]@{
        Context = $context
        LogPath = $logPath
        ForbiddenDescendantPidPath = $forbiddenDescendantPidPath
        ForbiddenDescendantReleasePath = $forbiddenDescendantReleasePath
        SelectedDotnet = $selectedDotnet
        SelectedMsBuild = $selectedMsBuild
        SelectedVstest = $selectedVstest
        SelectedTestHost = $selectedTestHost
        ShortLivedEvidenceRoot = $shortLivedEvidenceRoot
        Plan = $plan
        Raw = $raw
        Native = $native
        ProtocolOnlyPort = $protocolOnlyPort
        Environment = @{
            GODOTALS_P5A_DEFAULT_WORKFLOW_LOG = $logPath
            GODOTALS_P5A_DEFAULT_PLAN = $plan
            GODOTALS_P5A_DEFAULT_RAW = $raw
            GODOTALS_P5A_DEFAULT_NATIVE = $native
            GODOTALS_P5A_DEFAULT_PORT = $protocolOnlyPort
            GODOTALS_P5A_DEFAULT_DOTNET_SDK_BASE = $selectedSdkBase
            GODOTALS_P5A_DEFAULT_FORBIDDEN_DESCENDANT_PID = $forbiddenDescendantPidPath
            GODOTALS_P5A_DEFAULT_FORBIDDEN_DESCENDANT_RELEASE =
                $forbiddenDescendantReleasePath
            GODOTALS_P5A_SHORT_LIVED_EVIDENCE_ROOT = $shortLivedEvidenceRoot
            GODOTALS_P5A_SHORT_LIVED_MSBUILD_PATH = $selectedMsBuild
            GODOTALS_P5A_SHORT_LIVED_VSTEST_PATH = $selectedVstest
            GODOTALS_P5A_SHORT_LIVED_TESTHOST_PATH = $selectedTestHost
            PATH = $dotnetDirectory + [IO.Path]::PathSeparator +
                [Environment]::GetEnvironmentVariable('PATH', [EnvironmentVariableTarget]::Process)
        }
    }
}

function Get-TestP5aDefaultWorkflowToolchain
{
    if ($null -eq $script:P5aDefaultWorkflowToolchain)
    {
        $script:P5aDefaultWorkflowToolchain =
            New-TestP5aDefaultWorkflowToolchain 'shared-default-workflows'
    }
    return $script:P5aDefaultWorkflowToolchain
}

function Get-TestP5aCompleteSelectedDotnetDescendantClosure
{
    $toolchain = Get-TestP5aDefaultWorkflowToolchain
    $closures = @(Invoke-TestP5aWithProcessEnvironment `
        -Values $toolchain.Environment `
        -Action {
            Get-TestP5aSelectedDotnetDescendantClosure `
                -DotnetPath $toolchain.SelectedDotnet `
                -WorkingDirectory $toolchain.Context.Root
        })
    $closures.Count | Should Be 1
    $closure = $closures[0]
    @($closure.PathsByImage.Keys) | Should Be @(
        'dotnet.exe', 'MSBuild.exe', 'testhost.exe', 'vstest.console.exe')
    foreach ($image in @('dotnet.exe', 'MSBuild.exe', 'testhost.exe', 'vstest.console.exe'))
    {
        @($closure.PathsByImage[$image]).Count | Should Be 1
    }
    return $closure
}

function New-TestP5aNestedDotnetDescendantRecords
{
    param(
        [Parameter(Mandatory)][object]$Closure,
        [Parameter(Mandatory)][int]$DirectProcessId
    )

    $msbuildProcessId = 12001
    $vstestProcessId = 12002
    $testhostProcessId = 12003
    return @(
        [pscustomobject][ordered]@{
            processId = $msbuildProcessId
            parentProcessId = $DirectProcessId
            ancestorProcessIds = @($DirectProcessId)
            imageName = 'MSBuild.exe'
            executablePath = [string]@($Closure.PathsByImage['MSBuild.exe'])[0]
        }
        [pscustomobject][ordered]@{
            processId = $vstestProcessId
            parentProcessId = $msbuildProcessId
            ancestorProcessIds = @($DirectProcessId, $msbuildProcessId)
            imageName = 'vstest.console.exe'
            executablePath = [string]@($Closure.PathsByImage['vstest.console.exe'])[0]
        }
        [pscustomobject][ordered]@{
            processId = $testhostProcessId
            parentProcessId = $vstestProcessId
            ancestorProcessIds = @(
                $DirectProcessId, $msbuildProcessId, $vstestProcessId)
            imageName = 'testhost.exe'
            executablePath = [string]@($Closure.PathsByImage['testhost.exe'])[0]
        }
    )
}

Describe 'P5A golden synthetic process build and publication RED contract' {
    BeforeAll {
        if (Test-Path -LiteralPath $script:P5aGeneratorPath -PathType Leaf)
        {
            try
            {
                . $script:P5aGeneratorPath `
                    -UnrealEditorCmd '13b-red-dot-source' `
                    -UnrealProject '13b-red-dot-source' `
                    -ReferenceRoot '13b-red-dot-source'
            }
            catch
            {
                $script:P5aGeneratorLoadError = $_.Exception.Message
            }
        }
        if (Test-Path -LiteralPath $script:P5aVerifierPath -PathType Leaf)
        {
            try
            {
                . $script:P5aVerifierPath
            }
            catch
            {
                $script:P5aVerifierLoadError = $_.Exception.Message
            }
        }
    }

    It 'keeps the RED harness compile-clean and confined to TestDrive' {
        $root = Join-Path $TestDrive 'harness-sentinel'
        $path = Join-Path $root 'sentinel.txt'
        Write-TestP5aText $path "p5a-red-sentinel`n"

        Test-Path -LiteralPath $path -PathType Leaf | Should Be $true
        (Get-TestP5aFileHash $path) | Should Match '^[0-9a-f]{64}$'
        [IO.Path]::GetFullPath($path).StartsWith(
            [IO.Path]::GetFullPath($TestDrive), [StringComparison]::OrdinalIgnoreCase) |
            Should Be $true
    }

    It 'defines parse-clean guarded generator and verifier orchestration with external prerequisites only' {
        Assert-TestP5aPathCapability $script:P5aGeneratorPath 'scripts/generate-p5a-golden.ps1'
        Assert-TestP5aPathCapability $script:P5aVerifierPath 'scripts/verify-p5a-golden.ps1'
        if ($null -ne $script:P5aGeneratorLoadError)
        {
            "Missing 13B capability: generator dot-source ($($script:P5aGeneratorLoadError))" | Should BeNullOrEmpty
        }
        if ($null -ne $script:P5aVerifierLoadError)
        {
            "Missing 13B capability: verifier dot-source ($($script:P5aVerifierLoadError))" | Should BeNullOrEmpty
        }

        foreach ($path in @($script:P5aGeneratorPath, $script:P5aVerifierPath))
        {
            $tokens = $null
            $errors = $null
            [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
            @($errors).Count | Should Be 0
        }
        $generator = [IO.File]::ReadAllText($script:P5aGeneratorPath)
        $verifier = [IO.File]::ReadAllText($script:P5aVerifierPath)
        $generator | Should Not Match 'generate-p3-golden|verify-p5a-golden|dotnet\s+(build|run)|--write-build-manifest'
        $verifier | Should Not Match 'UnrealEditor|AlsLocomotionTrace|generate-p3-golden|generate-p5a-golden|dotnet\s+(build|run)'

        foreach ($path in @($script:P5aGeneratorPath, $script:P5aVerifierPath))
        {
            Assert-TestP5aProcessLaunchClosure -Path $path
            $baseName = [IO.Path]::GetFileNameWithoutExtension($path)
            $mutations = @(
                @{ Name = 'start-process'; Source = "function Invoke-MutatedLaunch { Start-Process -FilePath 'cmd.exe' }`n" }
                @{ Name = 'start-alias'; Source = "function Invoke-MutatedLaunch { start 'cmd.exe' }`n" }
                @{ Name = 'saps-alias'; Source = "function Invoke-MutatedLaunch { saps -FilePath 'cmd.exe' }`n" }
                @{ Name = 'sajb-alias'; Source = "function Invoke-MutatedLaunch { sajb { Start-Sleep 1 } }`n" }
                @{ Name = 'icm-alias'; Source = "function Invoke-MutatedLaunch { icm -ScriptBlock { Get-Date } }`n" }
                @{ Name = 'iex-alias'; Source = "function Invoke-MutatedLaunch { iex 'Get-Date' }`n" }
                @{ Name = 'ii-alias'; Source = "function Invoke-MutatedLaunch { ii 'cmd.exe' }`n" }
                @{ Name = 'static-process-start'; Source = "function Invoke-MutatedLaunch { [Diagnostics.Process]::Start('cmd.exe') }`n" }
                @{ Name = 'instance-process-start'; Source = 'function Invoke-MutatedLaunch { $process = [Diagnostics.Process]::new(); [void]$process.Start() }' + "`n" }
                @{ Name = 'new-object-process-start'; Source = 'function Invoke-MutatedLaunch { $process = New-Object Diagnostics.Process; [void]$process.Start() }' + "`n" }
                @{ Name = 'call-operator-external'; Source = 'function Invoke-MutatedLaunch { $executable = ''cmd.exe''; & $executable /c exit 0 }' + "`n" }
                @{ Name = 'direct-external'; Source = "function Invoke-MutatedLaunch { dotnet --info }`n" }
                @{ Name = 'alternate-process-invoker'; Source = 'function Invoke-MutatedLaunch { param([scriptblock]$AlternateProcessInvoker); & $AlternateProcessInvoker ''cmd.exe'' @() $pwd 10 ''bypass'' }' + "`n" }
                @{ Name = 'extra-process-invoker'; Source = 'function Invoke-MutatedLaunch { & $ProcessInvoker ''cmd.exe'' @() $pwd 10 ''bypass'' }' + "`n" }
                @{ Name = 'conditional-process-invoker'; Source = 'function Invoke-MutatedLaunch { if ($true) { & $ProcessInvoker ''cmd.exe'' @() $pwd 10 ''bypass'' } }' + "`n" }
                @{ Name = 'second-default-runner'; Source = "function Invoke-P5aGeneratorDefaultProcess { Add-Type -TypeDefinition 'public static class Hidden { public static void CreateProcessW() { } }' }`n" }
            )
            foreach ($mutation in $mutations)
            {
                $mutatedPath = Join-Path $TestDrive (
                    "process-closure-$baseName-$($mutation.Name).ps1")
                Write-TestP5aText $mutatedPath ($([IO.File]::ReadAllText($path)) + "`n" + $mutation.Source)
                $mutationTokens = $null
                $mutationErrors = $null
                [void][Management.Automation.Language.Parser]::ParseFile(
                    $mutatedPath, [ref]$mutationTokens, [ref]$mutationErrors)
                @($mutationErrors).Count | Should Be 0
                Test-TestP5aRejects {
                    Assert-TestP5aProcessLaunchClosure -Path $mutatedPath
                } | Should Be $true
            }

            $runnerTokens = $null
            $runnerErrors = $null
            $runnerAst = [Management.Automation.Language.Parser]::ParseFile(
                $path, [ref]$runnerTokens, [ref]$runnerErrors)
            @($runnerErrors).Count | Should Be 0
            $runnerName = (Get-TestP5aDefaultRunnerDefinition -Ast $runnerAst).Name
            $runnerText = (Get-TestP5aDefaultRunnerDefinition -Ast $runnerAst).Extent.Text
            foreach ($nativeMutation in @(
                @{ Name = 'missing-createprocessw'; Token = 'CreateProcessW' }
                @{ Name = 'missing-job-list'; Token = 'PROC_THREAD_ATTRIBUTE_JOB_LIST' }
                @{ Name = 'missing-handle-list'; Token = 'PROC_THREAD_ATTRIBUTE_HANDLE_LIST' }
                @{ Name = 'missing-job-accounting'; Token = 'QueryInformationJobObject' }
                @{ Name = 'missing-metadata'; Token = 'metadata' }
                @{ Name = 'missing-total-processes'; Token = 'TotalProcesses' }
                @{ Name = 'missing-stream-cap'; Token = 'MaxStdOutBytes' }
                @{ Name = 'process-start-fallback'; Token = 'ProcessStartInfo' }
                @{ Name = 'post-start-job-attach'; Token = 'AssignProcessToJobObject' }
                @{ Name = 'unbounded-read'; Token = 'ReadToEndAsync' }
                @{ Name = 'merged-output'; Token = '($stdout + $stderr)' }
                @{ Name = 'unbounded-wait'; Token = 'WaitForExit()' }
            ))
            {
                $mutatedRunnerText = if ($nativeMutation.Name -like 'missing-*') {
                    [regex]::Replace(
                        $runnerText,
                        [regex]::Escape([string]$nativeMutation.Token),
                        [string]::Empty,
                        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
                }
                else
                {
                    $runnerText + "`n$($nativeMutation.Token)"
                }
                Test-TestP5aRejects {
                    Assert-TestP5aNativeRunnerLexicalClosure -RunnerText $mutatedRunnerText
                } | Should Be $true
            }
            $defaultParameter = @($runnerAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.ParameterAst] -and
                    $node.Name.VariablePath.UserPath -ceq 'ProcessInvoker' -and
                    $null -ne $node.DefaultValue
            }, $true))[0]
            $fakeDefault = '{ param($FilePath,$Arguments,$WorkingDirectory,$TimeoutSeconds,$PhaseName); ' +
                '$null = ${function:' + $runnerName + '}; ' +
                '[pscustomobject]@{ ProcessId = 1; ExitCode = 0; TimedOut = $false; ' +
                'OutputLimitExceeded = $false; StdOutLines = @(); StdErrLines = @(); ' +
                'StdOutBytes = 0; StdErrBytes = 0; OutputLines = @(); ' +
                'DescendantProcesses = @(); JobTotalProcesses = 1; JobActiveProcesses = 0; ' +
                'JobProcessIds = @(1) } }'
            $productionSource = [IO.File]::ReadAllText($path)
            $deadReferenceSource = $productionSource.Substring(
                0, $defaultParameter.DefaultValue.Extent.StartOffset) + $fakeDefault +
                $productionSource.Substring($defaultParameter.DefaultValue.Extent.EndOffset)
            $deadReferencePath = Join-Path $TestDrive (
                "process-closure-$baseName-dead-runner-reference.ps1")
            Write-TestP5aText $deadReferencePath $deadReferenceSource
            $deadReferenceTokens = $null
            $deadReferenceErrors = $null
            [void][Management.Automation.Language.Parser]::ParseFile(
                $deadReferencePath, [ref]$deadReferenceTokens, [ref]$deadReferenceErrors)
            @($deadReferenceErrors).Count | Should Be 0
            Test-TestP5aRejects {
                Assert-TestP5aProcessLaunchClosure -Path $deadReferencePath
            } | Should Be $true
            $pwshPath = (Get-Command pwsh -CommandType Application -ErrorAction Stop).Source
            [IO.Path]::IsPathFullyQualified($pwshPath) | Should Be $true
            $escapedPwshPath = $pwshPath.Replace("'", "''")

            $streamProbe = Join-Path $TestDrive "default-runner-$baseName-streams.ps1"
            Write-TestP5aText $streamProbe @'
[Console]::Out.WriteLine('stdout-probe')
[Console]::Error.WriteLine('stderr-probe')
'@
            $streamResult = @(Invoke-TestP5aDefaultRunner `
                -Kind $(if ($path -ceq $script:P5aGeneratorPath) { 'generator' } else { 'verifier' }) `
                -EntrypointPath $path `
                -RunnerName $runnerName `
                -FilePath $pwshPath `
                -Arguments @('-NoProfile', '-File', $streamProbe) `
                -WorkingDirectory $TestDrive `
                -TimeoutSeconds 10 `
                -PhaseName 'default-runner-stream-capture')
            $streamResult.Count | Should Be 1
            $streamResult[0].ExitCode | Should Be 0
            $streamResult[0].TimedOut | Should Be $false
            $streamResult[0].OutputLimitExceeded | Should Be $false
            ([int]$streamResult[0].ProcessId) | Should BeGreaterThan 0
            @($streamResult[0].DescendantProcesses).Count | Should Be 0
            $stdoutLines = @($streamResult[0].StdOutLines | ForEach-Object { [string]$_ })
            $stderrLines = @($streamResult[0].StdErrLines | ForEach-Object { [string]$_ })
            @($stdoutLines | Where-Object { $_ -ceq 'stdout-probe' }).Count | Should Be 1
            @($stderrLines | Where-Object { $_ -ceq 'stderr-probe' }).Count | Should Be 1
            @($stdoutLines | Where-Object { $_ -ceq 'stderr-probe' }).Count | Should Be 0
            @($stderrLines | Where-Object { $_ -ceq 'stdout-probe' }).Count | Should Be 0
            ([int64]$streamResult[0].StdOutBytes) | Should BeGreaterThan 0
            ([int64]$streamResult[0].StdErrBytes) | Should BeGreaterThan 0
            [int]$streamResult[0].JobTotalProcesses | Should Be 1
            [int]$streamResult[0].JobActiveProcesses | Should Be 0
            @($streamResult[0].JobProcessIds | ForEach-Object { [int]$_ }) |
                Should Be @([int]$streamResult[0].ProcessId)

            $capDescendantProbe = Join-Path $TestDrive "default-runner-$baseName-cap-descendant.ps1"
            $capDescendantPidPath = Join-Path $TestDrive "default-runner-$baseName-cap-descendant.pid"
            $capProbe = Join-Path $TestDrive "default-runner-$baseName-cap.ps1"
            Write-TestP5aText $capDescendantProbe "Start-Sleep -Seconds 30`n"
            $escapedCapDescendantProbe = $capDescendantProbe.Replace("'", "''")
            $escapedCapDescendantPidPath = $capDescendantPidPath.Replace("'", "''")
            $capMethod = if ($path -ceq $script:P5aGeneratorPath) {
                'OpenStandardOutput'
            } else {
                'OpenStandardError'
            }
            Write-TestP5aText $capProbe @"
`$child = Start-Process -FilePath '$escapedPwshPath' -ArgumentList @(
    '-NoProfile', '-File', '$escapedCapDescendantProbe') -PassThru -NoNewWindow
[IO.File]::WriteAllText('$escapedCapDescendantPidPath', [string]`$child.Id)
`$payload = New-Object byte[] 8388609
`$stream = [Console]::$capMethod()
`$stream.Write(`$payload, 0, `$payload.Length)
`$stream.Flush()
Start-Sleep -Seconds 30
"@
            $capDescendantPid = 0
            try
            {
                $capResult = @(Invoke-TestP5aDefaultRunner `
                    -Kind $(if ($path -ceq $script:P5aGeneratorPath) { 'generator' } else { 'verifier' }) `
                    -EntrypointPath $path `
                    -RunnerName $runnerName `
                    -FilePath $pwshPath `
                    -Arguments @('-NoProfile', '-File', $capProbe) `
                    -WorkingDirectory $TestDrive `
                    -TimeoutSeconds 10 `
                    -PhaseName 'default-runner-stream-cap')
                $capResult.Count | Should Be 1
                $capResult[0].TimedOut | Should Be $false
                $capResult[0].OutputLimitExceeded | Should Be $true
                ([int64]$capResult[0].StdOutBytes -lt 8388610) | Should Be $true
                ([int64]$capResult[0].StdErrBytes -lt 8388610) | Should Be $true
                [int]$capResult[0].JobActiveProcesses | Should Be 0
                Test-Path -LiteralPath $capDescendantPidPath -PathType Leaf | Should Be $true
                $capDescendantPid = [int][IO.File]::ReadAllText($capDescendantPidPath)
                [int]$capResult[0].JobTotalProcesses | Should Be 2
                $expectedCapJobProcessIds = @([int]$capResult[0].ProcessId, $capDescendantPid)
                @($capResult[0].JobProcessIds | ForEach-Object { [int]$_ } | Sort-Object) |
                    Should Be @($expectedCapJobProcessIds | Sort-Object)
                for ($attempt = 0; $attempt -lt 20 -and
                     $null -ne (Get-Process -Id $capDescendantPid -ErrorAction SilentlyContinue); $attempt++)
                {
                    Start-Sleep -Milliseconds 100
                }
                Get-Process -Id $capDescendantPid -ErrorAction SilentlyContinue |
                    Should BeNullOrEmpty
            }
            finally
            {
                if ($capDescendantPid -gt 0)
                {
                    $leftover = Get-Process -Id $capDescendantPid -ErrorAction SilentlyContinue
                    if ($null -ne $leftover) { $leftover.Kill() }
                }
            }

            $shortLivedToolchain = Get-TestP5aDefaultWorkflowToolchain
            $shortLivedClosure = Get-TestP5aCompleteSelectedDotnetDescendantClosure
            $shortLivedEnvironment = @{}
            foreach ($entry in $shortLivedToolchain.Environment.GetEnumerator())
            {
                $shortLivedEnvironment[[string]$entry.Key] = [string]$entry.Value
            }
            $shortLivedEvidenceRoot = Join-Path $TestDrive (
                "default-runner-$baseName-short-lived-evidence")
            $shortLivedEnvironment.GODOTALS_P5A_SHORT_LIVED_EVIDENCE_ROOT =
                $shortLivedEvidenceRoot
            $shortLivedRuns = @(Invoke-TestP5aWithProcessEnvironment `
                -Values $shortLivedEnvironment `
                -Action {
                    Invoke-TestP5aDefaultRunner `
                        -Kind $(if ($path -ceq $script:P5aGeneratorPath) {
                            'generator'
                        } else {
                            'verifier'
                        }) `
                        -EntrypointPath $path `
                        -RunnerName $runnerName `
                        -FilePath $shortLivedToolchain.SelectedDotnet `
                        -Arguments @('--run-short-lived-sdk-chain') `
                        -WorkingDirectory $shortLivedToolchain.Context.Root `
                        -TimeoutSeconds 10 `
                        -PhaseName 'default-runner-short-lived-success-audit'
                })
            $shortLivedRuns.Count | Should Be 1
            $shortLivedRun = $shortLivedRuns[0]
            $shortLivedRun.ExitCode | Should Be 0
            $shortLivedRun.TimedOut | Should Be $false
            $shortLivedRun.OutputLimitExceeded | Should Be $false
            $evidenceValues = @{}
            foreach ($evidenceName in @(
                'direct.pid', 'msbuild.pid', 'msbuild.exited',
                'vstest.pid', 'vstest.exited',
                'testhost.pid', 'testhost.self.pid', 'testhost.exited',
                'chain.complete'))
            {
                $evidencePath = Join-Path $shortLivedEvidenceRoot $evidenceName
                Test-Path -LiteralPath $evidencePath -PathType Leaf | Should Be $true
                $evidenceValues[$evidenceName] = [IO.File]::ReadAllText($evidencePath)
            }
            $evidenceValues['direct.pid'] | Should Be ([string]$shortLivedRun.ProcessId)
            foreach ($exitEvidence in @(
                'msbuild.exited', 'vstest.exited', 'testhost.exited'))
            {
                $evidenceValues[$exitEvidence] | Should Be '0'
            }
            $evidenceValues['chain.complete'] | Should Be 'complete'
            $evidenceValues['testhost.self.pid'] | Should Be $evidenceValues['testhost.pid']

            $expectedShortLived = @(
                [pscustomobject]@{
                    Image = 'MSBuild.exe'
                    Path = [string]@($shortLivedClosure.PathsByImage['MSBuild.exe'])[0]
                    ProcessId = [int]$evidenceValues['msbuild.pid']
                    ParentProcessId = [int]$shortLivedRun.ProcessId
                    Ancestors = @([int]$shortLivedRun.ProcessId)
                }
                [pscustomobject]@{
                    Image = 'vstest.console.exe'
                    Path = [string]@($shortLivedClosure.PathsByImage['vstest.console.exe'])[0]
                    ProcessId = [int]$evidenceValues['vstest.pid']
                    ParentProcessId = [int]$evidenceValues['msbuild.pid']
                    Ancestors = @(
                        [int]$shortLivedRun.ProcessId,
                        [int]$evidenceValues['msbuild.pid'])
                }
                [pscustomobject]@{
                    Image = 'testhost.exe'
                    Path = [string]@($shortLivedClosure.PathsByImage['testhost.exe'])[0]
                    ProcessId = [int]$evidenceValues['testhost.pid']
                    ParentProcessId = [int]$evidenceValues['vstest.pid']
                    Ancestors = @(
                        [int]$shortLivedRun.ProcessId,
                        [int]$evidenceValues['msbuild.pid'],
                        [int]$evidenceValues['vstest.pid'])
                }
            )
            $shortLivedDescendants = @($shortLivedRun.DescendantProcesses)
            $shortLivedDescendants.Count | Should Be 3
            $expectedJobProcessIds = @([int]$shortLivedRun.ProcessId) + @(
                $shortLivedDescendants | ForEach-Object { [int]$_.processId })
            [int]$shortLivedRun.JobTotalProcesses | Should Be $expectedJobProcessIds.Count
            [int]$shortLivedRun.JobActiveProcesses | Should Be 0
            @($shortLivedRun.JobProcessIds | ForEach-Object { [int]$_ } | Sort-Object) |
                Should Be @($expectedJobProcessIds | Sort-Object)
            foreach ($expectedDescendant in $expectedShortLived)
            {
                $matches = @($shortLivedDescendants | Where-Object {
                    [int]$_.processId -eq $expectedDescendant.ProcessId
                })
                $matches.Count | Should Be 1
                $actualDescendant = $matches[0]
                [string]$actualDescendant.imageName | Should Be $expectedDescendant.Image
                [IO.Path]::GetFullPath([string]$actualDescendant.executablePath) |
                    Should Be ([IO.Path]::GetFullPath($expectedDescendant.Path))
                [int]$actualDescendant.parentProcessId |
                    Should Be $expectedDescendant.ParentProcessId
                @($actualDescendant.ancestorProcessIds | ForEach-Object { [int]$_ }) |
                    Should Be @($expectedDescendant.Ancestors)
                Get-Process -Id $expectedDescendant.ProcessId -ErrorAction SilentlyContinue |
                    Should BeNullOrEmpty
            }
            Get-Process -Id ([int]$shortLivedRun.ProcessId) -ErrorAction SilentlyContinue |
                Should BeNullOrEmpty

            $descendantProbe = Join-Path $TestDrive "default-runner-$baseName-descendant.ps1"
            $descendantPidPath = Join-Path $TestDrive "default-runner-$baseName-descendant.pid"
            $treeProbe = Join-Path $TestDrive "default-runner-$baseName-tree.ps1"
            Write-TestP5aText $descendantProbe "Start-Sleep -Seconds 30`n"
            $escapedPwshPath = $pwshPath.Replace("'", "''")
            $escapedDescendantProbe = $descendantProbe.Replace("'", "''")
            $escapedDescendantPidPath = $descendantPidPath.Replace("'", "''")
            Write-TestP5aText $treeProbe @"
`$child = Start-Process -FilePath '$escapedPwshPath' -ArgumentList @(
    '-NoProfile', '-File', '$escapedDescendantProbe') -PassThru -NoNewWindow
[IO.File]::WriteAllText('$escapedDescendantPidPath', [string]`$child.Id)
[Console]::Out.WriteLine('tree-probe-ready')
Start-Sleep -Seconds 30
"@
            $descendantPid = 0
            $rootPid = 0
            try
            {
                $treeResult = @(Invoke-TestP5aDefaultRunner `
                    -Kind $(if ($path -ceq $script:P5aGeneratorPath) { 'generator' } else { 'verifier' }) `
                    -EntrypointPath $path `
                    -RunnerName $runnerName `
                    -FilePath $pwshPath `
                    -Arguments @('-NoProfile', '-File', $treeProbe) `
                    -WorkingDirectory $TestDrive `
                    -TimeoutSeconds 2 `
                    -PhaseName 'default-runner-timeout-tree-audit')
                $treeResult.Count | Should Be 1
                $treeResult[0].ExitCode | Should Not Be 0
                $treeResult[0].TimedOut | Should Be $true
                $treeResult[0].OutputLimitExceeded | Should Be $false
                ([int]$treeResult[0].ProcessId) | Should BeGreaterThan 0
                $rootPid = [int]$treeResult[0].ProcessId
                [int]$treeResult[0].JobActiveProcesses | Should Be 0
                Test-Path -LiteralPath $descendantPidPath -PathType Leaf | Should Be $true
                $descendantPid = [int][IO.File]::ReadAllText($descendantPidPath)
                $returnedDescendant = @($treeResult[0].DescendantProcesses | Where-Object {
                    [int]$_.processId -eq $descendantPid
                })
                $returnedDescendant.Count | Should Be 1
                ([int]$returnedDescendant[0].parentProcessId) | Should Be ([int]$treeResult[0].ProcessId)
                @($returnedDescendant[0].ancestorProcessIds | ForEach-Object { [int]$_ }) |
                    Should Contain ([int]$treeResult[0].ProcessId)
                [IO.Path]::IsPathFullyQualified([string]$returnedDescendant[0].executablePath) |
                    Should Be $true
                [IO.Path]::GetFileName([string]$returnedDescendant[0].executablePath) |
                    Should Be ([string]$returnedDescendant[0].imageName)
                for ($attempt = 0; $attempt -lt 20 -and
                     $null -ne (Get-Process -Id $descendantPid -ErrorAction SilentlyContinue); $attempt++)
                {
                    Start-Sleep -Milliseconds 100
                }
                Get-Process -Id $descendantPid -ErrorAction SilentlyContinue |
                    Should BeNullOrEmpty
                Get-Process -Id $rootPid -ErrorAction SilentlyContinue |
                    Should BeNullOrEmpty
            }
            finally
            {
                if ($descendantPid -gt 0)
                {
                    $leftover = Get-Process -Id $descendantPid -ErrorAction SilentlyContinue
                    if ($null -ne $leftover) { $leftover.Kill() }
                }
                if ($rootPid -gt 0)
                {
                    $leftoverRoot = Get-Process -Id $rootPid -ErrorAction SilentlyContinue
                    if ($null -ne $leftoverRoot) { $leftoverRoot.Kill() }
                }
            }
        }
    }

    It 'runs both complete workflows through the actual default audited process runner binding' {
        Assert-TestP5aPathCapability $script:P5aGeneratorPath 'scripts/generate-p5a-golden.ps1'
        Assert-TestP5aPathCapability $script:P5aVerifierPath 'scripts/verify-p5a-golden.ps1'
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorWorkflow' generator
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierWorkflow' verifier
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        [IO.File]::WriteAllText($toolchain.LogPath, [string]::Empty)
        $context = $toolchain.Context
        $generatorCheckpoints = New-TestP5aCheckpointInvoker
        $verifierCheckpoints = New-TestP5aCheckpointInvoker
        $oracleLeasePaths = @(Get-TestP5aOracleLeasePaths $context)
        $verifierLeasePaths = @(Get-TestP5aVerifierLeasePaths $context)
        $deploymentLeasePaths = @(Get-TestP5aDeploymentLeasePaths $context)

        $runs = @(Invoke-TestP5aWithProcessEnvironment `
            -Values $toolchain.Environment `
            -Action {
                $dotnetCommands = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)
                $selectedDotnet = [IO.Path]::GetFullPath(
                    [string]$dotnetCommands[0].Source)
                $generatorOutput = @(& $script:P5aGeneratorPath `
                    -RepositoryRoot $context.Root `
                    -UnrealEditorCmd $context.Editor `
                    -UnrealProject $context.UProject `
                    -ReferenceRoot $context.Reference `
                    -StagingRoot $context.Staging `
                    -DestinationPath $context.Fixture `
                    -CheckpointInvoker $generatorCheckpoints.Invoker `
                    -TimeoutSeconds 10)
                $generatorStagingWasRemoved = -not (Test-Path -LiteralPath $context.Staging)
                $context.Staging = Join-Path $TestDrive 'default-workflows-verifier-staging'
                $verifierOutput = @(& $script:P5aVerifierPath `
                    -RepositoryRoot $context.Root `
                    -FixturePath $context.Fixture `
                    -StagingRoot $context.Staging `
                    -CheckpointInvoker $verifierCheckpoints.Invoker `
                    -TimeoutSeconds 10)
                [pscustomobject]@{
                    SelectedDotnet = $selectedDotnet
                    GeneratorOutput = @($generatorOutput)
                    VerifierOutput = @($verifierOutput)
                    GeneratorStagingWasRemoved = $generatorStagingWasRemoved
                    VerifierStagingWasRemoved = -not (Test-Path -LiteralPath $context.Staging)
                }
            })
        $runs.Count | Should Be 1
        $run = $runs[0]
        $run.SelectedDotnet | Should Be ([IO.Path]::GetFullPath($toolchain.SelectedDotnet))
        $run.GeneratorStagingWasRemoved | Should Be $true
        $run.VerifierStagingWasRemoved | Should Be $true
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($context.Fixture)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($toolchain.Native)))
        Assert-TestP5aExactParentMarker -Output @($run.GeneratorOutput) `
            -ExpectedMarker "P5A_GOLDEN_GENERATION_OK cases=8 commit=$script:P5aLockedCommit"
        Assert-TestP5aExactParentMarker -Output @($run.VerifierOutput) `
            -ExpectedMarker "P5A_GOLDEN_FIXTURE_OK cases=8 commit=$script:P5aLockedCommit"

        $records = @(Get-Content -LiteralPath $toolchain.LogPath | ForEach-Object {
            $_ | ConvertFrom-Json
        })
        $records.Count | Should Be 9
        @($records | ForEach-Object { [int]$_.processId } | Where-Object { $_ -gt 0 }).Count |
            Should Be 9
        @($records[0..7] | ForEach-Object {
            [IO.Path]::GetFullPath([string]$_.executablePath)
        }) | Should Be @((1..8 | ForEach-Object {
            [IO.Path]::GetFullPath($context.Oracle.AppHostPath)
        }))
        [IO.Path]::GetFullPath([string]$records[8].executablePath) |
            Should Be ([IO.Path]::GetFullPath($toolchain.SelectedDotnet))
        @($records | ForEach-Object { [string]$_.arguments[0] }) | Should Be @(
            '--write-native-plan', '--write-native-plan', $context.UProject,
            $context.UProject, $context.UProject,
            '--write-canonical-pair', '--write-canonical-pair',
            '--verify-fixture', 'test')

        Assert-TestP5aLeaseContinuity -CheckpointObserver $generatorCheckpoints `
            -ExpectedCheckpoints @(
                'OracleEvidenceOpened', 'DeploymentEvidenceOpened', 'ChildrenCompleted',
                'ReadyManifestWritten', 'ReadyManifestReopened', 'EvidenceRechecked',
                'BeforePublication') `
            -OracleHandleCount $oracleLeasePaths.Count `
            -DeploymentHandleCount $deploymentLeasePaths.Count
        Assert-TestP5aLeaseContinuity -CheckpointObserver $verifierCheckpoints `
            -ExpectedCheckpoints @(
                'OracleEvidenceOpened', 'BeforeOracleChild', 'OracleChildCompleted',
                'BeforeDotnetChild', 'ChildrenCompleted') `
            -OracleHandleCount $verifierLeasePaths.Count

        $forbiddenEnvironment = @{}
        foreach ($entry in $toolchain.Environment.GetEnumerator())
        {
            $forbiddenEnvironment[[string]$entry.Key] = [string]$entry.Value
        }
        $forbiddenEnvironment.GODOTALS_P5A_DEFAULT_FORBIDDEN_DESCENDANT_MODE =
            '--write-native-plan'
        $context.Staging = Join-Path $TestDrive 'default-workflows-forbidden-staging'
        $fixtureHashBeforeForbiddenDescendant = Get-TestP5aFileHash $context.Fixture
        $forbiddenOutput = [Collections.Generic.List[object]]::new()
        $forbiddenDescendantPid = 0
        try
        {
            $forbiddenRuns = @(Invoke-TestP5aWithProcessEnvironment `
                -Values $forbiddenEnvironment `
                -Action {
                    $failure = Invoke-TestP5aFailureCapture -Output $forbiddenOutput -Action {
                        & $script:P5aGeneratorPath `
                            -RepositoryRoot $context.Root `
                            -UnrealEditorCmd $context.Editor `
                            -UnrealProject $context.UProject `
                            -ReferenceRoot $context.Reference `
                            -StagingRoot $context.Staging `
                            -DestinationPath $context.Fixture `
                            -TimeoutSeconds 10
                    }
                    [pscustomobject]@{ Failure = $failure }
                })
            $forbiddenRuns.Count | Should Be 1
            $forbiddenRuns[0].Failure |
                Should Match '(?i)descendant.*Als\.P5aOracle(?:\.exe)?'
            @($forbiddenOutput | Where-Object {
                [string]$_ -match '^P5A_GOLDEN_'
            }).Count | Should Be 0
            (Get-TestP5aFileHash $context.Fixture) |
                Should Be $fixtureHashBeforeForbiddenDescendant
            Test-Path -LiteralPath $context.Staging | Should Be $false
            Test-Path -LiteralPath $toolchain.ForbiddenDescendantPidPath -PathType Leaf |
                Should Be $true
            $forbiddenDescendantPid = [int][IO.File]::ReadAllText(
                $toolchain.ForbiddenDescendantPidPath)
            $forbiddenDescendantPid | Should BeGreaterThan 0
        }
        finally
        {
            Write-TestP5aText $toolchain.ForbiddenDescendantReleasePath "release`n"
        }
        $releasedDescendant = Get-Process -Id $forbiddenDescendantPid `
            -ErrorAction SilentlyContinue
        if ($null -ne $releasedDescendant)
        {
            $releasedDescendant.WaitForExit(5000) | Should Be $true
        }
        Get-Process -Id $forbiddenDescendantPid -ErrorAction SilentlyContinue |
            Should BeNullOrEmpty
        @(Get-Content -LiteralPath $toolchain.LogPath).Count | Should Be 10
    }

    It 'requires one exact Release AfterBuild private-manifest invocation and only Import and Core references' {
        Assert-TestP5aPathCapability $script:P5aOracleProjectPath 'tools/Als.P5aOracle/Als.P5aOracle.csproj'
        Assert-TestP5aPathCapability $script:P5aOracleProgramPath 'tools/Als.P5aOracle/Program.cs'
        Assert-TestP5aCommandCapability 'Assert-P5aOracleProjectContract' generator
        $valid = New-TestP5aOracleRepository 'oracle-project-valid'
        Assert-P5aOracleProjectContract -RepositoryRoot $valid.Root

        $projectMutations = @(
            @{ Name = 'debug-target'; Old = "'`$(Configuration)' == 'Release'"; New = "'`$(Configuration)' == 'Debug'" }
            @{ Name = 'before-build'; Old = 'AfterTargets="Build"'; New = 'BeforeTargets="Build"' }
            @{ Name = 'wrong-mode'; Old = '--write-build-manifest'; New = '--write-native-plan' }
            @{ Name = 'repeated-mode'; Old = '--write-build-manifest'; New = '--write-build-manifest --write-build-manifest' }
            @{ Name = 'mode-after-root'; Old = '--write-build-manifest --repository-root &quot;$(P5aRepositoryRoot)&quot;'; New = '--repository-root &quot;$(P5aRepositoryRoot)&quot; --write-build-manifest' }
            @{ Name = 'wrong-root-argument'; Old = '--repository-root'; New = '--wrong-repository-root' }
            @{ Name = 'wrong-output-argument'; Old = '--output'; New = '--wrong-output' }
            @{ Name = 'target-path-dll'; Old = '&quot;$(TargetDir)Als.P5aOracle.exe&quot;'; New = '&quot;$(TargetPath)&quot;' }
            @{ Name = 'wrong-apphost'; Old = '&quot;$(TargetDir)Als.P5aOracle.exe&quot;'; New = '&quot;$(TargetDir)WrongOracle.exe&quot;' }
            @{ Name = 'dotnet-prefix'; Old = '&quot;$(TargetDir)Als.P5aOracle.exe&quot;'; New = 'dotnet &quot;$(TargetDir)Als.P5aOracle.exe&quot;' }
            @{ Name = 'missing-sdk'; Old = ' --sdk-version &quot;$(NETCoreSdkVersion)&quot;'; New = '' }
            @{ Name = 'package-reference'; Old = '</ItemGroup>'; New = '<PackageReference Include="Synthetic" Version="1.0.0" /></ItemGroup>' }
            @{ Name = 'assembly-reference'; Old = '</ItemGroup>'; New = '<Reference Include="Synthetic" /></ItemGroup>' }
            @{ Name = 'com-reference'; Old = '</ItemGroup>'; New = '<COMReference Include="Synthetic" /></ItemGroup>' }
            @{ Name = 'native-reference'; Old = '</ItemGroup>'; New = '<NativeReference Include="Synthetic" /></ItemGroup>' }
            @{ Name = 'content-copy'; Old = '</ItemGroup>'; New = '<Content Include="payload"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content></ItemGroup>' }
            @{ Name = 'runtime-copy'; Old = '</ItemGroup>'; New = '<None Include="runtime.dll"><CopyToOutputDirectory>Always</CopyToOutputDirectory></None></ItemGroup>' }
            @{ Name = 'extra-project'; Old = '</ItemGroup>'; New = '<ProjectReference Include="..\..\src\Extra\Extra.csproj" /></ItemGroup>' }
            @{ Name = 'missing-core-project'; Old = '<ProjectReference Include="..\..\src\Als.Core\Als.Core.csproj" />'; New = '' }
            @{ Name = 'duplicate-import-project'; Old = '<ProjectReference Include="..\..\src\Als.Import\Als.Import.csproj" />'; New = '<ProjectReference Include="..\..\src\Als.Import\Als.Import.csproj" /><ProjectReference Include="..\..\src\Als.Import\Als.Import.csproj" />' }
            @{ Name = 'duplicate-exec'; Old = '<Exec Command="&quot;$(TargetDir)Als.P5aOracle.exe&quot; --write-build-manifest --repository-root &quot;$(P5aRepositoryRoot)&quot; --output &quot;$(TargetDir)p5a-oracle-build.manifest&quot; --sdk-version &quot;$(NETCoreSdkVersion)&quot;" />'; New = '<Exec Command="&quot;$(TargetDir)Als.P5aOracle.exe&quot; --write-build-manifest --repository-root &quot;$(P5aRepositoryRoot)&quot; --output &quot;$(TargetDir)p5a-oracle-build.manifest&quot; --sdk-version &quot;$(NETCoreSdkVersion)&quot;" /><Exec Command="&quot;$(TargetDir)Als.P5aOracle.exe&quot; --write-build-manifest --repository-root &quot;$(P5aRepositoryRoot)&quot; --output &quot;$(TargetDir)p5a-oracle-build.manifest&quot; --sdk-version &quot;$(NETCoreSdkVersion)&quot;" />' }
            @{ Name = 'duplicate-target'; Old = '<Target Name="WriteP5aOracleBuildManifest"'; New = '<Target Name="DuplicateP5aOracleBuildManifest" AfterTargets="Build"><Exec Command="&quot;$(TargetDir)Als.P5aOracle.exe&quot; --write-build-manifest --repository-root &quot;$(P5aRepositoryRoot)&quot; --output &quot;$(TargetDir)duplicate.manifest&quot; --sdk-version &quot;$(NETCoreSdkVersion)&quot;" /></Target><Target Name="WriteP5aOracleBuildManifest"' }
        )
        foreach ($mutation in $projectMutations)
        {
            $fixture = New-TestP5aOracleRepository "oracle-project-$($mutation.Name)"
            $source = [IO.File]::ReadAllText($fixture.ProjectPath)
            $source.Contains($mutation.Old, [StringComparison]::Ordinal) | Should Be $true
            Write-TestP5aText $fixture.ProjectPath ($source.Replace($mutation.Old, $mutation.New))
            Set-TestP5aBuildManifestField `
                -ManifestPath $fixture.ManifestPath `
                -Name 'sourceTreeSha256' `
                -Value (Get-TestP5aTreeHash $fixture.Root $fixture.SourceRelativePaths)
            Test-TestP5aRejects {
                Assert-P5aOracleProjectContract -RepositoryRoot $fixture.Root
            } | Should Be $true
        }

        $missingImport = New-TestP5aOracleRepository 'oracle-project-missing-import'
        $xml = [IO.File]::ReadAllText($missingImport.ProjectPath)
        $xml = $xml -replace '(?m)^\s*<ProjectReference Include="\.\.\\\.\.\\src\\Als\.Import\\Als\.Import\.csproj" />\r?\n', ''
        Write-TestP5aText $missingImport.ProjectPath $xml
        Set-TestP5aBuildManifestField `
            -ManifestPath $missingImport.ManifestPath `
            -Name 'sourceTreeSha256' `
            -Value (Get-TestP5aTreeHash $missingImport.Root $missingImport.SourceRelativePaths)
        Test-TestP5aRejects {
            Assert-P5aOracleProjectContract -RepositoryRoot $missingImport.Root
        } | Should Be $true

        $extraDeps = New-TestP5aOracleRepository 'oracle-project-extra-deps'
        $deps = [IO.File]::ReadAllText($extraDeps.DepsPath)
        $deps.Contains('"Als.Core/1.0.0":{"type":"project"', [StringComparison]::Ordinal) |
            Should Be $true
        $deps = $deps.Replace(
            '"Als.Core/1.0.0":{"type":"project"',
            '"Synthetic.Local/1.0.0":{"type":"project","serviceable":false,"sha512":""},"Als.Core/1.0.0":{"type":"project"')
        Write-TestP5aText $extraDeps.DepsPath $deps
        Set-TestP5aBuildManifestField `
            -ManifestPath $extraDeps.ManifestPath `
            -Name 'depsSha256' `
            -Value (Get-TestP5aFileHash $extraDeps.DepsPath)
        Test-TestP5aRejects {
            Assert-P5aOracleProjectContract -RepositoryRoot $extraDeps.Root
        } | Should Be $true

        $depsMutations = @(
            @{ Name = 'missing-oracle'; Old = '"Als.P5aOracle/1.0.0":{"type":"project","serviceable":false,"sha512":""},'; New = '' }
            @{ Name = 'missing-import'; Old = '"Als.Import/1.0.0":{"type":"project","serviceable":false,"sha512":""},'; New = '' }
            @{ Name = 'missing-core'; Old = ',"Als.Core/1.0.0":{"type":"project","serviceable":false,"sha512":""}'; New = '' }
            @{ Name = 'package-library'; Old = '"Als.Import/1.0.0":{"type":"project"'; New = '"Als.Import/1.0.0":{"type":"package"' }
            @{ Name = 'extra-runtime-dll'; Old = '"Als.P5aOracle.dll":{}'; New = '"Als.P5aOracle.dll":{},"Synthetic.Local.dll":{}' }
            @{ Name = 'wrong-framework'; Old = '.NETCoreApp,Version=v8.0'; New = '.NETCoreApp,Version=v9.0' }
        )
        foreach ($mutation in $depsMutations)
        {
            $invalidDeps = New-TestP5aOracleRepository "oracle-deps-$($mutation.Name)"
            $depsText = [IO.File]::ReadAllText($invalidDeps.DepsPath)
            $depsText.Contains($mutation.Old, [StringComparison]::Ordinal) | Should Be $true
            Write-TestP5aText $invalidDeps.DepsPath ($depsText.Replace($mutation.Old, $mutation.New))
            Set-TestP5aBuildManifestField `
                -ManifestPath $invalidDeps.ManifestPath `
                -Name 'depsSha256' `
                -Value (Get-TestP5aFileHash $invalidDeps.DepsPath)
            Test-TestP5aRejects {
                Assert-P5aOracleProjectContract -RepositoryRoot $invalidDeps.Root
            } | Should Be $true
        }
    }

    It 'builds the real Oracle then executes its closed modes and private build-manifest contract against TestDrive' {
        Assert-TestP5aPathCapability $script:P5aOracleProjectPath 'tools/Als.P5aOracle/Als.P5aOracle.csproj'
        Assert-TestP5aPathCapability $script:P5aOracleProgramPath 'tools/Als.P5aOracle/Program.cs'
        Assert-TestP5aCommandCapability 'Assert-P5aOracleProjectContract' generator
        Assert-TestP5aCommandCapability 'Open-P5aOracleEvidenceLease' generator
        $dotnetPath = Get-TestP5aDotnetApplicationPath

        $build = Invoke-TestP5aExternalProcess `
            -FilePath $dotnetPath `
            -Arguments @(
                'build', 'tools/Als.P5aOracle/Als.P5aOracle.csproj',
                '-c', 'Release', '--nologo', '--no-incremental') `
            -WorkingDirectory $script:P5aRepositoryRoot
        if ($build.ExitCode -ne 0)
        {
            "Missing 13B capability: real Oracle Release build ($($build.StdErr)$($build.StdOut))" |
                Should BeNullOrEmpty
        }

        Assert-P5aOracleProjectContract -RepositoryRoot $script:P5aRepositoryRoot
        $actualOutput = Join-Path $script:P5aRepositoryRoot 'tools\Als.P5aOracle\bin\Release\net8.0'
        $actualAppHost = Join-Path $actualOutput 'Als.P5aOracle.exe'
        Assert-TestP5aPathCapability $actualAppHost 'fixed Release Oracle apphost'

        $synthetic = New-TestP5aOracleRepository 'real-oracle-build-manifest-smoke' -WithoutArtifacts
        [void][IO.Directory]::CreateDirectory($synthetic.OutputPath)
        foreach ($artifactName in $synthetic.ArtifactNames)
        {
            $sourceArtifact = Join-Path $actualOutput $artifactName
            Assert-TestP5aPathCapability $sourceArtifact "real Oracle artifact $artifactName"
            [IO.File]::Copy($sourceArtifact, (Join-Path $synthetic.OutputPath $artifactName), $true)
            [IO.File]::SetLastWriteTimeUtc(
                (Join-Path $synthetic.OutputPath $artifactName), [DateTime]::UtcNow)
        }
        $manifestRun = Invoke-TestP5aExternalProcess `
            -FilePath $synthetic.AppHostPath `
            -Arguments @(
                '--write-build-manifest',
                '--repository-root', $synthetic.Root,
                '--output', $synthetic.ManifestPath,
                '--sdk-version', '8.0.100') `
            -WorkingDirectory $synthetic.Root
        $manifestRun.ExitCode | Should Be 0
        ($manifestRun.StdOut + $manifestRun.StdErr) | Should Not Match 'P5A_'
        Test-Path -LiteralPath $synthetic.ManifestPath -PathType Leaf | Should Be $true
        $manifestBytes = [IO.File]::ReadAllBytes($synthetic.ManifestPath)
        $manifestBytes[0] | Should Not Be 0xef
        [Text.Encoding]::UTF8.GetString($manifestBytes) | Should Not Match "`r"
        [Text.Encoding]::UTF8.GetString($manifestBytes) | Should Match "runtimeConfigSha256=[0-9a-f]{64}`n$"
        $lease = Open-P5aOracleEvidenceLease -RepositoryRoot $synthetic.Root
        $lease.Dispose()

        $bundleRoot = Join-Path $TestDrive 'real-oracle-runtime-bundle'
        [void][IO.Directory]::CreateDirectory($bundleRoot)
        $bundleRun = Invoke-TestP5aExternalProcess `
            -FilePath $dotnetPath `
            -Arguments @(
                'test', 'tests/Als.Core.Tests/Als.Core.Tests.csproj',
                '-c', 'Debug',
                '--filter',
                'FullyQualifiedName=GodotAls.Core.Tests.AlsP5aTraceSchemaTests.SyntheticHarnessBuildsCanonicalBytesAndEveryMandatoryFamilyWithoutProductionCapabilities',
                '--nologo') `
            -WorkingDirectory $script:P5aRepositoryRoot `
            -Environment @{ GODOTALS_P5A_TEST_BUNDLE_DIRECTORY = $bundleRoot }
        if ($bundleRun.ExitCode -ne 0)
        {
            "Missing 13B capability: test-owned real Oracle mode bundle ($($bundleRun.StdErr)$($bundleRun.StdOut))" |
                Should BeNullOrEmpty
        }
        $bundle = [pscustomobject]@{
            Plan = Join-Path $bundleRoot 'plan.json'
            Raw = Join-Path $bundleRoot 'raw.json'
            Native = Join-Path $bundleRoot 'native.json'
            Fixture = Join-Path $bundleRoot 'fixture.json'
        }
        foreach ($property in $bundle.PSObject.Properties)
        {
            Assert-TestP5aPathCapability ([string]$property.Value) "test-owned bundle $($property.Name)"
            Assert-TestP5aPathUnderRoot ([string]$property.Value) $TestDrive
        }
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($bundle.Fixture)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($bundle.Native)))

        $planPath = Join-Path $TestDrive 'real-oracle-runtime-plan.json'
        $runtimeRun = Invoke-TestP5aExternalProcess `
            -FilePath $actualAppHost `
            -Arguments @(
                '--write-native-plan', '--repository-root', $script:P5aRepositoryRoot,
                '--output', $planPath) `
            -WorkingDirectory $script:P5aRepositoryRoot
        $runtimeRun.ExitCode | Should Be 0
        Test-Path -LiteralPath $planPath -PathType Leaf | Should Be $true
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($planPath)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($bundle.Plan)))
        $planHash = Get-TestP5aFileHash $planPath
        $expectedRuntimeMarker = "P5A_ORACLE_DIGESTS layout=$script:P5aLayoutDigest bindings=$script:P5aBindingDigest graph=$script:P5aGraphDigest plan=$planHash"
        $runtimeLines = @(($runtimeRun.StdOut + $runtimeRun.StdErr) -split '\r?\n' |
            Where-Object { -not [string]::IsNullOrEmpty($_) })
        @($runtimeLines | Where-Object { $_ -ceq $expectedRuntimeMarker }).Count | Should Be 1
        @($runtimeLines | Where-Object { $_ -match '^P5A_' -and $_ -cne $expectedRuntimeMarker }).Count |
            Should Be 0

        $actualNative = Join-Path $TestDrive 'real-oracle-native.json'
        $actualPort = Join-Path $TestDrive 'real-oracle-port.json'
        $actualNativeRepeat = Join-Path $TestDrive 'real-oracle-native-repeat.json'
        $actualPortRepeat = Join-Path $TestDrive 'real-oracle-port-repeat.json'
        $canonicalRun = Invoke-TestP5aExternalProcess `
            -FilePath $actualAppHost `
            -Arguments @(
                '--write-canonical-pair', '--repository-root', $script:P5aRepositoryRoot,
                '--trace-plan', $planPath, '--raw', $bundle.Raw,
                '--native-canonical', $actualNative, '--port-canonical', $actualPort) `
            -WorkingDirectory $script:P5aRepositoryRoot
        $canonicalRun.ExitCode | Should Be 0
        $canonicalLines = @(($canonicalRun.StdOut + $canonicalRun.StdErr) -split '\r?\n' |
            Where-Object { -not [string]::IsNullOrEmpty($_) })
        @($canonicalLines | Where-Object { $_ -ceq $expectedRuntimeMarker }).Count | Should Be 1
        @($canonicalLines | Where-Object { $_ -match '^P5A_' -and $_ -cne $expectedRuntimeMarker }).Count |
            Should Be 0
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($actualNative)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($bundle.Native)))
        $canonicalRepeatRun = Invoke-TestP5aExternalProcess `
            -FilePath $actualAppHost `
            -Arguments @(
                '--write-canonical-pair', '--repository-root', $script:P5aRepositoryRoot,
                '--trace-plan', $planPath, '--raw', $bundle.Raw,
                '--native-canonical', $actualNativeRepeat,
                '--port-canonical', $actualPortRepeat) `
            -WorkingDirectory $script:P5aRepositoryRoot
        $canonicalRepeatRun.ExitCode | Should Be 0
        $canonicalRepeatLines = @(
            ($canonicalRepeatRun.StdOut + $canonicalRepeatRun.StdErr) -split '\r?\n' |
                Where-Object { -not [string]::IsNullOrEmpty($_) })
        @($canonicalRepeatLines | Where-Object { $_ -ceq $expectedRuntimeMarker }).Count |
            Should Be 1
        @($canonicalRepeatLines | Where-Object {
            $_ -match '^P5A_' -and $_ -cne $expectedRuntimeMarker
        }).Count | Should Be 0
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($actualNativeRepeat)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($actualNative)))
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($actualPortRepeat)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($actualPort)))

        $limitCases = @(
            [pscustomobject]@{
                Name = 'document-bytes'
                Diagnostic = '(?i)(document|input).*(16\s*MiB|16777216|byte.?limit)'
                Oversize = $true
                Mutate = $null
            }
            [pscustomobject]@{
                Name = 'nesting-depth'
                Diagnostic = '(?i)(nesting|json).*depth.*64'
                Oversize = $false
                Mutate = {
                    param($document)
                    $cursor = $document
                    foreach ($depth in 0..64)
                    {
                        $next = [pscustomobject]@{}
                        Add-Member -InputObject $cursor -NotePropertyName "depth$depth" `
                            -NotePropertyValue $next
                        $cursor = $next
                    }
                }
            }
            [pscustomobject]@{
                Name = 'utf8-string'
                Diagnostic = '(?i)(utf-?8|string).*(4096|length)'
                Oversize = $false
                Mutate = {
                    param($document)
                    $document.reference.repository = 'x' * 4097
                }
            }
            [pscustomobject]@{
                Name = 'event-cardinality'
                Diagnostic = '(?i)(event|cardinalit).*(16|maximum|limit)'
                Oversize = $false
                Mutate = {
                    param($document)
                    $oracle = $document.cases[5].frames[1].nativeActual.canonicalAssetOracle
                    $sample = $oracle.events[0]
                    $events = [Collections.Generic.List[object]]::new()
                    while ($events.Count -lt 17) { $events.Add($sample) }
                    $oracle.events = @($events)
                }
            }
        )
        foreach ($limitCase in $limitCases)
        {
            $limitRoot = Join-Path $TestDrive "real-oracle-limit-$($limitCase.Name)"
            [void][IO.Directory]::CreateDirectory($limitRoot)
            $limitRaw = Join-Path $limitRoot 'raw.json'
            $limitNative = Join-Path $limitRoot 'native.json'
            $limitPort = Join-Path $limitRoot 'port.json'
            $sentinel = "limit-$($limitCase.Name)-unchanged`n"
            if ($limitCase.Oversize)
            {
                [IO.File]::Copy($bundle.Raw, $limitRaw)
                $limitStream = [IO.File]::Open(
                    $limitRaw, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $limitStream.SetLength((16L * 1024 * 1024) + 1) }
                finally { $limitStream.Dispose() }
            }
            else
            {
                $limitDocument = [IO.File]::ReadAllText($bundle.Raw) |
                    ConvertFrom-Json -Depth 100
                & $limitCase.Mutate $limitDocument
                Write-TestP5aText $limitRaw (
                    ($limitDocument | ConvertTo-Json -Depth 100 -Compress) + "`n")
            }
            Write-TestP5aText $limitNative $sentinel
            Write-TestP5aText $limitPort $sentinel
            $limitRun = Invoke-TestP5aExternalProcess `
                -FilePath $actualAppHost `
                -Arguments @(
                    '--write-canonical-pair', '--repository-root', $script:P5aRepositoryRoot,
                    '--trace-plan', $planPath, '--raw', $limitRaw,
                    '--native-canonical', $limitNative, '--port-canonical', $limitPort) `
                -WorkingDirectory $script:P5aRepositoryRoot
            $limitRun.ExitCode | Should Not Be 0
            ($limitRun.StdOut + $limitRun.StdErr) | Should Match $limitCase.Diagnostic
            ($limitRun.StdOut + $limitRun.StdErr) |
                Should Not Match '(?m)^P5A_ORACLE_DIGESTS '
            [IO.File]::ReadAllText($limitNative) | Should Be $sentinel
            [IO.File]::ReadAllText($limitPort) | Should Be $sentinel
            @(Get-ChildItem -LiteralPath $limitRoot -File).Count | Should Be 3
        }

        $verifyStagingRoot = Join-Path $TestDrive 'real-oracle-verify-staging'
        [void][IO.Directory]::CreateDirectory($verifyStagingRoot)
        $verifyRun = Invoke-TestP5aExternalProcess `
            -FilePath $actualAppHost `
            -Arguments @(
                '--verify-fixture', '--repository-root', $script:P5aRepositoryRoot,
                '--fixture', $bundle.Fixture) `
            -WorkingDirectory $script:P5aRepositoryRoot `
            -Environment @{
                GODOTALS_P5A_STAGING_ROOT = [IO.Path]::GetFullPath($verifyStagingRoot)
            }
        $verifyRun.ExitCode | Should Be 0
        $verifyLines = @(($verifyRun.StdOut + $verifyRun.StdErr) -split '\r?\n' |
            Where-Object { -not [string]::IsNullOrEmpty($_) })
        @($verifyLines | Where-Object { $_ -ceq $expectedRuntimeMarker }).Count | Should Be 1
        @($verifyLines | Where-Object { $_ -match '^P5A_' -and $_ -cne $expectedRuntimeMarker }).Count |
            Should Be 0
        @(Get-ChildItem -LiteralPath $verifyStagingRoot -Force).Count | Should Be 0

        $nonemptyVerifyStagingRoot = Join-Path $TestDrive 'real-oracle-verify-nonempty'
        [void][IO.Directory]::CreateDirectory($nonemptyVerifyStagingRoot)
        Write-TestP5aText (Join-Path $nonemptyVerifyStagingRoot 'sentinel.txt') "unchanged`n"
        $invalidVerifyEnvironments = @(
            [pscustomobject]@{
                Name = 'missing'; Value = $null
                Diagnostic = '(?i)GODOTALS_P5A_STAGING_ROOT.*(missing|required)'
            }
            [pscustomobject]@{
                Name = 'relative'; Value = 'relative-p5a-staging'
                Diagnostic = '(?i)staging.*absolute'
            }
            [pscustomobject]@{
                Name = 'unsafe-repo'; Value = $script:P5aRepositoryRoot
                Diagnostic = '(?i)staging.*(unsafe|repository|containment)'
            }
            [pscustomobject]@{
                Name = 'wrong-file'; Value = $bundle.Fixture
                Diagnostic = '(?i)staging.*directory'
            }
            [pscustomobject]@{
                Name = 'nonempty'
                Value = [IO.Path]::GetFullPath($nonemptyVerifyStagingRoot)
                Diagnostic = '(?i)staging.*non.?empty'
            }
        )
        $verifyEnvironmentHashes = @{
            fixture = Get-TestP5aFileHash $bundle.Fixture
            plan = Get-TestP5aFileHash $planPath
            native = Get-TestP5aFileHash $actualNative
            port = Get-TestP5aFileHash $actualPort
        }
        foreach ($invalidVerifyEnvironment in $invalidVerifyEnvironments)
        {
            $environment = @{
                GODOTALS_P5A_STAGING_ROOT = $invalidVerifyEnvironment.Value
            }
            $invalidVerifyRun = Invoke-TestP5aExternalProcess `
                -FilePath $actualAppHost `
                -Arguments @(
                    '--verify-fixture', '--repository-root', $script:P5aRepositoryRoot,
                    '--fixture', $bundle.Fixture) `
                -WorkingDirectory $script:P5aRepositoryRoot `
                -Environment $environment
            $invalidVerifyRun.ExitCode | Should Not Be 0
            $invalidVerifyDiagnostic = $invalidVerifyRun.StdOut + $invalidVerifyRun.StdErr
            $invalidVerifyDiagnostic | Should Match $invalidVerifyEnvironment.Diagnostic
            $invalidVerifyDiagnostic | Should Not Match '(?m)^P5A_'
            if ($invalidVerifyEnvironment.Name -ceq 'unsafe-repo')
            {
                $invalidVerifyDiagnostic | Should Not Match '(?i)non.?empty'
            }
            (Get-TestP5aFileHash $bundle.Fixture) | Should Be $verifyEnvironmentHashes.fixture
            (Get-TestP5aFileHash $planPath) | Should Be $verifyEnvironmentHashes.plan
            (Get-TestP5aFileHash $actualNative) | Should Be $verifyEnvironmentHashes.native
            (Get-TestP5aFileHash $actualPort) | Should Be $verifyEnvironmentHashes.port
            @(Get-ChildItem -LiteralPath $verifyStagingRoot -Force).Count | Should Be 0
            [IO.File]::ReadAllText((Join-Path $nonemptyVerifyStagingRoot 'sentinel.txt')) |
                Should Be "unchanged`n"
            @(Get-ChildItem -LiteralPath $nonemptyVerifyStagingRoot -Force).Count |
                Should Be 1
            Test-Path -LiteralPath (Join-Path $script:P5aRepositoryRoot 'relative-p5a-staging') |
                Should Be $false
        }

        $invalidModes = @(
            [pscustomobject]@{ Name = 'missing-mode'; Arguments = [string[]]@() }
            [pscustomobject]@{ Name = 'unknown-mode'; Arguments = [string[]]@('--unknown') }

            [pscustomobject]@{ Name = 'build-duplicate-mode'; Arguments = [string[]]@(
                '--write-build-manifest', '--write-build-manifest',
                '--repository-root', $synthetic.Root,
                '--output', $synthetic.ManifestPath, '--sdk-version', '8.0.100') }
            [pscustomobject]@{ Name = 'build-unknown-argument'; Arguments = [string[]]@(
                '--write-build-manifest', '--repository-root', $synthetic.Root,
                '--output', $synthetic.ManifestPath, '--sdk-version', '8.0.100',
                '--unknown', 'value') }
            [pscustomobject]@{ Name = 'build-runtime-only-fixture'; Arguments = [string[]]@(
                '--write-build-manifest', '--repository-root', $synthetic.Root,
                '--output', $synthetic.ManifestPath, '--sdk-version', '8.0.100',
                '--fixture', $bundle.Fixture) }
            [pscustomobject]@{ Name = 'build-runtime-only-trace-plan'; Arguments = [string[]]@(
                '--write-build-manifest', '--repository-root', $synthetic.Root,
                '--output', $synthetic.ManifestPath, '--sdk-version', '8.0.100',
                '--trace-plan', $planPath) }
            [pscustomobject]@{ Name = 'build-wrong-order'; Arguments = [string[]]@(
                '--write-build-manifest', '--output', $synthetic.ManifestPath,
                '--repository-root', $synthetic.Root, '--sdk-version', '8.0.100') }
            [pscustomobject]@{ Name = 'build-mixed-runtime-mode'; Arguments = [string[]]@(
                '--write-build-manifest', '--repository-root', $synthetic.Root,
                '--output', $synthetic.ManifestPath, '--sdk-version', '8.0.100',
                '--verify-fixture') }

            [pscustomobject]@{ Name = 'native-duplicate-mode'; Arguments = [string[]]@(
                '--write-native-plan', '--write-native-plan',
                '--repository-root', $script:P5aRepositoryRoot, '--output', $planPath) }
            [pscustomobject]@{ Name = 'native-unknown-argument'; Arguments = [string[]]@(
                '--write-native-plan', '--repository-root', $script:P5aRepositoryRoot,
                '--output', $planPath, '--unknown', 'value') }
            [pscustomobject]@{ Name = 'native-build-only-sdk'; Arguments = [string[]]@(
                '--write-native-plan', '--repository-root', $script:P5aRepositoryRoot,
                '--output', $planPath, '--sdk-version', '8.0.100') }
            [pscustomobject]@{ Name = 'native-other-runtime-fixture'; Arguments = [string[]]@(
                '--write-native-plan', '--repository-root', $script:P5aRepositoryRoot,
                '--output', $planPath, '--fixture', $bundle.Fixture) }
            [pscustomobject]@{ Name = 'native-duplicate-output'; Arguments = [string[]]@(
                '--write-native-plan', '--repository-root', $script:P5aRepositoryRoot,
                '--output', $planPath, '--output', $planPath) }
            [pscustomobject]@{ Name = 'native-wrong-order'; Arguments = [string[]]@(
                '--write-native-plan', '--output', $planPath,
                '--repository-root', $script:P5aRepositoryRoot) }

            [pscustomobject]@{ Name = 'canonical-duplicate-mode'; Arguments = [string[]]@(
                '--write-canonical-pair', '--write-canonical-pair',
                '--repository-root', $script:P5aRepositoryRoot,
                '--trace-plan', $planPath, '--raw', $bundle.Raw,
                '--native-canonical', $actualNative, '--port-canonical', $actualPort) }
            [pscustomobject]@{ Name = 'canonical-unknown-argument'; Arguments = [string[]]@(
                '--write-canonical-pair', '--repository-root', $script:P5aRepositoryRoot,
                '--trace-plan', $planPath, '--raw', $bundle.Raw,
                '--native-canonical', $actualNative, '--port-canonical', $actualPort,
                '--unknown', 'value') }
            [pscustomobject]@{ Name = 'canonical-build-only-sdk'; Arguments = [string[]]@(
                '--write-canonical-pair', '--repository-root', $script:P5aRepositoryRoot,
                '--trace-plan', $planPath, '--raw', $bundle.Raw,
                '--native-canonical', $actualNative, '--port-canonical', $actualPort,
                '--sdk-version', '8.0.100') }
            [pscustomobject]@{ Name = 'canonical-build-only-output'; Arguments = [string[]]@(
                '--write-canonical-pair', '--repository-root', $script:P5aRepositoryRoot,
                '--trace-plan', $planPath, '--raw', $bundle.Raw,
                '--native-canonical', $actualNative, '--port-canonical', $actualPort,
                '--output', $planPath) }
            [pscustomobject]@{ Name = 'canonical-other-runtime-fixture'; Arguments = [string[]]@(
                '--write-canonical-pair', '--repository-root', $script:P5aRepositoryRoot,
                '--trace-plan', $planPath, '--raw', $bundle.Raw,
                '--native-canonical', $actualNative, '--port-canonical', $actualPort,
                '--fixture', $bundle.Fixture) }
            [pscustomobject]@{ Name = 'canonical-duplicate-raw'; Arguments = [string[]]@(
                '--write-canonical-pair', '--repository-root', $script:P5aRepositoryRoot,
                '--trace-plan', $planPath, '--raw', $bundle.Raw, '--raw', $bundle.Raw,
                '--native-canonical', $actualNative, '--port-canonical', $actualPort) }
            [pscustomobject]@{ Name = 'canonical-wrong-order'; Arguments = [string[]]@(
                '--write-canonical-pair', '--repository-root', $script:P5aRepositoryRoot,
                '--raw', $bundle.Raw, '--trace-plan', $planPath,
                '--native-canonical', $actualNative, '--port-canonical', $actualPort) }

            [pscustomobject]@{ Name = 'verify-duplicate-mode'; Arguments = [string[]]@(
                '--verify-fixture', '--verify-fixture',
                '--repository-root', $script:P5aRepositoryRoot, '--fixture', $bundle.Fixture) }
            [pscustomobject]@{ Name = 'verify-unknown-argument'; Arguments = [string[]]@(
                '--verify-fixture', '--repository-root', $script:P5aRepositoryRoot,
                '--fixture', $bundle.Fixture, '--unknown', 'value') }
            [pscustomobject]@{ Name = 'verify-build-only-sdk'; Arguments = [string[]]@(
                '--verify-fixture', '--repository-root', $script:P5aRepositoryRoot,
                '--fixture', $bundle.Fixture, '--sdk-version', '8.0.100') }
            [pscustomobject]@{ Name = 'verify-build-only-output'; Arguments = [string[]]@(
                '--verify-fixture', '--repository-root', $script:P5aRepositoryRoot,
                '--fixture', $bundle.Fixture, '--output', $planPath) }
            [pscustomobject]@{ Name = 'verify-other-runtime-trace-plan'; Arguments = [string[]]@(
                '--verify-fixture', '--repository-root', $script:P5aRepositoryRoot,
                '--fixture', $bundle.Fixture, '--trace-plan', $planPath) }
            [pscustomobject]@{ Name = 'verify-duplicate-fixture'; Arguments = [string[]]@(
                '--verify-fixture', '--repository-root', $script:P5aRepositoryRoot,
                '--fixture', $bundle.Fixture, '--fixture', $bundle.Fixture) }
            [pscustomobject]@{ Name = 'verify-wrong-order'; Arguments = [string[]]@(
                '--verify-fixture', '--fixture', $bundle.Fixture,
                '--repository-root', $script:P5aRepositoryRoot) }
        )
        $manifestHashBeforeInvalidModes = Get-TestP5aFileHash $synthetic.ManifestPath
        $planHashBeforeInvalidModes = Get-TestP5aFileHash $planPath
        $nativeHashBeforeInvalidModes = Get-TestP5aFileHash $actualNative
        $portHashBeforeInvalidModes = Get-TestP5aFileHash $actualPort
        foreach ($invalidMode in $invalidModes)
        {
            $invalidEnvironment = if (
                $invalidMode.Arguments.Count -gt 0 -and
                $invalidMode.Arguments[0] -ceq '--verify-fixture') {
                @{ GODOTALS_P5A_STAGING_ROOT = [IO.Path]::GetFullPath($verifyStagingRoot) }
            } else {
                @{}
            }
            $invalidRun = Invoke-TestP5aExternalProcess `
                -FilePath $actualAppHost `
                -Arguments $invalidMode.Arguments `
                -WorkingDirectory $script:P5aRepositoryRoot `
                -Environment $invalidEnvironment
            $invalidRun.ExitCode | Should Not Be 0
            ($invalidRun.StdOut + $invalidRun.StdErr) | Should Not Match '(?m)^P5A_ORACLE_DIGESTS '
            (Get-TestP5aFileHash $synthetic.ManifestPath) | Should Be $manifestHashBeforeInvalidModes
            (Get-TestP5aFileHash $planPath) | Should Be $planHashBeforeInvalidModes
            (Get-TestP5aFileHash $actualNative) | Should Be $nativeHashBeforeInvalidModes
            (Get-TestP5aFileHash $actualPort) | Should Be $portHashBeforeInvalidModes
            @(Get-ChildItem -LiteralPath $verifyStagingRoot -Force).Count | Should Be 0
        }
    }

    It 'closes the Oracle source tree over fixed roots and excludes every bin or obj segment ordinal-ignore-case' {
        Assert-TestP5aCommandCapability 'Get-P5aOracleSourceClosure' generator
        $fixture = New-TestP5aOracleRepository 'oracle-source-closure'
        $closure = Get-P5aOracleSourceClosure -RepositoryRoot $fixture.Root
        @($closure.RelativePaths) | Should Be @($fixture.SourceRelativePaths)
        $closure.TreeSha256 | Should Be (Get-TestP5aTreeHash $fixture.Root $fixture.SourceRelativePaths)
        @($closure.RelativePaths | Where-Object { $_ -match '(^|/)(bin|obj)(/|$)' }).Count | Should Be 0

        $baseline = $closure.TreeSha256
        foreach ($relativePath in $fixture.SourceRelativePaths)
        {
            $copy = New-TestP5aOracleRepository (
                'oracle-source-mutate-' + ($relativePath -replace '[^A-Za-z0-9]', '-'))
            [IO.File]::AppendAllText((Join-Path $copy.Root $relativePath), 'mutation')
            (Get-P5aOracleSourceClosure -RepositoryRoot $copy.Root).TreeSha256 | Should Not Be $baseline
        }

        [IO.File]::AppendAllText((Join-Path $fixture.Root 'src\Als.Import\bin\Generated.cs'), 'ignored mutation')
        [IO.File]::AppendAllText((Join-Path $fixture.Root 'src\Als.Core\OBJ\Generated.cs'), 'ignored mutation')
        (Get-P5aOracleSourceClosure -RepositoryRoot $fixture.Root).TreeSha256 | Should Be $baseline
        Write-TestP5aText (Join-Path $fixture.Root 'src\Als.Core\NewEligible.cs') "namespace Als.Core; class NewEligible { }`n"
        $expanded = Get-P5aOracleSourceClosure -RepositoryRoot $fixture.Root
        @($expanded.RelativePaths | Where-Object { $_ -ceq 'src/Als.Core/NewEligible.cs' }).Count | Should Be 1
        $expanded.TreeSha256 | Should Not Be $baseline

        $empty = New-TestP5aOracleRepository 'oracle-source-empty-family'
        Remove-Item -LiteralPath (Join-Path $empty.Root 'src\Als.Import\ImportOne.cs') -Force
        Remove-Item -LiteralPath (Join-Path $empty.Root 'src\Als.Import\Nested\ImportTwo.cs') -Force
        Test-TestP5aRejects {
            Get-P5aOracleSourceClosure -RepositoryRoot $empty.Root | Out-Null
        } | Should Be $true
    }

    It 'audits canonical manifest bytes all six Release artifacts freshness handles and post-run hash drift' {
        Assert-TestP5aCommandCapability 'Open-P5aOracleEvidenceLease' generator
        Assert-TestP5aCommandCapability 'Assert-P5aOracleEvidenceUnchanged' generator
        $fixture = New-TestP5aOracleRepository 'oracle-evidence-valid'
        $lease = Open-P5aOracleEvidenceLease -RepositoryRoot $fixture.Root
        $leaseHandles = @()
        try
        {
            @($lease.Evidence.PSObject.Properties.Name) | Should Be @(
                'sdkVersion', 'sourceTreeSha256', 'buildManifestSha256',
                'executableSha256', 'oracleAssemblySha256', 'importAssemblySha256',
                'coreAssemblySha256', 'depsSha256', 'runtimeConfigSha256')
            $lease.AppHostPath | Should Be $fixture.AppHostPath
            @($lease.SourcePaths) | Should Be @($fixture.SourcePaths)
            $lease.ManifestPath | Should Be $fixture.ManifestPath
            @($lease.ArtifactPaths) | Should Be @($fixture.ArtifactPaths)
            $heldPaths = @($lease.SourcePaths) + @($lease.ManifestPath) + @($lease.ArtifactPaths)
            $leaseHandles = @($lease.Handles)
            $leaseHandles.Count | Should Be $heldPaths.Count
            @($leaseHandles | ForEach-Object {
                [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($_)
            } | Sort-Object -Unique).Count | Should Be $heldPaths.Count
            @($leaseHandles | ForEach-Object { [IO.Path]::GetFullPath($_.Name) } | Sort-Object) |
                Should Be @($heldPaths | ForEach-Object { [IO.Path]::GetFullPath($_) } | Sort-Object)
            foreach ($heldPath in $heldPaths)
            {
                Test-TestP5aRejects {
                    $writer = [IO.File]::Open(
                        $heldPath, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::Read)
                    $writer.Dispose()
                } | Should Be $true
            }
            $snapshot = $lease.Evidence
        }
        finally
        {
            $lease.Dispose()
        }
        foreach ($leaseHandle in $leaseHandles)
        {
            $leaseHandle.SafeFileHandle.IsClosed | Should Be $true
        }
        [IO.File]::AppendAllText($fixture.ArtifactPaths[0], 'drift')
        Test-TestP5aRejects {
            Assert-P5aOracleEvidenceUnchanged -RepositoryRoot $fixture.Root -ExpectedEvidence $snapshot
        } | Should Be $true

        foreach ($artifactName in $fixture.ArtifactNames)
        {
            $mutated = New-TestP5aOracleRepository "oracle-artifact-$artifactName"
            [IO.File]::AppendAllText((Join-Path $mutated.OutputPath $artifactName), 'hash drift')
            Test-TestP5aRejects {
                $invalidLease = Open-P5aOracleEvidenceLease -RepositoryRoot $mutated.Root
                $invalidLease.Dispose()
            } | Should Be $true
        }
        foreach ($freshName in @('Als.P5aOracle.exe', 'Als.P5aOracle.dll', 'Als.Import.dll', 'Als.Core.dll'))
        {
            $stale = New-TestP5aOracleRepository "oracle-stale-$freshName"
            [IO.File]::SetLastWriteTimeUtc(
                (Join-Path $stale.OutputPath $freshName), [DateTime]::UtcNow.AddDays(-1))
            Test-TestP5aRejects {
                $invalidLease = Open-P5aOracleEvidenceLease -RepositoryRoot $stale.Root
                $invalidLease.Dispose()
            } | Should Be $true
        }

        $manifestMutations = @(
            @{ Name = 'bom'; Apply = {
                param($path)
                [IO.File]::WriteAllText($path, [IO.File]::ReadAllText($path), [Text.UTF8Encoding]::new($true))
            } }
            @{ Name = 'crlf'; Apply = {
                param($path)
                Write-TestP5aText $path (([IO.File]::ReadAllText($path) -replace "`n", "`r`n"))
            } }
            @{ Name = 'extra-final-lf'; Apply = {
                param($path)
                [IO.File]::AppendAllText($path, "`n")
            } }
            @{ Name = 'uppercase-hash'; Apply = {
                param($path)
                Write-TestP5aText $path ([IO.File]::ReadAllText($path).Replace('sourceTreeSha256=', 'sourceTreeSha256=ABCDEF'))
            } }
            @{ Name = 'debug'; Apply = {
                param($path)
                Write-TestP5aText $path ([IO.File]::ReadAllText($path).Replace('configuration=Release', 'configuration=Debug'))
            } }
            @{ Name = 'extra-line'; Apply = {
                param($path)
                Write-TestP5aText $path ([IO.File]::ReadAllText($path) + "extra=true`n")
            } }
        )
        foreach ($mutation in $manifestMutations)
        {
            $invalid = New-TestP5aOracleRepository "oracle-manifest-$($mutation.Name)"
            & $mutation.Apply $invalid.ManifestPath
            Test-TestP5aRejects {
                $invalidLease = Open-P5aOracleEvidenceLease -RepositoryRoot $invalid.Root
                $invalidLease.Dispose()
            } | Should Be $true
        }
    }

    It 'fails a clean synthetic checkout without the fixed apphost and audits a valid build without starting a child' {
        Assert-TestP5aCommandCapability 'Open-P5aOracleEvidenceLease' generator
        $clean = New-TestP5aOracleRepository 'oracle-clean-no-apphost' -WithoutArtifacts
        $childLog = [Collections.Generic.List[string]]::new()
        Test-TestP5aRejects {
            $invalidLease = Open-P5aOracleEvidenceLease -RepositoryRoot $clean.Root
            $invalidLease.Dispose()
        } | Should Be $true
        $childLog.Count | Should Be 0

        foreach ($alternate in @('Debug\net8.0', 'Release\net8.0\win-x64'))
        {
            $wrong = New-TestP5aOracleRepository (
                'oracle-alternate-' + ($alternate -replace '[^A-Za-z0-9]', '-')) -WithoutArtifacts
            $alternateRoot = Join-Path $wrong.Root "tools\Als.P5aOracle\bin\$alternate"
            Write-TestP5aText (Join-Path $alternateRoot 'Als.P5aOracle.exe') "alternate apphost`n"
            Test-TestP5aRejects {
                $invalidLease = Open-P5aOracleEvidenceLease -RepositoryRoot $wrong.Root
                $invalidLease.Dispose()
            } | Should Be $true
        }

        $valid = New-TestP5aOracleRepository 'oracle-audit-no-child'
        $lease = Open-P5aOracleEvidenceLease -RepositoryRoot $valid.Root
        try
        {
            $lease.Evidence.sourceTreeSha256 | Should Match '^[0-9a-f]{64}$'
            $childLog.Count | Should Be 0
        }
        finally
        {
            $lease.Dispose()
        }
    }

    It 'audits deployed owned bytes receipt BuildId manifests DLL freshness and closed build evidence' {
        Assert-TestP5aCommandCapability 'Open-P5aDeployedBuildEvidenceLease' generator
        Assert-TestP5aCommandCapability 'Assert-P5aDeployedBuildEvidenceUnchanged' generator
        $fixture = New-TestP5aDeployment 'deployment-valid'
        $lease = Open-P5aDeployedBuildEvidenceLease `
            -RepositoryRoot $fixture.RepositoryRoot `
            -UnrealProject $fixture.UProject
        $leaseHandles = @()
        try
        {
            @($lease.Evidence.PSObject.Properties.Name) | Should Be @(
                'ownedPluginTreeSha256', 'targetReceiptSha256',
                'alsModuleManifestSha256', 'traceModuleManifestSha256',
                'alsModuleDllSha256', 'traceModuleDllSha256')
            (($lease.Evidence | ConvertTo-Json -Compress) -match [regex]::Escape($fixture.Root)) |
                Should Be $false
            $expectedOwnedPaths = @(Get-ChildItem -LiteralPath $fixture.OwnedRoot -File -Recurse |
                Where-Object { $_.FullName -notmatch '[\\/](Binaries|Intermediate)([\\/]|$)' } |
                ForEach-Object { $_.FullName })
            $expectedDeployedPaths = @(Get-ChildItem -LiteralPath $fixture.DeployedRoot -File -Recurse |
                Where-Object { $_.FullName -notmatch '[\\/](Binaries|Intermediate)([\\/]|$)' } |
                ForEach-Object { $_.FullName })
            $expectedEvidencePaths = @(
                $fixture.ReceiptPath, $fixture.AlsManifestPath, $fixture.TraceManifestPath,
                $fixture.AlsDllPath, $fixture.TraceDllPath)
            @($lease.OwnedPaths | Sort-Object) | Should Be @($expectedOwnedPaths | Sort-Object)
            @($lease.DeployedPaths | Sort-Object) | Should Be @($expectedDeployedPaths | Sort-Object)
            @($lease.EvidencePaths | Sort-Object) | Should Be @($expectedEvidencePaths | Sort-Object)
            $heldPaths = @($lease.OwnedPaths) + @($lease.DeployedPaths) + @($lease.EvidencePaths)
            $leaseHandles = @($lease.Handles)
            $leaseHandles.Count | Should Be $heldPaths.Count
            @($leaseHandles | ForEach-Object {
                [Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($_)
            } | Sort-Object -Unique).Count | Should Be $heldPaths.Count
            @($leaseHandles | ForEach-Object { [IO.Path]::GetFullPath($_.Name) } | Sort-Object) |
                Should Be @($heldPaths | ForEach-Object { [IO.Path]::GetFullPath($_) } | Sort-Object)
            foreach ($heldPath in $heldPaths)
            {
                Test-TestP5aRejects {
                    $writer = [IO.File]::Open(
                        $heldPath, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::Read)
                    $writer.Dispose()
                } | Should Be $true
            }
            $snapshot = $lease.Evidence
        }
        finally
        {
            $lease.Dispose()
        }
        foreach ($leaseHandle in $leaseHandles)
        {
            $leaseHandle.SafeFileHandle.IsClosed | Should Be $true
        }
        [IO.File]::AppendAllText($fixture.TraceDllPath, 'drift')
        Test-TestP5aRejects {
            Assert-P5aDeployedBuildEvidenceUnchanged `
                -RepositoryRoot $fixture.RepositoryRoot `
                -UnrealProject $fixture.UProject `
                -ExpectedEvidence $snapshot
        } | Should Be $true

        $mutations = @(
            @{ Name = 'deployed-source'; Apply = {
                param($x) [IO.File]::AppendAllText((Join-Path $x.DeployedRoot 'Source\AlsLocomotionTrace\Private\Trace.cpp'), 'drift')
            } }
            @{ Name = 'stale-dll'; Apply = {
                param($x) [IO.File]::SetLastWriteTimeUtc($x.TraceDllPath, [DateTime]::UtcNow.AddDays(-1))
            } }
            @{ Name = 'receipt-build-id'; Apply = {
                param($x) Write-TestP5aText $x.ReceiptPath ([IO.File]::ReadAllText($x.ReceiptPath).Replace('SYNTHETIC-BUILD-ID-13B', 'WRONG'))
            } }
            @{ Name = 'als-build-id'; Apply = {
                param($x) Write-TestP5aText $x.AlsManifestPath ([IO.File]::ReadAllText($x.AlsManifestPath).Replace('SYNTHETIC-BUILD-ID-13B', 'WRONG'))
            } }
            @{ Name = 'trace-module'; Apply = {
                param($x) Write-TestP5aText $x.TraceManifestPath ([IO.File]::ReadAllText($x.TraceManifestPath).Replace('AlsLocomotionTrace', 'WrongTrace'))
            } }
            @{ Name = 'missing-als-dll'; Apply = {
                param($x) Remove-Item -LiteralPath $x.AlsDllPath -Force
            } }
            @{ Name = 'ambiguous-editor-target'; Apply = {
                param($x) Write-TestP5aText (Join-Path $x.ProjectRoot 'Source\OtherEditor.Target.cs') 'class OtherEditorTarget { }'
            } }
        )
        foreach ($mutation in $mutations)
        {
            $invalid = New-TestP5aDeployment "deployment-$($mutation.Name)"
            & $mutation.Apply $invalid
            Test-TestP5aRejects {
                $invalidLease = Open-P5aDeployedBuildEvidenceLease `
                    -RepositoryRoot $invalid.RepositoryRoot `
                    -UnrealProject $invalid.UProject
                $invalidLease.Dispose()
            } | Should Be $true
        }
    }

    It 'accepts only one exact bare or UE-wrapped child marker and rejects every gate failure' {
        Assert-TestP5aCommandCapability 'Assert-P5aChildGateOutput' either
        $ready = "P5A_TRACE_READY_OK cases=8 commit=$script:P5aLockedCommit"
        $wrapped = '[2026.09.01-10.11.12:345][  0]LogTemp: Display: ' + $ready
        foreach ($valid in @($ready, $wrapped))
        {
            $forwarded = @(Assert-P5aChildGateOutput `
                -PhaseName 'synthetic ready' `
                -StdOutLines @('ordinary output', $valid) `
                -StdErrLines @() `
                -ExitCode 0 `
                -TimedOut $false `
                -ExpectedMarker $ready `
                -AllowUeWrapper)
            @($forwarded | Where-Object { $_ -match '^\[?.*P5A_' }).Count | Should Be 0
        }

        $invalidCases = @(
            @{ StdOut = @('ordinary output'); StdErr = @(); Exit = 0; Timeout = $false }
            @{ StdOut = @($ready, $ready); StdErr = @(); Exit = 0; Timeout = $false }
            @{ StdOut = @("prefix $ready"); StdErr = @(); Exit = 0; Timeout = $false }
            @{ StdOut = @("$ready suffix"); StdErr = @(); Exit = 0; Timeout = $false }
            @{ StdOut = @("P5A_TRACE_GENERATION_OK cases=8 commit=$script:P5aLockedCommit"); StdErr = @(); Exit = 0; Timeout = $false }
            @{ StdOut = @($ready); StdErr = @(); Exit = 7; Timeout = $false }
            @{ StdOut = @($ready); StdErr = @(); Exit = 0; Timeout = $true }
            @{ StdOut = @($ready); StdErr = @('LogTemp: Warning: warning'); Exit = 0; Timeout = $false }
            @{ StdOut = @($ready); StdErr = @('LogTemp: Error: error'); Exit = 0; Timeout = $false }
            @{ StdOut = @('ordinary output'); StdErr = @($ready); Exit = 0; Timeout = $false }
            @{ StdOut = @($ready, 'P5A_GOLDEN_GENERATION_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17'); StdErr = @(); Exit = 0; Timeout = $false }
        )
        foreach ($invalid in $invalidCases)
        {
            Test-TestP5aRejects {
                Assert-P5aChildGateOutput `
                    -PhaseName 'synthetic invalid marker' `
                    -StdOutLines $invalid.StdOut `
                    -StdErrLines $invalid.StdErr `
                    -ExitCode $invalid.Exit `
                    -TimedOut $invalid.Timeout `
                    -ExpectedMarker $ready `
                    -AllowUeWrapper | Out-Null
            } | Should Be $true
        }
    }

    It 'runs exactly seven generator calls in frozen order with exact apphost UE argv and one ready plus two captures' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorProcessProtocol' generator
        $context = New-TestP5aProcessContext 'generator-seven'
        $shim = New-TestP5aProcessShim 'generator-seven-shim'
        $output = @(Invoke-TestP5aGeneratorProtocol $context $shim)
        $records = @(Get-TestP5aShimRecords $shim)

        $records.Count | Should Be 7
        $expectedWorkingDirectories = @($context.Root, $context.Root, $context.Root, $context.Root,
            $context.Root, $context.Root, $context.Root)
        @($records | ForEach-Object { $_.workingDirectory }) | Should Be $expectedWorkingDirectories
        @($records | ForEach-Object { $_.filePath }) | Should Be @(
            $context.Oracle.AppHostPath, $context.Oracle.AppHostPath,
            $context.Editor, $context.Editor, $context.Editor,
            $context.Oracle.AppHostPath, $context.Oracle.AppHostPath)
        @($records | ForEach-Object { $_.arguments[0] }) | Should Be @(
            '--write-native-plan', '--write-native-plan', $context.UProject,
            $context.UProject, $context.UProject,
            '--write-canonical-pair', '--write-canonical-pair')
        @($records | Where-Object { @($_.arguments | Where-Object { $_ -ceq '-ReadyCheck' }).Count -eq 1 }).Count |
            Should Be 1
        @($records | Where-Object {
            $_.filePath -ceq $context.Editor -and
            @($_.arguments | Where-Object { $_ -ceq '-ReadyCheck' }).Count -eq 0
        }).Count | Should Be 2

        $paths = Get-TestP5aExpectedProcessPaths $context
        $planA = $paths.PlanA
        $planB = $paths.PlanB
        @($records[0].arguments) | Should Be @(
            '--write-native-plan', '--repository-root', $context.Root, '--output', $planA)
        @($records[1].arguments) | Should Be @(
            '--write-native-plan', '--repository-root', $context.Root, '--output', $planB)
        $planA | Should Not Be $planB
        (Get-TestP5aFileHash $planA) | Should Be (Get-TestP5aFileHash $planB)

        $planHash = Get-TestP5aFileHash $planA
        $ueOutputs = @($paths.ReadyOutput, $paths.RawA, $paths.RawB)
        $ueLogs = @($paths.ReadyLog, $paths.CaptureALog, $paths.CaptureBLog)
        for ($index = 2; $index -le 4; $index++)
        {
            $expected = @(
                $context.UProject
                '-run=AlsLocomotionTrace'
                '-TraceKind=P5A'
            )
            if ($index -eq 2) { $expected += '-ReadyCheck' }
            $expected += @(
                "-Output=$($ueOutputs[$index - 2])"
                "-ReferenceRoot=$($context.Reference)"
                "-ReferenceCommit=$script:P5aLockedCommit"
                "-PatchHashes=$script:P5aLockedPatch"
                "-P5ATracePlan=$planA"
                "-P5ATracePlanSha256=$planHash"
                '-stdout'
                '-FullStdOutLogOutput'
                '-unattended'
                '-nosplash'
                '-nullrhi'
                '-nosound'
                "-abslog=$($ueLogs[$index - 2])"
            )
            @($records[$index].arguments) | Should Be $expected
        }
        Test-Path -LiteralPath $paths.ReadyOutput | Should Be $false

        @($records[5].arguments) | Should Be @(
            '--write-canonical-pair', '--repository-root', $context.Root,
            '--trace-plan', $planA, '--raw', $paths.RawA,
            '--native-canonical', $paths.NativeA, '--port-canonical', $paths.PortA)
        @($records[6].arguments) | Should Be @(
            '--write-canonical-pair', '--repository-root', $context.Root,
            '--trace-plan', $planA, '--raw', $paths.RawB,
            '--native-canonical', $paths.NativeB, '--port-canonical', $paths.PortB)
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($paths.RawA)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($paths.RawB)))
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($paths.NativeA)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($paths.NativeB)))
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($paths.PortA)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($paths.PortB)))
        @(
            Get-TestP5aFileHash $paths.PlanA
            Get-TestP5aFileHash $paths.RawA
            Get-TestP5aFileHash $paths.NativeA
            Get-TestP5aFileHash $paths.PortA
        ) | Sort-Object -Unique | Measure-Object | Select-Object -ExpandProperty Count |
            Should Be 4
        $ownedPaths = @(
            $paths.PlanA, $paths.PlanB, $paths.ReadyOutput, $paths.RawA, $paths.RawB,
            $paths.NativeA, $paths.NativeB, $paths.PortA, $paths.PortB,
            $paths.ReadyLog, $paths.CaptureALog, $paths.CaptureBLog)
        foreach ($ownedPath in $ownedPaths) { Assert-TestP5aPathUnderRoot $ownedPath $context.Staging }
        @($ownedPaths | Sort-Object -Unique).Count | Should Be $ownedPaths.Count
        @($output | Where-Object { $_ -match 'P5A_(TRACE|ORACLE)_' }).Count | Should Be 0
        @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
    }

    It 'rejects each plan raw native or port determinism drift at its dedicated prepublication gate' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorProcessProtocol' generator
        $initialFailures = @(
            @{ Name = 'plan-b-bytes'; Control = @{ PlanBDrift = $true }; Calls = 2; Diagnostic = '(?i)plan.*byte' }
            @{ Name = 'plan-a-marker'; Control = @{ PlanMarkerMismatchCall = 1 }; Calls = 1; Diagnostic = '(?i)plan.*(marker|digest)' }
            @{ Name = 'plan-b-marker'; Control = @{ PlanMarkerMismatchCall = 2 }; Calls = 2; Diagnostic = '(?i)plan.*(marker|digest)' }
            @{ Name = 'raw-b-bytes'; Control = @{ RawBDrift = $true }; Calls = 5; Diagnostic = '(?i)raw.*same-engine.*byte' }
            @{ Name = 'native-b-bytes'; Control = @{ NativeBDrift = $true }; Calls = 7; Diagnostic = '(?i)native.*same-engine.*byte' }
            @{ Name = 'port-b-bytes'; Control = @{ PortBDrift = $true }; Calls = 7; Diagnostic = '(?i)port.*same-engine.*byte' }
        )
        foreach ($case in $initialFailures)
        {
            $context = New-TestP5aProcessContext "generator-$($case.Name)"
            $shim = New-TestP5aProcessShim "generator-$($case.Name)-shim"
            Set-TestP5aShimControl $shim $case.Control
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                Invoke-TestP5aGeneratorProtocol $context $shim
            }
            $failure | Should Not BeNullOrEmpty
            $failure | Should Match $case.Diagnostic
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
            @(Get-TestP5aShimRecords $shim).Count | Should Be $case.Calls
            Test-Path -LiteralPath $context.Staging | Should Be $false
        }

        $context = New-TestP5aProcessContext 'generator-plan-immutable'
        $shim = New-TestP5aProcessShim 'generator-plan-immutable-shim'
        Set-TestP5aShimControl $shim @{ MutatePlanOnCall = 4 }
        $output = [Collections.Generic.List[object]]::new()
        $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
            Invoke-TestP5aGeneratorProtocol $context $shim
        }
        $failure | Should Not BeNullOrEmpty
        @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
        $records = @(Get-TestP5aShimRecords $shim)
        $planA = [string]$records[0].arguments[4]
        $planB = [string]$records[1].arguments[4]
        for ($index = 2; $index -lt $records.Count; $index++)
        {
            (@($records[$index].arguments | Where-Object {
                $_ -ceq $planB -or $_ -ceq "-P5ATracePlan=$planB"
            })).Count | Should Be 0
        }
        $planA | Should Not Be $planB
    }

    It 'stops generator children on every process gate failure and suppresses all child markers' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorProcessProtocol' generator
        foreach ($call in 1..7)
        {
            foreach ($kind in @('nonzero', 'timeout', 'warning', 'error', 'missing', 'duplicate', 'prefix', 'suffix', 'wrong'))
            {
                $context = New-TestP5aProcessContext "generator-gate-$call-$kind"
                $shim = New-TestP5aProcessShim "generator-gate-$call-$kind-shim"
                Set-TestP5aShimControl $shim @{ FailCall = $call; FailureKind = $kind }
                $forwarded = [Collections.Generic.List[object]]::new()
                $failure = Invoke-TestP5aFailureCapture -Output $forwarded -Action {
                    Invoke-TestP5aGeneratorProtocol $context $shim
                }
                $failure | Should Not BeNullOrEmpty
                $failure | Should Match (Get-TestP5aProcessFailureDiagnosticPattern $kind)
                @(Get-TestP5aShimRecords $shim).Count | Should Be $call
                @($forwarded | Where-Object {
                    [string]$_ -match 'P5A_(TRACE|ORACLE|GOLDEN)_'
                }).Count | Should Be 0
                Test-Path -LiteralPath $context.Staging | Should Be $false
            }
        }
    }

    It 'executes the generator script entrypoint through audited leases seven children manifest reopen and atomic commit' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorWorkflow' generator
        $context = New-TestP5aProcessContext 'generator-entrypoint-success'
        $shim = New-TestP5aProcessShim 'generator-entrypoint-success-shim'
        $oracleLeasePaths = @(Get-TestP5aOracleLeasePaths $context)
        $deploymentLeasePaths = @(Get-TestP5aDeploymentLeasePaths $context)
        $leasePaths = @($oracleLeasePaths) + @($deploymentLeasePaths)
        Set-TestP5aShimControl $shim @{
            ProbeLockedPaths = @($leasePaths)
        }
        $fileSystem = New-TestP5aAtomicFileSystem -ProbeLockedPaths $leasePaths
        $checkpoints = New-TestP5aCheckpointInvoker
        $output = @(& $script:P5aGeneratorPath `
            -RepositoryRoot $context.Root `
            -UnrealEditorCmd $context.Editor `
            -UnrealProject $context.UProject `
            -ReferenceRoot $context.Reference `
            -StagingRoot $context.Staging `
            -DestinationPath $context.Fixture `
            -ProcessInvoker $shim.Invoker `
            -FileSystemInvoker $fileSystem.Invoker `
            -CheckpointInvoker $checkpoints.Invoker `
            -TimeoutSeconds 10)
        $records = @(Get-TestP5aShimRecords $shim)

        $records.Count | Should Be 7
        Assert-TestP5aLeaseProbeRecords -Records $records -ExpectedPaths $leasePaths
        Assert-TestP5aLeaseContinuity -CheckpointObserver $checkpoints `
            -ExpectedCheckpoints @(
                'OracleEvidenceOpened', 'DeploymentEvidenceOpened', 'ChildrenCompleted',
                'ReadyManifestWritten', 'ReadyManifestReopened', 'EvidenceRechecked',
                'BeforePublication') `
            -OracleHandleCount $oracleLeasePaths.Count `
            -DeploymentHandleCount $deploymentLeasePaths.Count
        @($fileSystem.Records | Where-Object { $_.operation -ceq 'File.Replace' }).Count | Should Be 1
        @($fileSystem.Records | Where-Object { $_.operation -ceq 'File.Move' }).Count | Should Be 0
        $replaceRecord = @($fileSystem.Records | Where-Object { $_.operation -ceq 'File.Replace' })[0]
        $copyRecord = @($fileSystem.Records | Where-Object { $_.operation -ceq 'File.Copy' })[0]
        $processPaths = Get-TestP5aExpectedProcessPaths $context
        $copyRecord.sourcePath | Should Be $processPaths.NativeA
        $replaceRecord.sourcePath | Should Be $copyRecord.destinationPath
        $replaceRecord.destinationPath | Should Be $context.Fixture
        $publicationRecords = @($fileSystem.Records | Where-Object {
            $_.operation -in @('File.Copy', 'File.Replace', 'File.Move')
        })
        Assert-TestP5aLeaseProbeRecords -Records $publicationRecords -ExpectedPaths $leasePaths
        $prePublicationObservation = @($checkpoints.Observations)[-1]
        foreach ($publicationRecord in $publicationRecords)
        {
            ([int]$publicationRecord.oracleLeaseIdentity) |
                Should Be ([int]$prePublicationObservation.oracleLeaseIdentity)
            ([int]$publicationRecord.deploymentLeaseIdentity) |
                Should Be ([int]$prePublicationObservation.deploymentLeaseIdentity)
            @($publicationRecord.oracleHandleIdentities) |
                Should Be @($prePublicationObservation.oracleHandleIdentities)
            @($publicationRecord.deploymentHandleIdentities) |
                Should Be @($prePublicationObservation.deploymentHandleIdentities)
        }
        [IO.File]::ReadAllText($context.Fixture) | Should Be "{`"representation`":`"native`"}`n"
        $expectedParentMarker =
            "P5A_GOLDEN_GENERATION_OK cases=8 commit=$script:P5aLockedCommit"
        Assert-TestP5aExactParentMarker -Output @($output) `
            -ExpectedMarker $expectedParentMarker
        Assert-TestP5aParentMarkerMutationsRejected -Output @($output) `
            -ExpectedMarker $expectedParentMarker -Kind generation
        @($output | Where-Object { $_ -match 'P5A_(TRACE|ORACLE)_' }).Count | Should Be 0

        foreach ($driftCase in @(
            @{ Name = 'raw'; Control = @{ RawBDrift = $true }; Calls = 5; Diagnostic = '(?i)raw.*same-engine.*byte' }
            @{ Name = 'native'; Control = @{ NativeBDrift = $true }; Calls = 7; Diagnostic = '(?i)native.*same-engine.*byte' }
            @{ Name = 'port'; Control = @{ PortBDrift = $true }; Calls = 7; Diagnostic = '(?i)port.*same-engine.*byte' }
        ))
        {
            $driftContext = New-TestP5aProcessContext "generator-entrypoint-$($driftCase.Name)-drift"
            $driftShim = New-TestP5aProcessShim "generator-entrypoint-$($driftCase.Name)-drift-shim"
            Set-TestP5aShimControl $driftShim $driftCase.Control
            $driftFileSystem = New-TestP5aAtomicFileSystem
            $driftCheckpoints = New-TestP5aCheckpointInvoker
            $fixtureHashBefore = Get-TestP5aFileHash $driftContext.Fixture
            $driftOutput = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $driftOutput -Action {
                & $script:P5aGeneratorPath `
                    -RepositoryRoot $driftContext.Root `
                    -UnrealEditorCmd $driftContext.Editor `
                    -UnrealProject $driftContext.UProject `
                    -ReferenceRoot $driftContext.Reference `
                    -StagingRoot $driftContext.Staging `
                    -DestinationPath $driftContext.Fixture `
                    -ProcessInvoker $driftShim.Invoker `
                    -FileSystemInvoker $driftFileSystem.Invoker `
                    -CheckpointInvoker $driftCheckpoints.Invoker `
                    -TimeoutSeconds 10
            }
            $failure | Should Match $driftCase.Diagnostic
            @(Get-TestP5aShimRecords $driftShim).Count | Should Be $driftCase.Calls
            @($driftFileSystem.Records | Where-Object {
                $_.operation -in @('File.Copy', 'File.Replace', 'File.Move')
            }).Count | Should Be 0
            (Get-TestP5aFileHash $driftContext.Fixture) | Should Be $fixtureHashBefore
            Test-Path -LiteralPath $driftContext.Staging | Should Be $false
            @($driftOutput | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
        }
    }

    It 'keeps the prior fixture and emits no parent marker at every generator precommit checkpoint' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorWorkflow' generator
        foreach ($checkpoint in @(
            'OracleEvidenceOpened', 'DeploymentEvidenceOpened', 'ChildrenCompleted',
            'ReadyManifestWritten', 'ReadyManifestReopened', 'EvidenceRechecked',
            'BeforePublication'))
        {
            $context = New-TestP5aProcessContext "generator-precommit-$checkpoint"
            $shim = New-TestP5aProcessShim "generator-precommit-$checkpoint-shim"
            $fileSystem = New-TestP5aAtomicFileSystem
            $checkpoints = New-TestP5aCheckpointInvoker -FailAt $checkpoint
            $before = Get-TestP5aFileHash $context.Fixture
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                & $script:P5aGeneratorPath `
                    -RepositoryRoot $context.Root `
                    -UnrealEditorCmd $context.Editor `
                    -UnrealProject $context.UProject `
                    -ReferenceRoot $context.Reference `
                    -StagingRoot $context.Staging `
                    -DestinationPath $context.Fixture `
                    -ProcessInvoker $shim.Invoker `
                    -FileSystemInvoker $fileSystem.Invoker `
                    -CheckpointInvoker $checkpoints.Invoker `
                    -TimeoutSeconds 10
            }
            $failure | Should Not BeNullOrEmpty
            (Get-TestP5aFileHash $context.Fixture) | Should Be $before
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
            @($fileSystem.Records | Where-Object {
                $_.operation -in @('File.Replace', 'File.Move')
            }).Count | Should Be 0
            Test-Path -LiteralPath $context.Staging | Should Be $false
        }
    }

    It 'propagates every child failure through the generator entrypoint without touching the prior fixture' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorWorkflow' generator
        foreach ($call in 1..7)
        {
            $context = New-TestP5aProcessContext "generator-entrypoint-child-$call"
            $shim = New-TestP5aProcessShim "generator-entrypoint-child-$call-shim"
            Set-TestP5aShimControl $shim @{ FailCall = $call; FailureKind = 'nonzero' }
            $fileSystem = New-TestP5aAtomicFileSystem
            $checkpoints = New-TestP5aCheckpointInvoker
            $before = Get-TestP5aFileHash $context.Fixture
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                & $script:P5aGeneratorPath `
                    -RepositoryRoot $context.Root `
                    -UnrealEditorCmd $context.Editor `
                    -UnrealProject $context.UProject `
                    -ReferenceRoot $context.Reference `
                    -StagingRoot $context.Staging `
                    -DestinationPath $context.Fixture `
                    -ProcessInvoker $shim.Invoker `
                    -FileSystemInvoker $fileSystem.Invoker `
                    -CheckpointInvoker $checkpoints.Invoker `
                    -TimeoutSeconds 10
            }
            $failure | Should Not BeNullOrEmpty
            @(Get-TestP5aShimRecords $shim).Count | Should Be $call
            (Get-TestP5aFileHash $context.Fixture) | Should Be $before
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
            @($fileSystem.Records | Where-Object {
                $_.operation -in @('File.Replace', 'File.Move')
            }).Count | Should Be 0
            Test-Path -LiteralPath $context.Staging | Should Be $false
        }
    }

    It 'suppresses generator success and cleans staging and same-directory temp on Copy Replace or Move failure' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorWorkflow' generator
        foreach ($case in @(
            @{ Operation = 'File.Copy'; DestinationExists = $true }
            @{ Operation = 'File.Replace'; DestinationExists = $true }
            @{ Operation = 'File.Move'; DestinationExists = $false }
        ))
        {
            $name = $case.Operation -replace '\.', '-'
            $context = New-TestP5aProcessContext "generator-entrypoint-atomic-$name"
            if (-not $case.DestinationExists)
            {
                Remove-Item -LiteralPath $context.Fixture -Force
            }
            $shim = New-TestP5aProcessShim "generator-entrypoint-atomic-$name-shim"
            $leasePaths = @(Get-TestP5aGeneratorLeasePaths $context)
            Set-TestP5aShimControl $shim @{ ProbeLockedPaths = @($leasePaths) }
            $fileSystem = New-TestP5aAtomicFileSystem `
                -FailOnOperation $case.Operation `
                -ProbeLockedPaths $leasePaths
            $checkpoints = New-TestP5aCheckpointInvoker
            $before = if ($case.DestinationExists) {
                Get-TestP5aFileHash $context.Fixture
            } else {
                $null
            }
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                & $script:P5aGeneratorPath `
                    -RepositoryRoot $context.Root `
                    -UnrealEditorCmd $context.Editor `
                    -UnrealProject $context.UProject `
                    -ReferenceRoot $context.Reference `
                    -StagingRoot $context.Staging `
                    -DestinationPath $context.Fixture `
                    -ProcessInvoker $shim.Invoker `
                    -FileSystemInvoker $fileSystem.Invoker `
                    -CheckpointInvoker $checkpoints.Invoker `
                    -TimeoutSeconds 10
            }
            $failure | Should Not BeNullOrEmpty
            $childRecords = @(Get-TestP5aShimRecords $shim)
            $childRecords.Count | Should Be 7
            Assert-TestP5aLeaseProbeRecords -Records $childRecords `
                -ExpectedPaths $leasePaths
            $publicationRecords = @($fileSystem.Records | Where-Object {
                $_.operation -in @('File.Copy', 'File.Replace', 'File.Move')
            })
            Assert-TestP5aLeaseProbeRecords -Records $publicationRecords `
                -ExpectedPaths $leasePaths
            if ($case.DestinationExists)
            {
                (Get-TestP5aFileHash $context.Fixture) | Should Be $before
            }
            else
            {
                Test-Path -LiteralPath $context.Fixture | Should Be $false
            }
            @($fileSystem.Records | Where-Object {
                $_.operation -ceq $case.Operation
            }).Count | Should Be 1
            $copyRecord = @($fileSystem.Records | Where-Object {
                $_.operation -ceq 'File.Copy'
            })[0]
            if ($null -ne $copyRecord)
            {
                (Split-Path -Parent $copyRecord.destinationPath) |
                    Should Be (Split-Path -Parent $context.Fixture)
                Test-Path -LiteralPath $copyRecord.destinationPath | Should Be $false
            }
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
            Test-Path -LiteralPath $context.Staging | Should Be $false
            $officialFiles = @(Get-ChildItem -LiteralPath (Split-Path -Parent $context.Fixture) `
                -File -ErrorAction SilentlyContinue)
            $officialFiles.Count | Should Be $(if ($case.DestinationExists) { 1 } else { 0 })
        }
    }

    It 'cleans owned generator staging before atomic commit and preserves the prior fixture when cleanup is blocked' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorWorkflow' generator
        $context = New-TestP5aProcessContext 'generator-strict-precommit-cleanup'
        $shim = New-TestP5aProcessShim 'generator-strict-precommit-cleanup-shim'
        $fileSystem = New-TestP5aAtomicFileSystem
        $paths = Get-TestP5aExpectedProcessPaths $context
        $lockState = [pscustomobject]@{ Stream = $null }
        $capturedLockState = $lockState
        $capturedPlanPath = $paths.PlanA
        $lockMutation = {
            param($Checkpoint)
            $capturedLockState.Stream = [IO.File]::Open(
                $capturedPlanPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
                [IO.FileShare]::Read)
        }.GetNewClosure()
        $checkpoints = New-TestP5aCheckpointInvoker `
            -MutateAt 'BeforePublication' -MutationAction $lockMutation
        $before = Get-TestP5aFileHash $context.Fixture
        $output = [Collections.Generic.List[object]]::new()
        try
        {
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                & $script:P5aGeneratorPath `
                    -RepositoryRoot $context.Root `
                    -UnrealEditorCmd $context.Editor `
                    -UnrealProject $context.UProject `
                    -ReferenceRoot $context.Reference `
                    -StagingRoot $context.Staging `
                    -DestinationPath $context.Fixture `
                    -ProcessInvoker $shim.Invoker `
                    -FileSystemInvoker $fileSystem.Invoker `
                    -CheckpointInvoker $checkpoints.Invoker `
                    -TimeoutSeconds 10
            }
            $failure | Should Match '(?i)(used by another process|sharing|access|cleanup)'
            @(Get-TestP5aShimRecords $shim).Count | Should Be 7
            @($fileSystem.Records | Where-Object {
                $_.operation -ceq 'File.Copy'
            }).Count | Should Be 1
            @($fileSystem.Records | Where-Object {
                $_.operation -in @('File.Replace', 'File.Move')
            }).Count | Should Be 0
            (Get-TestP5aFileHash $context.Fixture) | Should Be $before
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
            Test-Path -LiteralPath $context.Staging -PathType Container |
                Should Be $true
        }
        finally
        {
            if ($null -ne $lockState.Stream) { $lockState.Stream.Dispose() }
            if (Test-Path -LiteralPath $context.Staging)
            {
                Remove-Item -LiteralPath $context.Staging -Recurse -Force
            }
        }
    }

    It 'reopens the generated ready manifest and rejects a staged payload mutation before publication' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorWorkflow' generator
        $context = New-TestP5aProcessContext 'generator-manifest-reopen-entrypoint'
        $shim = New-TestP5aProcessShim 'generator-manifest-reopen-entrypoint-shim'
        $fileSystem = New-TestP5aAtomicFileSystem
        $paths = Get-TestP5aExpectedProcessPaths $context
        $checkpoints = New-TestP5aCheckpointInvoker `
            -MutateAt 'ReadyManifestWritten' `
            -MutatePath $paths.RawA
        $before = Get-TestP5aFileHash $context.Fixture
        $output = [Collections.Generic.List[object]]::new()
        $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
            & $script:P5aGeneratorPath `
                -RepositoryRoot $context.Root `
                -UnrealEditorCmd $context.Editor `
                -UnrealProject $context.UProject `
                -ReferenceRoot $context.Reference `
                -StagingRoot $context.Staging `
                -DestinationPath $context.Fixture `
                -ProcessInvoker $shim.Invoker `
                -FileSystemInvoker $fileSystem.Invoker `
                -CheckpointInvoker $checkpoints.Invoker `
                -TimeoutSeconds 10
        }
        $failure | Should Not BeNullOrEmpty
        (Get-TestP5aFileHash $context.Fixture) | Should Be $before
        @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
        @($fileSystem.Records | Where-Object {
            $_.operation -in @('File.Replace', 'File.Move')
        }).Count | Should Be 0
        Test-Path -LiteralPath $context.Staging | Should Be $false
    }

    It 'writes and reopens a closed staging manifest over every payload and exact Oracle and build evidence' {
        Assert-TestP5aCommandCapability 'Write-P5aReadyManifest' generator
        Assert-TestP5aCommandCapability 'Assert-P5aReadyManifest' generator
        $staging = Join-Path $TestDrive 'ready-manifest-valid'
        foreach ($relative in @(
            'plan-a.json', 'plan-b.json', 'raw-a.json', 'raw-b.json',
            'native-a.json', 'native-b.json', 'port-a.json', 'port-b.json', 'fixture.json'
        ))
        {
            Write-TestP5aText (Join-Path $staging $relative) "payload:$relative`n"
        }
        $oracleEvidence = Get-TestP5aEvidence
        $buildEvidence = Get-TestP5aBuildEvidence
        $manifestPath = Write-P5aReadyManifest `
            -StagingRoot $staging `
            -OracleEvidence $oracleEvidence `
            -BuildEvidence $buildEvidence
        Assert-P5aReadyManifest -StagingRoot $staging -ManifestPath $manifestPath

        $raw = [IO.File]::ReadAllText($manifestPath)
        $raw.Contains([IO.Path]::GetFullPath($staging), [StringComparison]::OrdinalIgnoreCase) |
            Should Be $false
        foreach ($payload in @(Get-ChildItem -LiteralPath $staging -File | Where-Object {
            $_.FullName -cne $manifestPath
        }))
        {
            $raw | Should Match ([regex]::Escape($payload.Name))
            $raw | Should Match ([regex]::Escape((Get-TestP5aFileHash $payload.FullName)))
        }
        $document = $raw | ConvertFrom-Json
        @($document.oracleEvidence.PSObject.Properties.Name) | Should Be @(
            'sdkVersion', 'sourceTreeSha256', 'buildManifestSha256',
            'executableSha256', 'oracleAssemblySha256', 'importAssemblySha256',
            'coreAssemblySha256', 'depsSha256', 'runtimeConfigSha256')
        @($document.buildEvidence.PSObject.Properties.Name) | Should Be @(
            'ownedPluginTreeSha256', 'targetReceiptSha256',
            'alsModuleManifestSha256', 'traceModuleManifestSha256',
            'alsModuleDllSha256', 'traceModuleDllSha256')

        [IO.File]::AppendAllText((Join-Path $staging 'raw-a.json'), 'drift')
        Test-TestP5aRejects {
            Assert-P5aReadyManifest -StagingRoot $staging -ManifestPath $manifestPath
        } | Should Be $true

        $extra = Join-Path $TestDrive 'ready-manifest-extra'
        Write-TestP5aText (Join-Path $extra 'fixture.json') "fixture`n"
        $extraManifest = Write-P5aReadyManifest -StagingRoot $extra `
            -OracleEvidence (Get-TestP5aEvidence) -BuildEvidence (Get-TestP5aBuildEvidence)
        Write-TestP5aText (Join-Path $extra 'late-extra.json') "extra`n"
        Test-TestP5aRejects {
            Assert-P5aReadyManifest -StagingRoot $extra -ManifestPath $extraManifest
        } | Should Be $true

        $closed = Join-Path $TestDrive 'ready-manifest-closed'
        Write-TestP5aText (Join-Path $closed 'fixture.json') "fixture`n"
        $closedManifest = Write-P5aReadyManifest -StagingRoot $closed `
            -OracleEvidence (Get-TestP5aEvidence) -BuildEvidence (Get-TestP5aBuildEvidence)
        $closedDocument = [IO.File]::ReadAllText($closedManifest) | ConvertFrom-Json
        Add-Member -InputObject $closedDocument.oracleEvidence -NotePropertyName absolutePath -NotePropertyValue 'forbidden'
        Write-TestP5aText $closedManifest (($closedDocument | ConvertTo-Json -Depth 20 -Compress) + "`n")
        Test-TestP5aRejects {
            Assert-P5aReadyManifest -StagingRoot $closed -ManifestPath $closedManifest
        } | Should Be $true

        $closedBuild = Join-Path $TestDrive 'ready-manifest-closed-build'
        Write-TestP5aText (Join-Path $closedBuild 'fixture.json') "fixture`n"
        $closedBuildManifest = Write-P5aReadyManifest -StagingRoot $closedBuild `
            -OracleEvidence (Get-TestP5aEvidence) -BuildEvidence (Get-TestP5aBuildEvidence)
        $closedBuildDocument = [IO.File]::ReadAllText($closedBuildManifest) | ConvertFrom-Json
        Add-Member -InputObject $closedBuildDocument.buildEvidence -NotePropertyName buildPath -NotePropertyValue 'forbidden'
        Write-TestP5aText $closedBuildManifest (($closedBuildDocument | ConvertTo-Json -Depth 20 -Compress) + "`n")
        Test-TestP5aRejects {
            Assert-P5aReadyManifest -StagingRoot $closedBuild -ManifestPath $closedBuildManifest
        } | Should Be $true
    }

    It 'atomically replaces one fixture or moves the first fixture and preserves prior bytes on precommit failures' {
        Assert-TestP5aCommandCapability 'Publish-P5aFixtureAtomically' generator
        $atomicClosure = Assert-TestP5aAtomicPublicationClosure `
            -Path $script:P5aGeneratorPath
        $generatorAst = $atomicClosure.Ast
        $publishText = $atomicClosure.Publish.Extent.Text
        $publishText | Should Not Match '(?i)Remove-Item|Delete\s*\(\s*\$DestinationPath'
        foreach ($commitCommand in $atomicClosure.CommitCalls)
        {
            $commitStatement = $commitCommand
            while ($null -ne $commitStatement.Parent -and
                   $commitStatement.Parent -isnot [Management.Automation.Language.StatementBlockAst])
            {
                $commitStatement = $commitStatement.Parent
            }
            ($commitStatement.Parent -is [Management.Automation.Language.StatementBlockAst]) |
                Should Be $true
            $commitBlock = $commitStatement.Parent
            $statementIndex = [Array]::IndexOf(
                [Management.Automation.Language.StatementAst[]]@($commitBlock.Statements),
                $commitStatement)
            ($statementIndex -ge 0) | Should Be $true
            $afterCommit = @($commitBlock.Statements | Select-Object -Skip ($statementIndex + 1))
            $afterCommit.Count | Should Be 1
            ($afterCommit[0] -is [Management.Automation.Language.ReturnStatementAst]) |
                Should Be $true
            @($afterCommit[0].FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -or
                    $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -or
                    $node -is [Management.Automation.Language.ThrowStatementAst] -or
                    $node -is [Management.Automation.Language.ExitStatementAst]
            }, $true)).Count | Should Be 0
        }
        $publishFinallyStatements = @($atomicClosure.Publish.FindAll({
            param($node)
            $node -is [Management.Automation.Language.TryStatementAst] -and
                $null -ne $node.Finally
        }, $true) | Where-Object {
            $tryStatement = $_
            @($atomicClosure.CommitCalls | Where-Object {
                $_.Extent.StartOffset -ge $tryStatement.Body.Extent.StartOffset -and
                    $_.Extent.EndOffset -le $tryStatement.Body.Extent.EndOffset
            }).Count -gt 0
        })
        foreach ($tryStatement in $publishFinallyStatements)
        {
            Assert-TestP5aBestEffortFinally -Finally $tryStatement.Finally
        }

        $generatorSource = [IO.File]::ReadAllText($script:P5aGeneratorPath)
        $deadDefault = '{ param($Operation,$SourcePath,$DestinationPath); ' +
            '$null = ${function:' + $atomicClosure.Runner.Name + '}; ' +
            '[IO.File]::Copy($SourcePath,$DestinationPath,$true) }'
        $defaultParameter = $atomicClosure.DefaultParameter.DefaultValue
        $deadSource = $generatorSource.Substring(0, $defaultParameter.Extent.StartOffset) +
            $deadDefault + $generatorSource.Substring($defaultParameter.Extent.EndOffset)
        $deadPath = Join-Path $TestDrive 'atomic-dead-default-runner-reference.ps1'
        Write-TestP5aText $deadPath $deadSource
        $deadTokens = $null
        $deadErrors = $null
        [void][Management.Automation.Language.Parser]::ParseFile(
            $deadPath, [ref]$deadTokens, [ref]$deadErrors)
        @($deadErrors).Count | Should Be 0
        Test-TestP5aRejects {
            Assert-TestP5aAtomicPublicationClosure -Path $deadPath
        } | Should Be $true

        foreach ($runnerCommit in @($atomicClosure.RunnerCommitCalls))
        {
            $fallibleTail = '; & { throw ''post-commit failure'' } -ErrorAction SilentlyContinue'
            $tailSource = $generatorSource.Substring(0, $runnerCommit.Extent.EndOffset) +
                $fallibleTail + $generatorSource.Substring($runnerCommit.Extent.EndOffset)
            $tailPath = Join-Path $TestDrive "atomic-runner-post-$($runnerCommit.Member.Value).ps1"
            Write-TestP5aText $tailPath $tailSource
            $tailTokens = $null
            $tailErrors = $null
            [void][Management.Automation.Language.Parser]::ParseFile(
                $tailPath, [ref]$tailTokens, [ref]$tailErrors)
            @($tailErrors).Count | Should Be 0
            Test-TestP5aRejects {
                Assert-TestP5aAtomicPublicationClosure -Path $tailPath
            } | Should Be $true
        }

        $workflowDefinitions = @($generatorAst.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Invoke-P5aGeneratorWorkflow'
        }, $true))
        $workflowDefinitions.Count | Should Be 1
        $publicationCalls = @($workflowDefinitions[0].FindAll({
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -ceq 'Publish-P5aFixtureAtomically'
        }, $true))
        $publicationCalls.Count | Should Be 1
        $successMarkers = @($workflowDefinitions[0].FindAll({
            param($node)
            ($node -is [Management.Automation.Language.StringConstantExpressionAst] -or
             $node -is [Management.Automation.Language.ExpandableStringExpressionAst]) -and
                $node.Extent.Text -match 'P5A_GOLDEN_GENERATION_OK'
        }, $true))
        $successMarkers.Count | Should Be 1
        $publicationCalls[0].Extent.EndOffset | Should BeLessThan $successMarkers[0].Extent.StartOffset
        $workflowFinallyStatements = @($workflowDefinitions[0].FindAll({
            param($node)
            $node -is [Management.Automation.Language.TryStatementAst] -and
                $null -ne $node.Finally
        }, $true) | Where-Object {
            $publicationCalls[0].Extent.StartOffset -ge $_.Body.Extent.StartOffset -and
                $publicationCalls[0].Extent.EndOffset -le $_.Body.Extent.EndOffset
        })
        foreach ($tryStatement in $workflowFinallyStatements)
        {
            Assert-TestP5aBestEffortFinally -Finally $tryStatement.Finally
        }
        $falliblePostCommitNodes = @($workflowDefinitions[0].FindAll({
            param($node)
            ($node -is [Management.Automation.Language.CommandAst] -or
             $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -or
             $node -is [Management.Automation.Language.ThrowStatementAst] -or
             $node -is [Management.Automation.Language.ExitStatementAst]) -and
                $node.Extent.StartOffset -ge $publicationCalls[0].Extent.EndOffset
        }, $true))
        $unguardedPostCommitNodes = @($falliblePostCommitNodes | Where-Object {
            $node = $_
            -not @($workflowFinallyStatements | Where-Object {
                $node.Extent.StartOffset -ge $_.Finally.Extent.StartOffset -and
                    $node.Extent.EndOffset -le $_.Finally.Extent.EndOffset
            }).Count
        })
        $unguardedPostCommitNodes.Count | Should Be 0

        foreach ($destinationExists in @($false, $true))
        {
            $root = Join-Path $TestDrive "atomic-success-$destinationExists"
            $source = Join-Path $root 'staging\fixture.json'
            $destination = Join-Path $root 'official\trace_p5a.json'
            Write-TestP5aText $source "new fixture`n"
            if ($destinationExists) { Write-TestP5aText $destination "old fixture`n" }
            $fileSystem = New-TestP5aAtomicFileSystem
            $output = @(Publish-P5aFixtureAtomically `
                -ValidatedFixturePath $source `
                -DestinationPath $destination `
                -FileSystemInvoker $fileSystem.Invoker)
            [IO.File]::ReadAllText($destination) | Should Be "new fixture`n"
            $output.Count | Should Be 0
            @(Get-ChildItem -LiteralPath (Split-Path -Parent $destination) -File).Count | Should Be 1
            $commitOperation = if ($destinationExists) { 'File.Replace' } else { 'File.Move' }
            $copyRecords = @($fileSystem.Records | Where-Object {
                $_.operation -ceq 'File.Copy'
            })
            $copyRecords.Count | Should Be 1
            $copyRecords[0].sourcePath | Should Be $source
            (Split-Path -Parent $copyRecords[0].destinationPath) |
                Should Be (Split-Path -Parent $destination)
            @($fileSystem.Records | Where-Object { $_.operation -ceq $commitOperation }).Count | Should Be 1
            @($fileSystem.Records | Where-Object {
                $_.operation -in @('File.Replace', 'File.Move') -and $_.operation -cne $commitOperation
            }).Count | Should Be 0
            $commit = @($fileSystem.Records | Where-Object { $_.operation -ceq $commitOperation })[0]
            (Split-Path -Parent $commit.sourcePath) | Should Be (Split-Path -Parent $destination)
            [IO.Path]::GetPathRoot($commit.sourcePath) | Should Be ([IO.Path]::GetPathRoot($destination))
            $commit.destinationPath | Should Be $destination
            @($fileSystem.Records | Where-Object {
                $_.operation -ceq 'File.Delete' -and
                $_.sourcePath -ceq $destination
            }).Count | Should Be 0
        }

        foreach ($destinationExists in @($false, $true))
        {
            $root = Join-Path $TestDrive "atomic-default-success-$destinationExists"
            $source = Join-Path $root 'staging\fixture.json'
            $destination = Join-Path $root 'official\trace_p5a.json'
            Write-TestP5aText $source "default new fixture`n"
            if ($destinationExists) { Write-TestP5aText $destination "default old fixture`n" }
            $observation = Invoke-TestP5aObservedDefaultAtomicPublication `
                -SourcePath $source -DestinationPath $destination
            @($observation.Output).Count | Should Be 0
            [IO.File]::ReadAllText($destination) | Should Be "default new fixture`n"
            Test-Path -LiteralPath (Split-Path -Parent $source) | Should Be $false
            @(Get-ChildItem -LiteralPath (Split-Path -Parent $destination) -File).Count |
                Should Be 1
            $resolvedDestination = [IO.Path]::GetFullPath($destination)
            $incoming = @($observation.Renamed | Where-Object {
                $_.FullPath -ceq $resolvedDestination -and
                    $_.OldFullPath -cne $resolvedDestination
            })
            $incoming.Count | Should Be 1
            (Split-Path -Parent $incoming[0].OldFullPath) |
                Should Be (Split-Path -Parent $resolvedDestination)
            [IO.Path]::GetPathRoot($incoming[0].OldFullPath) |
                Should Be ([IO.Path]::GetPathRoot($resolvedDestination))
            $outgoing = @($observation.Renamed | Where-Object {
                $_.OldFullPath -ceq $resolvedDestination -and
                    $_.FullPath -cne $resolvedDestination
            })
            if ($destinationExists)
            {
                $outgoing.Count | Should Be 0
                @($observation.Renamed).Count | Should Be 1
                @($observation.DeletedPaths | Where-Object {
                    $_ -ceq $resolvedDestination
                }).Count | Should Be 1
                $observation.OldHandleText | Should Be "default old fixture`n"
            }
            else
            {
                $outgoing.Count | Should Be 0
                @($observation.Renamed).Count | Should Be 1
                @($observation.DeletedPaths | Where-Object {
                    $_ -ceq $resolvedDestination
                }).Count | Should Be 0
                $observation.OldHandleText | Should BeNullOrEmpty
            }
        }

        foreach ($destinationExists in @($false, $true))
        {
            $root = Join-Path $TestDrive "atomic-default-best-effort-cleanup-$destinationExists"
            $source = Join-Path $root 'staging\fixture.json'
            $destination = Join-Path $root 'official\trace_p5a.json'
            Write-TestP5aText $source "cleanup-resistant new fixture`n"
            if ($destinationExists) { Write-TestP5aText $destination "cleanup-resistant old fixture`n" }
            $sourceLock = [IO.File]::Open(
                $source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            try
            {
                $output = @(Publish-P5aFixtureAtomically `
                    -ValidatedFixturePath $source `
                    -DestinationPath $destination)
                $output.Count | Should Be 0
                [IO.File]::ReadAllText($destination) |
                    Should Be "cleanup-resistant new fixture`n"
                Test-Path -LiteralPath (Split-Path -Parent $source) -PathType Container |
                    Should Be $true
                @(Get-ChildItem -LiteralPath (Split-Path -Parent $destination) -File).Count |
                    Should Be 1
            }
            finally
            {
                $sourceLock.Dispose()
            }
        }

        foreach ($failure in @(
            @{ Exists = $true; Operation = 'File.Copy' }
            @{ Exists = $true; Operation = 'File.Replace' }
            @{ Exists = $false; Operation = 'File.Copy' }
            @{ Exists = $false; Operation = 'File.Move' }
        ))
        {
            $name = "atomic-injected-$($failure.Exists)-$($failure.Operation -replace '\.', '-')"
            $root = Join-Path $TestDrive $name
            $source = Join-Path $root 'staging\fixture.json'
            $destination = Join-Path $root 'official\trace_p5a.json'
            Write-TestP5aText $source "new fixture`n"
            if ($failure.Exists) { Write-TestP5aText $destination "old fixture`n" }
            $before = if ($failure.Exists) { Get-TestP5aFileHash $destination } else { $null }
            $fileSystem = New-TestP5aAtomicFileSystem -FailOnOperation $failure.Operation
            Test-TestP5aRejects {
                Publish-P5aFixtureAtomically `
                    -ValidatedFixturePath $source `
                    -DestinationPath $destination `
                    -FileSystemInvoker $fileSystem.Invoker
            } | Should Be $true
            if ($failure.Exists)
            {
                (Get-TestP5aFileHash $destination) | Should Be $before
            }
            else
            {
                Test-Path -LiteralPath $destination | Should Be $false
            }
            @($fileSystem.Records | Where-Object { $_.operation -ceq $failure.Operation }).Count | Should Be 1
            @($fileSystem.Records | Where-Object {
                $_.operation -in @('File.Replace', 'File.Move') -and
                $_.operation -cne $failure.Operation
            }).Count | Should Be 0
            $copyRecord = @($fileSystem.Records | Where-Object {
                $_.operation -ceq 'File.Copy'
            })[0]
            if ($null -ne $copyRecord)
            {
                (Split-Path -Parent $copyRecord.destinationPath) |
                    Should Be (Split-Path -Parent $destination)
                Test-Path -LiteralPath $copyRecord.destinationPath | Should Be $false
            }
            Test-Path -LiteralPath (Split-Path -Parent $source) | Should Be $false
            $officialFiles = @(Get-ChildItem -LiteralPath (Split-Path -Parent $destination) `
                -File -ErrorAction SilentlyContinue)
            $officialFiles.Count | Should Be $(if ($failure.Exists) { 1 } else { 0 })
        }

        $lockedRoot = Join-Path $TestDrive 'atomic-locked-destination'
        $lockedSource = Join-Path $lockedRoot 'staging\fixture.json'
        $lockedDestination = Join-Path $lockedRoot 'official\trace_p5a.json'
        Write-TestP5aText $lockedSource "new fixture`n"
        Write-TestP5aText $lockedDestination "old fixture`n"
        $before = Get-TestP5aFileHash $lockedDestination
        $lock = [IO.File]::Open(
            $lockedDestination, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try
        {
            Test-TestP5aRejects {
                Publish-P5aFixtureAtomically `
                    -ValidatedFixturePath $lockedSource `
                    -DestinationPath $lockedDestination
            } | Should Be $true
        }
        finally
        {
            $lock.Dispose()
        }
        (Get-TestP5aFileHash $lockedDestination) | Should Be $before
        Test-Path -LiteralPath (Split-Path -Parent $lockedSource) | Should Be $false
        @(Get-ChildItem -LiteralPath (Split-Path -Parent $lockedDestination) -File).Count | Should Be 1

        $missingRoot = Join-Path $TestDrive 'atomic-missing-source'
        $missingSource = Join-Path $missingRoot 'staging\missing.json'
        $missingDestination = Join-Path $missingRoot 'official\trace_p5a.json'
        Write-TestP5aText $missingDestination "old fixture`n"
        $before = Get-TestP5aFileHash $missingDestination
        Test-TestP5aRejects {
            Publish-P5aFixtureAtomically `
                -ValidatedFixturePath $missingSource `
                -DestinationPath $missingDestination
        } | Should Be $true
        (Get-TestP5aFileHash $missingDestination) | Should Be $before
        Test-Path -LiteralPath (Split-Path -Parent $missingSource) | Should Be $false
        @(Get-ChildItem -LiteralPath (Split-Path -Parent $missingDestination) -File).Count | Should Be 1
    }

    It 'rejects copied incoming fixture corruption before atomic commit' {
        Assert-TestP5aCommandCapability 'Publish-P5aFixtureAtomically' generator
        Assert-TestP5aCommandCapability 'Open-P5aGeneratorOwnedStaging' generator
        Assert-TestP5aCommandCapability 'Open-P5aGeneratorPinnedFile' generator
        $root = Join-Path $TestDrive 'atomic-corrupt-incoming'
        $staging = Join-Path $root 'staging'
        $source = Join-Path $staging 'native-a.json'
        $destination = Join-Path $root 'official\trace_p5a.json'
        $stagingLease = $null
        $sourceLease = $null
        try
        {
            [void][IO.Directory]::CreateDirectory($root)
            $stagingLease = Open-P5aGeneratorOwnedStaging $staging
            Write-TestP5aText $source "trusted fixture`n"
            Write-TestP5aText $destination "prior fixture`n"
            $before = Get-TestP5aFileHash $destination
            $sourceLease = Open-P5aGeneratorPinnedFile $source
            $expectedFixtureSha256 = [string]$sourceLease.HashSha256()
            $records = [Collections.Generic.List[object]]::new()
            $capturedRecords = $records
            $corruptingFileSystem = {
                param(
                    $Operation,
                    $SourcePath,
                    $DestinationPath,
                    $OracleLease,
                    $DeploymentLease,
                    $ValidatedFixtureLease,
                    $PublicationLease
                )

                $capturedRecords.Add([pscustomobject]@{
                    operation = [string]$Operation
                    sourcePath = [string]$SourcePath
                    destinationPath = [string]$DestinationPath
                })
                if ([string]$Operation -cne 'File.Copy')
                {
                    throw "Unexpected copied-temp commit operation: $Operation"
                }
                if ($null -eq $ValidatedFixtureLease)
                {
                    throw 'Copied-temp corruption test did not receive the pinned source lease.'
                }
                $ValidatedFixtureLease.CopyTo([string]$DestinationPath)
                [IO.File]::WriteAllText(
                    [string]$DestinationPath,
                    "corrupted incoming fixture`n",
                    [Text.UTF8Encoding]::new($false))
            }.GetNewClosure()
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                Publish-P5aFixtureAtomically `
                    -ValidatedFixturePath $source `
                    -DestinationPath $destination `
                    -FileSystemInvoker $corruptingFileSystem `
                    -StagingLease $stagingLease `
                    -ValidatedFixtureLease $sourceLease `
                    -StagingPayloadLeases @($sourceLease) `
                    -ExpectedFixtureSha256 $expectedFixtureSha256
            }

            $failure | Should Match '(?i)copied fixture bytes.*validated manifest payload'
            @($records | ForEach-Object { $_.operation }) | Should Be @('File.Copy')
            (Get-TestP5aFileHash $destination) | Should Be $before
            Test-Path -LiteralPath $staging | Should Be $false
            @(Get-ChildItem -LiteralPath (Split-Path -Parent $destination) -File).Count |
                Should Be 1
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
        }
        finally
        {
            try { if ($null -ne $sourceLease) { $sourceLease.Dispose() } } catch { }
            try { if ($null -ne $stagingLease) { $stagingLease.Dispose() } } catch { }
            if (Test-Path -LiteralPath $staging)
            {
                Remove-Item -LiteralPath $staging -Recurse -Force
            }
        }
    }

    It 'rejects a file symlink when opening pinned generator and verifier sources' {
        Assert-TestP5aCommandCapability 'Open-P5aGeneratorPinnedFile' generator
        Assert-TestP5aCommandCapability 'Open-P5aVerifierFixedFileLease' verifier
        $root = Join-Path $TestDrive 'pinned-source-file-symlink'
        $target = Join-Path $root 'target.json'
        $link = Join-Path $root 'native-a.json'
        Write-TestP5aText $target "symlink target`n"
        try
        {
            New-Item -ItemType SymbolicLink -Path $link -Target $target `
                -ErrorAction Stop | Out-Null
        }
        catch
        {
            Set-ItResult -Skipped -Because `
                "file symlink creation is unavailable without elevation: $($_.Exception.Message)"
            return
        }

        ((Get-Item -LiteralPath $link -Force).Attributes -band
            [IO.FileAttributes]::ReparsePoint) | Should Not Be 0
        foreach ($commandName in @(
            'Open-P5aGeneratorPinnedFile',
            'Open-P5aVerifierFixedFileLease'))
        {
            $leaseState = [pscustomobject]@{ Value = $null }
            $output = [Collections.Generic.List[object]]::new()
            try
            {
                $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                    $leaseState.Value = & $commandName -Path $link
                }
                $failure | Should Not BeNullOrEmpty
                $failure | Should Match '(?i)(reparse|canonical.*path|requested.*path)'
            }
            finally
            {
                if ($null -ne $leaseState.Value) { $leaseState.Value.Dispose() }
            }
        }
    }

    It 'runs exactly two verifier direct children with fixed apphost then exact focused dotnet cwd argv and allows descendants' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierProcessProtocol' verifier
        $context = New-TestP5aProcessContext 'verifier-two'
        $shim = New-TestP5aProcessShim 'verifier-two-shim'
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $dotnetClosure = Get-TestP5aCompleteSelectedDotnetDescendantClosure
        $nestedDescendants = @(
            New-TestP5aNestedDotnetDescendantRecords `
                -Closure $dotnetClosure -DirectProcessId 5002)
        Set-TestP5aShimControl $shim @{
            DescendantRecords = @($nestedDescendants)
        }
        $ambientEnvironment = 'test-ambient-verifier-two'
        $ambientOracleApphost = 'test-ambient-prebuilt-oracle'
        $selectedEnvironment = @{}
        foreach ($entry in $toolchain.Environment.GetEnumerator())
        {
            $selectedEnvironment[[string]$entry.Key] = [string]$entry.Value
        }
        $selectedEnvironment.GODOTALS_P5A_PREBUILT_ORACLE_APPHOST =
            $ambientOracleApphost
        $selectedRun = {
            $selectedResult = Invoke-TestP5aWithAmbientStagingEnvironment `
                -AmbientValue $ambientEnvironment `
                -Action { Invoke-TestP5aVerifierProtocol $context $shim }
            $selectedResult | Add-Member -NotePropertyName PrebuiltOracleValueAfterAction `
                -NotePropertyValue ([Environment]::GetEnvironmentVariable(
                    'GODOTALS_P5A_PREBUILT_ORACLE_APPHOST',
                    [EnvironmentVariableTarget]::Process))
            return $selectedResult
        }
        $runs = @(Invoke-TestP5aWithProcessEnvironment `
            -Values $selectedEnvironment -Action $selectedRun)
        $runs.Count | Should Be 1
        $run = $runs[0]
        $output = @($run.Output)
        $records = @(Get-TestP5aShimRecords $shim)

        $records.Count | Should Be 2
        Assert-TestP5aDescendantRecordShape -DirectChildRecord $records[0] `
            -ExpectedImages @()
        Assert-TestP5aDescendantRecordShape -DirectChildRecord $records[1] `
            -ExpectedImages @($nestedDescendants | ForEach-Object { $_.imageName }) `
            -ExpectedExecutablePaths @($nestedDescendants | ForEach-Object {
                $_.executablePath
            })
        $records[0].filePath | Should Be $context.Oracle.AppHostPath
        $records[0].workingDirectory | Should Be $context.Root
        @($records[0].arguments) | Should Be @(
            '--verify-fixture', '--repository-root', $context.Root,
            '--fixture', $context.Fixture)
        $records[1].filePath | Should Be $toolchain.SelectedDotnet
        $records[1].workingDirectory | Should Be $context.Root
        $trxPath = (Get-TestP5aExpectedProcessPaths $context).Trx
        @($records[1].arguments) | Should Be @(
            'test', 'tests/Als.Core.Tests/Als.Core.Tests.csproj',
            '-c', 'Debug',
            '--filter', 'FullyQualifiedName~AlsP5aGoldenTests',
            '--logger', "trx;LogFileName=$trxPath")
        $records[0].p5aStagingRootEnvironment | Should Be ([IO.Path]::GetFullPath($context.Staging))
        $records[1].p5aStagingRootEnvironment | Should Be $ambientEnvironment
        $records[0].p5aPrebuiltOracleApphostEnvironment | Should Be $ambientOracleApphost
        $records[1].p5aPrebuiltOracleApphostEnvironment |
            Should Be ([IO.Path]::GetFullPath($context.Oracle.AppHostPath))
        $run.ValueAfterAction | Should Be $ambientEnvironment
        $run.PrebuiltOracleValueAfterAction | Should Be $ambientOracleApphost
        Assert-TestP5aPathUnderRoot $trxPath $context.Staging
        $expectedParentMarker = "P5A_GOLDEN_FIXTURE_OK cases=8 commit=$script:P5aLockedCommit"
        Assert-TestP5aExactParentMarker -Output @($output) `
            -ExpectedMarker $expectedParentMarker
        @($output | Where-Object { $_ -match 'P5A_ORACLE_DIGESTS' }).Count | Should Be 0
    }

    It 'executes the verifier script entrypoint with its Oracle lease held through both direct children' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierWorkflow' verifier
        $context = New-TestP5aProcessContext 'verifier-entrypoint-success'
        $shim = New-TestP5aProcessShim 'verifier-entrypoint-success-shim'
        $leasePaths = @(Get-TestP5aVerifierLeasePaths $context)
        $checkpoints = New-TestP5aCheckpointInvoker
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $dotnetClosure = Get-TestP5aCompleteSelectedDotnetDescendantClosure
        $nestedDescendants = @(
            New-TestP5aNestedDotnetDescendantRecords `
                -Closure $dotnetClosure -DirectProcessId 5002)
        Set-TestP5aShimControl $shim @{
            DescendantRecords = @($nestedDescendants)
            ProbeLockedPaths = @($leasePaths)
        }
        $ambientEnvironment = 'test-ambient-verifier-entrypoint'
        $selectedRun = {
            Invoke-TestP5aWithAmbientStagingEnvironment `
                -AmbientValue $ambientEnvironment `
                -Action {
                    & $script:P5aVerifierPath `
                        -RepositoryRoot $context.Root `
                        -FixturePath $context.Fixture `
                        -StagingRoot $context.Staging `
                        -ProcessInvoker $shim.Invoker `
                        -CheckpointInvoker $checkpoints.Invoker `
                        -TimeoutSeconds 10
                }
        }
        $runs = @(Invoke-TestP5aWithProcessEnvironment `
            -Values $toolchain.Environment -Action $selectedRun)
        $runs.Count | Should Be 1
        $run = $runs[0]
        $output = @($run.Output)
        $records = @(Get-TestP5aShimRecords $shim)

        $records.Count | Should Be 2
        Assert-TestP5aDescendantRecordShape -DirectChildRecord $records[0] `
            -ExpectedImages @()
        Assert-TestP5aDescendantRecordShape -DirectChildRecord $records[1] `
            -ExpectedImages @($nestedDescendants | ForEach-Object { $_.imageName }) `
            -ExpectedExecutablePaths @($nestedDescendants | ForEach-Object {
                $_.executablePath
            })
        Assert-TestP5aLeaseProbeRecords -Records $records -ExpectedPaths $leasePaths
        Assert-TestP5aLeaseContinuity -CheckpointObserver $checkpoints `
            -ExpectedCheckpoints @(
                'OracleEvidenceOpened', 'BeforeOracleChild', 'OracleChildCompleted',
                'BeforeDotnetChild', 'ChildrenCompleted') `
            -OracleHandleCount $leasePaths.Count
        $records[0].p5aStagingRootEnvironment | Should Be ([IO.Path]::GetFullPath($context.Staging))
        $records[1].p5aStagingRootEnvironment | Should Be $ambientEnvironment
        $run.ValueAfterAction | Should Be $ambientEnvironment
        $expectedParentMarker = "P5A_GOLDEN_FIXTURE_OK cases=8 commit=$script:P5aLockedCommit"
        Assert-TestP5aExactParentMarker -Output @($output) `
            -ExpectedMarker $expectedParentMarker
        Assert-TestP5aParentMarkerMutationsRejected -Output @($output) `
            -ExpectedMarker $expectedParentMarker -Kind fixture
        @($output | Where-Object { $_ -match 'P5A_ORACLE_DIGESTS' }).Count | Should Be 0
    }

    It 'rejects missing relative outside or wrong verifier staging environment and restores ambient state' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierWorkflow' verifier
        foreach ($caseName in @('missing', 'relative', 'outside', 'wrong'))
        {
            $context = New-TestP5aProcessContext "verifier-staging-environment-$caseName"
            $shim = New-TestP5aProcessShim "verifier-staging-environment-$caseName-shim"
            $outsidePath = Join-Path $TestDrive "verifier-staging-outside-$caseName"
            $wrongPath = Join-Path $context.Root 'wrong-verifier-staging'
            if ($caseName -eq 'outside')
            {
                [void][IO.Directory]::CreateDirectory($outsidePath)
            }
            if ($caseName -eq 'wrong')
            {
                [void][IO.Directory]::CreateDirectory($wrongPath)
            }
            $invalidValue = switch ($caseName)
            {
                'missing' { $null }
                'relative' { 'relative-verifier-staging' }
                'outside' { [IO.Path]::GetFullPath($outsidePath) }
                'wrong' { [IO.Path]::GetFullPath($wrongPath) }
            }
            $mutatingInvoker = if ($caseName -eq 'missing') {
                New-TestP5aEnvironmentMutatingInvoker -Shim $shim -Remove
            } else {
                New-TestP5aEnvironmentMutatingInvoker -Shim $shim -Value $invalidValue
            }

            $environmentName = 'GODOTALS_P5A_STAGING_ROOT'
            $previousEnvironment = [Environment]::GetEnvironmentVariable(
                $environmentName, [EnvironmentVariableTarget]::Process)
            $ambientEnvironment = "test-ambient-invalid-$caseName"
            [Environment]::SetEnvironmentVariable(
                $environmentName, $ambientEnvironment,
                [EnvironmentVariableTarget]::Process)
            try
            {
                $output = [Collections.Generic.List[object]]::new()
                $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                    & $script:P5aVerifierPath `
                        -RepositoryRoot $context.Root `
                        -FixturePath $context.Fixture `
                        -StagingRoot $context.Staging `
                        -ProcessInvoker $mutatingInvoker `
                        -TimeoutSeconds 10
                }
                $failure | Should Not BeNullOrEmpty
                $records = @(Get-TestP5aShimRecords $shim)
                $records.Count | Should Be 1
                $records[0].p5aStagingRootEnvironment | Should Be $invalidValue
                [Environment]::GetEnvironmentVariable(
                    $environmentName, [EnvironmentVariableTarget]::Process) |
                    Should Be $ambientEnvironment
                @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                    Should Be 0
                Test-Path -LiteralPath $context.Staging | Should Be $false
                if ($caseName -eq 'outside')
                {
                    Test-Path -LiteralPath $outsidePath -PathType Container | Should Be $true
                }
                if ($caseName -eq 'wrong')
                {
                    Test-Path -LiteralPath $wrongPath -PathType Container | Should Be $true
                }
            }
            finally
            {
                [Environment]::SetEnvironmentVariable(
                    $environmentName, $previousEnvironment,
                    [EnvironmentVariableTarget]::Process)
            }
        }
    }

    It 'canonicalizes supported extended paths and rejects device namespace aliases before any child starts' {
        Assert-TestP5aCommandCapability 'Assert-P5aGeneratorInvocationContract' generator
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierProcessProtocol' verifier
        $generatorContext = New-TestP5aProcessContext 'generator-extended-staging-alias'
        $generatorInsideStaging = [IO.Path]::GetFullPath((Join-Path `
            $generatorContext.Root 'forbidden-generator-staging'))
        $generatorExtendedStaging = '\\?\' + $generatorInsideStaging
        Test-TestP5aRejects {
            Assert-P5aGeneratorInvocationContract `
                -RepositoryRoot $generatorContext.Root `
                -UnrealEditorCmd $generatorContext.Editor `
                -UnrealProject $generatorContext.UProject `
                -ReferenceRoot $generatorContext.Reference `
                -StagingRoot $generatorExtendedStaging `
                -DestinationPath $generatorContext.Fixture `
                -TimeoutSeconds 10
        } | Should Be $true

        $context = New-TestP5aProcessContext 'verifier-extended-staging-alias'
        $shim = New-TestP5aProcessShim 'verifier-extended-staging-alias-shim'
        $insideStaging = [IO.Path]::GetFullPath((Join-Path `
            $context.Root 'forbidden-verifier-staging'))
        $context.Staging = '\\?\' + $insideStaging

        Test-TestP5aRejects {
            Invoke-TestP5aVerifierProtocol $context $shim
        } | Should Be $true
        @(Get-TestP5aShimRecords $shim).Count | Should Be 0
        Test-Path -LiteralPath $insideStaging | Should Be $false

        foreach ($unsupportedPath in @(
            '\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\staging'
            '\\?\Volume{00000000-0000-0000-0000-000000000000}\staging'
            ('\\.\' + [IO.Path]::GetFullPath((Join-Path $TestDrive 'device-path'))) ))
        {
            Test-TestP5aRejects {
                Get-P5aGeneratorFullPath $unsupportedPath
            } | Should Be $true
            Test-TestP5aRejects {
                Get-P5aVerifierFullPath $unsupportedPath
            } | Should Be $true
        }

        $volumeRoot = [IO.Path]::GetPathRoot($TestDrive)
        (Get-P5aGeneratorFullPath $volumeRoot) | Should Be $volumeRoot
        (Get-P5aVerifierFullPath $volumeRoot) | Should Be $volumeRoot
        $volumeChild = Join-Path $volumeRoot 'p5a-boundary-probe'
        (Test-P5aGeneratorPathWithin -Path $volumeChild -Root $volumeRoot) |
            Should Be $true
        (Test-P5aVerifierPathWithin -Path $volumeChild -Root $volumeRoot) |
            Should Be $true
    }

    It 'holds fixture schemas manifest and profiles through the final verifier checkpoint' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierWorkflow' verifier
        $context = New-TestP5aProcessContext 'verifier-runtime-input-lease'
        $shim = New-TestP5aProcessShim 'verifier-runtime-input-lease-shim'
        $leasePaths = @(Get-TestP5aVerifierLeasePaths $context)
        $fixtureHash = Get-TestP5aFileHash $context.Fixture
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $dotnetClosure = Get-TestP5aCompleteSelectedDotnetDescendantClosure
        $nestedDescendants = @(
            New-TestP5aNestedDotnetDescendantRecords `
                -Closure $dotnetClosure -DirectProcessId 5002)
        Set-TestP5aShimControl $shim @{
            DescendantRecords = @($nestedDescendants)
            ProbeLockedPaths = @($leasePaths)
        }
        $checkpoints = New-TestP5aCheckpointInvoker `
            -MutateAt 'ChildrenCompleted' -MutatePath $context.Fixture
        $selectedAction = {
            & $script:P5aVerifierPath `
                -RepositoryRoot $context.Root `
                -FixturePath $context.Fixture `
                -StagingRoot $context.Staging `
                -ProcessInvoker $shim.Invoker `
                -CheckpointInvoker $checkpoints.Invoker `
                -TimeoutSeconds 10
        }
        $output = [Collections.Generic.List[object]]::new()
        $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
            Invoke-TestP5aWithProcessEnvironment `
                -Values $toolchain.Environment -Action $selectedAction
        }

        $failure | Should Match '(?i)(used by another process|sharing|access|lease)'
        @(Get-TestP5aShimRecords $shim).Count | Should Be 2
        Assert-TestP5aLeaseProbeRecords `
            -Records @(Get-TestP5aShimRecords $shim) -ExpectedPaths $leasePaths
        Assert-TestP5aLeaseContinuity -CheckpointObserver $checkpoints `
            -ExpectedCheckpoints @(
                'OracleEvidenceOpened', 'BeforeOracleChild', 'OracleChildCompleted',
                'BeforeDotnetChild', 'ChildrenCompleted') `
            -OracleHandleCount $leasePaths.Count
        (Get-TestP5aFileHash $context.Fixture) | Should Be $fixtureHash
        @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
            Should Be 0
        Test-Path -LiteralPath $context.Staging | Should Be $false
    }

    It 'pins the focused test project and recursive sources and rejects a new source before dotnet starts' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierWorkflow' verifier
        foreach ($case in @(
            @{ Name = 'mutate'; Existing = $true; Diagnostic = '(?i)(used by another process|sharing|access|lease)' }
            @{ Name = 'add'; Existing = $false; Diagnostic = '(?i)runtime input closure changed' }
        ))
        {
            $context = New-TestP5aProcessContext `
                "verifier-focused-test-input-$($case.Name)"
            $shim = New-TestP5aProcessShim `
                "verifier-focused-test-input-$($case.Name)-shim"
            $leasePaths = @(Get-TestP5aVerifierLeasePaths $context)
            $toolchain = Get-TestP5aDefaultWorkflowToolchain
            $target = if ($case.Existing) {
                Join-Path $context.Root 'tests\Als.Core.Tests\AlsP5aGoldenTests.cs'
            } else {
                Join-Path $context.Root 'tests\Als.Core.Tests\InjectedP5aTest.cs'
            }
            $targetBefore = if ($case.Existing) { Get-TestP5aFileHash $target } else { '' }
            $capturedTarget = $target
            $mutation = {
                param($Checkpoint)
                [IO.File]::AppendAllText(
                    $capturedTarget,
                    "namespace Injected; public sealed class SpoofedTest { }`n")
            }.GetNewClosure()
            $checkpoints = New-TestP5aCheckpointInvoker `
                -MutateAt 'BeforeDotnetChild' -MutationAction $mutation
            Set-TestP5aShimControl $shim @{ ProbeLockedPaths = @($leasePaths) }
            $selectedAction = {
                & $script:P5aVerifierPath `
                    -RepositoryRoot $context.Root `
                    -FixturePath $context.Fixture `
                    -StagingRoot $context.Staging `
                    -ProcessInvoker $shim.Invoker `
                    -CheckpointInvoker $checkpoints.Invoker `
                    -TimeoutSeconds 10
            }
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                Invoke-TestP5aWithProcessEnvironment `
                    -Values $toolchain.Environment -Action $selectedAction
            }

            $failure | Should Match $case.Diagnostic
            @(Get-TestP5aShimRecords $shim).Count | Should Be 1
            Assert-TestP5aLeaseProbeRecords `
                -Records @(Get-TestP5aShimRecords $shim) -ExpectedPaths $leasePaths
            Assert-TestP5aLeaseContinuity -CheckpointObserver $checkpoints `
                -ExpectedCheckpoints @(
                    'OracleEvidenceOpened', 'BeforeOracleChild',
                    'OracleChildCompleted', 'BeforeDotnetChild') `
                -OracleHandleCount $leasePaths.Count
            if ($case.Existing)
            {
                (Get-TestP5aFileHash $target) | Should Be $targetBefore
            }
            else
            {
                Test-Path -LiteralPath $target -PathType Leaf | Should Be $true
            }
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
            Test-Path -LiteralPath $context.Staging | Should Be $false
        }
    }

    It 'fails closed without a success marker when strict verifier staging cleanup is blocked' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierWorkflow' verifier
        $context = New-TestP5aProcessContext 'verifier-strict-cleanup'
        $shim = New-TestP5aProcessShim 'verifier-strict-cleanup-shim'
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $dotnetClosure = Get-TestP5aCompleteSelectedDotnetDescendantClosure
        $nestedDescendants = @(
            New-TestP5aNestedDotnetDescendantRecords `
                -Closure $dotnetClosure -DirectProcessId 5002)
        Set-TestP5aShimControl $shim @{
            DescendantRecords = @($nestedDescendants)
        }
        $lockState = [pscustomobject]@{ Stream = $null }
        $capturedLockState = $lockState
        $capturedTrxPath = (Get-TestP5aExpectedProcessPaths $context).Trx
        $lockMutation = {
            param($Checkpoint)
            $capturedLockState.Stream = [IO.File]::Open(
                $capturedTrxPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
                [IO.FileShare]::None)
        }.GetNewClosure()
        $checkpoints = New-TestP5aCheckpointInvoker `
            -MutateAt 'ChildrenCompleted' -MutationAction $lockMutation
        $selectedAction = {
            & $script:P5aVerifierPath `
                -RepositoryRoot $context.Root `
                -FixturePath $context.Fixture `
                -StagingRoot $context.Staging `
                -ProcessInvoker $shim.Invoker `
                -CheckpointInvoker $checkpoints.Invoker `
                -TimeoutSeconds 10
        }
        $output = [Collections.Generic.List[object]]::new()
        try
        {
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                Invoke-TestP5aWithProcessEnvironment `
                    -Values $toolchain.Environment -Action $selectedAction
            }
            $failure | Should Match '(?i)(used by another process|sharing|access|cleanup)'
            @(Get-TestP5aShimRecords $shim).Count | Should Be 2
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
            Test-Path -LiteralPath $context.Staging -PathType Container | Should Be $true
        }
        finally
        {
            if ($null -ne $lockState.Stream) { $lockState.Stream.Dispose() }
            if (Test-Path -LiteralPath $context.Staging)
            {
                Remove-Item -LiteralPath $context.Staging -Recurse -Force
            }
        }
    }

    It 'never traverses an unexpected verifier staging child directory during cleanup' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierWorkflow' verifier
        $context = New-TestP5aProcessContext 'verifier-directory-cleanup'
        $shim = New-TestP5aProcessShim 'verifier-directory-cleanup-shim'
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $dotnetClosure = Get-TestP5aCompleteSelectedDotnetDescendantClosure
        $nestedDescendants = @(
            New-TestP5aNestedDotnetDescendantRecords `
                -Closure $dotnetClosure -DirectProcessId 5002)
        Set-TestP5aShimControl $shim @{
            DescendantRecords = @($nestedDescendants)
        }
        $outside = Join-Path $TestDrive 'verifier-directory-cleanup-outside'
        $outsideSentinel = Join-Path $outside 'sentinel.txt'
        Write-TestP5aText $outsideSentinel "outside sentinel`n"
        $capturedStaging = $context.Staging
        $capturedOutside = $outside
        $junctionMutation = {
            param($Checkpoint)
            New-Item -ItemType Junction `
                -Path (Join-Path $capturedStaging 'unexpected-directory') `
                -Target $capturedOutside | Out-Null
        }.GetNewClosure()
        $checkpoints = New-TestP5aCheckpointInvoker `
            -MutateAt 'ChildrenCompleted' -MutationAction $junctionMutation
        $selectedAction = {
            & $script:P5aVerifierPath `
                -RepositoryRoot $context.Root `
                -FixturePath $context.Fixture `
                -StagingRoot $context.Staging `
                -ProcessInvoker $shim.Invoker `
                -CheckpointInvoker $checkpoints.Invoker `
                -TimeoutSeconds 10
        }
        $output = [Collections.Generic.List[object]]::new()
        try
        {
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                Invoke-TestP5aWithProcessEnvironment `
                    -Values $toolchain.Environment -Action $selectedAction
            }
            $failure | Should Match '(?i)unexpected.*directory'
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
            Test-Path -LiteralPath $outsideSentinel -PathType Leaf |
                Should Be $true
            [IO.File]::ReadAllText($outsideSentinel) | Should Be "outside sentinel`n"
            Test-Path -LiteralPath $context.Staging -PathType Container |
                Should Be $true
        }
        finally
        {
            if (Test-Path -LiteralPath $context.Staging)
            {
                Remove-Item -LiteralPath $context.Staging -Recurse -Force
            }
        }
    }

    It 'executes both top-level entrypoints in clean runspaces without cross-script helpers' {
        Assert-TestP5aPathCapability $script:P5aGeneratorPath 'scripts/generate-p5a-golden.ps1'
        Assert-TestP5aPathCapability $script:P5aVerifierPath 'scripts/verify-p5a-golden.ps1'

        $generatorContext = New-TestP5aProcessContext 'generator-isolated-entrypoint'
        $generatorShim = New-TestP5aProcessShim 'generator-isolated-entrypoint-shim'
        $generatorLeasePaths = @(Get-TestP5aGeneratorLeasePaths $generatorContext)
        Set-TestP5aShimControl $generatorShim @{
            ProbeLockedPaths = @($generatorLeasePaths)
        }
        $generatorOutput = @(Invoke-TestP5aIsolatedEntrypoint `
            -Kind generator -Context $generatorContext -Shim $generatorShim)
        $generatorRecords = @(Get-TestP5aShimRecords $generatorShim)
        $generatorRecords.Count | Should Be 7
        Assert-TestP5aLeaseProbeRecords -Records $generatorRecords `
            -ExpectedPaths $generatorLeasePaths
        [IO.File]::ReadAllText($generatorContext.Fixture) |
            Should Be "{`"representation`":`"native`"}`n"
        Assert-TestP5aExactParentMarker -Output @($generatorOutput) `
            -ExpectedMarker "P5A_GOLDEN_GENERATION_OK cases=8 commit=$script:P5aLockedCommit"

        $verifierContext = New-TestP5aProcessContext 'verifier-isolated-entrypoint'
        $verifierShim = New-TestP5aProcessShim 'verifier-isolated-entrypoint-shim'
        $verifierLeasePaths = @(Get-TestP5aVerifierLeasePaths $verifierContext)
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $dotnetClosure = Get-TestP5aCompleteSelectedDotnetDescendantClosure
        $nestedDescendants = @(
            New-TestP5aNestedDotnetDescendantRecords `
                -Closure $dotnetClosure -DirectProcessId 5002)
        Set-TestP5aShimControl $verifierShim @{
            DescendantRecords = @($nestedDescendants)
            ProbeLockedPaths = @($verifierLeasePaths)
        }
        $verifierAmbientEnvironment = 'test-ambient-verifier-isolated'
        $selectedRun = {
            Invoke-TestP5aWithAmbientStagingEnvironment `
                -AmbientValue $verifierAmbientEnvironment `
                -Action {
                    Invoke-TestP5aIsolatedEntrypoint `
                        -Kind verifier -Context $verifierContext -Shim $verifierShim
                }
        }
        $runs = @(Invoke-TestP5aWithProcessEnvironment `
            -Values $toolchain.Environment -Action $selectedRun)
        $runs.Count | Should Be 1
        $verifierRun = $runs[0]
        $verifierOutput = @($verifierRun.Output)
        $verifierRecords = @(Get-TestP5aShimRecords $verifierShim)
        $verifierRecords.Count | Should Be 2
        Assert-TestP5aDescendantRecordShape -DirectChildRecord $verifierRecords[0] `
            -ExpectedImages @()
        Assert-TestP5aDescendantRecordShape -DirectChildRecord $verifierRecords[1] `
            -ExpectedImages @($nestedDescendants | ForEach-Object { $_.imageName }) `
            -ExpectedExecutablePaths @($nestedDescendants | ForEach-Object {
                $_.executablePath
            })
        $verifierRecords[1].filePath | Should Be $toolchain.SelectedDotnet
        Assert-TestP5aLeaseProbeRecords -Records $verifierRecords `
            -ExpectedPaths $verifierLeasePaths
        $verifierRecords[0].p5aStagingRootEnvironment |
            Should Be ([IO.Path]::GetFullPath($verifierContext.Staging))
        $verifierRecords[1].p5aStagingRootEnvironment | Should Be $verifierAmbientEnvironment
        $verifierRun.ValueAfterAction | Should Be $verifierAmbientEnvironment
        Assert-TestP5aExactParentMarker -Output @($verifierOutput) `
            -ExpectedMarker "P5A_GOLDEN_FIXTURE_OK cases=8 commit=$script:P5aLockedCommit"
    }

    It 'keeps the complete nested selected-SDK dotnet test chain under the second direct child' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierProcessProtocol' verifier
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $dotnetClosure = Get-TestP5aCompleteSelectedDotnetDescendantClosure
        $nestedDescendants = @(
            New-TestP5aNestedDotnetDescendantRecords `
                -Closure $dotnetClosure -DirectProcessId 5002)
        $context = New-TestP5aProcessContext 'verifier-descendant-complete-nested'
        $shim = New-TestP5aProcessShim 'verifier-descendant-complete-nested-shim'
        Set-TestP5aShimControl $shim @{
            DescendantRecords = @($nestedDescendants)
        }
        $ambientEnvironment = 'test-ambient-descendant-complete-nested'
        $selectedRun = {
            Invoke-TestP5aWithAmbientStagingEnvironment `
                -AmbientValue $ambientEnvironment `
                -Action { Invoke-TestP5aVerifierProtocol $context $shim }
        }
        $runs = @(Invoke-TestP5aWithProcessEnvironment `
            -Values $toolchain.Environment -Action $selectedRun)
        $runs.Count | Should Be 1
        $run = $runs[0]
        $output = @($run.Output)
        $records = @(Get-TestP5aShimRecords $shim)
        $records.Count | Should Be 2
        Assert-TestP5aDescendantRecordShape -DirectChildRecord $records[0] `
            -ExpectedImages @()
        Assert-TestP5aDescendantRecordShape -DirectChildRecord $records[1] `
            -ExpectedImages @(
                'MSBuild.exe', 'vstest.console.exe', 'testhost.exe') `
            -ExpectedExecutablePaths @($nestedDescendants | ForEach-Object {
                $_.executablePath
            })
        $records[0].p5aStagingRootEnvironment |
            Should Be ([IO.Path]::GetFullPath($context.Staging))
        $records[1].p5aStagingRootEnvironment | Should Be $ambientEnvironment
        $run.ValueAfterAction | Should Be $ambientEnvironment
        Assert-TestP5aExactParentMarker -Output @($output) `
            -ExpectedMarker "P5A_GOLDEN_FIXTURE_OK cases=8 commit=$script:P5aLockedCommit"
    }

    It 'allows only the fixed repository testhost and system conhost paths in the selected dotnet closure' {
        Assert-TestP5aCommandCapability 'Get-P5aVerifierDotnetClosure' verifier
        $context = New-TestP5aProcessContext 'verifier-real-dotnet-closure'
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $projectTestHost = Join-Path `
            $context.Root 'tests\Als.Core.Tests\bin\Debug\net8.0\testhost.exe'
        $shadowTestHost = Join-Path `
            $context.Root 'tests\Als.Core.Tests\bin\Debug\net8.0\shadow\testhost.exe'
        $shadowConhost = Join-Path $context.Root 'shadow\conhost.exe'
        Write-TestP5aText $projectTestHost "fixed project testhost`n"
        Write-TestP5aText $shadowTestHost "shadow project testhost`n"
        Write-TestP5aText $shadowConhost "shadow system conhost`n"

        $getClosure = {
            Get-P5aVerifierDotnetClosure -RepositoryRoot $context.Root
        }
        $closures = @(Invoke-TestP5aWithProcessEnvironment `
            -Values $toolchain.Environment -Action $getClosure)
        $closures.Count | Should Be 1
        $closure = $closures[0]
        $systemConhost = [IO.Path]::GetFullPath(
            (Join-Path ([Environment]::SystemDirectory) 'conhost.exe'))

        @($closure.PathsByImage['testhost.exe']) | Should Be @(
            [IO.Path]::GetFullPath($toolchain.SelectedTestHost),
            [IO.Path]::GetFullPath($projectTestHost))
        @($closure.PathsByImage['conhost.exe']) | Should Be @($systemConhost)
        @($closure.PathsByImage['Als.P5aOracle.exe']) | Should Be @(
            [IO.Path]::GetFullPath($context.Oracle.AppHostPath))
        @($closure.PathsByImage.Values | ForEach-Object { @($_) }) |
            Should Not Contain ([IO.Path]::GetFullPath($shadowTestHost))
        @($closure.PathsByImage.Values | ForEach-Object { @($_) }) |
            Should Not Contain ([IO.Path]::GetFullPath($shadowConhost))
    }

    It 'accepts the real dotnet testhost conhost and prebuilt Oracle descendant chain' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierProcessProtocol' verifier
        $context = New-TestP5aProcessContext 'verifier-real-dotnet-chain'
        $shim = New-TestP5aProcessShim 'verifier-real-dotnet-chain-shim'
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $projectTestHost = Join-Path `
            $context.Root 'tests\Als.Core.Tests\bin\Debug\net8.0\testhost.exe'
        Write-TestP5aText $projectTestHost "fixed project testhost`n"
        $systemConhost = [IO.Path]::GetFullPath(
            (Join-Path ([Environment]::SystemDirectory) 'conhost.exe'))
        Test-Path -LiteralPath $systemConhost -PathType Leaf | Should Be $true
        $realDescendants = @(
            [pscustomobject][ordered]@{
                processId = 12001
                parentProcessId = 5002
                ancestorProcessIds = @(5002)
                imageName = 'dotnet.exe'
                executablePath = [IO.Path]::GetFullPath($toolchain.SelectedDotnet)
            }
            [pscustomobject][ordered]@{
                processId = 12002
                parentProcessId = 12001
                ancestorProcessIds = @(5002, 12001)
                imageName = 'testhost.exe'
                executablePath = [IO.Path]::GetFullPath($projectTestHost)
            }
            [pscustomobject][ordered]@{
                processId = 12003
                parentProcessId = 12002
                ancestorProcessIds = @(5002, 12001, 12002)
                imageName = 'conhost.exe'
                executablePath = $systemConhost
            }
            [pscustomobject][ordered]@{
                processId = 12004
                parentProcessId = 12002
                ancestorProcessIds = @(5002, 12001, 12002)
                imageName = 'Als.P5aOracle.exe'
                executablePath = [IO.Path]::GetFullPath($context.Oracle.AppHostPath)
            }
        )
        Set-TestP5aShimControl $shim @{
            DescendantRecords = @($realDescendants)
        }
        $ambientEnvironment = 'test-ambient-real-dotnet-chain'
        $selectedRun = {
            Invoke-TestP5aWithAmbientStagingEnvironment `
                -AmbientValue $ambientEnvironment `
                -Action { Invoke-TestP5aVerifierProtocol $context $shim }
        }
        $runs = @(Invoke-TestP5aWithProcessEnvironment `
            -Values $toolchain.Environment -Action $selectedRun)
        $runs.Count | Should Be 1
        $run = $runs[0]
        $records = @(Get-TestP5aShimRecords $shim)

        $records.Count | Should Be 2
        Assert-TestP5aDescendantRecordShape -DirectChildRecord $records[1] `
            -ExpectedImages @(
                'dotnet.exe', 'testhost.exe', 'conhost.exe', 'Als.P5aOracle.exe') `
            -ExpectedExecutablePaths @($realDescendants | ForEach-Object {
                $_.executablePath
            })
        Assert-TestP5aExactParentMarker -Output @($run.Output) `
            -ExpectedMarker "P5A_GOLDEN_FIXTURE_OK cases=8 commit=$script:P5aLockedCommit"
    }

    It 'rejects illegal descendants from both initial Oracle children and either verifier child' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorProcessProtocol' generator
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierProcessProtocol' verifier
        $toolchain = Get-TestP5aDefaultWorkflowToolchain
        $invokeSelectedVerifier = {
            param([Parameter(Mandatory)][object]$Context,
                  [Parameter(Mandatory)][object]$Shim)

            $selectedContext = $Context
            $selectedShim = $Shim
            $selectedAction = {
                Invoke-TestP5aVerifierProtocol $selectedContext $selectedShim
            }
            Invoke-TestP5aWithProcessEnvironment `
                -Values $toolchain.Environment -Action $selectedAction
        }
        $illegalGeneratorImages = @(
            'pwsh.exe',
            'cmd.exe',
            'UnrealEditor.exe',
            'UnrealEditorCmd.exe',
            'Als.P5aOracle.exe',
            'arbitrary-apphost.exe',
            'dotnet.exe',
            'MSBuild.exe',
            'testhost.exe',
            'vstest.console.exe',
            'conhost.exe')

        foreach ($call in 1..7)
        {
            foreach ($image in $illegalGeneratorImages)
            {
                $safeImage = $image -replace '[^A-Za-z0-9]', '-'
                $context = New-TestP5aProcessContext "generator-descendant-$call-$safeImage"
                $shim = New-TestP5aProcessShim "generator-descendant-$call-$safeImage-shim"
                Set-TestP5aShimControl $shim @{
                    DescendantCall = $call
                    DescendantImages = @($image)
                }
                $output = [Collections.Generic.List[object]]::new()
                $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                    Invoke-TestP5aGeneratorProtocol $context $shim
                }
                $failure | Should Not BeNullOrEmpty
                $failure | Should Match '(?i)descendant.*(forbidden|not allowed|unexpected)'
                @(Get-TestP5aShimRecords $shim).Count | Should Be $call
                @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                    Should Be 0
                Test-Path -LiteralPath $context.Staging | Should Be $false
            }
        }

        foreach ($call in @(1, 2))
        {
            $illegalVerifierImages = if ($call -eq 1) {
                @($illegalGeneratorImages)
            } else {
                @('pwsh.exe', 'cmd.exe', 'UnrealEditor.exe', 'UnrealEditorCmd.exe',
                  'Als.P5aOracle.exe', 'arbitrary-apphost.exe')
            }
            foreach ($image in $illegalVerifierImages)
            {
                $safeImage = $image -replace '[^A-Za-z0-9]', '-'
                $context = New-TestP5aProcessContext "verifier-descendant-$call-$safeImage"
                $shim = New-TestP5aProcessShim "verifier-descendant-$call-$safeImage-shim"
                Set-TestP5aShimControl $shim @{
                    DescendantCall = $call
                    DescendantImages = @($image)
                }
                $environmentName = 'GODOTALS_P5A_STAGING_ROOT'
                $previousEnvironment = [Environment]::GetEnvironmentVariable(
                    $environmentName, [EnvironmentVariableTarget]::Process)
                $ambientEnvironment = "test-ambient-illegal-descendant-$call-$safeImage"
                [Environment]::SetEnvironmentVariable(
                    $environmentName, $ambientEnvironment,
                    [EnvironmentVariableTarget]::Process)
                try
                {
                    $output = [Collections.Generic.List[object]]::new()
                    $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                        & $invokeSelectedVerifier $context $shim
                    }
                    $failure | Should Not BeNullOrEmpty
                    $failure | Should Match `
                        '(?i)descendant.*(forbidden|not allowed|unexpected|selected.*dotnet|executable.*closure)'
                    @(Get-TestP5aShimRecords $shim).Count | Should Be $call
                    [Environment]::GetEnvironmentVariable(
                        $environmentName, [EnvironmentVariableTarget]::Process) |
                        Should Be $ambientEnvironment
                    @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                        Should Be 0
                    Test-Path -LiteralPath $context.Staging | Should Be $false
                }
                finally
                {
                    [Environment]::SetEnvironmentVariable(
                        $environmentName, $previousEnvironment,
                        [EnvironmentVariableTarget]::Process)
                }
            }
        }

        $dotnetClosure = Get-TestP5aCompleteSelectedDotnetDescendantClosure
        foreach ($image in @(
            'dotnet.exe', 'MSBuild.exe', 'testhost.exe', 'vstest.console.exe',
            'conhost.exe'))
        {
            $context = New-TestP5aProcessContext "verifier-descendant-outside-sdk-$image"
            $shim = New-TestP5aProcessShim "verifier-descendant-outside-sdk-$image-shim"
            Set-TestP5aShimControl $shim @{
                DescendantCall = 2
                DescendantImages = @($image)
            }
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                & $invokeSelectedVerifier $context $shim
            }
            $failure | Should Match '(?i)descendant.*(selected.*dotnet|sdk.*closure|executable.*closure)'
            @(Get-TestP5aShimRecords $shim).Count | Should Be 2
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
            Test-Path -LiteralPath $context.Staging | Should Be $false
        }

        foreach ($treeCase in @(
            @{ Name = 'orphan-parent'; Diagnostic = '(?i)descendant.*parent' }
            @{ Name = 'missing-direct-ancestor'; Diagnostic = '(?i)descendant.*ancestor' }
            @{ Name = 'foreign-root-before-direct'; Diagnostic = '(?i)descendant.*(ancestor|subtree)' }
            @{ Name = 'foreign-after-direct'; Diagnostic = '(?i)descendant.*(ancestor|subtree|foreign)' }
            @{ Name = 'self-parent'; Diagnostic = '(?i)descendant.*(self|cycle|parent)' }
            @{ Name = 'cycle'; Diagnostic = '(?i)descendant.*(cycle|ancestor)' }
            @{ Name = 'duplicate-ancestor'; Diagnostic = '(?i)descendant.*(duplicate.*ancestor|ancestor.*duplicate)' }
            @{ Name = 'wrong-ancestor-order'; Diagnostic = '(?i)descendant.*(ancestor.*order|order.*ancestor|root)' }
            @{ Name = 'parent-not-immediate-predecessor'; Diagnostic = '(?i)descendant.*(parent|ancestor|order)' }
            @{ Name = 'relative-path'; Diagnostic = '(?i)descendant.*path.*absolute' }
            @{ Name = 'mismatched-image-path'; Diagnostic = '(?i)descendant.*(image|basename).*path' }
            @{ Name = 'duplicate-process-id'; Diagnostic = '(?i)descendant.*duplicate.*process' }
            @{ Name = 'conhost-wrong-parent'; Diagnostic = '(?i)conhost.*parent.*testhost' }
            @{ Name = 'oracle-wrong-parent'; Diagnostic = '(?i)Oracle apphost.*parent.*testhost' }
            @{ Name = 'project-testhost-wrong-parent'; Diagnostic = '(?i)project testhost.*parent.*dotnet' }
        ))
        {
            $context = New-TestP5aProcessContext "verifier-malformed-tree-$($treeCase.Name)"
            $shim = New-TestP5aProcessShim "verifier-malformed-tree-$($treeCase.Name)-shim"
            $directProcessId = 5002
            $msbuildPath = [string]@($dotnetClosure.PathsByImage['MSBuild.exe'])[0]
            $vstestPath = [string]@($dotnetClosure.PathsByImage['vstest.console.exe'])[0]
            $testhostPath = [string]@($dotnetClosure.PathsByImage['testhost.exe'])[0]
            $projectTesthostPath = Join-Path `
                $context.Root 'tests\Als.Core.Tests\bin\Debug\net8.0\testhost.exe'
            if ($treeCase.Name -ceq 'project-testhost-wrong-parent')
            {
                Write-TestP5aText $projectTesthostPath "fixed project testhost`n"
            }
            $records = switch ($treeCase.Name)
            {
                'orphan-parent' { @(@{
                    processId = 12001; parentProcessId = 99999
                    ancestorProcessIds = @($directProcessId); imageName = 'MSBuild.exe'
                    executablePath = $msbuildPath
                }) }
                'missing-direct-ancestor' { @(@{
                    processId = 12001; parentProcessId = $directProcessId
                    ancestorProcessIds = @(99999); imageName = 'MSBuild.exe'
                    executablePath = $msbuildPath
                }) }
                'foreign-root-before-direct' { @(@{
                    processId = 12001; parentProcessId = $directProcessId
                    ancestorProcessIds = @(99999, $directProcessId); imageName = 'MSBuild.exe'
                    executablePath = $msbuildPath
                }) }
                'foreign-after-direct' { @(
                    @{
                        processId = 12001; parentProcessId = $directProcessId
                        ancestorProcessIds = @($directProcessId); imageName = 'MSBuild.exe'
                        executablePath = $msbuildPath
                    },
                    @{
                        processId = 12002; parentProcessId = 12001
                        ancestorProcessIds = @($directProcessId, 99999, 12001)
                        imageName = 'vstest.console.exe'; executablePath = $vstestPath
                    }) }
                'self-parent' { @(@{
                    processId = 12001; parentProcessId = 12001
                    ancestorProcessIds = @($directProcessId, 12001); imageName = 'MSBuild.exe'
                    executablePath = $msbuildPath
                }) }
                'cycle' { @(
                    @{
                        processId = 12001; parentProcessId = 12002
                        ancestorProcessIds = @($directProcessId, 12002)
                        imageName = 'MSBuild.exe'; executablePath = $msbuildPath
                    },
                    @{
                        processId = 12002; parentProcessId = 12001
                        ancestorProcessIds = @($directProcessId, 12001)
                        imageName = 'vstest.console.exe'; executablePath = $vstestPath
                    }) }
                'duplicate-ancestor' { @(
                    @{
                        processId = 12001; parentProcessId = $directProcessId
                        ancestorProcessIds = @($directProcessId); imageName = 'MSBuild.exe'
                        executablePath = $msbuildPath
                    },
                    @{
                        processId = 12002; parentProcessId = 12001
                        ancestorProcessIds = @($directProcessId, $directProcessId, 12001)
                        imageName = 'vstest.console.exe'; executablePath = $vstestPath
                    }) }
                'wrong-ancestor-order' { @(
                    @{
                        processId = 12001; parentProcessId = $directProcessId
                        ancestorProcessIds = @($directProcessId); imageName = 'MSBuild.exe'
                        executablePath = $msbuildPath
                    },
                    @{
                        processId = 12002; parentProcessId = 12001
                        ancestorProcessIds = @(12001, $directProcessId)
                        imageName = 'vstest.console.exe'; executablePath = $vstestPath
                    }) }
                'parent-not-immediate-predecessor' { @(
                    @{
                        processId = 12001; parentProcessId = $directProcessId
                        ancestorProcessIds = @($directProcessId); imageName = 'MSBuild.exe'
                        executablePath = $msbuildPath
                    },
                    @{
                        processId = 12002; parentProcessId = 12001
                        ancestorProcessIds = @($directProcessId, 12001)
                        imageName = 'vstest.console.exe'; executablePath = $vstestPath
                    },
                    @{
                        processId = 12003; parentProcessId = 12002
                        ancestorProcessIds = @($directProcessId, 12002, 12001)
                        imageName = 'testhost.exe'; executablePath = $testhostPath
                    }) }
                'relative-path' { @(@{
                    processId = 12001; parentProcessId = $directProcessId
                    ancestorProcessIds = @($directProcessId); imageName = 'MSBuild.exe'
                    executablePath = 'relative\MSBuild.exe'
                }) }
                'mismatched-image-path' { @(@{
                    processId = 12001; parentProcessId = $directProcessId
                    ancestorProcessIds = @($directProcessId); imageName = 'testhost.exe'
                    executablePath = $msbuildPath
                }) }
                'duplicate-process-id' { @(
                    @{
                        processId = 12001; parentProcessId = $directProcessId
                        ancestorProcessIds = @($directProcessId); imageName = 'MSBuild.exe'
                        executablePath = $msbuildPath
                    },
                    @{
                        processId = 12001; parentProcessId = $directProcessId
                        ancestorProcessIds = @($directProcessId); imageName = 'vstest.console.exe'
                        executablePath = $vstestPath
                    }) }
                'conhost-wrong-parent' { @(@{
                    processId = 12001; parentProcessId = $directProcessId
                    ancestorProcessIds = @($directProcessId); imageName = 'conhost.exe'
                    executablePath = [IO.Path]::GetFullPath(
                        (Join-Path ([Environment]::SystemDirectory) 'conhost.exe'))
                }) }
                'oracle-wrong-parent' { @(@{
                    processId = 12001; parentProcessId = $directProcessId
                    ancestorProcessIds = @($directProcessId)
                    imageName = 'Als.P5aOracle.exe'
                    executablePath = [IO.Path]::GetFullPath($context.Oracle.AppHostPath)
                }) }
                'project-testhost-wrong-parent' { @(
                    @{
                        processId = 12001; parentProcessId = $directProcessId
                        ancestorProcessIds = @($directProcessId); imageName = 'MSBuild.exe'
                        executablePath = $msbuildPath
                    },
                    @{
                        processId = 12002; parentProcessId = 12001
                        ancestorProcessIds = @($directProcessId, 12001)
                        imageName = 'testhost.exe'
                        executablePath = [IO.Path]::GetFullPath($projectTesthostPath)
                    }) }
            }
            Set-TestP5aShimControl $shim @{
                DescendantCall = 2
                DescendantRecords = @($records)
            }
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                & $invokeSelectedVerifier $context $shim
            }
            $failure | Should Not BeNullOrEmpty
            $failure | Should Match $treeCase.Diagnostic
            @(Get-TestP5aShimRecords $shim).Count | Should Be 2
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
            Test-Path -LiteralPath $context.Staging | Should Be $false
        }
    }

    It 'rejects zero skipped failed malformed missing or extra TRX and suppresses verifier success' {
        Assert-TestP5aCommandCapability 'Assert-P5aTrx' verifier
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierProcessProtocol' verifier
        foreach ($mode in @('zero', 'partial', 'skipped', 'failed', 'error', 'notExecuted', 'malformed', 'missing'))
        {
            $context = New-TestP5aProcessContext "verifier-trx-$mode"
            $shim = New-TestP5aProcessShim "verifier-trx-$mode-shim"
            Set-TestP5aShimControl $shim @{ TrxMode = $mode }
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                Invoke-TestP5aVerifierProtocol $context $shim
            }
            $failure | Should Not BeNullOrEmpty
            @(Get-TestP5aShimRecords $shim).Count | Should Be 2
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
            Test-Path -LiteralPath $context.Staging | Should Be $false
        }

        $context = New-TestP5aProcessContext 'verifier-trx-extra'
        $shim = New-TestP5aProcessShim 'verifier-trx-extra-shim'
        Set-TestP5aShimControl $shim @{ ExtraTrx = $true }
        $output = [Collections.Generic.List[object]]::new()
        $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
            Invoke-TestP5aVerifierProtocol $context $shim
        }
        $failure | Should Not BeNullOrEmpty
        @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
        Test-Path -LiteralPath $context.Staging | Should Be $false

    }

    It 'stops verifier on Oracle or dotnet process failures with no retry extra child or leaked child marker' {
        Assert-TestP5aCommandCapability 'Invoke-P5aVerifierProcessProtocol' verifier
        foreach ($call in @(1, 2))
        {
            foreach ($kind in @('nonzero', 'timeout', 'warning', 'error', 'missing', 'duplicate', 'prefix', 'suffix', 'wrong'))
            {
                if ($call -eq 2 -and $kind -in @('missing', 'duplicate', 'prefix', 'suffix', 'wrong'))
                {
                    continue
                }
                $context = New-TestP5aProcessContext "verifier-gate-$call-$kind"
                $shim = New-TestP5aProcessShim "verifier-gate-$call-$kind-shim"
                Set-TestP5aShimControl $shim @{ FailCall = $call; FailureKind = $kind }
                $output = [Collections.Generic.List[object]]::new()
                $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                    Invoke-TestP5aVerifierProtocol $context $shim
                }
                $failure | Should Not BeNullOrEmpty
                $failure | Should Match (Get-TestP5aProcessFailureDiagnosticPattern $kind)
                @(Get-TestP5aShimRecords $shim).Count | Should Be $call
                @($output | Where-Object {
                    [string]$_ -match 'P5A_(ORACLE|GOLDEN)_'
                }).Count | Should Be 0
                Test-Path -LiteralPath $context.Staging | Should Be $false
            }
        }
    }

    It 'rejects generator staging that is preexisting repository-owned nonempty destination-overlapping or reparse-backed without touching sentinels' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorWorkflow' generator
        foreach ($caseName in @('preexisting-empty', 'repository', 'nonempty', 'destination-in-staging', 'reparse'))
        {
            $context = New-TestP5aProcessContext "generator-exclusive-staging-$caseName"
            $outsideSentinel = Join-Path $TestDrive "generator-exclusive-staging-$caseName-outside.txt"
            Write-TestP5aText $outsideSentinel "outside sentinel $caseName`n"
            $outsideHash = Get-TestP5aFileHash $outsideSentinel
            $fixtureHash = Get-TestP5aFileHash $context.Fixture
            $staging = $context.Staging
            $destination = $context.Fixture
            $caseSentinel = $outsideSentinel

            switch ($caseName)
            {
                'preexisting-empty' {
                    [void][IO.Directory]::CreateDirectory($context.Staging)
                }
                'repository' {
                    $staging = $context.Root
                    $caseSentinel = Join-Path $context.Root 'repository-sentinel.txt'
                    Write-TestP5aText $caseSentinel "repository sentinel`n"
                }
                'nonempty' {
                    $caseSentinel = Join-Path $context.Staging 'nonempty-sentinel.txt'
                    Write-TestP5aText $caseSentinel "nonempty sentinel`n"
                }
                'destination-in-staging' {
                    $destination = Join-Path $context.Staging 'published\trace_p5a.json'
                }
                'reparse' {
                    $reparseTarget = Join-Path $TestDrive "generator-exclusive-staging-$caseName-target"
                    $caseSentinel = Join-Path $reparseTarget 'target-sentinel.txt'
                    Write-TestP5aText $caseSentinel "reparse target sentinel`n"
                    $staging = Join-Path $TestDrive "generator-exclusive-staging-$caseName-link"
                    try
                    {
                        New-Item -ItemType Junction -Path $staging -Value $reparseTarget -ErrorAction Stop | Out-Null
                    }
                    catch
                    {
                        "Missing 13B capability: TestDrive junction ($($_.Exception.Message))" |
                            Should BeNullOrEmpty
                    }
                    ((Get-Item -LiteralPath $staging -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) |
                        Should Not Be 0
                }
            }

            $caseHash = Get-TestP5aFileHash $caseSentinel
            $calls = [Collections.Generic.List[int]]::new()
            $capturedCalls = $calls
            $safeInvoker = {
                param($FilePath, $Arguments, $WorkingDirectory, $TimeoutSeconds, $PhaseName)

                $capturedCalls.Add(1)
                throw 'Unexpected generator child reached an invalid staging directory.'
            }.GetNewClosure()
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                Invoke-P5aGeneratorWorkflow `
                    -RepositoryRoot $context.Root `
                    -UnrealEditorCmd $context.Editor `
                    -UnrealProject $context.UProject `
                    -ReferenceRoot $context.Reference `
                    -StagingRoot $staging `
                    -DestinationPath $destination `
                    -ProcessInvoker $safeInvoker `
                    -TimeoutSeconds 10
            }

            $failure | Should Match '(?i)(staging|repository|destination|reparse)'
            $calls.Count | Should Be 0
            Test-Path -LiteralPath $outsideSentinel -PathType Leaf | Should Be $true
            Test-Path -LiteralPath $caseSentinel -PathType Leaf | Should Be $true
            Test-Path -LiteralPath $context.Fixture -PathType Leaf | Should Be $true
            (Get-TestP5aFileHash $outsideSentinel) | Should Be $outsideHash
            (Get-TestP5aFileHash $caseSentinel) | Should Be $caseHash
            (Get-TestP5aFileHash $context.Fixture) | Should Be $fixtureHash
            if ($caseName -ceq 'preexisting-empty')
            {
                Test-Path -LiteralPath $context.Staging -PathType Container | Should Be $true
                @(Get-ChildItem -LiteralPath $context.Staging -Force).Count | Should Be 0
            }
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
        }
    }

    It 'rejects native-a mutation while its retained lease spans both publication checkpoints' {
        Assert-TestP5aCommandCapability 'Invoke-P5aGeneratorWorkflow' generator
        foreach ($checkpoint in @('ChildrenCompleted', 'BeforePublication'))
        {
            $context = New-TestP5aProcessContext `
                "generator-retained-native-mutation-$checkpoint"
            $shim = New-TestP5aProcessShim `
                "generator-retained-native-mutation-$checkpoint-shim"
            $fileSystem = New-TestP5aAtomicFileSystem
            $paths = Get-TestP5aExpectedProcessPaths $context
            $checkpoints = New-TestP5aCheckpointInvoker `
                -MutateAt $checkpoint `
                -MutatePath $paths.NativeA
            $before = Get-TestP5aFileHash $context.Fixture
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                & $script:P5aGeneratorPath `
                    -RepositoryRoot $context.Root `
                    -UnrealEditorCmd $context.Editor `
                    -UnrealProject $context.UProject `
                    -ReferenceRoot $context.Reference `
                    -StagingRoot $context.Staging `
                    -DestinationPath $context.Fixture `
                    -ProcessInvoker $shim.Invoker `
                    -FileSystemInvoker $fileSystem.Invoker `
                    -CheckpointInvoker $checkpoints.Invoker `
                    -TimeoutSeconds 10
            }

            $failure | Should Not BeNullOrEmpty
            $failure | Should Match `
                '(?i)(used by another process|sharing|access|locked|lease)'
            @($checkpoints.Records) | Should Contain $checkpoint
            (Get-TestP5aFileHash $context.Fixture) | Should Be $before
            @($fileSystem.Records | Where-Object {
                $_.operation -in @('File.Replace', 'File.Move')
            }).Count | Should Be 0
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count |
                Should Be 0
            Test-Path -LiteralPath $context.Staging | Should Be $false
        }

        $reparseContext = New-TestP5aProcessContext 'generator-final-publication-reparse'
        $reparseShim = New-TestP5aProcessShim 'generator-final-publication-reparse-shim'
        $reparseFileSystem = New-TestP5aAtomicFileSystem
        $reparseTarget = Join-Path $TestDrive 'generator-final-publication-reparse-target'
        $reparseOutsideSentinel = Join-Path $TestDrive 'generator-final-publication-reparse-outside.txt'
        Write-TestP5aText $reparseOutsideSentinel "outside reparse sentinel`n"
        $reparseOutsideHash = Get-TestP5aFileHash $reparseOutsideSentinel
        $reparseBefore = Get-TestP5aFileHash $reparseContext.Fixture
        $capturedStaging = $reparseContext.Staging
        $capturedReparseTarget = $reparseTarget
        $reparseAttempts = [Collections.Generic.List[bool]]::new()
        $capturedReparseAttempts = $reparseAttempts
        $reparseMutation = {
            param($Checkpoint)

            Test-Path -LiteralPath $capturedStaging -PathType Container | Should Be $true
            $capturedReparseAttempts.Add($true)
            Move-Item -LiteralPath $capturedStaging -Destination $capturedReparseTarget -ErrorAction Stop
            try
            {
                New-Item -ItemType Junction -Path $capturedStaging -Value $capturedReparseTarget `
                    -ErrorAction Stop | Out-Null
            }
            catch
            {
                "Missing 13B capability: TestDrive checkpoint junction ($($_.Exception.Message))" |
                    Should BeNullOrEmpty
            }
            ((Get-Item -LiteralPath $capturedStaging -Force).Attributes -band
                [IO.FileAttributes]::ReparsePoint) | Should Not Be 0
        }.GetNewClosure()
        $reparseCheckpoints = New-TestP5aCheckpointInvoker `
            -MutateAt 'BeforePublication' `
            -MutationAction $reparseMutation
        $reparseOutput = [Collections.Generic.List[object]]::new()
        $reparseFailure = Invoke-TestP5aFailureCapture -Output $reparseOutput -Action {
            & $script:P5aGeneratorPath `
                -RepositoryRoot $reparseContext.Root `
                -UnrealEditorCmd $reparseContext.Editor `
                -UnrealProject $reparseContext.UProject `
                -ReferenceRoot $reparseContext.Reference `
                -StagingRoot $reparseContext.Staging `
                -DestinationPath $reparseContext.Fixture `
                -ProcessInvoker $reparseShim.Invoker `
                -FileSystemInvoker $reparseFileSystem.Invoker `
                -CheckpointInvoker $reparseCheckpoints.Invoker `
                -TimeoutSeconds 10
        }

        $reparseAttempts.Count | Should Be 1
        $reparseFailure | Should Match `
            '(?i)(reparse|staging|directory|manifest|payload|used by another process|sharing|access)'
        @(Get-TestP5aShimRecords $reparseShim).Count | Should Be 7
        (Get-TestP5aFileHash $reparseContext.Fixture) | Should Be $reparseBefore
        (Get-TestP5aFileHash $reparseOutsideSentinel) | Should Be $reparseOutsideHash
        Test-Path -LiteralPath $reparseOutsideSentinel -PathType Leaf | Should Be $true
        if (Test-Path -LiteralPath $reparseTarget -PathType Container)
        {
            Test-Path -LiteralPath (Join-Path $reparseTarget 'native-a.json') -PathType Leaf |
                Should Be $true
        }
        @($reparseFileSystem.Records | Where-Object {
            $_.operation -in @('File.Replace', 'File.Move')
        }).Count | Should Be 0
        @($reparseOutput | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
    }

    It 'rejects a valid Oracle marker when any prefixed P5A marker is also emitted' {
        Assert-TestP5aCommandCapability 'Assert-P5aVerifierOracleChild' verifier
        $marker = 'P5A_ORACLE_DIGESTS layout=d6fef54173240d32 bindings=2b4be600d531c734 graph=44403c2869d8f615 plan=' + ('a' * 64)
        foreach ($streams in @(
            @{ StdOut = @($marker, "prefix $marker"); StdErr = @() }
            @{ StdOut = @($marker); StdErr = @($marker) }
        ))
        {
            $result = [pscustomobject]@{
                TimedOut = $false
                ExitCode = 0
                StdOutLines = @($streams.StdOut)
                StdErrLines = @($streams.StdErr)
                DescendantProcesses = @()
            }

            Test-TestP5aRejects {
                Assert-P5aVerifierOracleChild -Result $result
            } | Should Be $true
        }
    }

    It 'rejects a same-named executable inside the SDK that is not in the selected closure' {
        Assert-TestP5aCommandCapability 'Assert-P5aVerifierDescendantClosure' verifier
        $sdkVersion = '8.0.100'
        $sdkRoot = Join-Path $TestDrive "synthetic-dotnet\sdk\$sdkVersion"
        $expectedPath = Join-Path $sdkRoot 'MSBuild.exe'
        $foreignPath = Join-Path $sdkRoot 'shadow\MSBuild.exe'
        Write-TestP5aText $expectedPath "expected synthetic MSBuild image`n"
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $foreignPath))
        Write-TestP5aText $foreignPath "foreign synthetic MSBuild image`n"
        $expectedPath = [IO.Path]::GetFullPath($expectedPath)
        $foreignPath = [IO.Path]::GetFullPath($foreignPath)
        Assert-TestP5aPathUnderRoot -Path $foreignPath -Root $sdkRoot
        $pathsByImage = @{ 'MSBuild.exe' = [string[]]@($expectedPath) }
        @($pathsByImage['MSBuild.exe'] | Where-Object { $_ -ceq $foreignPath }).Count |
            Should Be 0
        $closure = [pscustomobject]@{
            SdkVersion = $sdkVersion
            PathsByImage = $pathsByImage
        }
        $result = [pscustomobject]@{
            ProcessId = 5002
            DescendantProcesses = @([pscustomobject]@{
                processId = 12001
                parentProcessId = 5002
                ancestorProcessIds = @(5002)
                imageName = 'MSBuild.exe'
                executablePath = $foreignPath
            })
        }

        Test-TestP5aRejects {
            Assert-P5aVerifierDescendantClosure -Result $result -Closure $closure
        } | Should Be $true
    }

    It 'accepts the frozen abbreviated entrypoints or fails closed only on an audited external prerequisite' {
        foreach ($kind in @('generator', 'verifier'))
        {
            $context = New-TestP5aProcessContext "frozen-default-entrypoint-$kind"
            $scriptRoot = Join-Path $context.Root 'scripts'
            [void][IO.Directory]::CreateDirectory($scriptRoot)
            $entrypointName = if ($kind -ceq 'generator') {
                'generate-p5a-golden.ps1'
            } else {
                'verify-p5a-golden.ps1'
            }
            $entrypoint = Join-Path $scriptRoot $entrypointName
            $sourceEntrypoint = if ($kind -ceq 'generator') {
                $script:P5aGeneratorPath
            } else {
                $script:P5aVerifierPath
            }
            [IO.File]::Copy($sourceEntrypoint, $entrypoint, $true)
            Remove-Item -LiteralPath $context.Oracle.AppHostPath -Force
            $powerShell = [Management.Automation.PowerShell]::Create()
            try
            {
                [void]$powerShell.AddCommand($entrypoint)
                if ($kind -ceq 'generator')
                {
                    [void]$powerShell.AddParameter('UnrealEditorCmd', $context.Editor)
                    [void]$powerShell.AddParameter('UnrealProject', $context.UProject)
                    [void]$powerShell.AddParameter('ReferenceRoot', $context.Reference)
                }
                $output = @()
                $invokeFailure = $null
                try { $output = @($powerShell.Invoke()) }
                catch { $invokeFailure = $_.Exception.Message }
                $errors = @($powerShell.Streams.Error | ForEach-Object { $_.ToString() })
                if (-not [string]::IsNullOrEmpty($invokeFailure)) { $errors += $invokeFailure }
                $errors.Count | Should BeGreaterThan 0
                $diagnostic = $errors -join ' | '
                $diagnostic | Should Match '(?i)(apphost|oracle.*evidence|fixed.*release|external.*prerequisite)'
                $diagnostic | Should Not Match '(?i)(mandatory|null|cannot bind.*(argument|empty))'
                @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
            }
            finally
            {
                $powerShell.Dispose()
            }
        }
    }

    It 'rejects nonpositive timeouts and relative UE or reference paths before any child starts' {
        foreach ($kind in @('generator', 'verifier'))
        {
            foreach ($timeout in @(0, -1))
            {
                $context = New-TestP5aProcessContext "nonpositive-timeout-$kind-$timeout"
                $fixtureHash = Get-TestP5aFileHash $context.Fixture
                $calls = [Collections.Generic.List[int]]::new()
                $capturedCalls = $calls
                $safeInvoker = {
                    param($FilePath, $Arguments, $WorkingDirectory, $TimeoutSeconds, $PhaseName)

                    $capturedCalls.Add(1)
                    throw 'Unexpected child reached a nonpositive timeout.'
                }.GetNewClosure()
                $output = [Collections.Generic.List[object]]::new()
                $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                    if ($kind -ceq 'generator')
                    {
                        Invoke-P5aGeneratorWorkflow `
                            -RepositoryRoot $context.Root `
                            -UnrealEditorCmd $context.Editor `
                            -UnrealProject $context.UProject `
                            -ReferenceRoot $context.Reference `
                            -StagingRoot $context.Staging `
                            -DestinationPath $context.Fixture `
                            -ProcessInvoker $safeInvoker `
                            -TimeoutSeconds $timeout
                    }
                    else
                    {
                        Invoke-P5aVerifierWorkflow `
                            -RepositoryRoot $context.Root `
                            -FixturePath $context.Fixture `
                            -StagingRoot $context.Staging `
                            -ProcessInvoker $safeInvoker `
                            -TimeoutSeconds $timeout
                    }
                }

                $failure | Should Match '(?i)timeout.*positive'
                $calls.Count | Should Be 0
                (Get-TestP5aFileHash $context.Fixture) | Should Be $fixtureHash
                @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
            }
        }

        foreach ($pathCase in @(
            @{ Name = 'relative-editor'; UnrealEditorCmd = 'relative-editor.exe'; ReferenceRoot = $null; Diagnostic = '(?i)(unreal|editor).*absolute' }
            @{ Name = 'relative-reference'; UnrealEditorCmd = $null; ReferenceRoot = 'relative-reference'; Diagnostic = '(?i)reference.*absolute' }
        ))
        {
            $context = New-TestP5aProcessContext "generator-absolute-$($pathCase.Name)"
            $calls = [Collections.Generic.List[int]]::new()
            $capturedCalls = $calls
            $safeInvoker = {
                param($FilePath, $Arguments, $WorkingDirectory, $TimeoutSeconds, $PhaseName)

                $capturedCalls.Add(1)
                throw 'Unexpected child reached a relative generator path.'
            }.GetNewClosure()
            $output = [Collections.Generic.List[object]]::new()
            $failure = Invoke-TestP5aFailureCapture -Output $output -Action {
                Invoke-P5aGeneratorProcessProtocol `
                    -RepositoryRoot $context.Root `
                    -OracleAppHost $context.Oracle.AppHostPath `
                    -UnrealEditorCmd $(if ($null -eq $pathCase.UnrealEditorCmd) { $context.Editor } else { $pathCase.UnrealEditorCmd }) `
                    -UnrealProject $context.UProject `
                    -ReferenceRoot $(if ($null -eq $pathCase.ReferenceRoot) { $context.Reference } else { $pathCase.ReferenceRoot }) `
                    -StagingRoot $context.Staging `
                    -ProcessInvoker $safeInvoker `
                    -TimeoutSeconds 10
            }

            $failure | Should Match $pathCase.Diagnostic
            $calls.Count | Should Be 0
            @($output | Where-Object { [string]$_ -match '^P5A_GOLDEN_' }).Count | Should Be 0
        }
    }
}
