# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ApplicationVersion,

    [ValidateSet('development', 'release')]
    [string]$Channel = 'development'
)

$ErrorActionPreference = 'Stop'

if ($ApplicationVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'ApplicationVersion must be a three-part semantic version.'
}

$projectPath = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $projectPath -PathType Container)) {
    throw "Project root was not found: $projectPath"
}

$generatedRoot = [IO.Path]::GetFullPath((Join-Path $projectPath 'build\generated')).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$generatedPrefix = $generatedRoot + [IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($generatedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build identity output must be a child of build\generated.'
}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

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
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $Value, $encoding)
}

function Get-BuildInputFiles([string]$Root) {
    $files = @()
    $sourceRoot = Join-Path $Root 'src'
    if (Test-Path -LiteralPath $sourceRoot -PathType Container) {
        $files += Get-ChildItem -LiteralPath $sourceRoot -File -Filter '*.cs'
    }

    foreach ($relativePath in @(
        'app.config',
        'app.manifest',
        'build.ps1',
        'LICENSE',
        'THIRD_PARTY_NOTICES.md',
        'assets\branding\tarkov-server-guard-tsg.ico',
        'tools\Prepare-BuildIdentity.ps1')) {
        $candidate = Join-Path $Root $relativePath
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $files += Get-Item -LiteralPath $candidate
        }
    }

    return @($files | Sort-Object FullName -Unique)
}

function New-CanonicalBuildInputManifest([string]$Root) {
    $rows = New-Object Collections.Generic.List[string]
    $rows.Add('tsg-build-inputs-v1')
    foreach ($file in (Get-BuildInputFiles $Root)) {
        $relativePath = $file.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
        if ($relativePath.Contains("`t") -or $relativePath.Contains("`r") -or $relativePath.Contains("`n")) {
            throw 'Build input paths cannot contain tabs or line breaks.'
        }
        $rows.Add(("{0}`t{1}`t{2}" -f (
            Get-FileSha256Hex $file.FullName),
            $file.Length,
            $relativePath))
    }
    return ($rows -join "`n") + "`n"
}

function Get-GitSourceState([string]$Root, [string]$ArchiveTreeHash) {
    $revision = $null
    $state = 'archive'
    try {
        $topLevelOutput = @(& git -C $Root rev-parse --show-toplevel 2>$null)
        $topLevelMatches = $false
        if ($LASTEXITCODE -eq 0 -and $topLevelOutput.Count -gt 0) {
            $topLevel = [IO.Path]::GetFullPath(([string]$topLevelOutput[-1]).Trim()).TrimEnd(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar)
            $topLevelMatches = [string]::Equals(
                $topLevel,
                $Root,
                [StringComparison]::OrdinalIgnoreCase)
        }

        if ($topLevelMatches) {
            $revisionOutput = @(& git -C $Root rev-parse --verify HEAD 2>$null)
            if ($LASTEXITCODE -eq 0 -and $revisionOutput.Count -gt 0) {
                $candidate = ([string]$revisionOutput[-1]).Trim().ToLowerInvariant()
                if ($candidate -match '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
                    $revision = $candidate
                    $statusOutput = @(& git -C $Root status --porcelain --untracked-files=normal 2>$null)
                    $state = if ($LASTEXITCODE -eq 0 -and $statusOutput.Count -eq 0) {
                        'clean'
                    }
                    else {
                        'dirty'
                    }
                }
            }
        }
    }
    catch {
        $revision = $null
    }

    if ([string]::IsNullOrWhiteSpace($revision)) {
        $revision = 'tree-' + $ArchiveTreeHash
        $state = 'archive'
    }

    return [PSCustomObject]@{
        Revision = $revision
        State = $state
    }
}

function New-BinaryBuildMaterial(
    [string]$Version,
    [string]$Revision,
    [string]$BuildChannel,
    [string]$BuildInputManifestSha256) {
    return @(
        'schema=tsg-binary-build-v1',
        ('version=' + $Version),
        ('revision=' + $Revision),
        ('channel=' + $BuildChannel),
        ('buildInputsSha256=' + $BuildInputManifestSha256)
    ) -join "`n"
}

$inputManifest = New-CanonicalBuildInputManifest $projectPath
$inputManifestBytes = [Text.Encoding]::UTF8.GetBytes($inputManifest)
$inputManifestPath = Join-Path $outputPath 'build-inputs.manifest'
Write-Utf8NoBom $inputManifestPath $inputManifest
$inputManifestHash = Get-Sha256HexFromBytes $inputManifestBytes
$writtenInputManifestBytes = [IO.File]::ReadAllBytes($inputManifestPath)
if ([long]$writtenInputManifestBytes.LongLength -ne [long]$inputManifestBytes.LongLength -or
    (Get-Sha256HexFromBytes $writtenInputManifestBytes) -cne $inputManifestHash) {
    throw 'The written build-input manifest did not match its canonical bytes.'
}
$sourceState = Get-GitSourceState $projectPath $inputManifestHash
$binaryBuildMaterial = New-BinaryBuildMaterial `
    $ApplicationVersion `
    $sourceState.Revision `
    $Channel `
    $inputManifestHash
$binaryBuildId = 'tsg-bin-v1-' + (Get-Sha256Hex $binaryBuildMaterial)

$manifest = [ordered]@{
    schemaVersion = 1
    identityScheme = 'tsg-binary-build-v1'
    applicationVersion = $ApplicationVersion
    sourceRevision = $sourceState.Revision
    channel = $Channel
    buildInputManifestSha256 = $inputManifestHash
    binaryBuildId = $binaryBuildId
}
$manifestPath = Join-Path $outputPath 'build-identity.json'
$manifestJson = $manifest | ConvertTo-Json -Compress
$manifestBytes = [Text.Encoding]::UTF8.GetBytes($manifestJson)
$manifestHash = Get-Sha256HexFromBytes $manifestBytes
Write-Utf8NoBom $manifestPath $manifestJson
$writtenManifestBytes = [IO.File]::ReadAllBytes($manifestPath)
if ([long]$writtenManifestBytes.LongLength -ne [long]$manifestBytes.LongLength -or
    (Get-Sha256HexFromBytes $writtenManifestBytes) -cne $manifestHash) {
    throw 'The written build identity JSON did not match its canonical bytes.'
}

$generatedSource = @"
// Generated by tools/Prepare-BuildIdentity.ps1. Do not edit or commit this file.
[assembly: System.Reflection.AssemblyMetadata("TSG.IdentityScheme", "tsg-binary-build-v1")]
[assembly: System.Reflection.AssemblyMetadata("TSG.ApplicationVersion", "$ApplicationVersion")]
[assembly: System.Reflection.AssemblyMetadata("TSG.SourceRevision", "$($sourceState.Revision)")]
[assembly: System.Reflection.AssemblyMetadata("TSG.BuildChannel", "$Channel")]
[assembly: System.Reflection.AssemblyMetadata("TSG.BuildInputManifestSha256", "$inputManifestHash")]
[assembly: System.Reflection.AssemblyMetadata("TSG.BinaryBuildId", "$binaryBuildId")]

namespace TarkovServerReporter
{
    internal static class GeneratedBuildIdentity
    {
        internal const int SchemaVersion = 1;
        internal const string IdentityScheme = "tsg-binary-build-v1";
        internal const string ApplicationVersion = "$ApplicationVersion";
        internal const string SourceRevision = "$($sourceState.Revision)";
        internal const string Channel = "$Channel";
        internal const string BuildInputManifestSha256 = "$inputManifestHash";
        internal const string BinaryBuildId = "$binaryBuildId";
    }
}
"@

$sourcePath = Join-Path $outputPath 'BuildIdentity.Generated.cs'
$sourceBytes = [Text.Encoding]::UTF8.GetBytes($generatedSource)
$sourceHash = Get-Sha256HexFromBytes $sourceBytes
Write-Utf8NoBom $sourcePath $generatedSource
$writtenSourceBytes = [IO.File]::ReadAllBytes($sourcePath)
if ([long]$writtenSourceBytes.LongLength -ne [long]$sourceBytes.LongLength -or
    (Get-Sha256HexFromBytes $writtenSourceBytes) -cne $sourceHash) {
    throw 'The written generated identity source did not match its canonical bytes.'
}

[PSCustomObject]@{
    SourcePath = $sourcePath
    ManifestPath = $manifestPath
    InputManifestPath = $inputManifestPath
    Channel = $Channel
    BinaryBuildId = $binaryBuildId
    BuildId = $binaryBuildId
    SourceRevision = $sourceState.Revision
    SourceState = $sourceState.State
    BuildInputManifestSha256 = $inputManifestHash
    BuildIdentityManifestSha256 = $manifestHash
    BuildIdentityManifestLength = [long]$manifestBytes.LongLength
    BuildIdentitySourceSha256 = $sourceHash
    BuildIdentitySourceLength = [long]$sourceBytes.LongLength
}
