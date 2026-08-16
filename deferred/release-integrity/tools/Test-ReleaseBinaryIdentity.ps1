# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [Parameter(Mandatory = $true)]
    [string]$AssetDirectory,

    [Parameter(Mandatory = $true)]
    [string]$InspectorPath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,

    [ValidateSet('development', 'release')]
    [string]$ExpectedChannel = 'release',

    [Parameter(Mandatory = $true)]
    [string]$ExpectedBinaryBuildId,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedRevision,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedBuildInputManifestSha256
)

$ErrorActionPreference = 'Stop'
$assetPath = [IO.Path]::GetFullPath($AssetDirectory)
$inspector = [IO.Path]::GetFullPath($InspectorPath)
if (-not (Test-Path -LiteralPath $assetPath -PathType Container)) {
    throw 'Release asset directory was not found.'
}
if (-not (Test-Path -LiteralPath $inspector -PathType Leaf)) {
    throw 'Build identity app inspector was not found.'
}
if ($ExpectedBinaryBuildId -notmatch '^tsg-bin-v1-[0-9a-f]{64}$' -or
    $ExpectedRevision -notmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64}|tree-[0-9a-f]{64})$' -or
    $ExpectedBuildInputManifestSha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'Expected external build identity values were invalid.'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$maximumExecutableBytes = 64MB
$maximumArchiveCount = 32
$maximumEntriesPerArchive = 20000
$maximumGlobalEntries = 40000
$maximumEntryBytes = 256MB
$maximumExpandedBytesPerArchive = 2GB
$maximumGlobalExpandedBytes = 4GB
$maximumCompressionRatio = 250.0
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'TSG-ReleaseIdentityScan-' + [Guid]::NewGuid().ToString('N'))
$inspectedArchives = 0
$globalEntryCount = 0
$globalExpandedBytes = [long]0

try {
    New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null
    $archives = @(Get-ChildItem -LiteralPath $assetPath -File -Recurse | Where-Object {
        $_.Extension -ieq '.zip' -or $_.Extension -ieq '.nupkg'
    })
    if ($archives.Count -eq 0 -or $archives.Count -gt $maximumArchiveCount) {
        throw 'Release archive count exceeded the inspection boundary.'
    }
    foreach ($archiveFile in $archives) {
        if (($archiveFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Release archive cannot be a reparse point.'
        }

        $stream = [IO.File]::Open(
            $archiveFile.FullName,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        $archive = $null
        try {
            $archive = New-Object IO.Compression.ZipArchive(
                $stream,
                [IO.Compression.ZipArchiveMode]::Read,
                $false)
            if ($archive.Entries.Count -gt $maximumEntriesPerArchive -or
                $globalEntryCount + $archive.Entries.Count -gt $maximumGlobalEntries) {
                throw 'Release archive entry count exceeded the inspection boundary.'
            }
            $globalEntryCount += $archive.Entries.Count
            $archiveExpandedBytes = [long]0
            $candidates = New-Object Collections.Generic.List[object]
            foreach ($entry in $archive.Entries) {
                if ($entry.Length -lt 0 -or $entry.Length -gt $maximumEntryBytes -or
                    [long]::MaxValue - $archiveExpandedBytes -lt $entry.Length) {
                    throw 'Release archive entry size exceeded the inspection boundary.'
                }
                $archiveExpandedBytes += [long]$entry.Length
                if ($entry.Length -gt 0 -and $entry.CompressedLength -eq 0) {
                    throw 'Release archive compression ratio exceeded the inspection boundary.'
                }
                if ($entry.CompressedLength -gt 0 -and
                    ([double]$entry.Length / [double]$entry.CompressedLength) -gt
                        $maximumCompressionRatio) {
                    throw 'Release archive compression ratio exceeded the inspection boundary.'
                }
                $entryPath = $entry.FullName.Replace('\', '/')
                $invalidEntryPath = [string]::IsNullOrWhiteSpace($entryPath)
                $invalidEntryPath = $invalidEntryPath -or [IO.Path]::IsPathRooted($entryPath)
                $invalidEntryPath = $invalidEntryPath -or $entryPath.StartsWith('/')
                $invalidEntryPath = $invalidEntryPath -or $entryPath.Contains(':')
                $invalidEntryPath = $invalidEntryPath -or $entryPath -match '(^|/)\.\.(/|$)'
                if ($invalidEntryPath) {
                    throw 'Release archive contained a non-canonical entry path.'
                }
                if ([string]::Equals(
                    $entry.Name,
                    'TarkovServerGuard.exe',
                    [StringComparison]::OrdinalIgnoreCase)) {
                    $candidates.Add($entry)
                }
            }

            if ($archiveExpandedBytes -gt $maximumExpandedBytesPerArchive -or
                [long]::MaxValue - $globalExpandedBytes -lt $archiveExpandedBytes -or
                $globalExpandedBytes + $archiveExpandedBytes -gt $maximumGlobalExpandedBytes) {
                throw 'Release archive expanded size exceeded the inspection boundary.'
            }
            $globalExpandedBytes += $archiveExpandedBytes

            if ($candidates.Count -eq 0) {
                throw 'Release archive did not contain TarkovServerGuard.exe.'
            }
            if ($candidates.Count -ne 1) {
                throw 'Release archive contained more than one TarkovServerGuard.exe.'
            }

            $candidate = $candidates[0]
            if ($candidate.Length -le 0 -or $candidate.Length -gt $maximumExecutableBytes) {
                throw 'Archived TarkovServerGuard.exe exceeded the inspection size boundary.'
            }
            $extractedPath = Join-Path $temporaryRoot (
                'TarkovServerGuard-' + $inspectedArchives + '.exe')
            $input = $candidate.Open()
            $output = [IO.File]::Create($extractedPath)
            try {
                $buffer = New-Object byte[] 65536
                $written = 0L
                while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $written += $read
                    if ($written -gt $maximumExecutableBytes) {
                        throw 'Archived executable expanded beyond the inspection size boundary.'
                    }
                    $output.Write($buffer, 0, $read)
                }
            }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
            if ($written -ne $candidate.Length) {
                throw 'Archived executable length changed during extraction.'
            }

            & $inspector `
                $extractedPath `
                $ExpectedVersion `
                $ExpectedChannel `
                $ExpectedBinaryBuildId `
                $ExpectedRevision `
                $ExpectedBuildInputManifestSha256
            if ($LASTEXITCODE -ne 0) {
                throw 'An archived app failed public build identity inspection.'
            }
            $inspectedArchives++
            Remove-Item -LiteralPath $extractedPath -Force
        }
        finally {
            if ($null -ne $archive) { $archive.Dispose() }
            $stream.Dispose()
        }
    }

    if ($inspectedArchives -eq 0) {
        throw 'No release archive containing TarkovServerGuard.exe was found.'
    }

    [PSCustomObject]@{
        ArchiveCount = $inspectedArchives
        ExpectedVersion = $ExpectedVersion
        ExpectedChannel = $ExpectedChannel
        ExpectedBinaryBuildId = $ExpectedBinaryBuildId
        ExpectedRevision = $ExpectedRevision
        ExpectedBuildInputManifestSha256 = $ExpectedBuildInputManifestSha256
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolved = [IO.Path]::GetFullPath($temporaryRoot)
        $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolved.StartsWith(
            $temporaryPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected archive scan cleanup path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
