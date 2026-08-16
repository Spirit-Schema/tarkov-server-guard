# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedManifest,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repository = 'Spirit-Schema/tarkov-server-guard'
$maximumManifestBytes = 1MB

function Read-StrictUtf8Json([string]$Path) {
    $item = Get-Item -Force -LiteralPath $Path -ErrorAction Stop
    if ($item.PSIsContainer -or $item.Length -lt 2 -or
        $item.Length -gt $maximumManifestBytes) {
        throw '기대 manifest의 형식 또는 크기가 올바르지 않습니다.'
    }
    $bytes = [IO.File]::ReadAllBytes($item.FullName)
    try {
        $text = (New-Object Text.UTF8Encoding($false, $true)).GetString($bytes)
        if ($text.Length -gt 0 -and $text[0] -eq [char]0xfeff) {
            $text = $text.Substring(1)
        }
        return $text | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw '기대 manifest는 올바른 UTF-8 JSON이어야 합니다.'
    }
}

function Test-IsFileLink([IO.FileSystemInfo]$Item) {
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
    return $false
}

function Write-AtomicUtf8NoBom([string]$Path, [string]$Text) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($parent) -or
        -not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw '릴리스 준비 marker 출력 폴더를 찾지 못했습니다.'
    }
    if (Test-Path -LiteralPath $fullPath) {
        $existing = Get-Item -Force -LiteralPath $fullPath
        if ($existing.PSIsContainer -or (Test-IsFileLink $existing)) {
            throw '릴리스 준비 marker는 링크가 아닌 일반 파일이어야 합니다.'
        }
    }

    $temporary = Join-Path $parent (
        [IO.Path]::GetFileName($fullPath) + '.tmp.' + [Guid]::NewGuid().ToString('N'))
    $backup = Join-Path $parent (
        [IO.Path]::GetFileName($fullPath) + '.bak.' + [Guid]::NewGuid().ToString('N'))
    try {
        $bytes = (New-Object Text.UTF8Encoding($false, $true)).GetBytes($Text)
        $stream = New-Object IO.FileStream(
            $temporary,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None,
            4096,
            [IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally { $stream.Dispose() }

        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            [IO.File]::Replace($temporary, $fullPath, $backup, $true)
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
        }
        else {
            [IO.File]::Move($temporary, $fullPath)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $backup -PathType Leaf) {
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw '버전은 1.2.3 형식이어야 합니다.'
}
$manifestPath = [IO.Path]::GetFullPath($ExpectedManifest)
$markerPath = [IO.Path]::GetFullPath($OutputPath)
$expectedManifestName = 'release-assets-v' + $Version + '.expected.json'
$expectedMarkerName = 'release-assets-v' + $Version + '.ready.json'
if ([IO.Path]::GetFileName($manifestPath) -cne $expectedManifestName -or
    [IO.Path]::GetFileName($markerPath) -cne $expectedMarkerName -or
    [string]::Equals($manifestPath, $markerPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw '릴리스 준비 marker 경로와 기대 manifest 경로가 버전별 규칙과 일치하지 않습니다.'
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw '릴리스 준비 marker에 연결할 기대 manifest를 찾지 못했습니다.'
}
$manifest = Read-StrictUtf8Json $manifestPath
if ([int]$manifest.schemaVersion -ne 1 -or
    [string]$manifest.repository -cne $repository -or
    [string]$manifest.version -cne $Version -or
    [string]$manifest.tag -cne ('v' + $Version)) {
    throw '기대 manifest의 저장소·버전·태그가 릴리스 준비 요청과 일치하지 않습니다.'
}

$manifestHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash.ToUpperInvariant()
$marker = [PSCustomObject][ordered]@{
    schemaVersion = 1
    repository = $repository
    version = $Version
    tag = 'v' + $Version
    expectedManifestFile = $expectedManifestName
    expectedManifestSha256 = $manifestHash
}
$json = ($marker | ConvertTo-Json -Compress) + "`n"
Write-AtomicUtf8NoBom $markerPath $json

[PSCustomObject]@{
    MarkerPath = $markerPath
    ExpectedManifestPath = $manifestPath
    ExpectedManifestSha256 = $manifestHash
}
