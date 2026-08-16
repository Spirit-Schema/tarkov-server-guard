# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [Parameter(Mandatory = $true)]
    [string]$BinaryIdentityManifest,

    [Parameter(Mandatory = $true)]
    [string]$AssetDirectory,

    [string[]]$AdditionalAssetPath = @(),

    [string[]]$AdditionalAssetName = @()
)

$ErrorActionPreference = 'Stop'

function Get-Sha256HexFromBytes([byte[]]$Bytes) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha256.ComputeHash($Bytes)
    }
    finally {
        $sha256.Dispose()
    }
    return ([BitConverter]::ToString($digest)).Replace('-', '').ToLowerInvariant()
}

function Get-Sha256Hex([string]$Value) {
    return Get-Sha256HexFromBytes ([Text.Encoding]::UTF8.GetBytes($Value))
}

function Get-FileSha256Hex([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha256.ComputeHash($stream)
    }
    finally {
        $stream.Dispose()
        $sha256.Dispose()
    }
    return ([BitConverter]::ToString($digest)).Replace('-', '').ToLowerInvariant()
}

function Write-Utf8NoBom([string]$Path, [string]$Value) {
    [IO.File]::WriteAllText($Path, $Value, (New-Object Text.UTF8Encoding($false)))
}

function Read-StrictUtf8Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'Binary build identity manifest was not found.'
    }
    try {
        $text = [IO.File]::ReadAllText(
            $Path,
            (New-Object Text.UTF8Encoding($false, $true)))
        return $text | ConvertFrom-Json
    }
    catch {
        throw 'Binary build identity manifest must be valid UTF-8 JSON.'
    }
}

function Get-BinaryBuildId(
    [string]$Version,
    [string]$Revision,
    [string]$Channel,
    [string]$BuildInputManifestSha256) {
    $material = @(
        'schema=tsg-binary-build-v1',
        ('version=' + $Version),
        ('revision=' + $Revision),
        ('channel=' + $Channel),
        ('buildInputsSha256=' + $BuildInputManifestSha256)
    ) -join "`n"
    return 'tsg-bin-v1-' + (Get-Sha256Hex $material)
}

$identityPath = [IO.Path]::GetFullPath($BinaryIdentityManifest)
$assetPath = [IO.Path]::GetFullPath($AssetDirectory).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $assetPath -PathType Container)) {
    throw 'Release asset directory was not found.'
}

$identity = Read-StrictUtf8Json $identityPath
$propertyNames = @($identity.PSObject.Properties.Name | Sort-Object)
$expectedProperties = @(
    'applicationVersion',
    'binaryBuildId',
    'buildInputManifestSha256',
    'channel',
    'identityScheme',
    'schemaVersion',
    'sourceRevision')
if (($propertyNames -join '|') -cne ($expectedProperties -join '|')) {
    throw 'Binary build identity manifest fields did not match the public schema.'
}
$identityValuesInvalid = [int]$identity.schemaVersion -ne 1
$identityValuesInvalid = $identityValuesInvalid -or `
    [string]$identity.identityScheme -cne 'tsg-binary-build-v1'
$identityValuesInvalid = $identityValuesInvalid -or `
    [string]$identity.applicationVersion -notmatch '^\d+\.\d+\.\d+$'
$identityValuesInvalid = $identityValuesInvalid -or `
    [string]$identity.sourceRevision -notmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64}|tree-[0-9a-f]{64})$'
$identityValuesInvalid = $identityValuesInvalid -or `
    [string]$identity.channel -cne 'release'
$identityValuesInvalid = $identityValuesInvalid -or `
    [string]$identity.buildInputManifestSha256 -notmatch '^[0-9a-f]{64}$'
$identityValuesInvalid = $identityValuesInvalid -or `
    [string]$identity.binaryBuildId -notmatch '^tsg-bin-v1-[0-9a-f]{64}$'
if ($identityValuesInvalid) {
    throw 'Binary build identity manifest values were invalid for a release.'
}
$expectedBinaryBuildId = Get-BinaryBuildId `
    ([string]$identity.applicationVersion) `
    ([string]$identity.sourceRevision) `
    ([string]$identity.channel) `
    ([string]$identity.buildInputManifestSha256)
if ([string]$identity.binaryBuildId -cne $expectedBinaryBuildId) {
    throw 'Binary Build ID did not match its public components.'
}

$publishedIdentityPath = Join-Path $assetPath 'binary-build-identity.json'
$publishedInputManifestPath = Join-Path $assetPath 'build-inputs.manifest'
if (-not (Test-Path -LiteralPath $publishedIdentityPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $publishedInputManifestPath -PathType Leaf)) {
    throw 'Release assets did not contain both public binary identity files.'
}
if ((Get-FileSha256Hex $publishedIdentityPath) -cne
    (Get-FileSha256Hex $identityPath)) {
    throw 'Published binary identity manifest did not match the inspected build manifest.'
}
if ((Get-FileSha256Hex $publishedInputManifestPath) -cne
    [string]$identity.buildInputManifestSha256) {
    throw 'Published build-input manifest did not match the Binary Build ID.'
}

$excludedRootFiles = @(
    'release-assets.manifest',
    'release-provenance.json')
$assetRecords = New-Object Collections.Generic.List[object]
foreach ($file in (Get-ChildItem -LiteralPath $assetPath -File -Recurse)) {
    $relativePath = $file.FullName.Substring($assetPath.Length).TrimStart('\', '/').Replace('\', '/')
    if ($relativePath.Contains("`t") -or $relativePath.Contains("`r") -or $relativePath.Contains("`n")) {
        throw 'Release asset paths cannot contain tabs or line breaks.'
    }
    if ($relativePath.IndexOf('/') -lt 0 -and $excludedRootFiles -contains $relativePath) {
        continue
    }
    $assetRecords.Add([PSCustomObject]@{
        RelativePath = $relativePath
        Length = $file.Length
        Sha256 = Get-FileSha256Hex $file.FullName
    })
}
if ($AdditionalAssetPath.Count -ne $AdditionalAssetName.Count) {
    throw 'Additional public asset paths and names must have the same count.'
}
for ($index = 0; $index -lt $AdditionalAssetPath.Count; $index++) {
    $additionalPath = [IO.Path]::GetFullPath($AdditionalAssetPath[$index])
    if (-not (Test-Path -LiteralPath $additionalPath -PathType Leaf)) {
        throw 'An additional public release asset was not found.'
    }
    $relativePath = ([string]$AdditionalAssetName[$index]).Replace('\', '/')
    $invalidPublicName = [string]::IsNullOrWhiteSpace($relativePath)
    $invalidPublicName = $invalidPublicName -or [IO.Path]::IsPathRooted($relativePath)
    $invalidPublicName = $invalidPublicName -or `
        $relativePath.StartsWith('/', [StringComparison]::Ordinal)
    $invalidPublicName = $invalidPublicName -or $relativePath -match '(^|/)\.\.(/|$)'
    $invalidPublicName = $invalidPublicName -or $relativePath.Contains("`t")
    $invalidPublicName = $invalidPublicName -or $relativePath.Contains("`r")
    $invalidPublicName = $invalidPublicName -or $relativePath.Contains("`n")
    if ($invalidPublicName) {
        throw 'An additional public asset name was not canonical.'
    }
    $file = Get-Item -LiteralPath $additionalPath
    $assetRecords.Add([PSCustomObject]@{
        RelativePath = $relativePath
        Length = $file.Length
        Sha256 = Get-FileSha256Hex $file.FullName
    })
}
if ($assetRecords.Count -eq 0) {
    throw 'No public release assets were found.'
}

$recordsByPath = @{}
foreach ($record in $assetRecords) {
    if ($recordsByPath.ContainsKey([string]$record.RelativePath)) {
        throw 'Public release asset names must be unique.'
    }
    $recordsByPath[[string]$record.RelativePath] = $record
}
$sortedPaths = [string[]]@($recordsByPath.Keys)
[Array]::Sort($sortedPaths, [StringComparer]::Ordinal)
$assetRows = New-Object Collections.Generic.List[string]
$assetRows.Add('tsg-release-assets-v1')
foreach ($relativePath in $sortedPaths) {
    $record = $recordsByPath[$relativePath]
    $assetRows.Add(("{0}`t{1}`t{2}" -f $record.Sha256, $record.Length, $record.RelativePath))
}
$assetManifestText = ($assetRows -join "`n") + "`n"
$assetManifestHash = Get-Sha256Hex $assetManifestText
$assetManifestPath = Join-Path $assetPath 'release-assets.manifest'
Write-Utf8NoBom $assetManifestPath $assetManifestText

$bundleMaterial = @(
    'schema=tsg-release-bundle-v1',
    ('binaryBuildId=' + [string]$identity.binaryBuildId),
    ('publicAssetManifestSha256=' + $assetManifestHash)
) -join "`n"
$releaseBundleId = 'tsg-bundle-v1-' + (Get-Sha256Hex $bundleMaterial)
$provenance = [ordered]@{
    schemaVersion = 1
    provenanceScheme = 'tsg-release-bundle-v1'
    binaryIdentityScheme = [string]$identity.identityScheme
    applicationVersion = [string]$identity.applicationVersion
    sourceRevision = [string]$identity.sourceRevision
    channel = [string]$identity.channel
    buildInputManifestSha256 = [string]$identity.buildInputManifestSha256
    binaryBuildId = [string]$identity.binaryBuildId
    publicAssetManifestSha256 = $assetManifestHash
    releaseBundleId = $releaseBundleId
}
$provenancePath = Join-Path $assetPath 'release-provenance.json'
Write-Utf8NoBom $provenancePath ($provenance | ConvertTo-Json -Compress)

[PSCustomObject]@{
    AssetManifestPath = $assetManifestPath
    ProvenancePath = $provenancePath
    BinaryBuildId = [string]$identity.binaryBuildId
    PublicAssetManifestSha256 = $assetManifestHash
    ReleaseBundleId = $releaseBundleId
}
