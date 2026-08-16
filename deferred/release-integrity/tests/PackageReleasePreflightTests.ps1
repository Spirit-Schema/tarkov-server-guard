# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
$sourceProject = [IO.Path]::GetFullPath($ProjectRoot)
$packageScript = Join-Path $sourceProject 'package-release.ps1'
$prepareVelopackScript = Join-Path $sourceProject 'tools\Prepare-Velopack.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'TSG-PackagePreflightTests-' + [Guid]::NewGuid().ToString('N'))
$junctionPath = $null
$resetJunctionPath = $null
$velopackJunctionPath = $null
$velopackNestedJunctionPath = $null
$readinessJunctionPath = $null

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Write-TestFile([string]$Path, [string]$Value) {
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Value, (New-Object Text.UTF8Encoding($false)))
}

function Expect-Rejection([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try {
        & $Action
    }
    catch {
        $rejected = $true
    }
    Assert $rejected $Message
}

function Set-PreflightProject([string]$Path) {
    $script:projectRoot = [IO.Path]::GetFullPath($Path)
    $script:projectRootPrefix = $script:projectRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
}

try {
    New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $packageScript,
        [ref]$tokens,
        [ref]$parseErrors)
    Assert ($parseErrors.Count -eq 0) 'package-release.ps1 did not parse.'
    foreach ($functionName in @(
        'Reset-ProjectChild',
        'Assert-ProjectGitRoot',
        'Get-TrackedReviewSourceFiles',
        'Test-IsFileSystemLink',
         'Remove-ReleaseReadinessFile',
         'Assert-NoFileSystemLinkWithinProject',
         'Assert-TrackedCleanReviewInputs',
         'Copy-TrackedReviewSources',
         'New-ReviewInputSnapshot',
         'Assert-ReviewInputSnapshot',
         'Assert-LiveReviewInputBaseline',
         'Assert-BuildInputManifestMatchesSnapshot',
         'Assert-FileMatchesIntegrity',
         'Copy-VerifiedFileToStage',
         'Get-CurrentFileIntegrity',
         'New-ExactDirectorySnapshot',
         'Assert-PendingCompletionMarker',
         'Move-ReleaseCompletionMarkerAtomically')) {
        $definition = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] `
                -and $node.Name -eq $functionName
        }, $true)
        Assert ($null -ne $definition) ('Preflight function was not found: ' + $functionName)
        . ([scriptblock]::Create($definition.Extent.Text))
    }

    $velopackTokens = $null
    $velopackParseErrors = $null
    $velopackAst = [System.Management.Automation.Language.Parser]::ParseFile(
        $prepareVelopackScript,
        [ref]$velopackTokens,
        [ref]$velopackParseErrors)
    Assert ($velopackParseErrors.Count -eq 0) 'Prepare-Velopack.ps1 did not parse.'
    foreach ($functionName in @(
        'Initialize-VelopackReparseInspector',
        'Test-IsUnsafeCacheReparsePoint',
        'Assert-NoUnsafeCacheReparsePoints',
        'Assert-SafeCacheTreeForRemoval',
        'Assert-ChildPath',
        'Ensure-Extracted')) {
        $definition = $velopackAst.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] `
                -and $node.Name -eq $functionName
        }, $true)
        Assert ($null -ne $definition) ('Velopack safety function was not found: ' + $functionName)
        . ([scriptblock]::Create($definition.Extent.Text))
    }

    $packageText = [IO.File]::ReadAllText($packageScript)
    $prepareVelopackText = [IO.File]::ReadAllText($prepareVelopackScript)
    foreach ($propertyName in @(
        'VelopackDllSha256',
        'VelopackDllLength',
        'NewtonsoftJsonDllSha256',
        'NewtonsoftJsonDllLength',
        'VpkDllSha256',
        'VpkDllLength')) {
        Assert ($prepareVelopackText.Contains($propertyName)) `
            ('Prepare-Velopack did not return frozen-input metadata: ' + $propertyName)
    }
    Assert ($packageText.Contains('$frozenVpkDll,')) `
        'Velopack packaging does not execute the frozen vpk.dll copy.'
    Assert ($packageText.Contains("'--mainExe', 'TarkovServerGuard.exe'")) `
        'Velopack main executable configuration required for Installed Apps removal is missing.'
    Assert (-not $packageText.Contains('Uninstall.exe')) `
        'A duplicate custom uninstaller entered the package instead of using Velopack defaults.'
    $finalMoveCall = $packageText.LastIndexOf(
        'Move-ReleaseCompletionMarkerAtomically', [StringComparison]::Ordinal)
    Assert ($finalMoveCall -ge 0) 'The final completion-marker promotion call was not found.'
    $afterFinalMove = $packageText.Substring($finalMoveCall)
    Assert ($afterFinalMove -notmatch '(?m)^\s*(?:Assert-|Remove-Item|Reset-ProjectChild|Copy-Item|Set-Content|Write-Utf8NoBom|Compress-Archive|New-Item)\b') `
        'A fallible file assertion or mutation remains after final marker promotion.'

    # OneDrive Cloud Files use a non-redirecting reparse tag. The safety gate
    # must inspect and allow that owned path while rejecting name surrogates.
    $script:CacheRoot = $sourceProject.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    Assert-NoUnsafeCacheReparsePoints $sourceProject

    $script:CacheRoot = Join-Path $temporaryRoot 'velopack-cache'
    New-Item -ItemType Directory -Force -Path $script:CacheRoot | Out-Null
    $dummyPackage = Join-Path $script:CacheRoot 'fixture.nupkg'
    Write-TestFile $dummyPackage 'synthetic pinned package fixture'
    $velopackOutsideTarget = Join-Path $temporaryRoot 'velopack-outside-target'
    Write-TestFile (Join-Path $velopackOutsideTarget 'must-survive.txt') 'outside sentinel'
    $velopackJunctionPath = Join-Path $script:CacheRoot 'velopack'
    New-Item -ItemType Junction -Path $velopackJunctionPath `
        -Target $velopackOutsideTarget | Out-Null
    Expect-Rejection {
        Ensure-Extracted `
            $dummyPackage `
            $velopackJunctionPath `
            'lib\net472\Velopack.dll' `
            ('0' * 64) | Out-Null
    } 'A Velopack cache junction was accepted as a recursive cleanup target.'
    Assert (Test-Path -LiteralPath (
        Join-Path $velopackOutsideTarget 'must-survive.txt')) `
        'Velopack cache cleanup changed data behind a rejected junction.'
    [IO.Directory]::Delete($velopackJunctionPath)
    $velopackJunctionPath = $null

    $velopackRegularDestination = Join-Path $script:CacheRoot 'newtonsoft'
    New-Item -ItemType Directory -Force -Path $velopackRegularDestination | Out-Null
    $velopackNestedOutside = Join-Path $temporaryRoot 'velopack-nested-outside'
    Write-TestFile (Join-Path $velopackNestedOutside 'must-survive.txt') `
        'nested outside sentinel'
    $velopackNestedJunctionPath = Join-Path $velopackRegularDestination 'linked-child'
    New-Item -ItemType Junction -Path $velopackNestedJunctionPath `
        -Target $velopackNestedOutside | Out-Null
    Expect-Rejection {
        Ensure-Extracted `
            $dummyPackage `
            $velopackRegularDestination `
            'lib\net45\Newtonsoft.Json.dll' `
            ('0' * 64) | Out-Null
    } 'A nested Velopack cache junction was accepted by recursive cleanup.'
    Assert (Test-Path -LiteralPath (
        Join-Path $velopackNestedOutside 'must-survive.txt')) `
        'Velopack cache cleanup changed data behind a nested junction.'
    [IO.Directory]::Delete($velopackNestedJunctionPath)
    $velopackNestedJunctionPath = $null

    Set-PreflightProject $temporaryRoot
    $readinessRoot = Join-Path $temporaryRoot 'readiness-build'
    New-Item -ItemType Directory -Force -Path $readinessRoot | Out-Null
    $expectedReadiness = Join-Path $readinessRoot 'release-assets-v0.8.0.expected.json'
    $completionReadiness = Join-Path $readinessRoot 'release-assets-v0.8.0.ready.json'
    Write-TestFile $expectedReadiness 'stale expected manifest'
    Write-TestFile $completionReadiness 'stale completion marker'
    Remove-ReleaseReadinessFile `
        $completionReadiness `
        $readinessRoot `
        'release-assets-v0.8.0.ready.json'
    Remove-ReleaseReadinessFile `
        $expectedReadiness `
        $readinessRoot `
        'release-assets-v0.8.0.expected.json'
    Assert (-not (Test-Path -LiteralPath $completionReadiness) -and
        -not (Test-Path -LiteralPath $expectedReadiness)) `
        'A new package preflight did not invalidate both stale readiness trust roots.'

    $outsideReadiness = Join-Path $temporaryRoot 'must-survive.ready.json'
    Write-TestFile $outsideReadiness 'outside readiness sentinel'
    Expect-Rejection {
        Remove-ReleaseReadinessFile `
            $outsideReadiness `
            $readinessRoot `
            'release-assets-v0.8.0.ready.json'
    } 'Release readiness invalidation accepted a file outside the exact build root.'
    Assert (Test-Path -LiteralPath $outsideReadiness -PathType Leaf) `
        'Rejected readiness invalidation changed an outside sentinel.'

    $readinessOutsideTarget = Join-Path $temporaryRoot 'readiness-outside-target'
    $readinessOutsideMarker = Join-Path $readinessOutsideTarget `
        'release-assets-v0.8.0.ready.json'
    Write-TestFile $readinessOutsideMarker 'linked outside readiness sentinel'
    $readinessJunctionPath = Join-Path $temporaryRoot 'readiness-linked-build'
    New-Item -ItemType Junction -Path $readinessJunctionPath `
        -Target $readinessOutsideTarget | Out-Null
    Expect-Rejection {
        Remove-ReleaseReadinessFile `
            (Join-Path $readinessJunctionPath 'release-assets-v0.8.0.ready.json') `
            $readinessJunctionPath `
            'release-assets-v0.8.0.ready.json'
    } 'Release readiness invalidation followed an ancestor junction.'
    Assert (Test-Path -LiteralPath $readinessOutsideMarker -PathType Leaf) `
        'Rejected readiness invalidation removed data behind an ancestor junction.'
    [IO.Directory]::Delete($readinessJunctionPath)
    $readinessJunctionPath = $null

    Set-PreflightProject $sourceProject
    Assert-NoFileSystemLinkWithinProject (Join-Path $sourceProject 'src\MainForm.cs')

    $repository = Join-Path $temporaryRoot 'repository'
    Write-TestFile (Join-Path $repository '.gitignore') "*.local-secret.token`n"
    Write-TestFile (Join-Path $repository 'src\App.cs') 'tracked app source'
    Write-TestFile (Join-Path $repository 'src\linked\Linked.cs') 'tracked linked source'
    Write-TestFile (Join-Path $repository 'tests\AppTests.cs') 'tracked test source'
    Write-TestFile (Join-Path $repository 'tools\Build.ps1') 'tracked tool source'
    Write-TestFile (Join-Path $repository 'deferred\uninstall\README.md') `
        'tracked deferred design, not a product build input'
    Write-TestFile (Join-Path $repository 'README.md') 'tracked manual review input'
    & git -C $repository init --quiet
    Assert ($LASTEXITCODE -eq 0) 'Could not initialize the preflight repository fixture.'
    & git -C $repository add .gitignore src tests tools deferred README.md
    Assert ($LASTEXITCODE -eq 0) 'Could not stage the preflight repository fixture.'
    & git -C $repository `
        -c user.name=TSG-Test `
        -c user.email=tsg-test.invalid@example.invalid `
        commit --quiet -m 'preflight fixture'
    Assert ($LASTEXITCODE -eq 0) 'Could not commit the preflight repository fixture.'

    $ignoredSecret = Join-Path $repository 'tools\release.local-secret.token'
    Write-TestFile $ignoredSecret '{"synthetic":"must-not-copy"}'
    & git -C $repository check-ignore --quiet -- tools/release.local-secret.token
    Assert ($LASTEXITCODE -eq 0) 'Synthetic secret fixture was not ignored.'

    Set-PreflightProject $repository
    $tracked = @(Get-TrackedReviewSourceFiles)
    Assert ($tracked.Count -eq 4) 'Tracked source allowlist returned an unexpected file count.'
    Assert (-not ($tracked -contains 'deferred\uninstall\README.md')) `
        'A deferred design entered the executable/public source snapshot allowlist.'
    Assert (-not ($tracked -contains 'tools\release.local-secret.token')) `
        'Ignored local credential entered the tracked review allowlist.'
    Assert-TrackedCleanReviewInputs @('README.md')

    $liveSnapshotPaths = @($tracked) + @('README.md')
    $liveInputSnapshot = New-ReviewInputSnapshot $liveSnapshotPaths $liveSnapshotPaths
    Assert-LiveReviewInputBaseline $tracked @('README.md') $liveInputSnapshot
    $lateUntrackedSource = Join-Path $repository 'src\LateUntracked.cs'
    Write-TestFile $lateUntrackedSource 'late untracked build input'
    Expect-Rejection {
        Assert-LiveReviewInputBaseline $tracked @('README.md') $liveInputSnapshot
    } 'A source added after the captured baseline passed the live pre/post gate.'
    Remove-Item -LiteralPath $lateUntrackedSource -Force

    $expectedBuildSnapshot = New-ReviewInputSnapshot @('src\App.cs') @('src\App.cs')
    $appSource = Join-Path $repository 'src\App.cs'
    $appItem = Get-Item -Force -LiteralPath $appSource
    $appHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $appSource).Hash.ToLowerInvariant()
    $buildManifest = Join-Path $repository 'build\generated\build-inputs.manifest'
    $validBuildManifest = "tsg-build-inputs-v1`n$appHash`t$($appItem.Length)`tsrc/App.cs`n"
    Write-TestFile $buildManifest $validBuildManifest
    Assert-BuildInputManifestMatchesSnapshot $buildManifest $expectedBuildSnapshot

    $gitExclude = Join-Path $repository '.git\info\exclude'
    Write-TestFile $gitExclude "src/InjectedIgnored.cs`n"
    $ignoredBuildSource = Join-Path $repository 'src\InjectedIgnored.cs'
    Write-TestFile $ignoredBuildSource 'ignored injected build input'
    & git -C $repository check-ignore --quiet -- src/InjectedIgnored.cs
    Assert ($LASTEXITCODE -eq 0) 'Synthetic ignored C# build input was not ignored.'
    $ignoredItem = Get-Item -Force -LiteralPath $ignoredBuildSource
    $ignoredHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ignoredBuildSource).Hash.ToLowerInvariant()
    Write-TestFile $buildManifest (
        $validBuildManifest.TrimEnd("`n") +
        "`n$ignoredHash`t$($ignoredItem.Length)`tsrc/InjectedIgnored.cs`n")
    Expect-Rejection {
        Assert-BuildInputManifestMatchesSnapshot $buildManifest $expectedBuildSnapshot
    } 'An ignored C# file compiled outside the captured source snapshot was accepted.'
    Remove-Item -LiteralPath $ignoredBuildSource -Force

    Write-TestFile $buildManifest (
        "tsg-build-inputs-v1`n" + ('0' * 64) +
        "`t$($appItem.Length)`tsrc/App.cs`n")
    Expect-Rejection {
        Assert-BuildInputManifestMatchesSnapshot $buildManifest $expectedBuildSnapshot
    } 'A build manifest hash different from the captured source snapshot was accepted.'
    Write-TestFile $buildManifest (
        "tsg-build-inputs-v1`n$appHash`t$([long]$appItem.Length + 1)`tsrc/App.cs`n")
    Expect-Rejection {
        Assert-BuildInputManifestMatchesSnapshot $buildManifest $expectedBuildSnapshot
    } 'A build manifest length different from the captured source snapshot was accepted.'

    $dependencySource = Join-Path $repository 'build\dependency-source.dll'
    Write-TestFile $dependencySource 'synthetic pinned dependency'
    $dependencyIntegrity = Get-CurrentFileIntegrity $dependencySource
    $frozenInputRoot = Join-Path $repository 'build\frozen-package-inputs'
    $frozenDependency = Join-Path $frozenInputRoot 'dependencies\dependency.dll'
    Copy-VerifiedFileToStage `
        $dependencySource `
        $frozenDependency `
        $dependencyIntegrity.Sha256 `
        $dependencyIntegrity.Length
    $frozenSnapshot = New-ExactDirectorySnapshot `
        $frozenInputRoot `
        @('dependencies/dependency.dll')
    Assert-ReviewInputSnapshot $frozenSnapshot $frozenInputRoot -RejectUnexpectedFiles
    Write-TestFile (Join-Path $frozenInputRoot 'unexpected.dll') 'unexpected package payload'
    Expect-Rejection {
        Assert-ReviewInputSnapshot $frozenSnapshot $frozenInputRoot -RejectUnexpectedFiles
    } 'An unexpected file entered the explicit frozen package input set.'
    Remove-Item -LiteralPath (Join-Path $frozenInputRoot 'unexpected.dll') -Force
    Write-TestFile $dependencySource 'mutated dependency after validation'
    Expect-Rejection {
        Copy-VerifiedFileToStage `
            $dependencySource `
            (Join-Path $frozenInputRoot 'dependencies\mutated.dll') `
            $dependencyIntegrity.Sha256 `
            $dependencyIntegrity.Length
    } 'A dependency changed after Prepare-Velopack validation was frozen and accepted.'

    $readinessBuildRoot = Join-Path $repository 'build'
    $pendingMarkerRoot = Join-Path $readinessBuildRoot 'release-readiness-v0.8.0-pending'
    New-Item -ItemType Directory -Force -Path $pendingMarkerRoot | Out-Null
    $expectedManifest = Join-Path $readinessBuildRoot 'release-assets-v0.8.0.expected.json'
    Write-TestFile $expectedManifest '{"fixture":"expected assets"}'
    $expectedManifestHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $expectedManifest).Hash
    $pendingMarker = Join-Path $pendingMarkerRoot 'release-assets-v0.8.0.ready.json'
    $finalMarker = Join-Path $readinessBuildRoot 'release-assets-v0.8.0.ready.json'
    $markerJson = [PSCustomObject][ordered]@{
        schemaVersion = 1
        repository = 'Spirit-Schema/tarkov-server-guard'
        version = '0.8.0'
        tag = 'v0.8.0'
        expectedManifestFile = 'release-assets-v0.8.0.expected.json'
        expectedManifestSha256 = $expectedManifestHash
    } | ConvertTo-Json -Compress
    Write-TestFile $pendingMarker $markerJson
    $pendingIntegrity = Assert-PendingCompletionMarker `
        $pendingMarker `
        $expectedManifest `
        '0.8.0'
    Move-ReleaseCompletionMarkerAtomically `
        $pendingMarker `
        $finalMarker `
        $pendingMarkerRoot `
        $readinessBuildRoot `
        'release-assets-v0.8.0.ready.json' `
        $pendingIntegrity.Sha256 `
        $pendingIntegrity.Length
    Assert (Test-Path -LiteralPath $finalMarker -PathType Leaf) `
        'The verified pending completion marker was not atomically promoted.'
    Assert (-not (Test-Path -LiteralPath $pendingMarker)) `
        'The pending completion marker remained after atomic promotion.'
    Write-TestFile $pendingMarker $markerJson
    $pendingIntegrity = Assert-PendingCompletionMarker `
        $pendingMarker `
        $expectedManifest `
        '0.8.0'
    Expect-Rejection {
        Move-ReleaseCompletionMarkerAtomically `
            $pendingMarker `
            $finalMarker `
            $pendingMarkerRoot `
            $readinessBuildRoot `
            'release-assets-v0.8.0.ready.json' `
            $pendingIntegrity.Sha256 `
            $pendingIntegrity.Length
    } 'Atomic completion promotion overwrote an existing final marker.'
    Assert ((Get-Content -Raw -LiteralPath $finalMarker) -ceq $markerJson) `
        'A rejected completion promotion changed the existing final marker.'
    Assert (Test-Path -LiteralPath $pendingMarker -PathType Leaf) `
        'A rejected completion promotion removed the pending marker.'
    Remove-Item -LiteralPath $pendingMarker -Force

    $snapshotSources = @($tracked) + @('README.md', 'README.md')
    $snapshotTargets = @($tracked) + @('README.md', 'README-copy.md')
    $reviewInputSnapshot = New-ReviewInputSnapshot $snapshotSources $snapshotTargets
    $verifiedReview = Join-Path $repository 'build\review-input-snapshot'
    New-Item -ItemType Directory -Force -Path $verifiedReview | Out-Null
    Copy-TrackedReviewSources $tracked $verifiedReview
    Copy-Item -LiteralPath (Join-Path $repository 'README.md') `
        -Destination (Join-Path $verifiedReview 'README.md')
    Copy-Item -LiteralPath (Join-Path $repository 'README.md') `
        -Destination (Join-Path $verifiedReview 'README-copy.md')
    Assert-ReviewInputSnapshot `
        $reviewInputSnapshot `
        $verifiedReview `
        -RejectUnexpectedFiles

    $unexpectedReviewFile = Join-Path $verifiedReview 'unexpected-public-file.txt'
    Write-TestFile $unexpectedReviewFile 'unexpected source snapshot entry'
    Expect-Rejection {
        Assert-ReviewInputSnapshot `
            $reviewInputSnapshot `
            $verifiedReview `
            -RejectUnexpectedFiles
    } 'An unexpected file in the frozen public source snapshot was accepted.'
    Remove-Item -LiteralPath $unexpectedReviewFile -Force

    Write-TestFile (Join-Path $repository 'README.md') 'changed during review copy'
    Copy-Item -Force -LiteralPath (Join-Path $repository 'README.md') `
        -Destination (Join-Path $verifiedReview 'README.md')
    Write-TestFile (Join-Path $repository 'README.md') 'tracked manual review input'
    Expect-Rejection {
        Assert-ReviewInputSnapshot $reviewInputSnapshot $verifiedReview
    } 'A source mutation copied after clean preflight was not detected.'
    Copy-Item -Force -LiteralPath (Join-Path $repository 'README.md') `
        -Destination (Join-Path $verifiedReview 'README.md')
    Assert-ReviewInputSnapshot `
        $reviewInputSnapshot `
        $verifiedReview `
        -RejectUnexpectedFiles

    $review = Join-Path $temporaryRoot 'review'
    Copy-TrackedReviewSources $tracked $review
    $copied = @(Get-ChildItem -LiteralPath $review -File -Recurse)
    Assert ($copied.Count -eq 4) 'Review copy contained files outside the tracked allowlist.'
    Assert (-not (Test-Path -LiteralPath (
        Join-Path $review 'tools\release.local-secret.token'))) `
        'Ignored local credential was copied to the review source tree.'

    $modifiedTrackedSource = Join-Path $repository 'src\App.cs'
    Write-TestFile $modifiedTrackedSource 'modified tracked app source'
    Expect-Rejection { Get-TrackedReviewSourceFiles | Out-Null } `
        'A modified tracked source did not stop the release preflight.'
    Write-TestFile $modifiedTrackedSource 'tracked app source'

    $stagedTrackedSource = Join-Path $repository 'tests\AppTests.cs'
    Write-TestFile $stagedTrackedSource 'staged tracked test source'
    & git -C $repository add -- tests/AppTests.cs
    Assert ($LASTEXITCODE -eq 0) 'Could not stage the tracked-source fixture.'
    Expect-Rejection { Get-TrackedReviewSourceFiles | Out-Null } `
        'A staged tracked source did not stop the release preflight.'
    & git -C $repository restore --source=HEAD --staged --worktree -- tests/AppTests.cs
    Assert ($LASTEXITCODE -eq 0) 'Could not restore the staged tracked-source fixture.'

    $untrackedSource = Join-Path $repository 'src\Untracked.cs'
    Write-TestFile $untrackedSource 'untracked source'
    Expect-Rejection { Get-TrackedReviewSourceFiles | Out-Null } `
        'Untracked source did not stop the release preflight.'
    Remove-Item -LiteralPath $untrackedSource -Force

    $modifiedManualInput = Join-Path $repository 'README.md'
    Write-TestFile $modifiedManualInput 'modified tracked manual review input'
    Expect-Rejection { Assert-TrackedCleanReviewInputs @('README.md') } `
        'A modified tracked manual review input passed preflight.'
    Write-TestFile $modifiedManualInput 'tracked manual review input'

    $untrackedManualInput = Join-Path $repository 'release-scan-allowlist.json'
    Write-TestFile $untrackedManualInput '{}'
    Expect-Rejection {
        Assert-TrackedCleanReviewInputs @('release-scan-allowlist.json')
    } 'An untracked manual review input passed preflight.'
    Remove-Item -LiteralPath $untrackedManualInput -Force

    Expect-Rejection {
        Copy-TrackedReviewSources @('src\..\outside.cs') (Join-Path $temporaryRoot 'escape')
    } 'A parent-directory review path was accepted.'
    Expect-Rejection {
        Copy-TrackedReviewSources @('README.md') (Join-Path $temporaryRoot 'outside-allowlist')
    } 'A path outside src/tests/tools was accepted.'

    $parentRepository = Join-Path $temporaryRoot 'parent-repository'
    $nestedProject = Join-Path $parentRepository 'nested-project'
    Write-TestFile (Join-Path $nestedProject 'src\Nested.cs') 'nested source'
    & git -C $parentRepository init --quiet
    Assert ($LASTEXITCODE -eq 0) 'Could not initialize the parent repository fixture.'
    Set-PreflightProject $nestedProject
    Expect-Rejection { Get-TrackedReviewSourceFiles | Out-Null } `
        'A project nested inside an unrelated parent repository passed preflight.'

    Set-PreflightProject $repository
    $linkedSource = Join-Path $repository 'src\linked\Linked.cs'
    Remove-Item -LiteralPath $linkedSource -Force
    Remove-Item -LiteralPath (Split-Path -Parent $linkedSource) -Force
    $outsideTarget = Join-Path $temporaryRoot 'outside-target'
    Write-TestFile (Join-Path $outsideTarget 'Linked.cs') 'tracked linked source'
    $junctionPath = Join-Path $repository 'src\linked'
    New-Item -ItemType Junction -Path $junctionPath -Target $outsideTarget | Out-Null
    $trackedThroughJunction = @(Get-TrackedReviewSourceFiles)
    Expect-Rejection {
        Copy-TrackedReviewSources $trackedThroughJunction (Join-Path $temporaryRoot 'junction-review')
    } 'A tracked source reached through an ancestor junction was copied.'

    [IO.Directory]::Delete($junctionPath)
    $junctionPath = $null
    $resetOutsideTarget = Join-Path $temporaryRoot 'outside-reset-target'
    Write-TestFile (Join-Path $resetOutsideTarget 'must-survive.txt') 'outside sentinel'
    $resetParent = Join-Path $repository 'build'
    New-Item -ItemType Directory -Force -Path $resetParent | Out-Null
    $resetJunctionPath = Join-Path $resetParent 'publish-v9.9.9'
    New-Item -ItemType Junction -Path $resetJunctionPath -Target $resetOutsideTarget | Out-Null
    Expect-Rejection {
        Reset-ProjectChild $resetJunctionPath $resetParent | Out-Null
    } 'A junction was accepted as a recursive package cleanup target.'
    Assert (Test-Path -LiteralPath (Join-Path $resetOutsideTarget 'must-survive.txt')) `
        'Recursive package cleanup changed data behind a rejected junction.'

    Write-Host 'All package release preflight tests passed.'
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($readinessJunctionPath) -and
        (Test-Path -LiteralPath $readinessJunctionPath)) {
        [IO.Directory]::Delete([IO.Path]::GetFullPath($readinessJunctionPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($velopackJunctionPath) -and
        (Test-Path -LiteralPath $velopackJunctionPath)) {
        [IO.Directory]::Delete([IO.Path]::GetFullPath($velopackJunctionPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($velopackNestedJunctionPath) -and
        (Test-Path -LiteralPath $velopackNestedJunctionPath)) {
        [IO.Directory]::Delete([IO.Path]::GetFullPath($velopackNestedJunctionPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($resetJunctionPath) -and
        (Test-Path -LiteralPath $resetJunctionPath)) {
        [IO.Directory]::Delete([IO.Path]::GetFullPath($resetJunctionPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($junctionPath) -and
        (Test-Path -LiteralPath $junctionPath)) {
        $resolvedJunction = [IO.Path]::GetFullPath($junctionPath)
        $temporaryPrefix = [IO.Path]::GetFullPath($temporaryRoot).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedJunction.StartsWith(
            $temporaryPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected junction cleanup path: $resolvedJunction"
        }
        [IO.Directory]::Delete($resolvedJunction)
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $systemTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedTemporaryRoot.StartsWith(
            $systemTemporaryRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected test cleanup path: $resolvedTemporaryRoot"
        }
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
