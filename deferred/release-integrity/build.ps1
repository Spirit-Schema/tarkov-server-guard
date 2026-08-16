# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [switch]$SkipTests,
    [string]$OutputDirectory,
    [ValidateSet('development', 'release')]
    [string]$BuildChannel = 'development'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src'
$testRoot = Join-Path $projectRoot 'tests'
$distRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $projectRoot 'dist'
} elseif ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
}
$buildRoot = Join-Path $projectRoot 'build'
$appIcon = Join-Path $projectRoot 'assets\branding\tarkov-server-guard-tsg.ico'

if (-not (Test-Path -LiteralPath $appIcon -PathType Leaf)) {
    throw "앱 아이콘을 찾지 못했습니다: $appIcon"
}

$compilerCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$compiler = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) {
    throw '.NET Framework C# compiler(csc.exe)를 찾지 못했습니다.'
}

function Get-BuildSha256HexFromBytes([byte[]]$Bytes) {
    if ($null -eq $Bytes) { throw 'SHA-256 input bytes are required.' }
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha256.ComputeHash($Bytes)
    }
    finally {
        $sha256.Dispose()
    }
    return ([BitConverter]::ToString($digest)).Replace('-', '').ToLowerInvariant()
}

function Initialize-BuildReparseInspector {
    if ('TsgBuildReparsePoint' -as [type]) { return }
    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;

public static class TsgBuildReparsePoint
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FsctlGetReparsePoint = 0x000900A8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string name,
        uint access,
        uint share,
        IntPtr security,
        uint creation,
        uint flags,
        IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr device,
        uint controlCode,
        IntPtr input,
        uint inputSize,
        byte[] output,
        uint outputSize,
        out uint returned,
        IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static uint GetTag(string path)
    {
        IntPtr handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle == new IntPtr(-1))
            throw new IOException("Could not inspect a build-input reparse point.");
        try
        {
            byte[] buffer = new byte[16384];
            uint returned;
            if (!DeviceIoControl(
                handle,
                FsctlGetReparsePoint,
                IntPtr.Zero,
                0,
                buffer,
                (uint)buffer.Length,
                out returned,
                IntPtr.Zero) || returned < 8)
                throw new IOException("Could not read a build-input reparse tag.");
            return BitConverter.ToUInt32(buffer, 0);
        }
        finally { CloseHandle(handle); }
    }

    public static bool IsCloudPlaceholder(uint tag)
    {
        // Cloud Files tags use 0x9000?01A. They are not name-surrogate redirects.
        return (tag & 0xFFFF0FFFu) == 0x9000001Au;
    }
}
'@
}

function Test-IsUnsafeBuildReparsePoint([IO.FileSystemInfo]$Item) {
    if ($null -eq $Item) { return $false }
    $linkType = $Item.PSObject.Properties['LinkType']
    if ($null -ne $linkType -and
        -not [string]::IsNullOrWhiteSpace([string]$linkType.Value)) {
        return $true
    }
    $target = $Item.PSObject.Properties['Target']
    if ($null -ne $target) {
        foreach ($value in @($target.Value)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$value)) { return $true }
        }
    }
    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
        return $false
    }
    try {
        Initialize-BuildReparseInspector
        $tag = [TsgBuildReparsePoint]::GetTag($Item.FullName)
        return -not [TsgBuildReparsePoint]::IsCloudPlaceholder($tag)
    }
    catch {
        # Unknown or unreadable reparse points are redirects until proven safe.
        return $true
    }
}

function Assert-NoUnsafeBuildReparsePoints([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'A build path is required.' }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    if (-not [string]::Equals($fullPath, $pathRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $fullPath = $fullPath.TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
    }

    $candidate = $fullPath
    while ($true) {
        $item = Get-Item -Force -LiteralPath $candidate -ErrorAction SilentlyContinue
        if ($null -ne $item) { break }
        $parent = [IO.Path]::GetDirectoryName($candidate)
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals($parent, $candidate, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Build path has no inspectable filesystem ancestor.'
        }
        $candidate = $parent
    }

    while (-not [string]::IsNullOrWhiteSpace($candidate)) {
        $item = Get-Item -Force -LiteralPath $candidate -ErrorAction Stop
        if (Test-IsUnsafeBuildReparsePoint $item) {
            throw "Build paths cannot use junctions, symlinks, or name-surrogate reparse points: $candidate"
        }
        $parent = [IO.Path]::GetDirectoryName($candidate)
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals($parent, $candidate, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $candidate = $parent
    }
}

function Test-IsSafeBuildRelativePath([string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath.Contains('\')) {
        return $false
    }
    $segments = @($RelativePath -split '/', -1)
    if ($segments.Count -eq 0) { return $false }
    foreach ($segment in $segments) {
        if ([string]::IsNullOrWhiteSpace($segment) -or
            $segment -eq '.' -or
            $segment -eq '..' -or
            $segment.EndsWith('.') -or
            $segment.EndsWith(' ') -or
            $segment.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            $segment -match '^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)') {
            return $false
        }
    }
    return $true
}

function Read-VerifiedBuildFileBytes(
    [string]$Path,
    [string]$ExpectedSha256,
    [long]$ExpectedLength = -1) {
    if ($ExpectedSha256 -cnotmatch '^[0-9a-f]{64}$' -or $ExpectedLength -lt -1) {
        throw 'Expected build-input hash or length is invalid.'
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    Assert-NoUnsafeBuildReparsePoints $fullPath
    $item = Get-Item -Force -LiteralPath $fullPath -ErrorAction Stop
    if ($item.PSIsContainer -or (Test-IsUnsafeBuildReparsePoint $item)) {
        throw "Build input must be a regular file: $fullPath"
    }
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    Assert-NoUnsafeBuildReparsePoints $fullPath
    if (($ExpectedLength -ge 0 -and [long]$bytes.LongLength -ne $ExpectedLength) -or
        (Get-BuildSha256HexFromBytes $bytes) -cne $ExpectedSha256) {
        throw 'A build input no longer matches its expected exact bytes.'
    }
    return ,$bytes
}

function Write-VerifiedCompileSnapshotFile(
    [string]$SnapshotRoot,
    [string]$RelativePath,
    [byte[]]$Bytes,
    [string]$ExpectedSha256,
    [long]$ExpectedLength) {
    if (-not (Test-IsSafeBuildRelativePath $RelativePath) -or
        $ExpectedSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $ExpectedLength -lt 0 -or
        [long]$Bytes.LongLength -ne $ExpectedLength -or
        (Get-BuildSha256HexFromBytes $Bytes) -cne $ExpectedSha256) {
        throw 'A compile snapshot entry is invalid.'
    }
    $snapshotPath = [IO.Path]::GetFullPath($SnapshotRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $snapshotPrefix = $snapshotPath + [IO.Path]::DirectorySeparatorChar
    $targetPath = [IO.Path]::GetFullPath((Join-Path $snapshotPath $RelativePath))
    if (-not $targetPath.StartsWith($snapshotPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        (Test-Path -LiteralPath $targetPath)) {
        throw 'A compile snapshot entry escaped or collided with its exact destination.'
    }
    $targetParent = Split-Path -Parent $targetPath
    New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
    Assert-NoUnsafeBuildReparsePoints $targetParent
    [IO.File]::WriteAllBytes($targetPath, $Bytes)
    [void](Read-VerifiedBuildFileBytes $targetPath $ExpectedSha256 $ExpectedLength)
    return [PSCustomObject]@{
        RelativePath = $RelativePath
        Sha256 = $ExpectedSha256
        Length = $ExpectedLength
    }
}

function New-VerifiedCompileSnapshot(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,
    [Parameter(Mandatory = $true)]
    [string]$InputManifest,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedInputManifestSha256,
    [Parameter(Mandatory = $true)]
    [string]$GeneratedIdentityManifest,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedGeneratedIdentityManifestSha256,
    [Parameter(Mandatory = $true)]
    [long]$ExpectedGeneratedIdentityManifestLength,
    [Parameter(Mandatory = $true)]
    [string]$GeneratedIdentitySource,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedGeneratedIdentitySourceSha256,
    [Parameter(Mandatory = $true)]
    [long]$ExpectedGeneratedIdentitySourceLength,
    [Parameter(Mandatory = $true)]
    [string]$SnapshotRoot) {
    if ($ExpectedInputManifestSha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'The expected build-input manifest SHA-256 is invalid.'
    }
    $projectPath = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $projectPath -PathType Container)) {
        throw 'The compile snapshot project root does not exist.'
    }
    Assert-NoUnsafeBuildReparsePoints $projectPath
    $projectPrefix = $projectPath + [IO.Path]::DirectorySeparatorChar
    $generatedRoot = [IO.Path]::GetFullPath((Join-Path $projectPath 'build\generated')).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $generatedPrefix = $generatedRoot + [IO.Path]::DirectorySeparatorChar
    $snapshotPath = [IO.Path]::GetFullPath($SnapshotRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $snapshotPrefix = $snapshotPath + [IO.Path]::DirectorySeparatorChar
    if (-not $snapshotPath.StartsWith($generatedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        (Test-Path -LiteralPath $snapshotPath)) {
        throw 'The compile snapshot must be a new child of build\generated.'
    }
    Assert-NoUnsafeBuildReparsePoints $snapshotPath

    $generatedInputs = @(
        [PSCustomObject]@{
            SourcePath = [IO.Path]::GetFullPath($InputManifest)
            RelativePath = 'generated/build-identity/build-inputs.manifest'
            Sha256 = $ExpectedInputManifestSha256
            Length = [long]-1
        },
        [PSCustomObject]@{
            SourcePath = [IO.Path]::GetFullPath($GeneratedIdentityManifest)
            RelativePath = 'generated/build-identity/build-identity.json'
            Sha256 = $ExpectedGeneratedIdentityManifestSha256
            Length = $ExpectedGeneratedIdentityManifestLength
        },
        [PSCustomObject]@{
            SourcePath = [IO.Path]::GetFullPath($GeneratedIdentitySource)
            RelativePath = 'generated/build-identity/BuildIdentity.Generated.cs'
            Sha256 = $ExpectedGeneratedIdentitySourceSha256
            Length = $ExpectedGeneratedIdentitySourceLength
        })
    foreach ($generatedInput in $generatedInputs) {
        if (-not $generatedInput.SourcePath.StartsWith(
                $generatedPrefix,
                [StringComparison]::OrdinalIgnoreCase) -or
            $generatedInput.SourcePath.StartsWith(
                $snapshotPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Generated compile inputs must stay inside the trusted build\generated boundary.'
        }
        Assert-NoUnsafeBuildReparsePoints $generatedInput.SourcePath
    }

    $manifestBytes = Read-VerifiedBuildFileBytes `
        $generatedInputs[0].SourcePath `
        $ExpectedInputManifestSha256
    $manifestText = (New-Object Text.UTF8Encoding($false, $true)).GetString($manifestBytes)
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $reader = New-Object IO.StringReader($manifestText)
    try {
        while (($line = $reader.ReadLine()) -ne $null) { [void]$lines.Add($line) }
    }
    finally {
        $reader.Dispose()
    }
    if ($lines.Count -lt 2 -or $lines[0] -cne 'tsg-build-inputs-v1') {
        throw 'The compile input manifest format is invalid.'
    }

    New-Item -ItemType Directory -Path $snapshotPath | Out-Null
    Assert-NoUnsafeBuildReparsePoints $snapshotPath
    $entries = New-Object 'System.Collections.Generic.List[object]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)

    $generatedInputs[0].Length = [long]$manifestBytes.LongLength
    [void]$entries.Add((Write-VerifiedCompileSnapshotFile `
        $snapshotPath `
        $generatedInputs[0].RelativePath `
        $manifestBytes `
        $generatedInputs[0].Sha256 `
        $generatedInputs[0].Length))

    foreach ($line in @($lines | Select-Object -Skip 1)) {
        $parts = @($line -split "`t", -1)
        [long]$expectedLength = 0
        $relativePath = if ($parts.Count -eq 3) { [string]$parts[2] } else { $null }
        $invalid = $parts.Count -ne 3 -or [string]$parts[0] -cnotmatch '^[0-9a-f]{64}$'
        $invalid = $invalid -or -not [long]::TryParse([string]$parts[1], [ref]$expectedLength)
        $invalid = $invalid -or $expectedLength -lt 0
        $invalid = $invalid -or -not (Test-IsSafeBuildRelativePath $relativePath)
        $invalid = $invalid -or -not $seen.Add($relativePath)
        if ($invalid) { throw 'The compile input manifest contains an unsafe row.' }

        $sourcePath = [IO.Path]::GetFullPath((Join-Path $projectPath $relativePath))
        if (-not $sourcePath.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A compile input escaped the project boundary.'
        }
        $sourceBytes = Read-VerifiedBuildFileBytes `
            $sourcePath `
            ([string]$parts[0]) `
            $expectedLength
        [void]$entries.Add((Write-VerifiedCompileSnapshotFile `
            $snapshotPath `
            $relativePath `
            $sourceBytes `
            ([string]$parts[0]) `
            $expectedLength))
    }

    foreach ($generatedInput in @($generatedInputs | Select-Object -Skip 1)) {
        $bytes = Read-VerifiedBuildFileBytes `
            $generatedInput.SourcePath `
            $generatedInput.Sha256 `
            $generatedInput.Length
        [void]$entries.Add((Write-VerifiedCompileSnapshotFile `
            $snapshotPath `
            $generatedInput.RelativePath `
            $bytes `
            $generatedInput.Sha256 `
            $generatedInput.Length))
    }

    return [PSCustomObject]@{
        ProjectRoot = $projectPath
        SnapshotPath = $snapshotPath
        Files = @($entries.ToArray())
    }
}

function Assert-VerifiedCompileSnapshot([object]$Snapshot) {
    if ($null -eq $Snapshot -or
        [string]::IsNullOrWhiteSpace([string]$Snapshot.ProjectRoot) -or
        [string]::IsNullOrWhiteSpace([string]$Snapshot.SnapshotPath)) {
        throw 'The compile snapshot descriptor is invalid.'
    }
    $projectPath = [IO.Path]::GetFullPath([string]$Snapshot.ProjectRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $generatedRoot = [IO.Path]::GetFullPath((Join-Path $projectPath 'build\generated')).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $snapshotPath = [IO.Path]::GetFullPath([string]$Snapshot.SnapshotPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $snapshotPrefix = $snapshotPath + [IO.Path]::DirectorySeparatorChar
    if (-not $snapshotPath.StartsWith(
            $generatedRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The compile snapshot descriptor escaped build\generated.'
    }
    Assert-NoUnsafeBuildReparsePoints $projectPath
    Assert-NoUnsafeBuildReparsePoints $snapshotPath
    $snapshotItem = Get-Item -Force -LiteralPath $snapshotPath -ErrorAction Stop
    if (-not $snapshotItem.PSIsContainer -or
        (Test-IsUnsafeBuildReparsePoint $snapshotItem)) {
        throw 'The compile snapshot root must be a regular directory.'
    }

    $rows = @($Snapshot.Files)
    if ($rows.Count -lt 3) { throw 'The compile snapshot descriptor is incomplete.' }
    $expectedFiles = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)
    $expectedDirectories = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)
    $requiredGenerated = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($required in @(
        'generated/build-identity/build-inputs.manifest',
        'generated/build-identity/build-identity.json',
        'generated/build-identity/BuildIdentity.Generated.cs')) {
        [void]$requiredGenerated.Add($required)
    }

    foreach ($row in $rows) {
        [long]$length = 0
        $relativePath = [string]$row.RelativePath
        $sha256 = [string]$row.Sha256
        if (-not (Test-IsSafeBuildRelativePath $relativePath) -or
            $sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            -not [long]::TryParse([string]$row.Length, [ref]$length) -or
            $length -lt 0 -or
            -not $expectedFiles.Add($relativePath)) {
            throw 'The compile snapshot descriptor contains an invalid row.'
        }
        [void]$requiredGenerated.Remove($relativePath)
        $parent = [IO.Path]::GetDirectoryName($relativePath.Replace('/', '\'))
        while (-not [string]::IsNullOrWhiteSpace($parent)) {
            [void]$expectedDirectories.Add($parent.Replace('\', '/'))
            $parent = [IO.Path]::GetDirectoryName($parent)
        }
    }
    if ($requiredGenerated.Count -ne 0) {
        throw 'The compile snapshot descriptor omitted a generated identity input.'
    }

    $actualFiles = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($snapshotPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in @(Get-ChildItem -Force -LiteralPath $directory)) {
            $fullPath = [IO.Path]::GetFullPath($item.FullName)
            if (-not $fullPath.StartsWith($snapshotPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                (Test-IsUnsafeBuildReparsePoint $item)) {
                throw 'The compile snapshot tree escaped through a link or name surrogate.'
            }
            $relativePath = $fullPath.Substring($snapshotPrefix.Length).Replace('\', '/')
            if ($item.PSIsContainer) {
                if (-not $expectedDirectories.Contains($relativePath)) {
                    throw 'The compile snapshot contains an unexpected directory.'
                }
                $pending.Push($fullPath)
            }
            elseif (-not $expectedFiles.Contains($relativePath) -or
                -not $actualFiles.Add($relativePath)) {
                throw 'The compile snapshot contains an unexpected or duplicate file.'
            }
        }
    }
    if ($actualFiles.Count -ne $expectedFiles.Count) {
        throw 'The compile snapshot is missing an expected file.'
    }

    foreach ($row in $rows) {
        $targetPath = [IO.Path]::GetFullPath((Join-Path $snapshotPath ([string]$row.RelativePath)))
        if (-not $targetPath.StartsWith($snapshotPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A compile snapshot descriptor row escaped its root.'
        }
        [void](Read-VerifiedBuildFileBytes `
            $targetPath `
            ([string]$row.Sha256) `
            ([long]$row.Length))
    }
}

function Remove-FailedAppOutput([string]$AppOutput) {
    if ([string]::IsNullOrWhiteSpace($AppOutput)) { return }
    $fullPath = [IO.Path]::GetFullPath($AppOutput)
    if (-not (Test-Path -LiteralPath $fullPath)) { return }
    Assert-NoUnsafeBuildReparsePoints $fullPath
    $item = Get-Item -Force -LiteralPath $fullPath -ErrorAction Stop
    if ($item.PSIsContainer -or (Test-IsUnsafeBuildReparsePoint $item)) {
        throw 'A failed app output was not a regular file and was not deleted.'
    }
    [IO.File]::Delete($fullPath)
}

function Assert-VerifiedCompileSnapshotOrDeleteOutput(
    [object]$Snapshot,
    [string]$AppOutput) {
    try {
        Assert-VerifiedCompileSnapshot $Snapshot
    }
    catch {
        $validationFailure = $_
        Remove-FailedAppOutput $AppOutput
        throw $validationFailure
    }
}

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null

$programSource = Get-Content -Raw -LiteralPath (Join-Path $sourceRoot 'Program.cs')
$versionMatch = [regex]::Match(
    $programSource,
    'AssemblyVersion\("(?<version>\d+\.\d+\.\d+)\.\d+"\)')
if (-not $versionMatch.Success) {
    throw 'Program.cs에서 3자리 애플리케이션 버전을 찾지 못했습니다.'
}
$applicationVersion = $versionMatch.Groups['version'].Value
$identityParameters = @{
    ProjectRoot = $projectRoot
    OutputDirectory = (Join-Path $buildRoot 'generated\build-identity')
    ApplicationVersion = $applicationVersion
    Channel = $BuildChannel
}
$buildIdentity = & (Join-Path $projectRoot 'tools\Prepare-BuildIdentity.ps1') @identityParameters
if ($buildIdentity -is [array]) { $buildIdentity = $buildIdentity[-1] }

$compileSnapshotRoot = Join-Path $buildRoot (
    'generated\compile-snapshot-' + [Guid]::NewGuid().ToString('N'))
$compileSnapshot = New-VerifiedCompileSnapshot `
    -ProjectRoot $projectRoot `
    -InputManifest $buildIdentity.InputManifestPath `
    -ExpectedInputManifestSha256 $buildIdentity.BuildInputManifestSha256 `
    -GeneratedIdentityManifest $buildIdentity.ManifestPath `
    -ExpectedGeneratedIdentityManifestSha256 $buildIdentity.BuildIdentityManifestSha256 `
    -ExpectedGeneratedIdentityManifestLength $buildIdentity.BuildIdentityManifestLength `
    -GeneratedIdentitySource $buildIdentity.SourcePath `
    -ExpectedGeneratedIdentitySourceSha256 $buildIdentity.BuildIdentitySourceSha256 `
    -ExpectedGeneratedIdentitySourceLength $buildIdentity.BuildIdentitySourceLength `
    -SnapshotRoot $compileSnapshotRoot
$compileSnapshotRoot = $compileSnapshot.SnapshotPath
$compileSourceRoot = Join-Path $compileSnapshotRoot 'src'
$compileIdentityRoot = Join-Path $compileSnapshotRoot 'generated\build-identity'
$compileIdentityManifest = Join-Path $compileIdentityRoot 'build-identity.json'
$compileIdentitySource = Join-Path $compileIdentityRoot 'BuildIdentity.Generated.cs'

$commonReferences = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:Microsoft.CSharp.dll',
    '/reference:System.Net.Http.dll',
    '/reference:System.Web.Extensions.dll'
)

$appOutput = Join-Path $distRoot 'TarkovServerGuard.exe'
$appArguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    ('/out:' + $appOutput),
    ('/win32manifest:' + (Join-Path $compileSnapshotRoot 'app.manifest')),
    ('/win32icon:' + (Join-Path $compileSnapshotRoot 'assets\branding\tarkov-server-guard-tsg.ico')),
    ('/resource:' + (Join-Path $compileSnapshotRoot 'LICENSE') + ',TarkovServerReporter.LICENSE.txt'),
    ('/resource:' + (Join-Path $compileSnapshotRoot 'THIRD_PARTY_NOTICES.md') + ',TarkovServerReporter.THIRD_PARTY_NOTICES.md'),
    ('/resource:' + $compileIdentityManifest + ',TarkovServerReporter.BuildIdentity.json'),
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
) + $commonReferences + @(
    (Join-Path $compileSourceRoot 'AppBranding.cs'),
    (Join-Path $compileSourceRoot 'DataGridViewScrollCorner.cs'),
    (Join-Path $compileSourceRoot 'BuildIdentity.cs'),
    $compileIdentitySource,
    (Join-Path $compileSourceRoot 'Program.cs'),
    (Join-Path $compileSourceRoot 'MainForm.cs'),
    (Join-Path $compileSourceRoot 'GitHubUpdateService.cs'),
    (Join-Path $compileSourceRoot 'ReleaseNotesService.cs'),
    (Join-Path $compileSourceRoot 'UpdatePromptForm.cs'),
    (Join-Path $compileSourceRoot 'PatchNotesForm.cs'),
    (Join-Path $compileSourceRoot 'UsageNoticeForm.cs'),
    (Join-Path $compileSourceRoot 'LicenseForm.cs'),
    (Join-Path $compileSourceRoot 'ArenaBlockWarningForm.cs'),
    (Join-Path $compileSourceRoot 'FirewallRuleManager.cs'),
    (Join-Path $compileSourceRoot 'BlockedServerMetadataStore.cs'),
    (Join-Path $compileSourceRoot 'BlockedServerBackup.cs'),
    (Join-Path $compileSourceRoot 'BlockedServerRestorePreviewForm.cs'),
    (Join-Path $compileSourceRoot 'BlockedServersForm.cs'),
    (Join-Path $compileSourceRoot 'PingKickActionCell.cs'),
    (Join-Path $compileSourceRoot 'RaidNoteStore.cs'),
    (Join-Path $compileSourceRoot 'RaidNoteForm.cs'),
    (Join-Path $compileSourceRoot 'RaidNoteArchiveForm.cs'),
    (Join-Path $compileSourceRoot 'UserReportMemoStore.cs'),
    (Join-Path $compileSourceRoot 'UserReportMemoForm.cs'),
    (Join-Path $compileSourceRoot 'DbIpLiteMmdbReader.cs'),
    (Join-Path $compileSourceRoot 'DbIpLiteGeoService.cs'),
    (Join-Path $compileSourceRoot 'ServerReportCore.cs'),
    (Join-Path $compileSourceRoot 'TarkovLogServices.cs')
)

Assert-VerifiedCompileSnapshotOrDeleteOutput $compileSnapshot $appOutput
& $compiler $appArguments
$appCompileExitCode = $LASTEXITCODE
if ($appCompileExitCode -ne 0) {
    Remove-FailedAppOutput $appOutput
    exit $appCompileExitCode
}
Assert-VerifiedCompileSnapshotOrDeleteOutput $compileSnapshot $appOutput
Copy-Item -Force (Join-Path $compileSnapshotRoot 'app.config') ($appOutput + '.config')

$identityInspectorOutput = Join-Path $buildRoot 'BuildIdentityAppInspector.exe'
$identityInspectorArguments = @(
    '/nologo',
    '/target:exe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/main:TarkovServerReporter.Tests.BuildIdentityAppInspector',
    ('/out:' + $identityInspectorOutput),
    (Join-Path $testRoot 'BuildIdentityAppInspector.cs')
)
& $compiler $identityInspectorArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $identityInspectorOutput `
    $appOutput `
    $applicationVersion `
    $BuildChannel `
    $buildIdentity.BinaryBuildId `
    $buildIdentity.SourceRevision `
    $buildIdentity.BuildInputManifestSha256
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    $testOutput = Join-Path $buildRoot 'CoreTests.exe'
    $testArguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/warn:4',
        ('/out:' + $testOutput)
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'DbIpLiteMmdbReader.cs'),
        (Join-Path $sourceRoot 'DbIpLiteGeoService.cs'),
        (Join-Path $sourceRoot 'ServerReportCore.cs'),
        (Join-Path $sourceRoot 'FirewallRuleManager.cs'),
        (Join-Path $sourceRoot 'TarkovLogServices.cs'),
        (Join-Path $testRoot 'CoreTests.cs')
    )

    & $compiler $testArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $testOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $dbIpTestOutput = Join-Path $buildRoot 'DbIpLiteGeoTests.exe'
    $dbIpTestArguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/warn:4',
        ('/out:' + $dbIpTestOutput)
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'DbIpLiteMmdbReader.cs'),
        (Join-Path $sourceRoot 'DbIpLiteGeoService.cs'),
        (Join-Path $sourceRoot 'ServerReportCore.cs'),
        (Join-Path $testRoot 'DbIpLiteGeoTests.cs')
    )

    & $compiler $dbIpTestArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dbIpTestOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $storageTestOutput = Join-Path $buildRoot 'StorageAndBatchTests.exe'
    $storageTestArguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/warn:4',
        ('/out:' + $storageTestOutput)
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'FirewallRuleManager.cs'),
        (Join-Path $sourceRoot 'BlockedServerMetadataStore.cs'),
        (Join-Path $sourceRoot 'BlockedServerBackup.cs'),
        (Join-Path $testRoot 'StorageAndBatchTests.cs')
    )

    & $compiler $storageTestArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $storageTestOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $reportMemoTestOutput = Join-Path $buildRoot 'UserReportMemoTests.exe'
    $reportMemoTestArguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/warn:4',
        ('/out:' + $reportMemoTestOutput),
        '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll'
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'AppBranding.cs'),
        (Join-Path $sourceRoot 'DbIpLiteMmdbReader.cs'),
        (Join-Path $sourceRoot 'DbIpLiteGeoService.cs'),
        (Join-Path $sourceRoot 'ServerReportCore.cs'),
        (Join-Path $sourceRoot 'RaidNoteStore.cs'),
        (Join-Path $sourceRoot 'RaidNoteForm.cs'),
        (Join-Path $sourceRoot 'RaidNoteArchiveForm.cs'),
        (Join-Path $sourceRoot 'UserReportMemoStore.cs'),
        (Join-Path $sourceRoot 'UserReportMemoForm.cs'),
        (Join-Path $testRoot 'UserReportMemoTests.cs')
    )

    & $compiler $reportMemoTestArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $reportMemoTestOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $githubUpdateTestOutput = Join-Path $buildRoot 'GitHubUpdateTests.exe'
    $githubUpdateTestArguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/warn:4',
        ('/out:' + $githubUpdateTestOutput),
        '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll'
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'AppBranding.cs'),
        (Join-Path $sourceRoot 'GitHubUpdateService.cs'),
        (Join-Path $sourceRoot 'ReleaseNotesService.cs'),
        (Join-Path $sourceRoot 'UpdatePromptForm.cs'),
        (Join-Path $sourceRoot 'PatchNotesForm.cs'),
        (Join-Path $testRoot 'GitHubUpdateTests.cs')
    )

    & $compiler $githubUpdateTestArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $githubUpdateTestOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $releaseNotesTestOutput = Join-Path $buildRoot 'ReleaseNotesTests.exe'
    $releaseNotesTestArguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/warn:4',
        ('/out:' + $releaseNotesTestOutput),
        '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll'
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'AppBranding.cs'),
        (Join-Path $sourceRoot 'ReleaseNotesService.cs'),
        (Join-Path $sourceRoot 'PatchNotesForm.cs'),
        (Join-Path $sourceRoot 'UpdatePromptForm.cs'),
        (Join-Path $testRoot 'ReleaseNotesTests.cs')
    )

    & $compiler $releaseNotesTestArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $releaseNotesTestOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $buildIdentityTestOutput = Join-Path $buildRoot 'BuildIdentityTests.exe'
    $buildIdentityTestArguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/warn:4',
        ('/out:' + $buildIdentityTestOutput),
        ('/resource:' + $buildIdentity.ManifestPath + ',TarkovServerReporter.BuildIdentity.json')
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'BuildIdentity.cs'),
        $buildIdentity.SourcePath,
        (Join-Path $testRoot 'BuildIdentityTests.cs')
    )

    & $compiler $buildIdentityTestArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $buildIdentityTestOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & (Join-Path $testRoot 'BuildIdentityScriptTests.ps1') `
        -ProjectRoot $projectRoot `
        -ApplicationVersion $applicationVersion
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & (Join-Path $testRoot 'ReleaseProvenanceTests.ps1') `
        -ProjectRoot $projectRoot `
        -ApplicationVersion $applicationVersion
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & (Join-Path $testRoot 'ReleaseBinaryIdentityScannerTests.ps1') `
        -ProjectRoot $projectRoot `
        -AppPath $appOutput `
        -InspectorPath $identityInspectorOutput `
        -ExpectedVersion $applicationVersion `
        -ExpectedChannel $BuildChannel `
        -ExpectedBinaryBuildId $buildIdentity.BinaryBuildId `
        -ExpectedRevision $buildIdentity.SourceRevision `
        -ExpectedBuildInputManifestSha256 $buildIdentity.BuildInputManifestSha256
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & (Join-Path $testRoot 'PackageReleasePreflightTests.ps1') `
        -ProjectRoot $projectRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & (Join-Path $testRoot 'ReleaseSanitizationTests.ps1') `
        -ProjectRoot $projectRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $v080UiTestOutput = Join-Path $buildRoot 'V080UiTests.exe'
    $v080UiTestArguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/warn:4',
        '/main:TarkovServerReporter.Tests.V080UiTests',
        ('/out:' + $v080UiTestOutput),
        '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll'
    ) + $commonReferences + @(
        (Join-Path $testRoot 'BuildIdentityAppInspector.cs'),
        (Join-Path $testRoot 'V080UiTests.cs')
    )

    & $compiler $v080UiTestArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $v080UiTestOutput $appOutput $applicationVersion $BuildChannel
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & (Join-Path $testRoot 'PublishedReleaseAssetVerifierTests.ps1') `
        -ProjectRoot $projectRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host ('빌드 식별: ' + $buildIdentity.Channel + ' / ' + $buildIdentity.BinaryBuildId)
Write-Host ('완료: ' + $appOutput)
