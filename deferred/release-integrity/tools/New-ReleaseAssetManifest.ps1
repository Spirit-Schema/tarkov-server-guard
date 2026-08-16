# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$ReleaseDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ReviewZip,
    [Parameter(Mandatory = $true)]
    [string]$ReviewZipHash,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repository = 'Spirit-Schema/tarkov-server-guard'
$maximumAssetCount = 32
$maximumAssetBytes = 1GB
$maximumTotalBytes = 4GB

function Read-StrictUtf8Text([string]$Path, [int]$MaximumBytes) {
    $info = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($info.Length -lt 1 -or $info.Length -gt $MaximumBytes) {
        throw '텍스트 검증 파일의 크기가 허용 범위를 벗어났습니다.'
    }
    $bytes = [IO.File]::ReadAllBytes($info.FullName)
    $encoding = New-Object System.Text.UTF8Encoding($false, $true)
    $text = $encoding.GetString($bytes)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xfeff) {
        $text = $text.Substring(1)
    }
    return $text
}

function Write-AtomicUtf8NoBom([string]$Path, [string]$Text) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($parent) -or
        -not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw '기대 manifest 출력 폴더를 찾지 못했습니다.'
    }
    $temporary = Join-Path $parent (
        [IO.Path]::GetFileName($fullPath) + '.tmp.' + [Guid]::NewGuid().ToString('N'))
    $backup = Join-Path $parent (
        [IO.Path]::GetFileName($fullPath) + '.bak.' + [Guid]::NewGuid().ToString('N'))
    try {
        $encoding = New-Object System.Text.UTF8Encoding($false, $true)
        $bytes = $encoding.GetBytes($Text)
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
        finally {
            $stream.Dispose()
        }
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            # .NET Framework's File.Replace rejects a null backup path on some
            # supported Windows versions. A same-directory backup also preserves
            # the previous valid manifest until the atomic replacement succeeds.
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

function Get-ExpectedRole([string]$Name, [string]$ReleaseVersion) {
    $escapedVersion = [Regex]::Escape($ReleaseVersion)
    if ($Name -ceq 'SpiritSchema.TarkovServerGuard-win-Setup.exe') {
        return 'windows-setup'
    }
    if ($Name -ceq 'SpiritSchema.TarkovServerGuard-win-Portable.zip') {
        return 'windows-portable'
    }
    if ($Name -cmatch ('^SpiritSchema\.TarkovServerGuard-' + $escapedVersion + '-full\.nupkg$')) {
        return 'full-nupkg'
    }
    if ($Name -cmatch ('^SpiritSchema\.TarkovServerGuard-' + $escapedVersion + '-delta\.nupkg$')) {
        return 'delta-nupkg'
    }
    if ($Name -ceq 'releases.win.json') { return 'release-feed' }
    if ($Name -ceq 'RELEASES') { return 'legacy-release-feed' }
    if ($Name -ceq 'SHA256SUMS.txt') { return 'release-sha256sums' }
    if ($Name -ceq 'binary-build-identity.json') { return 'binary-build-identity' }
    if ($Name -ceq 'build-inputs.manifest') { return 'binary-build-input-manifest' }
    if ($Name -ceq 'release-assets.manifest') { return 'release-asset-provenance-manifest' }
    if ($Name -ceq 'release-provenance.json') { return 'release-provenance' }
    throw "공개 자산 역할을 결정할 수 없는 파일입니다: $Name"
}

function Assert-ReleaseHashList([string]$DirectoryPath) {
    $hashListPath = Join-Path $DirectoryPath 'SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $hashListPath -PathType Leaf)) {
        throw 'Release 폴더에 SHA256SUMS.txt가 없습니다.'
    }
    $text = Read-StrictUtf8Text $hashListPath 1MB
    $lines = @($text -split '\r?\n' | Where-Object { $_.Length -gt 0 })
    $listed = New-Object 'System.Collections.Generic.Dictionary[string,string]' (
        [StringComparer]::Ordinal)
    foreach ($line in $lines) {
        if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64})  (?<name>[^\\/\r\n]+)$') {
            throw 'SHA256SUMS.txt 형식이 올바르지 않습니다.'
        }
        $name = [string]$Matches['name']
        if ($listed.ContainsKey($name)) {
            throw 'SHA256SUMS.txt에 중복 파일명이 있습니다.'
        }
        $listed.Add($name, ([string]$Matches['hash']).ToUpperInvariant())
    }

    # These two provenance outputs are created after SHA256SUMS.txt by design:
    # release-assets.manifest cannot hash itself and release-provenance.json
    # depends on that manifest. They are independently hashed by the expected
    # publication manifest generated after packaging is otherwise complete.
    $postHashFiles = @('release-assets.manifest', 'release-provenance.json')
    $files = @(Get-ChildItem -LiteralPath $DirectoryPath -File |
        Where-Object {
            $_.Name -cne 'SHA256SUMS.txt' -and
            $postHashFiles -cnotcontains $_.Name
        } |
        Sort-Object Name)
    if ($listed.Count -ne $files.Count) {
        throw 'SHA256SUMS.txt 항목 수가 Release 파일 수와 다릅니다.'
    }
    foreach ($file in $files) {
        if (-not $listed.ContainsKey($file.Name)) {
            throw 'SHA256SUMS.txt에 Release 파일이 빠졌습니다.'
        }
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToUpperInvariant()
        if (-not [string]::Equals(
            $actual,
            $listed[$file.Name],
            [StringComparison]::Ordinal)) {
            throw 'SHA256SUMS.txt의 로컬 파일 해시가 일치하지 않습니다.'
        }
    }
}

function Assert-ReviewZipHash([string]$ZipPath, [string]$HashPath) {
    $text = (Read-StrictUtf8Text $HashPath 64KB).TrimEnd("`r", "`n")
    $expectedName = [IO.Path]::GetFileName($ZipPath)
    if ($text -notmatch '^(?<hash>[0-9A-Fa-f]{64})  (?<name>[^\\/\r\n]+)$' -or
        -not [string]::Equals(
            [string]$Matches['name'],
            $expectedName,
            [StringComparison]::Ordinal)) {
        throw '검토 ZIP 해시 파일의 형식 또는 파일명이 올바르지 않습니다.'
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath).Hash.ToUpperInvariant()
    if (-not [string]::Equals(
        $actual,
        ([string]$Matches['hash']).ToUpperInvariant(),
        [StringComparison]::Ordinal)) {
        throw '검토 ZIP 해시 파일과 실제 ZIP이 일치하지 않습니다.'
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw '버전은 1.2.3 형식이어야 합니다.'
}
$releasePath = [IO.Path]::GetFullPath($ReleaseDirectory)
$reviewZipPath = [IO.Path]::GetFullPath($ReviewZip)
$reviewHashPath = [IO.Path]::GetFullPath($ReviewZipHash)
$manifestPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $releasePath -PathType Container)) {
    throw 'Release 자산 폴더를 찾지 못했습니다.'
}
if (-not (Test-Path -LiteralPath $reviewZipPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $reviewHashPath -PathType Leaf)) {
    throw '검토 ZIP 또는 그 해시 파일을 찾지 못했습니다.'
}
$releasePrefix = $releasePath.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($manifestPath.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw '기대 manifest는 공개 Release 자산 폴더 밖에 저장해야 합니다.'
}

$releaseChildren = @(Get-ChildItem -LiteralPath $releasePath -Force -ErrorAction Stop)
foreach ($child in $releaseChildren) {
    if ($child.PSIsContainer) {
        throw "공개 Release 자산 폴더는 최상위 파일만 포함해야 합니다: $($child.Name)"
    }
    # OneDrive placeholder files also carry ReparsePoint without redirecting
    # name resolution. Reject only name-surrogate links (PowerShell exposes a
    # LinkType or Target), while still hashing hydrated placeholder contents.
    $linkType = $child.PSObject.Properties['LinkType']
    $target = $child.PSObject.Properties['Target']
    if (($linkType -and -not [string]::IsNullOrWhiteSpace([string]$linkType.Value)) -or
        ($target -and $null -ne $target.Value -and @($target.Value).Count -gt 0)) {
        throw "공개 Release 자산 폴더에는 파일 링크를 둘 수 없습니다: $($child.Name)"
    }
}

Assert-ReleaseHashList $releasePath
Assert-ReviewZipHash $reviewZipPath $reviewHashPath

$releaseFiles = @($releaseChildren | Sort-Object Name)
if ($releaseFiles.Count -lt 5) {
    throw '필수 Release 자산 수가 부족합니다.'
}
$assetRows = New-Object 'System.Collections.Generic.List[object]'
foreach ($file in $releaseFiles) {
    $role = Get-ExpectedRole $file.Name $Version
    if ([string]::IsNullOrWhiteSpace([string]$role)) {
        throw "공개 자산 역할이 없는 예상 밖 파일입니다: $($file.Name)"
    }
    $assetRows.Add([PSCustomObject][ordered]@{
        name = $file.Name
        role = $role
        size = [long]$file.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToUpperInvariant()
    })
}

$expectedReviewName = 'TarkovServerGuard-v' + $Version + '.zip'
$expectedReviewHashName = $expectedReviewName + '.sha256.txt'
if (-not [string]::Equals(
    [IO.Path]::GetFileName($reviewZipPath),
    $expectedReviewName,
    [StringComparison]::Ordinal) -or
    -not [string]::Equals(
        [IO.Path]::GetFileName($reviewHashPath),
        $expectedReviewHashName,
        [StringComparison]::Ordinal)) {
    throw '검토 ZIP 또는 해시 파일명이 릴리스 버전과 일치하지 않습니다.'
}
foreach ($item in @(
    [PSCustomObject]@{ Path = $reviewZipPath; Role = 'review-zip' },
    [PSCustomObject]@{ Path = $reviewHashPath; Role = 'review-zip-sha256' })) {
    $info = Get-Item -LiteralPath $item.Path
    $assetRows.Add([PSCustomObject][ordered]@{
        name = $info.Name
        role = $item.Role
        size = [long]$info.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $info.FullName).Hash.ToUpperInvariant()
    })
}

$assets = @($assetRows | Sort-Object name)
if ($assets.Count -gt $maximumAssetCount) {
    throw '공개 예정 자산 수가 안전 한도를 초과했습니다.'
}
$names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$totalBytes = [long]0
foreach ($asset in $assets) {
    if (-not $names.Add([string]$asset.name)) {
        throw '공개 예정 자산 파일명이 대소문자 구분 없이 중복됩니다.'
    }
    if ($asset.size -lt 1 -or $asset.size -gt $maximumAssetBytes) {
        throw '공개 예정 자산 크기가 안전 한도를 벗어났습니다.'
    }
    $totalBytes += [long]$asset.size
}
if ($totalBytes -gt $maximumTotalBytes) {
    throw '공개 예정 자산 전체 크기가 안전 한도를 초과했습니다.'
}

$requiredRoles = @(
    'windows-setup',
    'windows-portable',
    'full-nupkg',
    'release-feed',
    'release-sha256sums',
    'binary-build-identity',
    'binary-build-input-manifest',
    'release-asset-provenance-manifest',
    'release-provenance',
    'review-zip',
    'review-zip-sha256')
foreach ($role in $requiredRoles) {
    if (@($assets | Where-Object { $_.role -ceq $role }).Count -ne 1) {
        throw "필수 공개 자산 역할이 정확히 하나가 아닙니다: $role"
    }
}

$document = [PSCustomObject][ordered]@{
    schemaVersion = 1
    repository = $repository
    version = $Version
    tag = 'v' + $Version
    assets = $assets
}
$json = $document | ConvertTo-Json -Depth 6
Write-AtomicUtf8NoBom $manifestPath ($json + "`n")

[PSCustomObject]@{
    ManifestPath = $manifestPath
    AssetCount = $assets.Count
    TotalBytes = $totalBytes
}
