# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,

    [Parameter(Mandatory = $true)]
    [string]$AppPath,

    [Parameter(Mandatory = $true)]
    [string]$InspectorPath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedChannel,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedBinaryBuildId,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedRevision,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedBuildInputManifestSha256
)

$ErrorActionPreference = 'Stop'
$projectPath = [IO.Path]::GetFullPath($ProjectRoot)
$scanner = Join-Path $projectPath 'tools\Test-ReleaseBinaryIdentity.ps1'
$application = [IO.Path]::GetFullPath($AppPath)
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'TSG-ReleaseIdentityScannerTests-' + [Guid]::NewGuid().ToString('N'))

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function New-TestArchive(
    [string]$Path,
    [hashtable]$Entries) {
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $stream = [IO.File]::Create($Path)
    $archive = $null
    try {
        $archive = New-Object IO.Compression.ZipArchive(
            $stream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        foreach ($entryName in $Entries.Keys) {
            $entry = $archive.CreateEntry(
                [string]$entryName,
                [IO.Compression.CompressionLevel]::Optimal)
            $input = [IO.File]::OpenRead([string]$Entries[$entryName])
            $output = $entry.Open()
            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    }
    finally {
        if ($null -ne $archive) { $archive.Dispose() }
        $stream.Dispose()
    }
}

function Expect-Rejection([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try {
        & $Action 2>$null | Out-Null
    }
    catch {
        $rejected = $true
    }
    Assert $rejected $Message
}

try {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

    $validAssets = Join-Path $temporaryRoot 'valid-assets'
    New-TestArchive `
        (Join-Path $validAssets 'portable.zip') `
        @{ 'TarkovServerGuard.exe' = $application }
    New-TestArchive `
        (Join-Path $validAssets 'package.nupkg') `
        @{ 'lib/net48/TarkovServerGuard.exe' = $application }
    $result = & $scanner `
        -AssetDirectory $validAssets `
        -InspectorPath $InspectorPath `
        -ExpectedVersion $ExpectedVersion `
        -ExpectedChannel $ExpectedChannel `
        -ExpectedBinaryBuildId $ExpectedBinaryBuildId `
        -ExpectedRevision $ExpectedRevision `
        -ExpectedBuildInputManifestSha256 $ExpectedBuildInputManifestSha256
    if ($result -is [array]) { $result = $result[-1] }
    Assert ($result.ArchiveCount -eq 2) `
        'Safe archive scanner did not inspect both fixture packages.'

    $missingPortableAppAssets = Join-Path $temporaryRoot 'missing-portable-app-assets'
    New-TestArchive `
        (Join-Path $missingPortableAppAssets 'portable.zip') `
        @{ 'readme.bin' = $application }
    New-TestArchive `
        (Join-Path $missingPortableAppAssets 'package.nupkg') `
        @{ 'lib/net48/TarkovServerGuard.exe' = $application }
    Expect-Rejection {
        & $scanner `
            -AssetDirectory $missingPortableAppAssets `
            -InspectorPath $InspectorPath `
            -ExpectedVersion $ExpectedVersion `
            -ExpectedChannel $ExpectedChannel `
            -ExpectedBinaryBuildId $ExpectedBinaryBuildId `
            -ExpectedRevision $ExpectedRevision `
            -ExpectedBuildInputManifestSha256 $ExpectedBuildInputManifestSha256
    } 'Portable ZIP without TarkovServerGuard.exe was accepted.'

    $traversalAssets = Join-Path $temporaryRoot 'traversal-assets'
    New-TestArchive `
        (Join-Path $traversalAssets 'package.nupkg') `
        @{ '../TarkovServerGuard.exe' = $application }
    Expect-Rejection {
        & $scanner `
            -AssetDirectory $traversalAssets `
            -InspectorPath $InspectorPath `
            -ExpectedVersion $ExpectedVersion `
            -ExpectedChannel $ExpectedChannel `
            -ExpectedBinaryBuildId $ExpectedBinaryBuildId `
            -ExpectedRevision $ExpectedRevision `
            -ExpectedBuildInputManifestSha256 $ExpectedBuildInputManifestSha256
    } 'Archive traversal entry was accepted.'

    $duplicateAssets = Join-Path $temporaryRoot 'duplicate-assets'
    New-TestArchive `
        (Join-Path $duplicateAssets 'package.nupkg') `
        @{
            'lib/a/TarkovServerGuard.exe' = $application
            'lib/b/TarkovServerGuard.exe' = $application
        }
    Expect-Rejection {
        & $scanner `
            -AssetDirectory $duplicateAssets `
            -InspectorPath $InspectorPath `
            -ExpectedVersion $ExpectedVersion `
            -ExpectedChannel $ExpectedChannel `
            -ExpectedBinaryBuildId $ExpectedBinaryBuildId `
            -ExpectedRevision $ExpectedRevision `
            -ExpectedBuildInputManifestSha256 $ExpectedBuildInputManifestSha256
    } 'Archive with duplicate app executables was accepted.'

    $invalidApp = Join-Path $temporaryRoot 'not-an-app.exe'
    [IO.File]::WriteAllText(
        $invalidApp,
        'not an assembly',
        (New-Object Text.UTF8Encoding($false)))
    $invalidAssets = Join-Path $temporaryRoot 'invalid-app-assets'
    New-TestArchive `
        (Join-Path $invalidAssets 'package.nupkg') `
        @{ 'lib/net48/TarkovServerGuard.exe' = $invalidApp }
    Expect-Rejection {
        & $scanner `
            -AssetDirectory $invalidAssets `
            -InspectorPath $InspectorPath `
            -ExpectedVersion $ExpectedVersion `
            -ExpectedChannel $ExpectedChannel `
            -ExpectedBinaryBuildId $ExpectedBinaryBuildId `
            -ExpectedRevision $ExpectedRevision `
            -ExpectedBuildInputManifestSha256 $ExpectedBuildInputManifestSha256
    } 'Archive with a non-app executable payload was accepted.'

    Expect-Rejection {
        & $scanner `
            -AssetDirectory $validAssets `
            -InspectorPath $InspectorPath `
            -ExpectedVersion $ExpectedVersion `
            -ExpectedChannel $ExpectedChannel `
            -ExpectedBinaryBuildId ('tsg-bin-v1-' + ('0' * 64)) `
            -ExpectedRevision $ExpectedRevision `
            -ExpectedBuildInputManifestSha256 $ExpectedBuildInputManifestSha256
    } 'Archives from a different external Binary Build ID were accepted.'

    $global:LASTEXITCODE = 0
    Write-Host 'All release binary identity scanner tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolved = [IO.Path]::GetFullPath($temporaryRoot)
        $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolved.StartsWith(
            $temporaryPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected scanner fixture cleanup path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
