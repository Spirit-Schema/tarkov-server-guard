# SPDX-License-Identifier: MPL-2.0
# Copyright 2026 Spirit-Schema

param(
    [string]$CacheRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $CacheRoot = Join-Path $projectRoot 'build\tools\velopack-1.2.0'
}
$CacheRoot = [IO.Path]::GetFullPath($CacheRoot)

$velopackVersion = '1.2.0'
$newtonsoftVersion = '13.0.4'
$velopackHash = '018D0B23F2076179495EA45836A10E429E60675BDF22AA5CD43CAB8B655651BD'
$newtonsoftHash = 'F09081D457405BAF35A973FA0C50D6BF272ED683F2568C5A620A49DA952F6529'
$vpkHash = '3E458A676BE46D1122E522312DB18411F36EA8C70E586F81A676695D43F89DBC'

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
    if (Test-Path -LiteralPath $packagePath) {
        $currentHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash
        if ([string]::Equals($currentHash, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
            return $packagePath
        }
        throw "기존 패키지의 SHA-256이 일치하지 않습니다: $packagePath"
    }

    $temporaryPath = Assert-ChildPath ($packagePath + '.download.' + [Guid]::NewGuid().ToString('N'))
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
        Remove-Item -LiteralPath $destinationPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $destinationPath)
    if (-not (Test-Path -LiteralPath $expectedPath)) {
        throw "패키지에 필요한 파일이 없습니다: $ExpectedFile"
    }
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
New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null

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
    'DA40E2DFD3A4E34435BBEF76EB4BA2A04986AFF1E131AD6DC791FA55B0B4DE34'
$newtonsoftExpanded = Ensure-Extracted `
    $newtonsoftPackage `
    (Join-Path $CacheRoot 'newtonsoft') `
    'lib\net45\Newtonsoft.Json.dll' `
    'F0C07AF0E84D4DD4DA4BD7823BA4535BC0481B3BF623ED40B659B68147A6BB75'
$vpkExpanded = Ensure-Extracted `
    $vpkPackage `
    (Join-Path $CacheRoot 'vpk') `
    'tools\net8.0\any\vpk.dll' `
    '056AB002BC25B1E89A333E942A8921A2DD4295C352EE3C3C49F4BBC185D455D1'

[PSCustomObject]@{
    CacheRoot = $CacheRoot
    VelopackDll = Join-Path $velopackExpanded 'lib\net472\Velopack.dll'
    NewtonsoftJsonDll = Join-Path $newtonsoftExpanded 'lib\net45\Newtonsoft.Json.dll'
    VpkDll = Join-Path $vpkExpanded 'tools\net8.0\any\vpk.dll'
}
