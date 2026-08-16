# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [string]$CacheRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $CacheRoot = Join-Path $projectRoot 'build\tools\velopack-1.2.0'
}
$CacheRoot = [IO.Path]::GetFullPath($CacheRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
if ([string]::IsNullOrWhiteSpace([IO.Path]::GetDirectoryName($CacheRoot))) {
    throw 'Velopack cache root cannot be a filesystem root.'
}

$velopackVersion = '1.2.0'
$newtonsoftVersion = '13.0.4'
$velopackHash = '018D0B23F2076179495EA45836A10E429E60675BDF22AA5CD43CAB8B655651BD'
$newtonsoftHash = 'F09081D457405BAF35A973FA0C50D6BF272ED683F2568C5A620A49DA952F6529'
$vpkHash = '3E458A676BE46D1122E522312DB18411F36EA8C70E586F81A676695D43F89DBC'
$velopackDllHash = 'DA40E2DFD3A4E34435BBEF76EB4BA2A04986AFF1E131AD6DC791FA55B0B4DE34'
$newtonsoftDllHash = 'F0C07AF0E84D4DD4DA4BD7823BA4535BC0481B3BF623ED40B659B68147A6BB75'
$vpkDllHash = '056AB002BC25B1E89A333E942A8921A2DD4295C352EE3C3C49F4BBC185D455D1'

function Initialize-VelopackReparseInspector {
    if ('TsgVelopackReparsePoint' -as [type]) { return }
    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;

public static class TsgVelopackReparsePoint
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
            throw new IOException("Could not inspect a Velopack cache reparse point.");
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
                throw new IOException("Could not read a Velopack cache reparse tag.");
            return BitConverter.ToUInt32(buffer, 0);
        }
        finally { CloseHandle(handle); }
    }

    public static bool IsCloudPlaceholder(uint tag)
    {
        // Microsoft Cloud Files tags use 0x9000?01A. They preserve the same
        // owned path and are not name-surrogate redirects.
        return (tag & 0xFFFF0FFFu) == 0x9000001Au;
    }
}
'@
}

function Test-IsUnsafeCacheReparsePoint([IO.FileSystemInfo]$Item) {
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
        Initialize-VelopackReparseInspector
        $tag = [TsgVelopackReparsePoint]::GetTag($Item.FullName)
        return -not [TsgVelopackReparsePoint]::IsCloudPlaceholder($tag)
    }
    catch {
        # Unknown or unreadable reparse points are redirects until proven safe.
        return $true
    }
}

function Assert-NoUnsafeCacheReparsePoints([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $cachePrefix = $CacheRoot + [IO.Path]::DirectorySeparatorChar
    if (-not [string]::Equals(
            $fullPath,
            $CacheRoot,
            [StringComparison]::OrdinalIgnoreCase) -and
        -not $fullPath.StartsWith($cachePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Velopack cache safety check escaped its root: $fullPath"
    }

    $candidate = $fullPath
    while ($true) {
        $item = Get-Item -Force -LiteralPath $candidate -ErrorAction SilentlyContinue
        if ($null -ne $item) { break }
        $parent = [IO.Path]::GetDirectoryName($candidate)
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals($parent, $candidate, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Velopack cache path has no inspectable filesystem ancestor.'
        }
        $candidate = $parent
    }

    while (-not [string]::IsNullOrWhiteSpace($candidate)) {
        $item = Get-Item -Force -LiteralPath $candidate -ErrorAction Stop
        if (Test-IsUnsafeCacheReparsePoint $item) {
            throw "Velopack cache paths cannot use junctions, symlinks, or name-surrogate reparse points: $candidate"
        }
        $parent = [IO.Path]::GetDirectoryName($candidate)
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals($parent, $candidate, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $candidate = $parent
    }
}

function Assert-SafeCacheTreeForRemoval([string]$Root) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    Assert-NoUnsafeCacheReparsePoints $rootPath
    if (-not (Test-Path -LiteralPath $rootPath)) { return }

    $rootItem = Get-Item -Force -LiteralPath $rootPath
    if (-not $rootItem.PSIsContainer -or
        (Test-IsUnsafeCacheReparsePoint $rootItem)) {
        throw 'Velopack cache cleanup target must be a regular directory.'
    }
    $rootPrefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($rootPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in @(Get-ChildItem -Force -LiteralPath $directory)) {
            $fullPath = [IO.Path]::GetFullPath($item.FullName)
            if (-not $fullPath.StartsWith(
                $rootPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Velopack cache cleanup tree escaped its exact destination.'
            }
            if (Test-IsUnsafeCacheReparsePoint $item) {
                throw "Velopack cache cleanup tree contains a link or name surrogate: $fullPath"
            }
            if ($item.PSIsContainer) { $pending.Push($fullPath) }
        }
    }
}

function Assert-ChildPath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith(
        $CacheRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "예상하지 않은 Velopack 캐시 경로입니다: $fullPath"
    }
    return $fullPath
}

function Ensure-Package(
    [string]$Name,
    [string]$Url,
    [string]$ExpectedSha256) {
    $packagePath = Assert-ChildPath (Join-Path $CacheRoot $Name)
    Assert-NoUnsafeCacheReparsePoints $packagePath
    if (Test-Path -LiteralPath $packagePath) {
        $currentHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash
        if ([string]::Equals($currentHash, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
            return $packagePath
        }
        throw "기존 패키지의 SHA-256이 일치하지 않습니다: $packagePath"
    }

    $temporaryPath = Assert-ChildPath ($packagePath + '.download.' + [Guid]::NewGuid().ToString('N'))
    Assert-NoUnsafeCacheReparsePoints $temporaryPath
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $temporaryPath
        $downloadedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryPath).Hash
        if (-not [string]::Equals(
            $downloadedHash,
            $ExpectedSha256,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "다운로드한 $Name 패키지의 SHA-256이 일치하지 않습니다."
        }
        Move-Item -LiteralPath $temporaryPath -Destination $packagePath
        return $packagePath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Ensure-Extracted(
    [string]$PackagePath,
    [string]$Destination,
    [string]$ExpectedFile,
    [string]$ExpectedFileSha256) {
    $destinationPath = Assert-ChildPath $Destination
    $expectedPath = Assert-ChildPath (Join-Path $destinationPath $ExpectedFile)
    Assert-NoUnsafeCacheReparsePoints $PackagePath
    Assert-NoUnsafeCacheReparsePoints $destinationPath
    Assert-NoUnsafeCacheReparsePoints $expectedPath
    if (Test-Path -LiteralPath $expectedPath) {
        $cachedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $expectedPath).Hash
        if ([string]::Equals(
            $cachedHash,
            $ExpectedFileSha256,
            [StringComparison]::OrdinalIgnoreCase)) {
            return $destinationPath
        }
    }

    if (Test-Path -LiteralPath $destinationPath) {
        Assert-SafeCacheTreeForRemoval $destinationPath
        Remove-Item -LiteralPath $destinationPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null
    Assert-NoUnsafeCacheReparsePoints $destinationPath
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $destinationPath)
    if (-not (Test-Path -LiteralPath $expectedPath)) {
        throw "패키지에 필요한 파일이 없습니다: $ExpectedFile"
    }
    Assert-NoUnsafeCacheReparsePoints $expectedPath
    $extractedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $expectedPath).Hash
    if (-not [string]::Equals(
        $extractedHash,
        $ExpectedFileSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "추출한 파일의 SHA-256이 일치하지 않습니다: $ExpectedFile"
    }
    return $destinationPath
}

[Net.ServicePointManager]::SecurityProtocol =
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Assert-NoUnsafeCacheReparsePoints $CacheRoot
New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null
Assert-NoUnsafeCacheReparsePoints $CacheRoot

$velopackPackage = Ensure-Package `
    "velopack.$velopackVersion.nupkg" `
    "https://api.nuget.org/v3-flatcontainer/velopack/$velopackVersion/velopack.$velopackVersion.nupkg" `
    $velopackHash
$newtonsoftPackage = Ensure-Package `
    "newtonsoft.json.$newtonsoftVersion.nupkg" `
    "https://api.nuget.org/v3-flatcontainer/newtonsoft.json/$newtonsoftVersion/newtonsoft.json.$newtonsoftVersion.nupkg" `
    $newtonsoftHash
$vpkPackage = Ensure-Package `
    "vpk.$velopackVersion.nupkg" `
    "https://api.nuget.org/v3-flatcontainer/vpk/$velopackVersion/vpk.$velopackVersion.nupkg" `
    $vpkHash

$velopackExpanded = Ensure-Extracted `
    $velopackPackage `
    (Join-Path $CacheRoot 'velopack') `
    'lib\net472\Velopack.dll' `
    $velopackDllHash
$newtonsoftExpanded = Ensure-Extracted `
    $newtonsoftPackage `
    (Join-Path $CacheRoot 'newtonsoft') `
    'lib\net45\Newtonsoft.Json.dll' `
    $newtonsoftDllHash
$vpkExpanded = Ensure-Extracted `
    $vpkPackage `
    (Join-Path $CacheRoot 'vpk') `
    'tools\net8.0\any\vpk.dll' `
    $vpkDllHash

$velopackDll = Join-Path $velopackExpanded 'lib\net472\Velopack.dll'
$newtonsoftDll = Join-Path $newtonsoftExpanded 'lib\net45\Newtonsoft.Json.dll'
$vpkDll = Join-Path $vpkExpanded 'tools\net8.0\any\vpk.dll'

[PSCustomObject]@{
    CacheRoot = $CacheRoot
    VelopackDll = $velopackDll
    VelopackDllSha256 = $velopackDllHash
    VelopackDllLength = [long](Get-Item -Force -LiteralPath $velopackDll).Length
    NewtonsoftJsonDll = $newtonsoftDll
    NewtonsoftJsonDllSha256 = $newtonsoftDllHash
    NewtonsoftJsonDllLength = [long](Get-Item -Force -LiteralPath $newtonsoftDll).Length
    VpkDll = $vpkDll
    VpkDllSha256 = $vpkDllHash
    VpkDllLength = [long](Get-Item -Force -LiteralPath $vpkDll).Length
}
