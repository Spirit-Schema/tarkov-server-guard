# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,

    [Parameter(Mandatory = $true)]
    [string]$ApplicationVersion
)

$ErrorActionPreference = 'Stop'
$projectPath = [IO.Path]::GetFullPath($ProjectRoot)
$identityGenerator = Join-Path $projectPath 'tools\Prepare-BuildIdentity.ps1'
$provenanceGenerator = Join-Path $projectPath 'tools\New-ReleaseProvenance.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'TSG-ReleaseProvenanceTests-' + [Guid]::NewGuid().ToString('N'))

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

function Expect-Rejection([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action 2>$null | Out-Null }
    catch { $rejected = $true }
    Assert $rejected $Message
}

try {
    $fixtureRoot = Join-Path $temporaryRoot 'source-export'
    $identityOutput = Join-Path $fixtureRoot 'build\generated\identity'
    $assetRoot = Join-Path $temporaryRoot 'assets'
    Write-Utf8NoBom (Join-Path $fixtureRoot 'src\App.cs') 'internal static class App { }'
    New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
    Write-Utf8NoBom (Join-Path $assetRoot 'z-portable.zip') 'portable bytes'
    Write-Utf8NoBom (Join-Path $assetRoot 'A-setup.exe') 'setup bytes'
    Write-Utf8NoBom (Join-Path $assetRoot 'nested\package.nupkg') 'package bytes'
    Write-Utf8NoBom (Join-Path $assetRoot 'SHA256SUMS.txt') 'primary asset checksums'
    $reviewZip = Join-Path $temporaryRoot ('TarkovServerGuard-v' + $ApplicationVersion + '.zip')
    $reviewZipHash = $reviewZip + '.sha256.txt'
    Write-Utf8NoBom $reviewZip 'review archive bytes'
    Write-Utf8NoBom $reviewZipHash 'review archive checksum'
    $additionalPaths = @($reviewZip, $reviewZipHash)
    $additionalNames = @(
        ('review/' + (Split-Path -Leaf $reviewZip)),
        ('review/' + (Split-Path -Leaf $reviewZipHash)))

    $identity = & $identityGenerator `
        -ProjectRoot $fixtureRoot `
        -OutputDirectory $identityOutput `
        -ApplicationVersion $ApplicationVersion `
        -Channel release
    Copy-Item -Force -LiteralPath $identity.ManifestPath `
        -Destination (Join-Path $assetRoot 'binary-build-identity.json')
    Copy-Item -Force -LiteralPath $identity.InputManifestPath `
        -Destination (Join-Path $assetRoot 'build-inputs.manifest')
    $first = & $provenanceGenerator `
        -BinaryIdentityManifest $identity.ManifestPath `
        -AssetDirectory $assetRoot `
        -AdditionalAssetPath $additionalPaths `
        -AdditionalAssetName $additionalNames

    Assert ($first.BinaryBuildId -ceq $identity.BinaryBuildId) `
        'Release provenance changed the Binary Build ID.'
    Assert ($first.ReleaseBundleId -match '^tsg-bundle-v1-[0-9a-f]{64}$') `
        'Release Bundle ID did not use the documented public format.'
    $bundleMaterial = @(
        'schema=tsg-release-bundle-v1',
        ('binaryBuildId=' + $first.BinaryBuildId),
        ('publicAssetManifestSha256=' + $first.PublicAssetManifestSha256)
    ) -join "`n"
    $expectedBundleId = 'tsg-bundle-v1-' + (
        Get-Sha256Hex ([Text.Encoding]::UTF8.GetBytes($bundleMaterial)))
    Assert ($first.ReleaseBundleId -ceq $expectedBundleId) `
        'Release Bundle ID did not match the documented canonical material.'
    Assert ((Get-Sha256Hex ([IO.File]::ReadAllBytes($first.AssetManifestPath))) -ceq `
        $first.PublicAssetManifestSha256) `
        'Canonical public asset manifest hash did not match the file bytes.'

    $assetManifestText = Get-Content -Raw -LiteralPath $first.AssetManifestPath
    $assetLines = @($assetManifestText -split "`n" | Where-Object { $_ -match "`t" })
    $paths = @($assetLines | ForEach-Object { ($_ -split "`t", 3)[2] })
    $ordinalPaths = [string[]]$paths.Clone()
    [Array]::Sort($ordinalPaths, [StringComparer]::Ordinal)
    Assert (($paths -join '|') -ceq ($ordinalPaths -join '|')) `
        'Canonical public asset manifest paths were not ordinally sorted.'
    Assert (-not $assetManifestText.Contains('release-assets.manifest')) `
        'Canonical public asset manifest included itself.'
    Assert (-not $assetManifestText.Contains('release-provenance.json')) `
        'Canonical public asset manifest included its dependent provenance file.'
    Assert ($assetManifestText.Contains('SHA256SUMS.txt')) `
        'Canonical public asset manifest omitted the completed checksum index.'
    Assert ($assetManifestText.Contains($additionalNames[0])) `
        'Canonical public asset manifest omitted the review ZIP.'
    Assert ($assetManifestText.Contains($additionalNames[1])) `
        'Canonical public asset manifest omitted the review ZIP checksum.'

    $provenance = Get-Content -Raw -LiteralPath $first.ProvenancePath | ConvertFrom-Json
    Assert ($provenance.binaryBuildId -ceq $identity.BinaryBuildId) `
        'Public release provenance did not retain the Binary Build ID.'
    Assert ($provenance.buildInputManifestSha256 -ceq $identity.BuildInputManifestSha256) `
        'Public release provenance did not retain the build-input manifest hash.'
    Assert ($provenance.sourceRevision -ceq $identity.SourceRevision) `
        'Public release provenance did not retain the source revision.'
    Assert ($provenance.channel -ceq 'release') `
        'Public release provenance did not retain the explicit release channel.'
    Assert ($provenance.publicAssetManifestSha256 -ceq $first.PublicAssetManifestSha256) `
        'Public release provenance did not retain the canonical public asset hash.'

    $binaryManifestText = Get-Content -Raw -LiteralPath $identity.ManifestPath
    Assert (-not $binaryManifestText.Contains('publicAssetManifestSha256')) `
        'Binary identity depended on the final public asset manifest.'
    Assert (-not $binaryManifestText.Contains('releaseBundleId')) `
        'Binary identity depended on the external Release Bundle ID.'

    $publishedIdentityPath = Join-Path $assetRoot 'binary-build-identity.json'
    $publishedInputPath = Join-Path $assetRoot 'build-inputs.manifest'
    $publishedIdentityBytes = [IO.File]::ReadAllBytes($publishedIdentityPath)
    $publishedInputBytes = [IO.File]::ReadAllBytes($publishedInputPath)
    try {
        Write-Utf8NoBom $publishedIdentityPath '{"schemaVersion":1}'
        Expect-Rejection {
            & $provenanceGenerator `
                -BinaryIdentityManifest $identity.ManifestPath `
                -AssetDirectory $assetRoot `
                -AdditionalAssetPath $additionalPaths `
                -AdditionalAssetName $additionalNames
        } 'Release provenance accepted a different published binary identity manifest.'
    }
    finally {
        [IO.File]::WriteAllBytes($publishedIdentityPath, $publishedIdentityBytes)
    }
    try {
        Write-Utf8NoBom $publishedInputPath 'different build inputs'
        Expect-Rejection {
            & $provenanceGenerator `
                -BinaryIdentityManifest $identity.ManifestPath `
                -AssetDirectory $assetRoot `
                -AdditionalAssetPath $additionalPaths `
                -AdditionalAssetName $additionalNames
        } 'Release provenance accepted a mismatched published build-input manifest.'
    }
    finally {
        [IO.File]::WriteAllBytes($publishedInputPath, $publishedInputBytes)
    }

    $second = & $provenanceGenerator `
        -BinaryIdentityManifest $identity.ManifestPath `
        -AssetDirectory $assetRoot `
        -AdditionalAssetPath $additionalPaths `
        -AdditionalAssetName $additionalNames
    Assert ($second.PublicAssetManifestSha256 -ceq $first.PublicAssetManifestSha256) `
        'Re-running provenance changed the canonical public asset hash.'
    Assert ($second.ReleaseBundleId -ceq $first.ReleaseBundleId) `
        'Re-running provenance changed the Release Bundle ID.'

    Write-Utf8NoBom (Join-Path $assetRoot 'z-portable.zip') 'changed portable bytes'
    $changed = & $provenanceGenerator `
        -BinaryIdentityManifest $identity.ManifestPath `
        -AssetDirectory $assetRoot `
        -AdditionalAssetPath $additionalPaths `
        -AdditionalAssetName $additionalNames
    Assert ($changed.BinaryBuildId -ceq $first.BinaryBuildId) `
        'Changing a release asset changed the already-built binary identity.'
    Assert ($changed.PublicAssetManifestSha256 -ne $first.PublicAssetManifestSha256) `
        'Changing a release asset did not change the public asset manifest hash.'
    Assert ($changed.ReleaseBundleId -ne $first.ReleaseBundleId) `
        'Changing a release asset did not change the Release Bundle ID.'

    $developmentOutput = Join-Path $fixtureRoot 'build\generated\development'
    $developmentIdentity = & $identityGenerator `
        -ProjectRoot $fixtureRoot `
        -OutputDirectory $developmentOutput `
        -ApplicationVersion $ApplicationVersion `
        -Channel development
    $developmentRejected = $false
    try {
        & $provenanceGenerator `
            -BinaryIdentityManifest $developmentIdentity.ManifestPath `
            -AssetDirectory $assetRoot | Out-Null
    }
    catch {
        $developmentRejected = $true
    }
    Assert $developmentRejected 'Release provenance accepted a development-channel binary.'

    $global:LASTEXITCODE = 0
    Write-Host 'All two-stage release provenance tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolved = [IO.Path]::GetFullPath($temporaryRoot)
        $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolved.StartsWith(
            $temporaryPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected test cleanup path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
