# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,

    [Parameter(Mandatory = $true)]
    [string]$ApplicationVersion
)

$ErrorActionPreference = 'Stop'
$projectPath = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$generator = Join-Path $projectPath 'tools\Prepare-BuildIdentity.ps1'
$runId = [Guid]::NewGuid().ToString('N')
$projectOutputRoot = Join-Path $projectPath ('build\generated\identity-tests-' + $runId)
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('TSG-IdentityTests-' + $runId)

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Write-Utf8NoBom([string]$Path, [string]$Value) {
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Value, (New-Object Text.UTF8Encoding($false)))
}

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha256.ComputeHash($Bytes)
    }
    finally {
        $sha256.Dispose()
    }
    return ([BitConverter]::ToString($digest)).Replace('-', '').ToLowerInvariant()
}

function Get-OutputText([string]$Directory) {
    return @(
        Get-Content -Raw -LiteralPath (Join-Path $Directory 'BuildIdentity.Generated.cs')
        Get-Content -Raw -LiteralPath (Join-Path $Directory 'build-identity.json')
        Get-Content -Raw -LiteralPath (Join-Path $Directory 'build-inputs.manifest')
    ) -join "`n"
}

function Invoke-Generator(
    [string]$Root,
    [string]$Output,
    [string]$BuildChannel = 'development') {
    return & $generator `
        -ProjectRoot $Root `
        -OutputDirectory $Output `
        -ApplicationVersion $ApplicationVersion `
        -Channel $BuildChannel
}

try {
    New-Item -ItemType Directory -Force -Path $projectOutputRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

    $buildTokens = $null
    $buildParseErrors = $null
    $buildAst = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $projectPath 'build.ps1'),
        [ref]$buildTokens,
        [ref]$buildParseErrors)
    Assert ($buildParseErrors.Count -eq 0) 'build.ps1 did not parse for snapshot testing.'
    $buildScriptText = Get-Content -Raw -LiteralPath (Join-Path $projectPath 'build.ps1')
    $snapshotValidationCalls = [regex]::Matches(
        $buildScriptText,
        '(?m)^\s*Assert-VerifiedCompileSnapshotOrDeleteOutput \$compileSnapshot \$appOutput\s*$')
    $appCompilerCallIndex = $buildScriptText.IndexOf('& $compiler $appArguments')
    Assert ($snapshotValidationCalls.Count -eq 2 -and $appCompilerCallIndex -ge 0) `
        'The app compiler was not guarded by exactly two snapshot validation calls.'
    Assert ($snapshotValidationCalls[0].Index -lt $appCompilerCallIndex -and
        $snapshotValidationCalls[1].Index -gt $appCompilerCallIndex) `
        'The complete snapshot was not validated immediately before and after app compilation.'
    $requiredBuildFunctions = @(
        'Get-BuildSha256HexFromBytes',
        'Initialize-BuildReparseInspector',
        'Test-IsUnsafeBuildReparsePoint',
        'Assert-NoUnsafeBuildReparsePoints',
        'Test-IsSafeBuildRelativePath',
        'Read-VerifiedBuildFileBytes',
        'Write-VerifiedCompileSnapshotFile',
        'New-VerifiedCompileSnapshot',
        'Assert-VerifiedCompileSnapshot',
        'Remove-FailedAppOutput',
        'Assert-VerifiedCompileSnapshotOrDeleteOutput')
    foreach ($functionName in $requiredBuildFunctions) {
        $functionAst = $buildAst.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -eq $functionName
        }, $true)
        Assert ($null -ne $functionAst) ('Required build function was not found: ' + $functionName)
        . ([scriptblock]::Create($functionAst.Extent.Text))
    }

    # This path is a OneDrive Cloud Files placeholder in the normal workspace.
    # Cloud placeholders must remain usable while redirecting reparse points fail closed.
    Assert-NoUnsafeBuildReparsePoints $projectPath
    Assert-NoUnsafeBuildReparsePoints (Join-Path $projectPath 'src\Program.cs')
    $projectItem = Get-Item -Force -LiteralPath $projectPath
    if (($projectItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Assert (-not (Test-IsUnsafeBuildReparsePoint $projectItem)) `
            'The real OneDrive cloud-placeholder project root was rejected.'
    }

    $snapshotFixture = Join-Path $temporaryRoot 'compile-snapshot-fixture'
    $snapshotSource = Join-Path $snapshotFixture 'src\App.cs'
    $snapshotOriginal = 'internal static class SnapshotApp { }'
    Write-Utf8NoBom $snapshotSource $snapshotOriginal
    $snapshotBytes = [IO.File]::ReadAllBytes($snapshotSource)
    $snapshotIdentityRoot = Join-Path $snapshotFixture 'build\generated\build-identity'
    $snapshotManifest = Join-Path $snapshotIdentityRoot 'build-inputs.manifest'
    $snapshotManifestText = ((@(
        'tsg-build-inputs-v1',
        ((Get-Sha256Hex $snapshotBytes) + "`t" + $snapshotBytes.Length + "`tsrc/App.cs")
    ) -join "`n") + "`n")
    Write-Utf8NoBom $snapshotManifest $snapshotManifestText
    $snapshotManifestBytes = [IO.File]::ReadAllBytes($snapshotManifest)
    $snapshotManifestHash = Get-Sha256Hex $snapshotManifestBytes

    $snapshotIdentityManifest = Join-Path $snapshotIdentityRoot 'build-identity.json'
    Write-Utf8NoBom $snapshotIdentityManifest '{"schemaVersion":1}'
    $snapshotIdentityManifestBytes = [IO.File]::ReadAllBytes($snapshotIdentityManifest)
    $snapshotIdentityManifestHash = Get-Sha256Hex $snapshotIdentityManifestBytes
    $snapshotIdentitySource = Join-Path $snapshotIdentityRoot 'BuildIdentity.Generated.cs'
    Write-Utf8NoBom $snapshotIdentitySource 'namespace SnapshotIdentity { }'
    $snapshotIdentitySourceBytes = [IO.File]::ReadAllBytes($snapshotIdentitySource)
    $snapshotIdentitySourceHash = Get-Sha256Hex $snapshotIdentitySourceBytes

    $verifiedSnapshotPath = Join-Path $snapshotFixture 'build\generated\verified-compile-snapshot'
    $verifiedSnapshot = New-VerifiedCompileSnapshot `
        -ProjectRoot $snapshotFixture `
        -InputManifest $snapshotManifest `
        -ExpectedInputManifestSha256 $snapshotManifestHash `
        -GeneratedIdentityManifest $snapshotIdentityManifest `
        -ExpectedGeneratedIdentityManifestSha256 $snapshotIdentityManifestHash `
        -ExpectedGeneratedIdentityManifestLength $snapshotIdentityManifestBytes.LongLength `
        -GeneratedIdentitySource $snapshotIdentitySource `
        -ExpectedGeneratedIdentitySourceSha256 $snapshotIdentitySourceHash `
        -ExpectedGeneratedIdentitySourceLength $snapshotIdentitySourceBytes.LongLength `
        -SnapshotRoot $verifiedSnapshotPath
    Assert-VerifiedCompileSnapshot $verifiedSnapshot

    Write-Utf8NoBom $snapshotSource 'changed after identity generation'
    Assert-VerifiedCompileSnapshot $verifiedSnapshot
    Assert ((Get-Content -Raw -LiteralPath (
        Join-Path $verifiedSnapshotPath 'src\App.cs')) -ceq $snapshotOriginal) `
        'The compiler snapshot changed after the live source was modified.'
    $changedSnapshotRejected = $false
    try {
        New-VerifiedCompileSnapshot `
            -ProjectRoot $snapshotFixture `
            -InputManifest $snapshotManifest `
            -ExpectedInputManifestSha256 $snapshotManifestHash `
            -GeneratedIdentityManifest $snapshotIdentityManifest `
            -ExpectedGeneratedIdentityManifestSha256 $snapshotIdentityManifestHash `
            -ExpectedGeneratedIdentityManifestLength $snapshotIdentityManifestBytes.LongLength `
            -GeneratedIdentitySource $snapshotIdentitySource `
            -ExpectedGeneratedIdentitySourceSha256 $snapshotIdentitySourceHash `
            -ExpectedGeneratedIdentitySourceLength $snapshotIdentitySourceBytes.LongLength `
            -SnapshotRoot (Join-Path $snapshotFixture 'build\generated\rejected-source') | Out-Null
    }
    catch {
        $changedSnapshotRejected = $true
    }
    Assert $changedSnapshotRejected `
        'A source changed after identity generation entered the compiler snapshot.'

    $changedSourceBytes = [IO.File]::ReadAllBytes($snapshotSource)
    $selfConsistentManifestText = ((@(
        'tsg-build-inputs-v1',
        ((Get-Sha256Hex $changedSourceBytes) + "`t" + $changedSourceBytes.Length + "`tsrc/App.cs")
    ) -join "`n") + "`n")
    Write-Utf8NoBom $snapshotManifest $selfConsistentManifestText
    $rewrittenManifestRejected = $false
    try {
        New-VerifiedCompileSnapshot `
            -ProjectRoot $snapshotFixture `
            -InputManifest $snapshotManifest `
            -ExpectedInputManifestSha256 $snapshotManifestHash `
            -GeneratedIdentityManifest $snapshotIdentityManifest `
            -ExpectedGeneratedIdentityManifestSha256 $snapshotIdentityManifestHash `
            -ExpectedGeneratedIdentityManifestLength $snapshotIdentityManifestBytes.LongLength `
            -GeneratedIdentitySource $snapshotIdentitySource `
            -ExpectedGeneratedIdentitySourceSha256 $snapshotIdentitySourceHash `
            -ExpectedGeneratedIdentitySourceLength $snapshotIdentitySourceBytes.LongLength `
            -SnapshotRoot (Join-Path $snapshotFixture 'build\generated\rejected-manifest') | Out-Null
    }
    catch {
        $rewrittenManifestRejected = $true
    }
    Assert $rewrittenManifestRejected `
        'A self-consistent replacement manifest bypassed the expected manifest SHA-256.'
    [IO.File]::WriteAllBytes($snapshotManifest, $snapshotManifestBytes)
    [IO.File]::WriteAllBytes($snapshotSource, $snapshotBytes)

    $tamperedIdentityManifestBytes = [byte[]]$snapshotIdentityManifestBytes.Clone()
    $tamperedIdentityManifestBytes[0] = $tamperedIdentityManifestBytes[0] -bxor 1
    [IO.File]::WriteAllBytes($snapshotIdentityManifest, $tamperedIdentityManifestBytes)
    $identityManifestRejected = $false
    try {
        New-VerifiedCompileSnapshot `
            -ProjectRoot $snapshotFixture `
            -InputManifest $snapshotManifest `
            -ExpectedInputManifestSha256 $snapshotManifestHash `
            -GeneratedIdentityManifest $snapshotIdentityManifest `
            -ExpectedGeneratedIdentityManifestSha256 $snapshotIdentityManifestHash `
            -ExpectedGeneratedIdentityManifestLength $snapshotIdentityManifestBytes.LongLength `
            -GeneratedIdentitySource $snapshotIdentitySource `
            -ExpectedGeneratedIdentitySourceSha256 $snapshotIdentitySourceHash `
            -ExpectedGeneratedIdentitySourceLength $snapshotIdentitySourceBytes.LongLength `
            -SnapshotRoot (Join-Path $snapshotFixture 'build\generated\rejected-json') | Out-Null
    }
    catch {
        $identityManifestRejected = $true
    }
    Assert $identityManifestRejected 'Same-length generated identity JSON tampering was accepted.'
    [IO.File]::WriteAllBytes($snapshotIdentityManifest, $snapshotIdentityManifestBytes)

    $tamperedIdentitySourceBytes = [byte[]]$snapshotIdentitySourceBytes.Clone()
    $tamperedIdentitySourceBytes[0] = $tamperedIdentitySourceBytes[0] -bxor 1
    [IO.File]::WriteAllBytes($snapshotIdentitySource, $tamperedIdentitySourceBytes)
    $identitySourceRejected = $false
    try {
        New-VerifiedCompileSnapshot `
            -ProjectRoot $snapshotFixture `
            -InputManifest $snapshotManifest `
            -ExpectedInputManifestSha256 $snapshotManifestHash `
            -GeneratedIdentityManifest $snapshotIdentityManifest `
            -ExpectedGeneratedIdentityManifestSha256 $snapshotIdentityManifestHash `
            -ExpectedGeneratedIdentityManifestLength $snapshotIdentityManifestBytes.LongLength `
            -GeneratedIdentitySource $snapshotIdentitySource `
            -ExpectedGeneratedIdentitySourceSha256 $snapshotIdentitySourceHash `
            -ExpectedGeneratedIdentitySourceLength $snapshotIdentitySourceBytes.LongLength `
            -SnapshotRoot (Join-Path $snapshotFixture 'build\generated\rejected-source-identity') | Out-Null
    }
    catch {
        $identitySourceRejected = $true
    }
    Assert $identitySourceRejected 'Same-length generated identity C# tampering was accepted.'
    [IO.File]::WriteAllBytes($snapshotIdentitySource, $snapshotIdentitySourceBytes)

    $fakeAppOutput = Join-Path $snapshotFixture 'dist\TarkovServerGuard.exe'
    Write-Utf8NoBom $fakeAppOutput 'partial compiler output'
    Write-Utf8NoBom (Join-Path $verifiedSnapshotPath 'src\App.cs') 'snapshot mutated after pre-check'
    $postCompileMutationRejected = $false
    try {
        Assert-VerifiedCompileSnapshotOrDeleteOutput $verifiedSnapshot $fakeAppOutput
    }
    catch {
        $postCompileMutationRejected = $true
    }
    Assert $postCompileMutationRejected 'A post-compile snapshot mutation was accepted.'
    Assert (-not (Test-Path -LiteralPath $fakeAppOutput)) `
        'A failed post-compile snapshot check left the app output behind.'
    [IO.File]::WriteAllBytes((Join-Path $verifiedSnapshotPath 'src\App.cs'), $snapshotBytes)
    Assert-VerifiedCompileSnapshot $verifiedSnapshot

    Write-Utf8NoBom (Join-Path $verifiedSnapshotPath 'src\Unexpected.cs') 'unexpected'
    $unexpectedSnapshotFileRejected = $false
    try {
        Assert-VerifiedCompileSnapshot $verifiedSnapshot
    }
    catch {
        $unexpectedSnapshotFileRejected = $true
    }
    Assert $unexpectedSnapshotFileRejected 'An unexpected compile snapshot file was accepted.'
    [IO.File]::Delete((Join-Path $verifiedSnapshotPath 'src\Unexpected.cs'))

    $junctionOutside = Join-Path $temporaryRoot 'junction-source-outside'
    Write-Utf8NoBom (Join-Path $junctionOutside 'App.cs') $snapshotOriginal
    $junctionProject = Join-Path $temporaryRoot 'junction-source-project'
    New-Item -ItemType Directory -Force -Path $junctionProject | Out-Null
    $sourceJunction = Join-Path $junctionProject 'src'
    $sourceJunctionCreated = $false
    try {
        New-Item -ItemType Junction -Path $sourceJunction -Target $junctionOutside | Out-Null
        $sourceJunctionCreated = $true
        $junctionIdentityRoot = Join-Path $junctionProject 'build\generated\build-identity'
        Write-Utf8NoBom (Join-Path $junctionIdentityRoot 'build-inputs.manifest') $snapshotManifestText
        Write-Utf8NoBom (Join-Path $junctionIdentityRoot 'build-identity.json') '{"schemaVersion":1}'
        Write-Utf8NoBom (Join-Path $junctionIdentityRoot 'BuildIdentity.Generated.cs') `
            'namespace SnapshotIdentity { }'
        $junctionRejected = $false
        try {
            New-VerifiedCompileSnapshot `
                -ProjectRoot $junctionProject `
                -InputManifest (Join-Path $junctionIdentityRoot 'build-inputs.manifest') `
                -ExpectedInputManifestSha256 $snapshotManifestHash `
                -GeneratedIdentityManifest (Join-Path $junctionIdentityRoot 'build-identity.json') `
                -ExpectedGeneratedIdentityManifestSha256 $snapshotIdentityManifestHash `
                -ExpectedGeneratedIdentityManifestLength $snapshotIdentityManifestBytes.LongLength `
                -GeneratedIdentitySource (Join-Path $junctionIdentityRoot 'BuildIdentity.Generated.cs') `
                -ExpectedGeneratedIdentitySourceSha256 $snapshotIdentitySourceHash `
                -ExpectedGeneratedIdentitySourceLength $snapshotIdentitySourceBytes.LongLength `
                -SnapshotRoot (Join-Path $junctionProject 'build\generated\snapshot') | Out-Null
        }
        catch {
            $junctionRejected = $true
        }
        Assert $junctionRejected 'A source-directory junction entered the compile snapshot.'
    }
    finally {
        if ($sourceJunctionCreated -and (Test-Path -LiteralPath $sourceJunction)) {
            [IO.Directory]::Delete($sourceJunction, $false)
        }
    }

    $snapshotJunctionProject = Join-Path $temporaryRoot 'junction-snapshot-project'
    Write-Utf8NoBom (Join-Path $snapshotJunctionProject 'src\App.cs') $snapshotOriginal
    New-Item -ItemType Directory -Force -Path (Join-Path $snapshotJunctionProject 'build') | Out-Null
    $snapshotJunctionOutside = Join-Path $temporaryRoot 'junction-snapshot-outside'
    New-Item -ItemType Directory -Force -Path $snapshotJunctionOutside | Out-Null
    $snapshotParentJunction = Join-Path $snapshotJunctionProject 'build\generated'
    $snapshotParentJunctionCreated = $false
    try {
        New-Item -ItemType Junction -Path $snapshotParentJunction -Target $snapshotJunctionOutside | Out-Null
        $snapshotParentJunctionCreated = $true
        $junctionIdentityRoot = Join-Path $snapshotParentJunction 'build-identity'
        Write-Utf8NoBom (Join-Path $junctionIdentityRoot 'build-inputs.manifest') $snapshotManifestText
        Write-Utf8NoBom (Join-Path $junctionIdentityRoot 'build-identity.json') '{"schemaVersion":1}'
        Write-Utf8NoBom (Join-Path $junctionIdentityRoot 'BuildIdentity.Generated.cs') `
            'namespace SnapshotIdentity { }'
        $snapshotParentJunctionRejected = $false
        try {
            New-VerifiedCompileSnapshot `
                -ProjectRoot $snapshotJunctionProject `
                -InputManifest (Join-Path $junctionIdentityRoot 'build-inputs.manifest') `
                -ExpectedInputManifestSha256 $snapshotManifestHash `
                -GeneratedIdentityManifest (Join-Path $junctionIdentityRoot 'build-identity.json') `
                -ExpectedGeneratedIdentityManifestSha256 $snapshotIdentityManifestHash `
                -ExpectedGeneratedIdentityManifestLength $snapshotIdentityManifestBytes.LongLength `
                -GeneratedIdentitySource (Join-Path $junctionIdentityRoot 'BuildIdentity.Generated.cs') `
                -ExpectedGeneratedIdentitySourceSha256 $snapshotIdentitySourceHash `
                -ExpectedGeneratedIdentitySourceLength $snapshotIdentitySourceBytes.LongLength `
                -SnapshotRoot (Join-Path $snapshotParentJunction 'snapshot') | Out-Null
        }
        catch {
            $snapshotParentJunctionRejected = $true
        }
        Assert $snapshotParentJunctionRejected `
            'A junction-backed snapshot destination was accepted.'
    }
    finally {
        if ($snapshotParentJunctionCreated -and (Test-Path -LiteralPath $snapshotParentJunction)) {
            [IO.Directory]::Delete($snapshotParentJunction, $false)
        }
    }

    $developmentOne = Join-Path $projectOutputRoot 'development-one'
    $developmentTwo = Join-Path $projectOutputRoot 'development-two'
    $first = Invoke-Generator $projectPath $developmentOne
    $second = Invoke-Generator $projectPath $developmentTwo

    Assert ($first.Channel -eq 'development') 'Default channel was not development.'
    Assert ($first.BinaryBuildId -eq $second.BinaryBuildId) 'Binary Build ID was not deterministic.'
    Assert ((Get-OutputText $developmentOne) -ceq (Get-OutputText $developmentTwo)) `
        'Public identity outputs were not byte-for-byte deterministic.'

    $expectedRevision = (& git -C $projectPath rev-parse --verify HEAD).Trim().ToLowerInvariant()
    Assert ($LASTEXITCODE -eq 0) 'Could not read the exact project Git revision.'
    Assert ($first.SourceRevision -ceq $expectedRevision) `
        'The generator did not use the exact Git revision for the project repository.'
    Assert ($first.BinaryBuildId -match '^tsg-bin-v1-[0-9a-f]{64}$') `
        'Binary Build ID did not use the documented public format.'
    $canonicalMaterial = @(
        'schema=tsg-binary-build-v1',
        ('version=' + $ApplicationVersion),
        ('revision=' + $first.SourceRevision),
        ('channel=' + $first.Channel),
        ('buildInputsSha256=' + $first.BuildInputManifestSha256)
    ) -join "`n"
    $canonicalId = 'tsg-bin-v1-' + (
        Get-Sha256Hex ([Text.Encoding]::UTF8.GetBytes($canonicalMaterial)))
    Assert ($first.BinaryBuildId -ceq $canonicalId) `
        'Binary Build ID did not match the documented canonical material.'

    $inputManifestBytes = [IO.File]::ReadAllBytes($first.InputManifestPath)
    Assert ((Get-Sha256Hex $inputManifestBytes) -ceq $first.BuildInputManifestSha256) `
        'The canonical build-input manifest hash did not match the emitted file bytes.'
    $identityManifestBytes = [IO.File]::ReadAllBytes($first.ManifestPath)
    Assert ([long]$identityManifestBytes.LongLength -eq [long]$first.BuildIdentityManifestLength) `
        'The returned build identity JSON length did not match the emitted exact bytes.'
    Assert ((Get-Sha256Hex $identityManifestBytes) -ceq $first.BuildIdentityManifestSha256) `
        'The returned build identity JSON SHA-256 did not match the emitted exact bytes.'
    $identitySourceBytes = [IO.File]::ReadAllBytes($first.SourcePath)
    Assert ([long]$identitySourceBytes.LongLength -eq [long]$first.BuildIdentitySourceLength) `
        'The returned generated identity source length did not match the emitted exact bytes.'
    Assert ((Get-Sha256Hex $identitySourceBytes) -ceq $first.BuildIdentitySourceSha256) `
        'The returned generated identity source SHA-256 did not match the emitted exact bytes.'
    $actualProjectSnapshot = New-VerifiedCompileSnapshot `
        -ProjectRoot $projectPath `
        -InputManifest $first.InputManifestPath `
        -ExpectedInputManifestSha256 $first.BuildInputManifestSha256 `
        -GeneratedIdentityManifest $first.ManifestPath `
        -ExpectedGeneratedIdentityManifestSha256 $first.BuildIdentityManifestSha256 `
        -ExpectedGeneratedIdentityManifestLength $first.BuildIdentityManifestLength `
        -GeneratedIdentitySource $first.SourcePath `
        -ExpectedGeneratedIdentitySourceSha256 $first.BuildIdentitySourceSha256 `
        -ExpectedGeneratedIdentitySourceLength $first.BuildIdentitySourceLength `
        -SnapshotRoot (Join-Path $projectOutputRoot 'actual-project-compile-snapshot')
    Assert-VerifiedCompileSnapshot $actualProjectSnapshot
    $manifest = Get-Content -Raw -LiteralPath $first.ManifestPath | ConvertFrom-Json
    Assert ($manifest.binaryBuildId -ceq $first.BinaryBuildId) `
        'Embedded JSON data and the returned Binary Build ID diverged.'
    Assert ($manifest.buildInputManifestSha256 -ceq $first.BuildInputManifestSha256) `
        'Embedded JSON data and the canonical input hash diverged.'

    $generatedSource = Get-Content -Raw -LiteralPath $first.SourcePath
    foreach ($publicValue in @(
        $ApplicationVersion,
        $first.SourceRevision,
        $first.Channel,
        $first.BuildInputManifestSha256,
        $first.BinaryBuildId)) {
        Assert ($generatedSource.Contains($publicValue)) `
            'A public component was not present as plain readable assembly metadata.'
    }
    Assert ($generatedSource.Contains('System.Reflection.AssemblyMetadata')) `
        'Plain assembly metadata was not generated.'
    Assert (-not ($generatedSource -match 'new\s+byte\s*\[\]')) `
        'Generated metadata unexpectedly used an opaque byte container.'

    $releaseOutput = Join-Path $projectOutputRoot 'release'
    $release = Invoke-Generator $projectPath $releaseOutput 'release'
    Assert ($release.Channel -eq 'release') 'Explicit release channel was not preserved.'
    Assert ($release.BinaryBuildId -ne $first.BinaryBuildId) `
        'Changing the explicit channel did not change the Binary Build ID.'

    $stateRoot = Join-Path $temporaryRoot 'state-only-repository'
    Write-Utf8NoBom (Join-Path $stateRoot '.gitignore') "/build/`n"
    Write-Utf8NoBom (Join-Path $stateRoot 'src\App.cs') 'internal static class App { }'
    Write-Utf8NoBom (Join-Path $stateRoot 'tests\Diagnostic.txt') 'unchanged binary input'
    & git -C $stateRoot init --quiet
    Assert ($LASTEXITCODE -eq 0) 'Could not create the state-only repository fixture.'
    & git -C $stateRoot add .gitignore src tests
    Assert ($LASTEXITCODE -eq 0) 'Could not stage the state-only repository fixture.'
    & git -C $stateRoot `
        -c user.name=TSG-Test `
        -c user.email=tsg-test.invalid@example.invalid `
        commit --quiet -m 'state-only fixture'
    Assert ($LASTEXITCODE -eq 0) 'Could not commit the state-only repository fixture.'
    $cleanStateOutput = Join-Path $stateRoot 'build\generated\clean'
    $cleanState = Invoke-Generator $stateRoot $cleanStateOutput
    Assert ($cleanState.SourceState -eq 'clean') 'Clean fixture was not diagnosed as clean.'
    Write-Utf8NoBom (Join-Path $stateRoot 'tests\Diagnostic.txt') 'changed non-binary input'
    $dirtyStateOutput = Join-Path $stateRoot 'build\generated\dirty'
    $dirtyState = Invoke-Generator $stateRoot $dirtyStateOutput
    Assert ($dirtyState.SourceState -eq 'dirty') 'Changed fixture was not diagnosed as dirty.'
    Assert ($dirtyState.BinaryBuildId -ceq $cleanState.BinaryBuildId) `
        'Worktree state outside binary inputs changed the Binary Build ID.'
    Assert ((Get-OutputText $dirtyStateOutput) -ceq (Get-OutputText $cleanStateOutput)) `
        'Worktree state outside binary inputs changed generated EXE inputs.'

    $archiveRoot = Join-Path $temporaryRoot 'archive-source'
    Write-Utf8NoBom (Join-Path $archiveRoot 'src\App.cs') 'internal static class App { }'
    Write-Utf8NoBom (Join-Path $archiveRoot 'app.config') '<configuration />'
    $archiveOutputOne = Join-Path $archiveRoot 'build\generated\one'
    $archiveOne = Invoke-Generator $archiveRoot $archiveOutputOne
    Assert ($archiveOne.SourceState -eq 'archive') 'Source export was not marked archive.'
    Assert ($archiveOne.SourceRevision -ceq (
        'tree-' + $archiveOne.BuildInputManifestSha256)) `
        'Source export did not use its public canonical tree hash.'

    Write-Utf8NoBom (Join-Path $archiveRoot 'src\App.cs') 'internal static class App { internal const int Changed = 1; }'
    $archiveOutputTwo = Join-Path $archiveRoot 'build\generated\two'
    $archiveTwo = Invoke-Generator $archiveRoot $archiveOutputTwo
    Assert ($archiveTwo.BuildInputManifestSha256 -ne $archiveOne.BuildInputManifestSha256) `
        'Changing a build input did not change the canonical manifest hash.'
    Assert ($archiveTwo.BinaryBuildId -ne $archiveOne.BinaryBuildId) `
        'Changing a build input did not change the Binary Build ID.'

    $parentRepository = Join-Path $temporaryRoot 'parent-repository'
    $nestedArchive = Join-Path $parentRepository 'nested-export'
    Write-Utf8NoBom (Join-Path $nestedArchive 'src\Nested.cs') 'internal static class Nested { }'
    & git -C $parentRepository init --quiet
    Assert ($LASTEXITCODE -eq 0) 'Could not create the parent repository fixture.'
    & git -C $parentRepository `
        -c user.name=TSG-Test `
        -c user.email=tsg-test.invalid@example.invalid `
        commit --quiet --allow-empty -m 'parent revision'
    Assert ($LASTEXITCODE -eq 0) 'Could not commit the parent repository fixture.'
    $parentRevision = (& git -C $parentRepository rev-parse HEAD).Trim().ToLowerInvariant()
    $nested = Invoke-Generator `
        $nestedArchive `
        (Join-Path $nestedArchive 'build\generated\identity')
    Assert ($nested.SourceState -eq 'archive') `
        'A nested export inherited state from an unrelated parent repository.'
    Assert ($nested.SourceRevision -ne $parentRevision) `
        'A nested export inherited an unrelated parent Git revision.'
    Assert ($nested.SourceRevision -match '^tree-[0-9a-f]{64}$') `
        'Archive revision was not a full public tree hash.'

    $escapedOutputRejected = $false
    try {
        Invoke-Generator $archiveRoot (Join-Path $temporaryRoot 'outside-generated') | Out-Null
    }
    catch {
        $escapedOutputRejected = $true
    }
    Assert $escapedOutputRejected 'Generated identity output escaped build/generated.'

    $auditPaths = @(
        'tools\Prepare-BuildIdentity.ps1',
        'tools\New-ReleaseProvenance.ps1',
        'tools\Test-ReleaseBinaryIdentity.ps1',
        'src\BuildIdentity.cs',
        'tests\BuildIdentityAppInspector.cs',
        'tests\BuildIdentityTests.cs',
        'tests\ReleaseProvenanceTests.ps1',
        'build.ps1',
        'package-release.ps1',
        'DEVELOPMENT.md',
        'PRIVACY.md',
        'PUBLICATION_SCOPE.md')
    $legacyMarkers = @(
        ('Private' + 'Identity' + 'File'),
        ('Encoded' + 'Byte'),
        ('Mask' + 'Se' + 'ed'),
        ('s' + 'eed'))
    foreach ($relativePath in $auditPaths) {
        $text = Get-Content -Raw -LiteralPath (Join-Path $projectPath $relativePath)
        foreach ($marker in $legacyMarkers) {
            Assert (-not $text.Contains($marker)) `
                ('Legacy opaque identity marker remained in ' + $relativePath + '.')
        }
    }

    Write-Host 'All public build identity generator tests passed.'
}
finally {
    foreach ($cleanup in @($projectOutputRoot, $temporaryRoot)) {
        if (-not (Test-Path -LiteralPath $cleanup)) { continue }
        $resolved = [IO.Path]::GetFullPath($cleanup)
        $allowedProjectPrefix = [IO.Path]::GetFullPath(
            (Join-Path $projectPath 'build\generated')).TrimEnd(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $allowedTemporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $isGeneratedChild = $resolved.StartsWith(
            $allowedProjectPrefix,
            [StringComparison]::OrdinalIgnoreCase)
        $isTemporaryChild = $resolved.StartsWith(
            $allowedTemporaryPrefix,
            [StringComparison]::OrdinalIgnoreCase)
        if (-not ($isGeneratedChild -or $isTemporaryChild)) {
            throw "Unexpected test cleanup path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
