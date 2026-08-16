# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$version = '0.8.0'
$tag = 'v' + $version
$generator = Join-Path $ProjectRoot 'tools\New-ReleaseAssetManifest.ps1'
$completionGenerator = Join-Path $ProjectRoot 'tools\New-ReleaseCompletionMarker.ps1'
$verifier = Join-Path $ProjectRoot 'tools\Test-PublishedReleaseAssets.ps1'
$powershellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$encoding = New-Object System.Text.UTF8Encoding($false, $true)
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'TarkovServerGuard-PublishedReleaseAssetTests-' + [Guid]::NewGuid().ToString('N'))
$failures = 0

function Assert([bool]$Condition, [string]$Message) {
    if ($Condition) {
        Write-Host ('PASS: ' + $Message)
        return
    }
    $script:failures++
    Write-Host ('FAIL: ' + $Message)
}

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Write-Bytes([string]$Path, [string]$Text) {
    [IO.File]::WriteAllBytes($Path, $encoding.GetBytes($Text))
}

function Copy-FixtureAssets([string]$Destination, [object]$Manifest) {
    New-Item -ItemType Directory -Path $Destination | Out-Null
    foreach ($asset in @($Manifest.assets)) {
        $source = if (Test-Path -LiteralPath (Join-Path $script:releaseRoot $asset.name)) {
            Join-Path $script:releaseRoot $asset.name
        }
        else {
            Join-Path $script:reviewParent $asset.name
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $Destination $asset.name)
    }
}

function New-ReleaseFixture([object]$Manifest) {
    $assets = New-Object 'System.Collections.Generic.List[object]'
    foreach ($asset in @($Manifest.assets)) {
        $assets.Add([PSCustomObject][ordered]@{
            id = $assets.Count + 1000
            name = [string]$asset.name
            state = 'uploaded'
            size = [long]$asset.size
            digest = 'sha256:' + ([string]$asset.sha256).ToLowerInvariant()
            browser_download_url = 'https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/' +
                $tag + '/' + [Uri]::EscapeDataString([string]$asset.name)
        })
    }
    # The API digest is optional. Keep one normal asset without it to prevent the verifier
    # from accidentally treating absence as a failure.
    $assets[1].PSObject.Properties.Remove('digest')
    $assets[0] | Add-Member -NotePropertyName fixture_redirect_chain -NotePropertyValue @(
        'https://release-assets.githubusercontent.com/github-production-release-asset/fixture/asset?sp=fixture')
    return [PSCustomObject][ordered]@{
        tag_name = $tag
        html_url = 'https://github.com/Spirit-Schema/tarkov-server-guard/releases/tag/' + $tag
        draft = $false
        prerelease = $false
        assets = @($assets | ForEach-Object { $_ })
    }
}

function Invoke-Scenario(
    [string]$Name,
    [object]$BaseRelease,
    [object]$Manifest,
    [scriptblock]$Mutate,
    [bool]$ExpectedSuccess,
    [int]$ExpectedCompletedDownloads,
    [string]$ExpectedError,
    [string]$CompletionMarkerPath = $script:completionMarkerPath) {
    $scenarioRoot = Join-Path $temporaryRoot $Name
    $assetRoot = Join-Path $scenarioRoot 'assets'
    $tempParent = Join-Path $scenarioRoot 'temp-parent'
    New-Item -ItemType Directory -Path $scenarioRoot,$tempParent | Out-Null
    Copy-FixtureAssets $assetRoot $Manifest

    $release = (($BaseRelease | ConvertTo-Json -Depth 8) | ConvertFrom-Json)
    if ($null -ne $Mutate) { & $Mutate $release $assetRoot $Manifest }
    $releaseJson = Join-Path $scenarioRoot 'release.json'
    Write-Utf8 $releaseJson (($release | ConvertTo-Json -Depth 8) + "`n")
    $reportPath = Join-Path $scenarioRoot 'report.json'
    $verifierArguments = @(
        '-NoProfile',
        '-NonInteractive',
        '-ExecutionPolicy', 'Bypass',
        '-File', $verifier,
        '-ExpectedManifest', $script:manifestPath)
    if (-not [string]::IsNullOrWhiteSpace($CompletionMarkerPath)) {
        $verifierArguments += @('-CompletionMarker', $CompletionMarkerPath)
    }
    $verifierArguments += @(
        '-ReportPath', $reportPath,
        '-FixtureReleaseJson', $releaseJson,
        '-FixtureAssetDirectory', $assetRoot,
        '-TempRoot', $tempParent)

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        # Failure fixtures intentionally produce stderr. Capture it without turning
        # an expected nonzero verifier result into a terminating test exception.
        $output = @(& $powershellExe @verifierArguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    Assert (($exitCode -eq 0) -eq $ExpectedSuccess) (
        $Name + ' exits with the expected success state')
    Assert (Test-Path -LiteralPath $reportPath -PathType Leaf) (
        $Name + ' writes a JSON verification report')
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        Write-Host ($output -join "`n")
        return
    }

    $reportText = [IO.File]::ReadAllText($reportPath, $encoding)
    $report = $reportText | ConvertFrom-Json
    if (([bool]$report.success -ne $ExpectedSuccess) -or
        ([int]$report.summary.completedDownloadCount -ne $ExpectedCompletedDownloads)) {
        $failedRows = @($report.assets | Where-Object { -not $_.success } | ForEach-Object {
            ([string]$_.name + '=' + [string]::Join(',', @($_.errors)))
        })
        Write-Host ('DIAGNOSTIC ' + $Name + ': exit=' + $exitCode +
            '; completed=' + $report.summary.completedDownloadCount +
            '; errors=' + [string]::Join(',', @($report.errors)) +
            '; rows=' + [string]::Join('|', $failedRows))
    }
    Assert ([bool]$report.success -eq $ExpectedSuccess) (
        $Name + ' report success matches the process result')
    Assert ([int]$report.summary.completedDownloadCount -eq $ExpectedCompletedDownloads) (
        $Name + ' records the exact completed download count')
    Assert ($reportText.IndexOf($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -lt 0) (
        $Name + ' report contains no local fixture or temp path')
    Assert (@(Get-ChildItem -LiteralPath $tempParent -Force).Count -eq 0) (
        $Name + ' cleans its contained download directory')
    if ([string]::IsNullOrWhiteSpace($ExpectedError)) {
        Assert (@($report.errors).Count -eq 0) ($Name + ' has no top-level error code')
    }
    else {
        Assert (@($report.errors) -contains $ExpectedError) (
            $Name + ' reports ' + $ExpectedError)
    }
    return $report
}

try {
    if (-not (Test-Path -LiteralPath $powershellExe -PathType Leaf)) {
        throw 'Windows PowerShell executable was not found.'
    }
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $script:releaseRoot = Join-Path $temporaryRoot 'Releases-v0.8.0'
    $script:reviewParent = Join-Path $temporaryRoot 'review'
    New-Item -ItemType Directory -Path $script:releaseRoot,$script:reviewParent | Out-Null

    $releaseFiles = [ordered]@{
        'SpiritSchema.TarkovServerGuard-win-Setup.exe' = 'setup payload'
        'SpiritSchema.TarkovServerGuard-win-Portable.zip' = 'portable payload'
        'SpiritSchema.TarkovServerGuard-0.8.0-full.nupkg' = 'full nupkg payload'
        'releases.win.json' = '{"Assets":[]}'
        'RELEASES' = 'legacy feed payload'
        'binary-build-identity.json' = '{"binaryBuildId":"fixture"}'
        'build-inputs.manifest' = "fixture input manifest`n"
        'release-assets.manifest' = "fixture release asset provenance`n"
        'release-provenance.json' = '{"releaseBundleId":"fixture"}'
    }
    foreach ($pair in $releaseFiles.GetEnumerator()) {
        Write-Bytes (Join-Path $script:releaseRoot $pair.Key) $pair.Value
    }
    $hashLines = @(Get-ChildItem -LiteralPath $script:releaseRoot -File |
        Where-Object {
            @('release-assets.manifest', 'release-provenance.json') -cnotcontains $_.Name
        } |
        Sort-Object Name |
        ForEach-Object {
            (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash + '  ' + $_.Name
        })
    Write-Utf8 (Join-Path $script:releaseRoot 'SHA256SUMS.txt') (
        [string]::Join("`n", $hashLines) + "`n")

    $reviewZip = Join-Path $script:reviewParent 'TarkovServerGuard-v0.8.0.zip'
    Write-Bytes $reviewZip 'review zip fixture payload'
    $reviewHash = $reviewZip + '.sha256.txt'
    Write-Utf8 $reviewHash (
        (Get-FileHash -Algorithm SHA256 -LiteralPath $reviewZip).Hash +
        '  ' + [IO.Path]::GetFileName($reviewZip) + "`n")
    $script:manifestPath = Join-Path $temporaryRoot 'release-assets-v0.8.0.expected.json'

    $generatorOutput = @(& $powershellExe `
        -NoProfile `
        -NonInteractive `
        -ExecutionPolicy Bypass `
        -File $generator `
        -Version $version `
        -ReleaseDirectory $script:releaseRoot `
        -ReviewZip $reviewZip `
        -ReviewZipHash $reviewHash `
        -OutputPath $script:manifestPath 2>&1)
    Assert ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $script:manifestPath)) (
        'local expected manifest generator succeeds on canonical package fixtures')
    if (-not (Test-Path -LiteralPath $script:manifestPath)) {
        throw ($generatorOutput -join "`n")
    }
    $firstManifestBytes = [IO.File]::ReadAllBytes($script:manifestPath)
    & $powershellExe `
        -NoProfile `
        -NonInteractive `
        -ExecutionPolicy Bypass `
        -File $generator `
        -Version $version `
        -ReleaseDirectory $script:releaseRoot `
        -ReviewZip $reviewZip `
        -ReviewZipHash $reviewHash `
        -OutputPath $script:manifestPath | Out-Null
    $secondManifestBytes = [IO.File]::ReadAllBytes($script:manifestPath)
    Assert ($LASTEXITCODE -eq 0 -and
        [Convert]::ToBase64String($firstManifestBytes) -ceq
            [Convert]::ToBase64String($secondManifestBytes)) (
        'local expected manifest is canonical and deterministic')

    $script:completionMarkerPath = Join-Path $temporaryRoot `
        'release-assets-v0.8.0.ready.json'
    $completionOutput = @(& $powershellExe `
        -NoProfile `
        -NonInteractive `
        -ExecutionPolicy Bypass `
        -File $completionGenerator `
        -Version $version `
        -ExpectedManifest $script:manifestPath `
        -OutputPath $script:completionMarkerPath 2>&1)
    Assert ($LASTEXITCODE -eq 0 -and
        (Test-Path -LiteralPath $script:completionMarkerPath -PathType Leaf)) (
        'completion marker generator atomically binds the canonical expected manifest')
    if (-not (Test-Path -LiteralPath $script:completionMarkerPath -PathType Leaf)) {
        throw ($completionOutput -join "`n")
    }
    $completion = Get-Content -Raw -LiteralPath $script:completionMarkerPath |
        ConvertFrom-Json
    Assert ([string]$completion.expectedManifestSha256 -ceq
        (Get-FileHash -Algorithm SHA256 -LiteralPath $script:manifestPath).Hash) (
        'completion marker records the exact expected manifest hash')

    $manifestText = [IO.File]::ReadAllText($script:manifestPath, $encoding)
    $manifest = $manifestText | ConvertFrom-Json
    Assert (@($manifest.assets).Count -eq 12) (
        'manifest includes every Velopack/hash asset plus review ZIP and review hash')
    Assert (@($manifest.assets | Where-Object {
        $_.role -in @(
            'binary-build-identity',
            'binary-build-input-manifest',
            'release-asset-provenance-manifest',
            'release-provenance')
    }).Count -eq 4) ('manifest includes every public provenance asset')
    Assert (@($manifest.assets | Where-Object { $_.role -eq 'review-zip' }).Count -eq 1 -and
        @($manifest.assets | Where-Object { $_.role -eq 'review-zip-sha256' }).Count -eq 1) (
        'manifest assigns explicit roles to review ZIP assets')
    Assert ($manifestText.IndexOf($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -lt 0) (
        'manifest contains no local path')

    $canonicalManifestBytes = [IO.File]::ReadAllBytes($script:manifestPath)
    $canonicalHashListBytes = [IO.File]::ReadAllBytes(
        (Join-Path $script:releaseRoot 'SHA256SUMS.txt'))
    $unexpectedLocalAsset = Join-Path $script:releaseRoot 'unexpected-local.bin'
    Write-Bytes $unexpectedLocalAsset 'unexpected local publication asset'
    $unexpectedHashLines = @(Get-ChildItem -LiteralPath $script:releaseRoot -File |
        Where-Object {
            $_.Name -cne 'SHA256SUMS.txt' -and
            @('release-assets.manifest', 'release-provenance.json') -cnotcontains $_.Name
        } |
        Sort-Object Name |
        ForEach-Object {
            (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash + '  ' + $_.Name
        })
    Write-Utf8 (Join-Path $script:releaseRoot 'SHA256SUMS.txt') (
        [string]::Join("`n", $unexpectedHashLines) + "`n")
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $unknownOutput = @(& $powershellExe `
            -NoProfile `
            -NonInteractive `
            -ExecutionPolicy Bypass `
            -File $generator `
            -Version $version `
            -ReleaseDirectory $script:releaseRoot `
            -ReviewZip $reviewZip `
            -ReviewZipHash $reviewHash `
            -OutputPath $script:manifestPath 2>&1)
        $unknownExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        Remove-Item -LiteralPath $unexpectedLocalAsset -Force
        [IO.File]::WriteAllBytes(
            (Join-Path $script:releaseRoot 'SHA256SUMS.txt'),
            $canonicalHashListBytes)
    }
    Assert ($unknownExitCode -ne 0) ('manifest generator rejects an unknown local publication asset')
    Assert ([Convert]::ToBase64String($canonicalManifestBytes) -ceq
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($script:manifestPath))) (
        'unknown local asset rejection preserves the previous valid manifest')

    $unexpectedNestedDirectory = Join-Path $script:releaseRoot 'unexpected-nested'
    [IO.Directory]::CreateDirectory($unexpectedNestedDirectory) | Out-Null
    Write-Bytes (Join-Path $unexpectedNestedDirectory 'hidden-publication-asset.bin') (
        'unexpected nested publication asset')
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $nestedOutput = @(& $powershellExe `
            -NoProfile `
            -NonInteractive `
            -ExecutionPolicy Bypass `
            -File $generator `
            -Version $version `
            -ReleaseDirectory $script:releaseRoot `
            -ReviewZip $reviewZip `
            -ReviewZipHash $reviewHash `
            -OutputPath $script:manifestPath 2>&1)
        $nestedExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        Remove-Item -LiteralPath $unexpectedNestedDirectory -Recurse -Force
    }
    Assert ($nestedExitCode -ne 0) ('manifest generator rejects a nested publication asset')
    Assert ([Convert]::ToBase64String($canonicalManifestBytes) -ceq
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($script:manifestPath))) (
        'nested asset rejection preserves the previous valid manifest')

    $baseRelease = New-ReleaseFixture $manifest
    Invoke-Scenario `
        'missing-readiness' $baseRelease $manifest $null $false 0 `
        'completion-marker-required' '' | Out-Null

    $staleMarkerRoot = Join-Path $temporaryRoot 'stale-marker-input'
    New-Item -ItemType Directory -Path $staleMarkerRoot | Out-Null
    $staleMarkerPath = Join-Path $staleMarkerRoot 'release-assets-v0.8.0.ready.json'
    $staleMarker = (($completion | ConvertTo-Json -Depth 4) | ConvertFrom-Json)
    $staleMarker.expectedManifestSha256 = '0' * 64
    Write-Utf8 $staleMarkerPath (($staleMarker | ConvertTo-Json -Compress) + "`n")
    Invoke-Scenario `
        'stale-readiness' $baseRelease $manifest $null $false 0 `
        'completion-marker-manifest-hash-mismatch' $staleMarkerPath | Out-Null

    $normal = Invoke-Scenario `
        'normal' $baseRelease $manifest $null $true 12 $null
    Assert ([bool]$normal.summary.completionMarkerValidated) (
        'normal fixture requires and validates the completion marker')
    Assert (@($normal.assets | Where-Object { -not $_.apiDigestPresent }).Count -eq 1) (
        'normal fixture proves optional API digest absence is accepted')
    Assert (@($normal.assets | Where-Object {
        $_.finalUrl -and ([string]$_.finalUrl).Contains('?')
    }).Count -eq 0) ('report strips query strings from final URLs')
    Assert ([int]$normal.summary.httpDownloadRequestCount -eq 0) (
        'fixture mode performs no HTTP download requests')
    Assert (@($normal.assets | Where-Object {
        [int]$_.downloadCount -ne 1 -or [int]$_.httpRequestCount -ne 0
    }).Count -eq 0) ('report records logical and HTTP download counts per asset')
    Assert (@($normal.assets | Where-Object {
        [int]$_.redirectCount -eq 1 -and
        ([string]$_.finalUrl).StartsWith(
            'https://release-assets.githubusercontent.com/',
            [StringComparison]::Ordinal)
    }).Count -eq 1) ('allowed GitHub release asset redirect host passes fixture validation')

    Invoke-Scenario 'missing' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $release.assets = @($release.assets | Select-Object -Skip 1)
    } $false 11 'missing-assets' | Out-Null

    Invoke-Scenario 'duplicate' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $duplicate = (($release.assets[0] | ConvertTo-Json -Depth 5) | ConvertFrom-Json)
        $release.assets = @($release.assets) + @($duplicate)
    } $false 11 'duplicate-assets' | Out-Null

    Invoke-Scenario 'unexpected' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $release.assets = @($release.assets) + @([PSCustomObject]@{
            id = 9999
            name = 'unexpected.bin'
            state = 'uploaded'
            size = 4
            browser_download_url =
                'https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.0/unexpected.bin'
        })
    } $false 12 'unexpected-assets' | Out-Null

    Invoke-Scenario 'size-mismatch' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $release.assets[0].size = [long]$release.assets[0].size + 1
    } $false 12 'asset-verification-failed' | Out-Null

    Invoke-Scenario 'hash-mismatch' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $name = [string]$release.assets[0].name
        $path = Join-Path $assetRoot $name
        $bytes = [IO.File]::ReadAllBytes($path)
        $bytes[0] = $bytes[0] -bxor 0xff
        [IO.File]::WriteAllBytes($path, $bytes)
    } $false 12 'asset-verification-failed' | Out-Null

    $oversizeDownload = Invoke-Scenario 'oversize-download' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $name = [string]$release.assets[0].name
        $path = Join-Path $assetRoot $name
        $bytes = [IO.File]::ReadAllBytes($path)
        $expanded = New-Object byte[] ($bytes.Length + 1)
        [Array]::Copy($bytes, $expanded, $bytes.Length)
        $expanded[$expanded.Length - 1] = 0x7f
        [IO.File]::WriteAllBytes($path, $expanded)
    } $false 11 'asset-verification-failed'
    Assert (@($oversizeDownload.assets[0].errors) -contains 'asset-download-size-limit') (
        'download stops immediately after exceeding the expected asset size')

    Invoke-Scenario 'digest-mismatch' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        if ($null -eq $release.assets[0].PSObject.Properties['digest']) {
            $release.assets[0] | Add-Member -NotePropertyName digest -NotePropertyValue ('sha256:' + ('0' * 64))
        }
        else {
            $release.assets[0].digest = 'sha256:' + ('0' * 64)
        }
    } $false 12 'asset-verification-failed' | Out-Null

    Invoke-Scenario 'name-mismatch' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $oldName = [string]$release.assets[0].name
        $newName = $oldName.ToUpperInvariant()
        $release.assets[0].name = $newName
        $release.assets[0].browser_download_url =
            'https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v0.8.0/' +
            [Uri]::EscapeDataString($newName)
    } $false 11 'asset-name-mismatches' | Out-Null

    Invoke-Scenario 'wrong-tag' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $release.tag_name = 'v9.9.9'
    } $false 0 'release-tag-mismatch' | Out-Null

    Invoke-Scenario 'prerelease' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $release.prerelease = $true
    } $false 0 'release-is-prerelease' | Out-Null

    $httpRedirect = Invoke-Scenario 'http-redirect' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $release.assets[0].fixture_redirect_chain = @(
            'http://release-assets.githubusercontent.com/insecure')
    } $false 11 'asset-verification-failed'
    Assert (@($httpRedirect.assets[0].errors) -contains
        'asset-download-redirect-host-rejected') ('fixture rejects an HTTP redirect')

    $externalRedirect = Invoke-Scenario 'external-redirect' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $release.assets[0].fixture_redirect_chain = @('https://example.com/not-github')
    } $false 11 'asset-verification-failed'
    Assert (@($externalRedirect.assets[0].errors) -contains
        'asset-download-redirect-host-rejected') ('fixture rejects an external redirect host')

    $redirectLimit = Invoke-Scenario 'redirect-limit' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $release.assets[0].fixture_redirect_chain = @(1..6 | ForEach-Object {
            'https://release-assets.githubusercontent.com/fixture/redirect-' + $_
        })
    } $false 11 'asset-verification-failed'
    Assert (@($redirectLimit.assets[0].errors) -contains
        'asset-download-redirect-limit') ('fixture rejects a redirect chain over the bound')

    $wrongRedirectTag = Invoke-Scenario 'redirect-tag-binding' $baseRelease $manifest {
        param($release, $assetRoot, $manifestDocument)
        $name = [string]$release.assets[0].name
        $release.assets[0].fixture_redirect_chain = @(
            'https://github.com/Spirit-Schema/tarkov-server-guard/releases/download/v9.9.9/' +
                [Uri]::EscapeDataString($name))
    } $false 11 'asset-verification-failed'
    Assert (@($wrongRedirectTag.assets[0].errors) -contains
        'asset-download-redirect-binding-rejected') (
        'fixture rejects a GitHub redirect bound to the wrong tag')
}
catch {
    $failures++
    Write-Host ('FAIL: unexpected test exception: ' + $_.Exception.Message)
}
finally {
    try {
        $full = [IO.Path]::GetFullPath($temporaryRoot)
        $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ($full.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($full).StartsWith(
                'TarkovServerGuard-PublishedReleaseAssetTests-',
                [StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $full -PathType Container)) {
            Remove-Item -LiteralPath $full -Recurse -Force
        }
    }
    catch {
        $failures++
        Write-Host ('FAIL: fixture cleanup: ' + $_.Exception.GetType().Name)
    }
}

if ($failures -eq 0) {
    Write-Host 'ALL PUBLISHED RELEASE ASSET VERIFIER TESTS PASSED'
    $global:LASTEXITCODE = 0
    return
}
throw ($failures.ToString() + ' PUBLISHED RELEASE ASSET VERIFIER TEST(S) FAILED')
