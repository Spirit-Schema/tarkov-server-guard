# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [string]$Version = '0.8.0',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw '릴리스 버전은 1.2.3 같은 3자리 SemVer 형식이어야 합니다.'
}

$projectRootPrefix = $projectRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

$buildRoot = Join-Path $projectRoot 'build'
$publishRoot = Join-Path $buildRoot ("publish-v" + $Version)
$releasesRoot = Join-Path $buildRoot ("Releases-v" + $Version)
$releasesStageRoot = Join-Path $buildRoot ("Releases-v" + $Version + '-staging')
$sourceReviewStageRoot = Join-Path $buildRoot ("public-source-v" + $Version + '-staging')
$packageInputStageRoot = Join-Path $buildRoot ("package-inputs-v" + $Version + '-staging')
$completionPendingRoot = Join-Path $buildRoot ("release-readiness-v" + $Version + '-pending')
$reviewRoot = Join-Path $projectRoot ("review\TarkovServerGuard-v" + $Version)
$releaseNotes = Join-Path $projectRoot ("release-notes-v" + $Version + '.md')
$appIcon = Join-Path $projectRoot 'assets\branding\tarkov-server-guard-tsg.ico'
$releaseSanitizer = Join-Path $projectRoot 'tools\Assert-ReleaseSanitized.ps1'
$releaseSanitizerAllowlist = Join-Path $projectRoot 'release-scan-allowlist.json'
$releaseAssetManifestPath = Join-Path $buildRoot `
    ('release-assets-v' + $Version + '.expected.json')
$releaseCompletionMarkerPath = Join-Path $buildRoot `
    ('release-assets-v' + $Version + '.ready.json')

if (-not (Test-Path -LiteralPath $appIcon -PathType Leaf)) {
    throw "앱 아이콘을 찾지 못했습니다: $appIcon"
}
if (-not (Test-Path -LiteralPath $releaseSanitizer -PathType Leaf) -or
    -not (Test-Path -LiteralPath $releaseSanitizerAllowlist -PathType Leaf)) {
    throw '릴리스 민감정보 검사기 또는 exact allowlist를 찾지 못했습니다.'
}

function Reset-ProjectChild([string]$Path, [string]$ExpectedParent) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parentPath = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $actualParent = [IO.Path]::GetDirectoryName($fullPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if (-not [string]::Equals(
        $actualParent,
        $parentPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "예상하지 않은 빌드 정리 경로입니다: $fullPath"
    }

    # Remove-Item -Recurse must never receive a junction/symlink target or a
    # child of one. Validate every existing ancestor back to the exact project
    # root before creating or deleting the dedicated output directory.
    $projectBoundary = [IO.Path]::GetFullPath($projectRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $candidate = $fullPath
    while (-not (Test-Path -LiteralPath $candidate)) {
        $next = [IO.Path]::GetDirectoryName($candidate)
        if ([string]::IsNullOrWhiteSpace($next) -or
            [string]::Equals($next, $candidate, [StringComparison]::OrdinalIgnoreCase)) {
            throw '빌드 정리 경로의 기존 상위 폴더를 확인하지 못했습니다.'
        }
        $candidate = $next
    }
    $reachedProjectRoot = $false
    while (-not [string]::IsNullOrWhiteSpace($candidate)) {
        $normalizedCandidate = [IO.Path]::GetFullPath($candidate).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
        if (-not [string]::Equals(
                $normalizedCandidate,
                $projectBoundary,
                [StringComparison]::OrdinalIgnoreCase) -and
            -not $normalizedCandidate.StartsWith(
                $projectBoundary + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw '빌드 정리 경로의 기존 상위 폴더가 프로젝트 밖을 가리킵니다.'
        }
        $item = Get-Item -Force -LiteralPath $normalizedCandidate
        if (Test-IsFileSystemLink $item) {
            throw '빌드 정리 경로에는 junction 또는 심볼릭 링크를 사용할 수 없습니다.'
        }
        if ([string]::Equals(
            $normalizedCandidate,
            $projectBoundary,
            [StringComparison]::OrdinalIgnoreCase)) {
            $reachedProjectRoot = $true
            break
        }
        $candidate = [IO.Path]::GetDirectoryName($normalizedCandidate)
    }
    if (-not $reachedProjectRoot) {
        throw '빌드 정리 경로를 프로젝트 루트까지 확인하지 못했습니다.'
    }

    if (-not (Test-Path -LiteralPath $parentPath -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $parentPath | Out-Null
    }
    if (Test-Path -LiteralPath $fullPath) {
        $targetItem = Get-Item -Force -LiteralPath $fullPath
        if (-not $targetItem.PSIsContainer -or (Test-IsFileSystemLink $targetItem)) {
            throw '빌드 정리 대상은 링크가 아닌 전용 폴더여야 합니다.'
        }
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $fullPath | Out-Null
    return $fullPath
}

function Write-Utf8NoBom([string]$Path, [string[]]$Lines) {
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllLines($Path, $Lines, $encoding)
}

function Assert-ProjectGitRoot {
    $topLevelOutput = @(& git -C $projectRoot rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or $topLevelOutput.Count -eq 0) {
        throw '소스 검토 ZIP은 프로젝트 Git 저장소에서만 만들 수 있습니다.'
    }
    $topLevel = [IO.Path]::GetFullPath(([string]$topLevelOutput[-1]).Trim()).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $expectedTopLevel = $projectRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if (-not [string]::Equals(
        $topLevel,
        $expectedTopLevel,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw '프로젝트 루트와 Git 저장소 루트가 일치하지 않습니다.'
    }
}

function Get-TrackedReviewSourceFiles {
    Assert-ProjectGitRoot

    $tracked = @(& git -C $projectRoot -c core.quotepath=false ls-files -- src tests tools)
    if ($LASTEXITCODE -ne 0 -or $tracked.Count -eq 0) {
        throw '검토 ZIP에 넣을 추적 소스 파일 목록을 만들지 못했습니다.'
    }

    $status = @(& git -C $projectRoot -c core.quotepath=false status `
        --porcelain=v1 --untracked-files=all -- src tests tools)
    if ($LASTEXITCODE -ne 0) {
        throw '소스 파일의 Git 추적 상태를 확인하지 못했습니다.'
    }
    if ($status.Count -gt 0) {
        throw 'src, tests 또는 tools의 staged, modified, deleted, untracked 변경을 모두 정리하고 커밋해야 합니다.'
    }

    return @($tracked | ForEach-Object { ([string]$_).Replace('/', '\') } | Sort-Object -Unique)
}

function Test-IsFileSystemLink([IO.FileSystemInfo]$Item) {
    if ($null -eq $Item) { return $false }
    $linkTypeProperty = $Item.PSObject.Properties['LinkType']
    if ($null -ne $linkTypeProperty -and
        -not [string]::IsNullOrWhiteSpace([string]$linkTypeProperty.Value)) {
        return $true
    }
    $targetProperty = $Item.PSObject.Properties['Target']
    if ($null -ne $targetProperty) {
        foreach ($target in @($targetProperty.Value)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$target)) { return $true }
        }
    }
    return $false
}

function Remove-ReleaseReadinessFile(
    [string]$Path,
    [string]$ExpectedParent,
    [string]$ExpectedName) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parentPath = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if (-not [string]::Equals(
            [IO.Path]::GetDirectoryName($fullPath).TrimEnd(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar),
            $parentPath,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            [IO.Path]::GetFileName($fullPath),
            $ExpectedName,
            [StringComparison]::Ordinal)) {
        throw '이전 릴리스 준비 상태를 지울 경로가 정확한 버전별 build 파일이 아닙니다.'
    }
    if (-not (Test-Path -LiteralPath $fullPath)) { return }
    $item = Get-Item -Force -LiteralPath $fullPath
    if ($item.PSIsContainer -or (Test-IsFileSystemLink $item)) {
        throw '릴리스 준비 상태 파일은 링크가 아닌 일반 파일이어야 합니다.'
    }
    Assert-NoFileSystemLinkWithinProject $fullPath
    Remove-Item -LiteralPath $fullPath -Force
}

function Assert-NoFileSystemLinkWithinProject([string]$Path) {
    $projectBoundary = $projectRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $current = Get-Item -LiteralPath $Path
    while ($null -ne $current) {
        $currentPath = [IO.Path]::GetFullPath($current.FullName).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
        if ([string]::Equals(
            $currentPath,
            $projectBoundary,
            [StringComparison]::OrdinalIgnoreCase)) {
            return
        }
        if (-not $currentPath.StartsWith(
            $projectRootPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw '검토 소스의 실제 상위 경로가 프로젝트 밖을 가리킵니다.'
        }
        if (Test-IsFileSystemLink $current) {
            throw '검토 소스 경로에는 재분석 지점이나 심볼릭 링크를 사용할 수 없습니다.'
        }
        $current = if ($current -is [IO.FileInfo]) {
            $current.Directory
        }
        else {
            $current.Parent
        }
    }

    throw '검토 소스 경로를 프로젝트 루트까지 확인하지 못했습니다.'
}

function Assert-TrackedCleanReviewInputs([string[]]$RelativePaths) {
    Assert-ProjectGitRoot
    if ($null -eq $RelativePaths -or $RelativePaths.Count -eq 0) {
        throw '수동 검토 입력 목록이 비어 있습니다.'
    }

    $normalizedPaths = New-Object 'System.Collections.Generic.List[string]'
    $seenPaths = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::Ordinal)
    foreach ($rawPath in $RelativePaths) {
        $relativePath = ([string]$rawPath).Replace('\', '/')
        $invalidPath = [string]::IsNullOrWhiteSpace($relativePath)
        $invalidPath = $invalidPath -or [IO.Path]::IsPathRooted($relativePath)
        $invalidPath = $invalidPath -or $relativePath.StartsWith('/', [StringComparison]::Ordinal)
        $invalidPath = $invalidPath -or $relativePath.EndsWith('/', [StringComparison]::Ordinal)
        $invalidPath = $invalidPath -or ($relativePath -match '(^|/)\.\.(/|$)')
        $invalidPath = $invalidPath -or $relativePath.Contains("`r")
        $invalidPath = $invalidPath -or $relativePath.Contains("`n")
        if ($invalidPath -or -not $seenPaths.Add($relativePath)) {
            throw '수동 검토 입력 경로가 canonical relative path가 아니거나 중복됩니다.'
        }

        $sourcePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $relativePath))
        if (-not $sourcePath.StartsWith(
            $projectRootPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw '수동 검토 입력 파일을 프로젝트 안에서 찾지 못했습니다.'
        }

        $tracked = @(& git -C $projectRoot -c core.quotepath=false `
            ls-files --full-name -- $relativePath)
        if ($LASTEXITCODE -ne 0 -or $tracked.Count -ne 1 -or
            -not [string]::Equals(
                ([string]$tracked[0]).Replace('\', '/'),
                $relativePath,
                [StringComparison]::Ordinal)) {
            throw '수동 검토 입력은 정확한 경로로 Git에 추적되어야 합니다.'
        }

        Assert-NoFileSystemLinkWithinProject $sourcePath
        $normalizedPaths.Add($relativePath)
    }

    $statusArguments = @(
        '-C', $projectRoot,
        '-c', 'core.quotepath=false',
        'status', '--porcelain=v1', '--untracked-files=all', '--') +
        @($normalizedPaths)
    $status = @(& git @statusArguments)
    if ($LASTEXITCODE -ne 0) {
        throw '수동 검토 입력의 Git 상태를 확인하지 못했습니다.'
    }
    if ($status.Count -gt 0) {
        throw '수동 검토 입력의 staged, modified, deleted 또는 untracked 변경을 모두 커밋해야 합니다.'
    }
}

function Copy-TrackedReviewSources([string[]]$RelativePaths, [string]$DestinationRoot) {
    $destinationPath = [IO.Path]::GetFullPath($DestinationRoot)
    $destinationPrefix = $destinationPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    foreach ($relativePath in $RelativePaths) {
        $unexpectedPath = [string]::IsNullOrWhiteSpace($relativePath)
        $unexpectedPath = $unexpectedPath -or [IO.Path]::IsPathRooted($relativePath)
        $unexpectedPath = $unexpectedPath -or ($relativePath -match '(^|[\\/])\.\.([\\/]|$)')
        $unexpectedPath = $unexpectedPath -or ($relativePath -notmatch '^(src|tests|tools)[\\/]')
        if ($unexpectedPath) {
            throw 'Git에서 예상하지 않은 검토 소스 경로를 반환했습니다.'
        }

        $sourcePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $relativePath))
        if (-not $sourcePath.StartsWith(
            $projectRootPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw '검토 소스 경로가 프로젝트 밖을 가리킵니다.'
        }
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw 'Git 추적 소스 파일을 찾지 못했습니다.'
        }
        Assert-NoFileSystemLinkWithinProject $sourcePath

        $targetPath = [IO.Path]::GetFullPath((Join-Path $destinationPath $relativePath))
        if (-not $targetPath.StartsWith(
            $destinationPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw '검토 소스 복사 대상이 검토 폴더 밖을 가리킵니다.'
        }
        $targetParent = Split-Path -Parent $targetPath
        New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
        Copy-Item -Force -LiteralPath $sourcePath -Destination $targetPath
    }
}

function New-ReviewInputSnapshot(
    [string[]]$SourceRelativePaths,
    [string[]]$TargetRelativePaths) {
    if ($null -eq $SourceRelativePaths -or
        $null -eq $TargetRelativePaths -or
        $SourceRelativePaths.Count -eq 0 -or
        $SourceRelativePaths.Count -ne $TargetRelativePaths.Count) {
        throw '검토 입력 스냅샷의 원본·대상 목록이 올바르지 않습니다.'
    }

    $entries = New-Object 'System.Collections.Generic.List[object]'
    $seenTargets = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)
    for ($index = 0; $index -lt $SourceRelativePaths.Count; $index++) {
        $sourceRelative = ([string]$SourceRelativePaths[$index]).Replace('\', '/')
        $targetRelative = ([string]$TargetRelativePaths[$index]).Replace('\', '/')
        foreach ($relativePath in @($sourceRelative, $targetRelative)) {
            $invalidPath = [string]::IsNullOrWhiteSpace($relativePath)
            $invalidPath = $invalidPath -or [IO.Path]::IsPathRooted($relativePath)
            $invalidPath = $invalidPath -or $relativePath.StartsWith(
                '/', [StringComparison]::Ordinal)
            $invalidPath = $invalidPath -or $relativePath.EndsWith(
                '/', [StringComparison]::Ordinal)
            $invalidPath = $invalidPath -or ($relativePath -match '(^|/)\.\.(/|$)')
            $invalidPath = $invalidPath -or $relativePath.Contains("`r")
            $invalidPath = $invalidPath -or $relativePath.Contains("`n")
            if ($invalidPath) {
                throw '검토 입력 스냅샷에 안전하지 않은 상대 경로가 있습니다.'
            }
        }
        if (-not $seenTargets.Add($targetRelative)) {
            throw '검토 입력 스냅샷의 공개 대상 경로가 중복됩니다.'
        }

        $sourcePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $sourceRelative))
        if (-not $sourcePath.StartsWith(
                $projectRootPrefix,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw '검토 입력 스냅샷 원본을 프로젝트 안에서 찾지 못했습니다.'
        }
        Assert-NoFileSystemLinkWithinProject $sourcePath
        $sourceItem = Get-Item -Force -LiteralPath $sourcePath
        $entries.Add([PSCustomObject]@{
            SourceRelativePath = $sourceRelative
            TargetRelativePath = $targetRelative
            Length = [long]$sourceItem.Length
            Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash
        })
    }
    return $entries.ToArray()
}

function Assert-ReviewInputSnapshot(
    [object[]]$Snapshot,
    [string]$DestinationRoot,
    [switch]$RejectUnexpectedFiles) {
    if ($null -eq $Snapshot -or $Snapshot.Count -eq 0) {
        throw '확인할 검토 입력 스냅샷이 비어 있습니다.'
    }
    $destinationPath = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $destinationPrefix = $destinationPath + [IO.Path]::DirectorySeparatorChar
    $expectedTargets = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)
    $expectedDirectories = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)

    foreach ($entry in $Snapshot) {
        $targetRelative = ([string]$entry.TargetRelativePath).Replace('\', '/')
        if (-not $expectedTargets.Add($targetRelative)) {
            throw '확인할 검토 입력 스냅샷의 대상 경로가 중복됩니다.'
        }
        $targetParent = [IO.Path]::GetDirectoryName($targetRelative.Replace('/', '\'))
        while (-not [string]::IsNullOrWhiteSpace($targetParent)) {
            [void]$expectedDirectories.Add($targetParent.Replace('\', '/'))
            $targetParent = [IO.Path]::GetDirectoryName($targetParent)
        }
        $targetPath = [IO.Path]::GetFullPath((Join-Path `
            $destinationPath `
            $targetRelative))
        if (-not $targetPath.StartsWith(
                $destinationPrefix,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            throw '공개 검토 입력 스냅샷 파일이 누락되었거나 경계를 벗어났습니다.'
        }
        Assert-NoFileSystemLinkWithinProject $targetPath
        $targetItem = Get-Item -Force -LiteralPath $targetPath
        $targetHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash
        if ([long]$targetItem.Length -ne [long]$entry.Length -or
            -not [string]::Equals(
                $targetHash,
                [string]$entry.Sha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Git clean 확인 뒤 공개 검토 입력이 변경되어 패키징을 중단합니다.'
        }
    }

    if ($RejectUnexpectedFiles) {
        $actualCount = 0
        foreach ($actualItem in @(Get-ChildItem `
            -LiteralPath $destinationPath `
            -Force `
            -Recurse)) {
            $actualRelative = $actualItem.FullName.Substring(
                $destinationPath.Length + 1).Replace('\', '/')
            if (Test-IsFileSystemLink $actualItem) {
                throw '공개 소스 스냅샷에 파일시스템 링크가 추가되었습니다.'
            }
            if ($actualItem.PSIsContainer) {
                if (-not $expectedDirectories.Contains($actualRelative)) {
                    throw '공개 소스 스냅샷에 예상하지 않은 폴더가 추가되었습니다.'
                }
                continue
            }
            $actualCount++
            if (-not $expectedTargets.Contains($actualRelative)) {
                throw '공개 소스 스냅샷에 예상하지 않은 파일이 추가되었습니다.'
            }
        }
        if ($actualCount -ne $expectedTargets.Count) {
            throw '공개 소스 스냅샷 파일 수가 기대 목록과 일치하지 않습니다.'
        }
    }
}

function Assert-LiveReviewInputBaseline(
    [string[]]$CapturedTrackedPaths,
    [string[]]$ManualPaths,
    [object[]]$Snapshot) {
    $currentTrackedPaths = @(Get-TrackedReviewSourceFiles)
    if (($currentTrackedPaths -join "`n") -cne ($CapturedTrackedPaths -join "`n")) {
        throw '패키징 중 Git 추적 소스 목록이 변경되었습니다.'
    }
    Assert-TrackedCleanReviewInputs $ManualPaths
    Assert-ReviewInputSnapshot $Snapshot $projectRoot
}

function Assert-BuildInputManifestMatchesSnapshot(
    [string]$ManifestPath,
    [object[]]$ExpectedSnapshot) {
    $fullManifestPath = [IO.Path]::GetFullPath($ManifestPath)
    if (-not $fullManifestPath.StartsWith(
            $projectRootPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $fullManifestPath -PathType Leaf)) {
        throw '빌드 입력 manifest를 프로젝트 안에서 찾지 못했습니다.'
    }
    Assert-NoFileSystemLinkWithinProject $fullManifestPath
    $manifestItem = Get-Item -Force -LiteralPath $fullManifestPath
    if ($manifestItem.Length -lt 20 -or $manifestItem.Length -gt 16MB) {
        throw '빌드 입력 manifest 크기가 허용 범위를 벗어났습니다.'
    }
    try {
        $manifestText = (New-Object Text.UTF8Encoding($false, $true)).GetString(
            [IO.File]::ReadAllBytes($fullManifestPath))
    }
    catch {
        throw '빌드 입력 manifest가 올바른 UTF-8이 아닙니다.'
    }
    if ($manifestText.Contains("`r") -or -not $manifestText.EndsWith(
            "`n", [StringComparison]::Ordinal)) {
        throw '빌드 입력 manifest 줄바꿈 형식이 canonical 형식이 아닙니다.'
    }
    $lines = @($manifestText.Substring(0, $manifestText.Length - 1).Split("`n"))
    if ($lines.Count -lt 2 -or $lines[0] -cne 'tsg-build-inputs-v1') {
        throw '빌드 입력 manifest header가 올바르지 않습니다.'
    }

    $expected = New-Object 'System.Collections.Generic.Dictionary[string,object]' (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $ExpectedSnapshot) {
        $relativePath = ([string]$entry.TargetRelativePath).Replace('\', '/')
        if ($expected.ContainsKey($relativePath)) {
            throw '기대 빌드 입력 snapshot 경로가 중복됩니다.'
        }
        $expected.Add($relativePath, $entry)
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)
    for ($lineIndex = 1; $lineIndex -lt $lines.Count; $lineIndex++) {
        $match = [regex]::Match(
            $lines[$lineIndex],
            '^(?<sha>[0-9A-Fa-f]{64})\t(?<length>[0-9]+)\t(?<path>[^\t\r\n]+)$')
        if (-not $match.Success) {
            throw '빌드 입력 manifest row가 canonical 형식이 아닙니다.'
        }
        $relativePath = $match.Groups['path'].Value.Replace('\', '/')
        $invalidPath = [IO.Path]::IsPathRooted($relativePath)
        $invalidPath = $invalidPath -or $relativePath.StartsWith('/', [StringComparison]::Ordinal)
        $invalidPath = $invalidPath -or $relativePath.EndsWith('/', [StringComparison]::Ordinal)
        $invalidPath = $invalidPath -or ($relativePath -match '(^|/)\.\.(/|$)')
        if ($invalidPath -or -not $seen.Add($relativePath) -or
            -not $expected.ContainsKey($relativePath)) {
            throw '빌드 입력 manifest에 예상하지 않은 경로나 중복 경로가 있습니다.'
        }
        [long]$length = 0
        if (-not [long]::TryParse(
                $match.Groups['length'].Value,
                [Globalization.NumberStyles]::None,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$length)) {
            throw '빌드 입력 manifest 파일 길이가 올바르지 않습니다.'
        }
        $entry = $expected[$relativePath]
        if ($length -ne [long]$entry.Length -or
            -not [string]::Equals(
                $match.Groups['sha'].Value,
                [string]$entry.Sha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw '빌드 입력 manifest가 clean source snapshot과 일치하지 않습니다.'
        }
    }
    if ($seen.Count -ne $expected.Count) {
        throw '빌드 입력 manifest에 기대 파일이 누락되었습니다.'
    }
}

function Assert-FileMatchesIntegrity(
    [string]$Path,
    [string]$ExpectedSha256,
    [long]$ExpectedLength) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    if ($ExpectedSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or $ExpectedLength -lt 0 -or
        -not $fullPath.StartsWith(
            $projectRootPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw '고정 패키지 입력 파일의 경로 또는 기대값이 올바르지 않습니다.'
    }
    Assert-NoFileSystemLinkWithinProject $fullPath
    $item = Get-Item -Force -LiteralPath $fullPath
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath).Hash
    if ([long]$item.Length -ne $ExpectedLength -or
        -not [string]::Equals(
            $actualHash,
            $ExpectedSha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw '고정 패키지 입력 파일의 SHA-256 또는 길이가 변경되었습니다.'
    }
}

function Copy-VerifiedFileToStage(
    [string]$SourcePath,
    [string]$DestinationPath,
    [string]$ExpectedSha256,
    [long]$ExpectedLength) {
    Assert-FileMatchesIntegrity $SourcePath $ExpectedSha256 $ExpectedLength
    $destinationFullPath = [IO.Path]::GetFullPath($DestinationPath)
    if (-not $destinationFullPath.StartsWith(
        $projectRootPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw '고정 패키지 입력 복사 대상이 프로젝트 밖을 가리킵니다.'
    }
    $destinationParent = [IO.Path]::GetDirectoryName($destinationFullPath)
    New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
    Assert-NoFileSystemLinkWithinProject $destinationParent
    if (Test-Path -LiteralPath $destinationFullPath) {
        throw '고정 패키지 입력 복사 대상이 이미 존재합니다.'
    }
    Copy-Item -LiteralPath $SourcePath -Destination $destinationFullPath
    Assert-FileMatchesIntegrity $SourcePath $ExpectedSha256 $ExpectedLength
    Assert-FileMatchesIntegrity $destinationFullPath $ExpectedSha256 $ExpectedLength
}

function Get-CurrentFileIntegrity([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith(
            $projectRootPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw '고정할 빌드 출력 파일을 프로젝트 안에서 찾지 못했습니다.'
    }
    Assert-NoFileSystemLinkWithinProject $fullPath
    $item = Get-Item -Force -LiteralPath $fullPath
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath).Hash
    Assert-FileMatchesIntegrity $fullPath $hash ([long]$item.Length)
    return [PSCustomObject]@{
        Path = $fullPath
        Length = [long]$item.Length
        Sha256 = $hash
    }
}

function New-ExactDirectorySnapshot(
    [string]$Root,
    [string[]]$RelativePaths) {
    if ($null -eq $RelativePaths -or $RelativePaths.Count -eq 0) {
        throw '고정할 패키지 입력 파일 목록이 비어 있습니다.'
    }
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    if (-not $rootPath.StartsWith(
            $projectRootPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $rootPath -PathType Container)) {
        throw '고정할 패키지 입력 폴더가 프로젝트 안에 없습니다.'
    }
    Assert-NoFileSystemLinkWithinProject $rootPath
    $entries = New-Object 'System.Collections.Generic.List[object]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($rawRelativePath in $RelativePaths) {
        $relativePath = ([string]$rawRelativePath).Replace('\', '/')
        $invalidPath = [string]::IsNullOrWhiteSpace($relativePath)
        $invalidPath = $invalidPath -or [IO.Path]::IsPathRooted($relativePath)
        $invalidPath = $invalidPath -or $relativePath.StartsWith('/', [StringComparison]::Ordinal)
        $invalidPath = $invalidPath -or $relativePath.EndsWith('/', [StringComparison]::Ordinal)
        $invalidPath = $invalidPath -or ($relativePath -match '(^|/)\.\.(/|$)')
        if ($invalidPath -or -not $seen.Add($relativePath)) {
            throw '고정할 패키지 입력 상대 경로가 안전하지 않거나 중복됩니다.'
        }
        $fullPath = [IO.Path]::GetFullPath((Join-Path $rootPath $relativePath))
        if (-not $fullPath.StartsWith(
            $rootPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw '고정할 패키지 입력이 대상 폴더 밖을 가리킵니다.'
        }
        $integrity = Get-CurrentFileIntegrity $fullPath
        $entries.Add([PSCustomObject]@{
            SourceRelativePath = $relativePath
            TargetRelativePath = $relativePath
            Length = [long]$integrity.Length
            Sha256 = [string]$integrity.Sha256
        })
    }
    $snapshot = $entries.ToArray()
    Assert-ReviewInputSnapshot $snapshot $rootPath -RejectUnexpectedFiles
    return $snapshot
}

function Assert-PendingCompletionMarker(
    [string]$MarkerPath,
    [string]$ExpectedManifestPath,
    [string]$ExpectedVersion) {
    $markerFullPath = [IO.Path]::GetFullPath($MarkerPath)
    $manifestFullPath = [IO.Path]::GetFullPath($ExpectedManifestPath)
    foreach ($path in @($markerFullPath, $manifestFullPath)) {
        if (-not $path.StartsWith(
                $projectRootPrefix,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw '릴리스 준비 marker 검증 파일을 프로젝트 안에서 찾지 못했습니다.'
        }
        Assert-NoFileSystemLinkWithinProject $path
    }
    try {
        $markerText = (New-Object Text.UTF8Encoding($false, $true)).GetString(
            [IO.File]::ReadAllBytes($markerFullPath))
        $marker = $markerText | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw '릴리스 준비 marker가 올바른 UTF-8 JSON이 아닙니다.'
    }
    $expectedMarkerProperties = @(
        'schemaVersion',
        'repository',
        'version',
        'tag',
        'expectedManifestFile',
        'expectedManifestSha256')
    $actualMarkerProperties = @($marker.PSObject.Properties | ForEach-Object { $_.Name })
    if ($actualMarkerProperties.Count -ne $expectedMarkerProperties.Count -or
        @($actualMarkerProperties | Where-Object {
            $expectedMarkerProperties -cnotcontains $_
        }).Count -ne 0) {
        throw '릴리스 준비 marker schema가 exact 형식이 아닙니다.'
    }
    $expectedManifestName = 'release-assets-v' + $ExpectedVersion + '.expected.json'
    $expectedMarkerName = 'release-assets-v' + $ExpectedVersion + '.ready.json'
    $manifestHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestFullPath).Hash
    if ([IO.Path]::GetFileName($markerFullPath) -cne $expectedMarkerName -or
        [IO.Path]::GetFileName($manifestFullPath) -cne $expectedManifestName -or
        [int]$marker.schemaVersion -ne 1 -or
        [string]$marker.repository -cne 'Spirit-Schema/tarkov-server-guard' -or
        [string]$marker.version -cne $ExpectedVersion -or
        [string]$marker.tag -cne ('v' + $ExpectedVersion) -or
        [string]$marker.expectedManifestFile -cne $expectedManifestName -or
        [string]$marker.expectedManifestSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        -not [string]::Equals(
            [string]$marker.expectedManifestSha256,
            $manifestHash,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw '릴리스 준비 marker가 현재 기대 manifest와 일치하지 않습니다.'
    }
    return Get-CurrentFileIntegrity $markerFullPath
}

function Move-ReleaseCompletionMarkerAtomically(
    [string]$PendingMarkerPath,
    [string]$FinalMarkerPath,
    [string]$ExpectedPendingRoot,
    [string]$ExpectedBuildRoot,
    [string]$ExpectedName,
    [string]$ExpectedSha256,
    [long]$ExpectedLength) {
    $pendingPath = [IO.Path]::GetFullPath($PendingMarkerPath)
    $finalPath = [IO.Path]::GetFullPath($FinalMarkerPath)
    $pendingRoot = [IO.Path]::GetFullPath($ExpectedPendingRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $finalRoot = [IO.Path]::GetFullPath($ExpectedBuildRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $invalid = -not [string]::Equals(
        [IO.Path]::GetDirectoryName($pendingPath),
        $pendingRoot,
        [StringComparison]::OrdinalIgnoreCase)
    $invalid = $invalid -or -not [string]::Equals(
        [IO.Path]::GetDirectoryName($finalPath),
        $finalRoot,
        [StringComparison]::OrdinalIgnoreCase)
    $invalid = $invalid -or [IO.Path]::GetFileName($pendingPath) -cne $ExpectedName
    $invalid = $invalid -or [IO.Path]::GetFileName($finalPath) -cne $ExpectedName
    $invalid = $invalid -or -not [string]::Equals(
        [IO.Path]::GetPathRoot($pendingPath),
        [IO.Path]::GetPathRoot($finalPath),
        [StringComparison]::OrdinalIgnoreCase)
    if ($invalid -or (Test-Path -LiteralPath $finalPath)) {
        throw '릴리스 준비 marker 최종 이동 경로가 안전하지 않거나 이미 존재합니다.'
    }
    Assert-NoFileSystemLinkWithinProject $pendingRoot
    Assert-NoFileSystemLinkWithinProject $finalRoot
    Assert-FileMatchesIntegrity $pendingPath $ExpectedSha256 $ExpectedLength
    [IO.File]::Move($pendingPath, $finalPath)
}

$reviewRootSourceFiles = @(
    'app.config',
    'app.manifest',
    'build.ps1',
    'package-release.ps1',
    'release-scan-allowlist.json')
$reviewDocumentFiles = @(
    'README.md',
    'ROADMAP.md',
    'PRIVACY.md',
    'TROUBLESHOOTING.md',
    'LICENSING.md',
    'PUBLICATION_SCOPE.md',
    'CONTRIBUTING.md',
    'DEVELOPMENT.md',
    'LICENSE',
    'THIRD_PARTY_NOTICES.md')
$reviewBrandingFiles = @(
    'tarkov-server-guard-tsg.ico',
    'tarkov-server-guard-tsg-icon-master.png',
    'tarkov-server-guard-tsg-icon-size-preview.png')
$manualReviewInputFiles = @($reviewRootSourceFiles) + @($reviewDocumentFiles) + @(
    $reviewBrandingFiles | ForEach-Object { 'assets/branding/' + $_ })
$includeReleaseNotes = Test-Path -LiteralPath $releaseNotes -PathType Leaf
if ($includeReleaseNotes) {
    $manualReviewInputFiles += Split-Path -Leaf $releaseNotes
}

$trackedReviewSourceFiles = @(Get-TrackedReviewSourceFiles)
Assert-TrackedCleanReviewInputs $manualReviewInputFiles
$sourceSnapshotSources = @($trackedReviewSourceFiles) + @($manualReviewInputFiles) + @('LICENSE')
$sourceSnapshotTargets = @($trackedReviewSourceFiles) + @($manualReviewInputFiles) + @('LICENSE.txt')
$sourceReviewInputSnapshot = New-ReviewInputSnapshot `
    $sourceSnapshotSources `
    $sourceSnapshotTargets
$publishSnapshotSources = @($reviewDocumentFiles) + @(
    'LICENSE',
    'assets/branding/tarkov-server-guard-tsg-icon-master.png')
$publishSnapshotTargets = @($reviewDocumentFiles) + @(
    'LICENSE.txt',
    'assets/branding/tarkov-server-guard-tsg-icon-master.png')
$publishInputSnapshot = New-ReviewInputSnapshot `
    $publishSnapshotSources `
    $publishSnapshotTargets
$liveSnapshotPaths = @($trackedReviewSourceFiles) + @($manualReviewInputFiles)
$liveReviewInputSnapshot = New-ReviewInputSnapshot `
    $liveSnapshotPaths `
    $liveSnapshotPaths
$expectedBuildInputPaths = @($trackedReviewSourceFiles | Where-Object {
    ([string]$_).Replace('\', '/') -match '^src/[^/]+\.cs$'
}) + @(
    'app.config',
    'app.manifest',
    'build.ps1',
    'LICENSE',
    'THIRD_PARTY_NOTICES.md',
    'assets/branding/tarkov-server-guard-tsg.ico',
    'tools/Prepare-BuildIdentity.ps1')
$expectedBuildInputPaths = @($expectedBuildInputPaths | Sort-Object -Unique)
$expectedBuildInputSnapshot = New-ReviewInputSnapshot `
    $expectedBuildInputPaths `
    $expectedBuildInputPaths
$trackedAfterSnapshot = @(Get-TrackedReviewSourceFiles)
if (($trackedAfterSnapshot -join "`n") -cne ($trackedReviewSourceFiles -join "`n")) {
    throw '검토 입력 해시를 고정하는 동안 Git 추적 소스 목록이 변경되었습니다.'
}
Assert-TrackedCleanReviewInputs $manualReviewInputFiles

# A failed rerun must never leave the previous same-version outputs looking
# ready to publish. Invalidate the small trust roots before any fallible build,
# dependency, packaging, or scanning work; keep the bulky prior artifacts for
# diagnosis until a new run finishes successfully.
Remove-ReleaseReadinessFile `
    $releaseCompletionMarkerPath `
    $buildRoot `
    ('release-assets-v' + $Version + '.ready.json')
Remove-ReleaseReadinessFile `
    $releaseAssetManifestPath `
    $buildRoot `
    ('release-assets-v' + $Version + '.expected.json')

$publishRoot = Reset-ProjectChild $publishRoot $buildRoot
$releasesStageRoot = Reset-ProjectChild $releasesStageRoot $buildRoot
$sourceReviewStageRoot = Reset-ProjectChild $sourceReviewStageRoot $buildRoot
$packageInputStageRoot = Reset-ProjectChild $packageInputStageRoot $buildRoot
$completionPendingRoot = Reset-ProjectChild $completionPendingRoot $buildRoot

Assert-LiveReviewInputBaseline `
    $trackedReviewSourceFiles `
    $manualReviewInputFiles `
    $liveReviewInputSnapshot
$dependencies = & (Join-Path $projectRoot 'tools\Prepare-Velopack.ps1')
if ($dependencies -is [array]) { $dependencies = $dependencies[-1] }
$frozenDependencyRoot = Join-Path $packageInputStageRoot 'dependencies'
$frozenVelopackDll = Join-Path $frozenDependencyRoot 'Velopack.dll'
$frozenNewtonsoftJsonDll = Join-Path $frozenDependencyRoot 'Newtonsoft.Json.dll'
$frozenVpkDll = Join-Path $frozenDependencyRoot 'vpk.dll'
Copy-VerifiedFileToStage `
    $dependencies.VelopackDll `
    $frozenVelopackDll `
    ([string]$dependencies.VelopackDllSha256) `
    ([long]$dependencies.VelopackDllLength)
Copy-VerifiedFileToStage `
    $dependencies.NewtonsoftJsonDll `
    $frozenNewtonsoftJsonDll `
    ([string]$dependencies.NewtonsoftJsonDllSha256) `
    ([long]$dependencies.NewtonsoftJsonDllLength)
Copy-VerifiedFileToStage `
    $dependencies.VpkDll `
    $frozenVpkDll `
    ([string]$dependencies.VpkDllSha256) `
    ([long]$dependencies.VpkDllLength)
Assert-LiveReviewInputBaseline `
    $trackedReviewSourceFiles `
    $manualReviewInputFiles `
    $liveReviewInputSnapshot

$buildParameters = @{
    OutputDirectory = $publishRoot
    BuildChannel = 'release'
}
if ($SkipTests) { $buildParameters.SkipTests = $true }
Assert-LiveReviewInputBaseline `
    $trackedReviewSourceFiles `
    $manualReviewInputFiles `
    $liveReviewInputSnapshot
& (Join-Path $projectRoot 'build.ps1') @buildParameters
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Assert-LiveReviewInputBaseline `
    $trackedReviewSourceFiles `
    $manualReviewInputFiles `
    $liveReviewInputSnapshot

$publishExecutable = Join-Path $publishRoot 'TarkovServerGuard.exe'
$liveIdentityInspector = Join-Path $buildRoot 'BuildIdentityAppInspector.exe'
$liveBinaryIdentityRoot = Join-Path $buildRoot 'generated\build-identity'
$liveBinaryIdentityManifest = Join-Path $liveBinaryIdentityRoot 'build-identity.json'
$liveBuildInputManifest = Join-Path $liveBinaryIdentityRoot 'build-inputs.manifest'
$builtExecutableIntegrity = Get-CurrentFileIntegrity $publishExecutable
$identityInspectorIntegrity = Get-CurrentFileIntegrity $liveIdentityInspector
$binaryIdentityManifestIntegrity = Get-CurrentFileIntegrity $liveBinaryIdentityManifest
$buildInputManifestIntegrity = Get-CurrentFileIntegrity $liveBuildInputManifest
$frozenBuildRoot = Join-Path $packageInputStageRoot 'build'
$builtExecutable = Join-Path $frozenBuildRoot 'TarkovServerGuard.exe'
$identityInspector = Join-Path $frozenBuildRoot 'BuildIdentityAppInspector.exe'
$binaryIdentityManifest = Join-Path $frozenBuildRoot 'build-identity.json'
$buildInputManifest = Join-Path $frozenBuildRoot 'build-inputs.manifest'
Copy-VerifiedFileToStage `
    $publishExecutable `
    $builtExecutable `
    $builtExecutableIntegrity.Sha256 `
    $builtExecutableIntegrity.Length
Copy-VerifiedFileToStage `
    $liveIdentityInspector `
    $identityInspector `
    $identityInspectorIntegrity.Sha256 `
    $identityInspectorIntegrity.Length
Copy-VerifiedFileToStage `
    $liveBinaryIdentityManifest `
    $binaryIdentityManifest `
    $binaryIdentityManifestIntegrity.Sha256 `
    $binaryIdentityManifestIntegrity.Length
Copy-VerifiedFileToStage `
    $liveBuildInputManifest `
    $buildInputManifest `
    $buildInputManifestIntegrity.Sha256 `
    $buildInputManifestIntegrity.Length
$packageInputRelativePaths = @(
    'dependencies/Velopack.dll',
    'dependencies/Newtonsoft.Json.dll',
    'dependencies/vpk.dll',
    'build/TarkovServerGuard.exe',
    'build/BuildIdentityAppInspector.exe',
    'build/build-identity.json',
    'build/build-inputs.manifest')
$packageInputSnapshot = New-ExactDirectorySnapshot `
    $packageInputStageRoot `
    $packageInputRelativePaths
Assert-BuildInputManifestMatchesSnapshot `
    $buildInputManifest `
    $expectedBuildInputSnapshot

$expectedFileVersion = $Version + '.0'
$actualFileVersion = (Get-Item -LiteralPath $builtExecutable).VersionInfo.FileVersion
if (-not [string]::Equals(
    $actualFileVersion,
    $expectedFileVersion,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "소스 파일 버전($actualFileVersion)과 패키지 버전($Version)이 일치하지 않습니다."
}
try {
    $binaryIdentityInfo = [IO.File]::ReadAllText(
        $binaryIdentityManifest,
        (New-Object Text.UTF8Encoding($false, $true))) | ConvertFrom-Json
}
catch {
    throw '공개 빌드 출처 manifest가 올바른 UTF-8 JSON이 아닙니다.'
}
$binaryIdentityInvalid = [int]$binaryIdentityInfo.schemaVersion -ne 1
$binaryIdentityInvalid = $binaryIdentityInvalid -or `
    [string]$binaryIdentityInfo.applicationVersion -cne $Version
$binaryIdentityInvalid = $binaryIdentityInvalid -or `
    [string]$binaryIdentityInfo.channel -cne 'release'
$binaryIdentityInvalid = $binaryIdentityInvalid -or `
    [string]$binaryIdentityInfo.binaryBuildId -notmatch '^tsg-bin-v1-[0-9a-f]{64}$'
$binaryIdentityInvalid = $binaryIdentityInvalid -or `
    [string]$binaryIdentityInfo.sourceRevision -notmatch `
        '^(?:[0-9a-f]{40}|[0-9a-f]{64}|tree-[0-9a-f]{64})$'
$binaryIdentityInvalid = $binaryIdentityInvalid -or `
    [string]$binaryIdentityInfo.buildInputManifestSha256 -notmatch '^[0-9a-f]{64}$'
$binaryIdentityInvalid = $binaryIdentityInvalid -or `
    (Get-FileHash -Algorithm SHA256 -LiteralPath $buildInputManifest).Hash.ToLowerInvariant() `
        -cne [string]$binaryIdentityInfo.buildInputManifestSha256
if ($binaryIdentityInvalid) {
    throw '공개 빌드 출처 manifest와 실제 빌드 입력이 일치하지 않습니다.'
}
& $identityInspector `
    $builtExecutable `
    $Version `
    'release' `
    ([string]$binaryIdentityInfo.binaryBuildId) `
    ([string]$binaryIdentityInfo.sourceRevision) `
    ([string]$binaryIdentityInfo.buildInputManifestSha256)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Assert-ReviewInputSnapshot `
    $packageInputSnapshot `
    $packageInputStageRoot `
    -RejectUnexpectedFiles

Copy-Item -Force -LiteralPath $builtExecutable `
    -Destination $publishExecutable
Assert-FileMatchesIntegrity `
    $publishExecutable `
    $builtExecutableIntegrity.Sha256 `
    $builtExecutableIntegrity.Length
Copy-Item -Force -LiteralPath $frozenVelopackDll `
    -Destination (Join-Path $publishRoot 'Velopack.dll')
Copy-Item -Force -LiteralPath $frozenNewtonsoftJsonDll `
    -Destination (Join-Path $publishRoot 'Newtonsoft.Json.dll')
Assert-ReviewInputSnapshot `
    $packageInputSnapshot `
    $packageInputStageRoot `
    -RejectUnexpectedFiles
foreach ($document in $reviewDocumentFiles) {
    Copy-Item -Force -LiteralPath (Join-Path $projectRoot $document) `
        -Destination (Join-Path $publishRoot $document)
}
Copy-Item -Force -LiteralPath (Join-Path $projectRoot 'LICENSE') `
    -Destination (Join-Path $publishRoot 'LICENSE.txt')
$publishBrandingRoot = Join-Path $publishRoot 'assets\branding'
New-Item -ItemType Directory -Force -Path $publishBrandingRoot | Out-Null
Copy-Item -Force `
    -LiteralPath (Join-Path $projectRoot 'assets\branding\tarkov-server-guard-tsg-icon-master.png') `
    -Destination (Join-Path $publishBrandingRoot 'tarkov-server-guard-tsg-icon-master.png')
Assert-ReviewInputSnapshot $publishInputSnapshot $publishRoot
$publishPayloadRelativePaths = @(
    'TarkovServerGuard.exe',
    'Velopack.dll',
    'Newtonsoft.Json.dll') + @($reviewDocumentFiles) + @(
    'LICENSE.txt',
    'assets/branding/tarkov-server-guard-tsg-icon-master.png')
$publishPayloadRelativePaths = @($publishPayloadRelativePaths | Sort-Object -Unique)
$publishPayloadSnapshot = New-ExactDirectorySnapshot `
    $publishRoot `
    $publishPayloadRelativePaths

# Build the exact public source snapshot before packing. The same bytes are scanned
# here and copied into the final review directory later.
Copy-TrackedReviewSources $trackedReviewSourceFiles $sourceReviewStageRoot
foreach ($sourceFile in $reviewRootSourceFiles) {
    Copy-Item -Force -LiteralPath (Join-Path $projectRoot $sourceFile) `
        -Destination (Join-Path $sourceReviewStageRoot $sourceFile)
}
foreach ($sourceDocument in $reviewDocumentFiles) {
    Copy-Item -Force -LiteralPath (Join-Path $projectRoot $sourceDocument) `
        -Destination (Join-Path $sourceReviewStageRoot $sourceDocument)
}
Copy-Item -Force -LiteralPath (Join-Path $projectRoot 'LICENSE') `
    -Destination (Join-Path $sourceReviewStageRoot 'LICENSE.txt')
if ($includeReleaseNotes) {
    Copy-Item -Force -LiteralPath $releaseNotes `
        -Destination (Join-Path $sourceReviewStageRoot (Split-Path -Leaf $releaseNotes))
}
$brandingSourceStageRoot = Join-Path $sourceReviewStageRoot 'assets\branding'
New-Item -ItemType Directory -Force -Path $brandingSourceStageRoot | Out-Null
foreach ($brandingFile in $reviewBrandingFiles) {
    Copy-Item -Force -LiteralPath `
        (Join-Path $projectRoot ('assets\branding\' + $brandingFile)) `
        -Destination $brandingSourceStageRoot
}
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
$packageAppIcon = Join-Path `
    $sourceReviewStageRoot `
    'assets\branding\tarkov-server-guard-tsg.ico'
$installerLicense = Join-Path $sourceReviewStageRoot 'LICENSE'
$packageReleaseNotes = if ($includeReleaseNotes) {
    Join-Path $sourceReviewStageRoot (Split-Path -Leaf $releaseNotes)
}
else { $null }
$frozenReleaseSanitizer = Join-Path `
    $sourceReviewStageRoot `
    'tools\Assert-ReleaseSanitized.ps1'
$frozenReleaseSanitizerAllowlist = Join-Path `
    $sourceReviewStageRoot `
    'release-scan-allowlist.json'
$frozenBinaryIdentityScanner = Join-Path `
    $sourceReviewStageRoot `
    'tools\Test-ReleaseBinaryIdentity.ps1'
$frozenProvenanceGenerator = Join-Path `
    $sourceReviewStageRoot `
    'tools\New-ReleaseProvenance.ps1'
$frozenAssetManifestGenerator = Join-Path `
    $sourceReviewStageRoot `
    'tools\New-ReleaseAssetManifest.ps1'
$frozenCompletionMarkerGenerator = Join-Path `
    $sourceReviewStageRoot `
    'tools\New-ReleaseCompletionMarker.ps1'
foreach ($frozenTool in @(
    $frozenReleaseSanitizer,
    $frozenReleaseSanitizerAllowlist,
    $frozenBinaryIdentityScanner,
    $frozenProvenanceGenerator,
    $frozenAssetManifestGenerator,
    $frozenCompletionMarkerGenerator)) {
    if (-not (Test-Path -LiteralPath $frozenTool -PathType Leaf)) {
        throw '검증된 공개 소스 스냅샷에서 릴리스 보안 도구를 찾지 못했습니다.'
    }
}

$prepackSanitizationReport = Join-Path $buildRoot `
    ('release-sanitization-prepack-v' + $Version + '.json')
& $frozenReleaseSanitizer `
    -InputPath @($publishRoot, $sourceReviewStageRoot) `
    -InputLabel @('publish', 'source') `
    -InputMode @('Directory', 'Directory') `
    -ProjectRoot $projectRoot `
    -AllowlistPath $frozenReleaseSanitizerAllowlist `
    -ReportPath $prepackSanitizationReport `
    -ReturnStatus
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles

if (-not $SkipTests) {
    $updateTestRoot = Join-Path $buildRoot 'update-runtime-test'
    $updateTestRoot = Reset-ProjectChild $updateTestRoot $buildRoot
    foreach ($file in @('GitHubUpdateTests.exe')) {
        Copy-Item -Force -LiteralPath (Join-Path $buildRoot $file) `
            -Destination (Join-Path $updateTestRoot $file)
    }
    Assert-ReviewInputSnapshot `
        $packageInputSnapshot `
        $packageInputStageRoot `
        -RejectUnexpectedFiles
    Copy-Item -Force -LiteralPath $frozenVelopackDll `
        -Destination (Join-Path $updateTestRoot 'Velopack.dll')
    Copy-Item -Force -LiteralPath $frozenNewtonsoftJsonDll `
        -Destination (Join-Path $updateTestRoot 'Newtonsoft.Json.dll')
    & (Join-Path $updateTestRoot 'GitHubUpdateTests.exe')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Assert-ReviewInputSnapshot `
        $packageInputSnapshot `
        $packageInputStageRoot `
        -RejectUnexpectedFiles
}

# The public source and package-facing document snapshots must still match the
# clean Git state immediately before Velopack consumes them.
Assert-ReviewInputSnapshot `
    $publishPayloadSnapshot `
    $publishRoot `
    -RejectUnexpectedFiles
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
Assert-ReviewInputSnapshot `
    $packageInputSnapshot `
    $packageInputStageRoot `
    -RejectUnexpectedFiles

$vpkArguments = @(
    $frozenVpkDll,
    'pack',
    '--packId', 'SpiritSchema.TarkovServerGuard',
    '--packVersion', $Version,
    '--packDir', $publishRoot,
    '--mainExe', 'TarkovServerGuard.exe',
    '--packTitle', 'Tarkov Server Guard',
    '--packAuthors', 'Spirit-Schema',
    '--icon', $packageAppIcon,
    '--instLicense', $installerLicense,
    '--outputDir', $releasesStageRoot,
    '--instLocation', 'PerUser',
    # The bootstrap call is intentionally reflection-based so raw developer
    # builds still run without Velopack.dll; runtime binding is tested above.
    '--skipVeloAppCheck'
)
if (-not [string]::IsNullOrWhiteSpace($packageReleaseNotes)) {
    $vpkArguments += @('--releaseNotes', $packageReleaseNotes)
}
& dotnet @vpkArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Assert-ReviewInputSnapshot `
    $publishPayloadSnapshot `
    $publishRoot `
    -RejectUnexpectedFiles
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
Assert-ReviewInputSnapshot `
    $packageInputSnapshot `
    $packageInputStageRoot `
    -RejectUnexpectedFiles
$archiveIdentityInspection = & $frozenBinaryIdentityScanner `
    -AssetDirectory $releasesStageRoot `
    -InspectorPath $identityInspector `
    -ExpectedVersion $Version `
    -ExpectedChannel 'release' `
    -ExpectedBinaryBuildId ([string]$binaryIdentityInfo.binaryBuildId) `
    -ExpectedRevision ([string]$binaryIdentityInfo.sourceRevision) `
    -ExpectedBuildInputManifestSha256 `
        ([string]$binaryIdentityInfo.buildInputManifestSha256)
if ($archiveIdentityInspection -is [array]) {
    $archiveIdentityInspection = $archiveIdentityInspection[-1]
}
Assert-ReviewInputSnapshot `
    $packageInputSnapshot `
    $packageInputStageRoot `
    -RejectUnexpectedFiles
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles

$setupSanitizationAssets = @(Get-ChildItem -LiteralPath $releasesStageRoot -File |
    Where-Object { $_.Name -match '(?i)setup.*\.exe$' })
$archiveSanitizationAssets = @(Get-ChildItem -LiteralPath $releasesStageRoot -File |
    Where-Object { $_.Extension -in @('.zip', '.nupkg') })
if ($setupSanitizationAssets.Count -eq 0 -or
    @($archiveSanitizationAssets | Where-Object { $_.Extension -eq '.zip' }).Count -eq 0 -or
    @($archiveSanitizationAssets | Where-Object { $_.Extension -eq '.nupkg' }).Count -eq 0) {
    throw 'Setup, Portable ZIP 또는 nupkg 민감정보 검사 입력이 누락되었습니다.'
}
$postpackSanitizationAssets = @($setupSanitizationAssets) + @($archiveSanitizationAssets)
$postpackSanitizationPaths = @($postpackSanitizationAssets | ForEach-Object { $_.FullName })
$postpackSanitizationModes = @($postpackSanitizationAssets | ForEach-Object {
    if ($_.Extension -eq '.exe') { 'Setup' } else { 'Archive' }
})
$postpackSanitizationLabels = @(for (
    $sanitizationIndex = 0;
    $sanitizationIndex -lt $postpackSanitizationAssets.Count;
    $sanitizationIndex++) {
    'packaged-asset-' + ($sanitizationIndex + 1)
})
$postpackSanitizationReport = Join-Path $buildRoot `
    ('release-sanitization-postpack-v' + $Version + '.json')
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
& $frozenReleaseSanitizer `
    -InputPath $postpackSanitizationPaths `
    -InputLabel $postpackSanitizationLabels `
    -InputMode $postpackSanitizationModes `
    -ProjectRoot $projectRoot `
    -AllowlistPath $frozenReleaseSanitizerAllowlist `
    -ReportPath $postpackSanitizationReport `
    -ReturnStatus
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles

Assert-ReviewInputSnapshot `
    $packageInputSnapshot `
    $packageInputStageRoot `
    -RejectUnexpectedFiles
Copy-Item -Force -LiteralPath $binaryIdentityManifest `
    -Destination (Join-Path $releasesStageRoot 'binary-build-identity.json')
Copy-Item -Force -LiteralPath $buildInputManifest `
    -Destination (Join-Path $releasesStageRoot 'build-inputs.manifest')
Assert-FileMatchesIntegrity `
    (Join-Path $releasesStageRoot 'binary-build-identity.json') `
    $binaryIdentityManifestIntegrity.Sha256 `
    $binaryIdentityManifestIntegrity.Length
Assert-FileMatchesIntegrity `
    (Join-Path $releasesStageRoot 'build-inputs.manifest') `
    $buildInputManifestIntegrity.Sha256 `
    $buildInputManifestIntegrity.Length
Assert-ReviewInputSnapshot `
    $packageInputSnapshot `
    $packageInputStageRoot `
    -RejectUnexpectedFiles

$releaseHashLines = foreach ($file in (
    Get-ChildItem -LiteralPath $releasesStageRoot -File |
        Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
        Sort-Object Name)) {
    '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash, $file.Name
}
Write-Utf8NoBom `
    (Join-Path $releasesStageRoot 'SHA256SUMS.txt') `
    @($releaseHashLines)

$releasesRoot = Reset-ProjectChild $releasesRoot $buildRoot
Copy-Item -Force -Path (Join-Path $releasesStageRoot '*') `
    -Destination $releasesRoot -Recurse
$reviewRoot = Reset-ProjectChild $reviewRoot (Join-Path $projectRoot 'review')

Assert-ReviewInputSnapshot `
    $publishPayloadSnapshot `
    $publishRoot `
    -RejectUnexpectedFiles
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
Copy-Item -Force -Path (Join-Path $publishRoot '*') -Destination $reviewRoot -Recurse
Assert-ReviewInputSnapshot `
    $publishPayloadSnapshot `
    $reviewRoot `
    -RejectUnexpectedFiles
$velopackReviewRoot = Join-Path $reviewRoot 'Velopack-Release'
New-Item -ItemType Directory -Force -Path $velopackReviewRoot | Out-Null
Copy-Item -Force -Path (Join-Path $releasesRoot '*') `
    -Destination $velopackReviewRoot -Recurse

$sourceReviewRoot = Join-Path $reviewRoot 'source'
New-Item -ItemType Directory -Force -Path $sourceReviewRoot | Out-Null
Copy-Item -Force -Path (Join-Path $sourceReviewStageRoot '*') `
    -Destination $sourceReviewRoot -Recurse
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewRoot `
    -RejectUnexpectedFiles

$hashFiles = Get-ChildItem -LiteralPath $reviewRoot -File -Recurse |
    Sort-Object FullName
$hashLines = foreach ($file in $hashFiles) {
    $relative = $file.FullName.Substring($reviewRoot.Length + 1).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash, $relative
}
Write-Utf8NoBom (Join-Path $reviewRoot 'SHA256SUMS.txt') @($hashLines)

$reviewZip = Join-Path (Split-Path -Parent $reviewRoot) `
    ("TarkovServerGuard-v" + $Version + '.zip')
if (Test-Path -LiteralPath $reviewZip) { Remove-Item -LiteralPath $reviewZip -Force }
Compress-Archive -Path (Join-Path $reviewRoot '*') -DestinationPath $reviewZip -CompressionLevel Optimal
$reviewZipHashPath = $reviewZip + '.sha256.txt'
$reviewZipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $reviewZip).Hash
($reviewZipHash + '  ' + (Split-Path -Leaf $reviewZip)) |
    Set-Content -Encoding ASCII -LiteralPath $reviewZipHashPath

$reviewSanitizationReport = Join-Path $buildRoot `
    ('release-sanitization-review-v' + $Version + '.json')
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
& $frozenReleaseSanitizer `
    -InputPath $reviewZip `
    -InputLabel 'public-review' `
    -InputMode 'Archive' `
    -ProjectRoot $projectRoot `
    -AllowlistPath $frozenReleaseSanitizerAllowlist `
    -ReportPath $reviewSanitizationReport `
    -ReturnStatus
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles

Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
Assert-ReviewInputSnapshot `
    $packageInputSnapshot `
    $packageInputStageRoot `
    -RejectUnexpectedFiles
$releaseProvenance = & $frozenProvenanceGenerator `
    -BinaryIdentityManifest $binaryIdentityManifest `
    -AssetDirectory $releasesRoot `
    -AdditionalAssetPath @($reviewZip, $reviewZipHashPath) `
    -AdditionalAssetName @(
        ('review/' + (Split-Path -Leaf $reviewZip)),
        ('review/' + (Split-Path -Leaf $reviewZipHashPath)))
if ($releaseProvenance -is [array]) { $releaseProvenance = $releaseProvenance[-1] }
Assert-ReviewInputSnapshot `
    $packageInputSnapshot `
    $packageInputStageRoot `
    -RejectUnexpectedFiles
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles

# Provenance is written after the review ZIP by design. Scan the now-complete
# public release directory so feeds, hashes and provenance are also covered.
$releaseDirectorySanitizationReport = Join-Path $buildRoot `
    ('release-sanitization-public-assets-v' + $Version + '.json')
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
& $frozenReleaseSanitizer `
    -InputPath $releasesRoot `
    -InputLabel 'public-release' `
    -InputMode 'Directory' `
    -ProjectRoot $projectRoot `
    -AllowlistPath $frozenReleaseSanitizerAllowlist `
    -ReportPath $releaseDirectorySanitizationReport `
    -ReturnStatus
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles

# This verifier input is intentionally outside Releases-v* so it is not itself
# a public asset and cannot create a self-hash cycle. Generate it only after the
# review archive/hash and the final two provenance files all exist.
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
$releaseAssetManifest = & $frozenAssetManifestGenerator `
    -Version $Version `
    -ReleaseDirectory $releasesRoot `
    -ReviewZip $reviewZip `
    -ReviewZipHash $reviewZipHashPath `
    -OutputPath $releaseAssetManifestPath
if ($releaseAssetManifest -is [array]) {
    $releaseAssetManifest = $releaseAssetManifest[-1]
}
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles

# Generate and independently validate the marker under a same-volume pending
# directory. Only its final atomic rename is allowed to make the run ready.
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles
$pendingCompletionMarkerPath = Join-Path `
    $completionPendingRoot `
    ('release-assets-v' + $Version + '.ready.json')
$releaseCompletion = & $frozenCompletionMarkerGenerator `
    -Version $Version `
    -ExpectedManifest $releaseAssetManifestPath `
    -OutputPath $pendingCompletionMarkerPath
if ($releaseCompletion -is [array]) { $releaseCompletion = $releaseCompletion[-1] }
$pendingCompletionIntegrity = Assert-PendingCompletionMarker `
    $pendingCompletionMarkerPath `
    $releaseAssetManifestPath `
    $Version
Assert-ReviewInputSnapshot `
    $sourceReviewInputSnapshot `
    $sourceReviewStageRoot `
    -RejectUnexpectedFiles

if (Test-Path -LiteralPath $releasesStageRoot) {
    $emptyReleaseStage = Reset-ProjectChild $releasesStageRoot $buildRoot
    Remove-Item -LiteralPath $emptyReleaseStage -Force
}
if (Test-Path -LiteralPath $sourceReviewStageRoot) {
    $emptySourceStage = Reset-ProjectChild $sourceReviewStageRoot $buildRoot
    Remove-Item -LiteralPath $emptySourceStage -Force
}
if (Test-Path -LiteralPath $packageInputStageRoot) {
    $emptyPackageInputStage = Reset-ProjectChild $packageInputStageRoot $buildRoot
    Remove-Item -LiteralPath $emptyPackageInputStage -Force
}

$pendingCompletionIntegrityAfterCleanup = Assert-PendingCompletionMarker `
    $pendingCompletionMarkerPath `
    $releaseAssetManifestPath `
    $Version
if ([long]$pendingCompletionIntegrityAfterCleanup.Length -ne
        [long]$pendingCompletionIntegrity.Length -or
    -not [string]::Equals(
        [string]$pendingCompletionIntegrityAfterCleanup.Sha256,
        [string]$pendingCompletionIntegrity.Sha256,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw '정리 중 릴리스 준비 pending marker가 변경되었습니다.'
}
$completionMarkerName = 'release-assets-v' + $Version + '.ready.json'
$completionDisplayPath = $releaseCompletionMarkerPath
$assetManifestDisplayPath = $releaseAssetManifest.ManifestPath
$binaryBuildIdDisplay = $releaseProvenance.BinaryBuildId
$releaseBundleIdDisplay = $releaseProvenance.ReleaseBundleId
$archiveCountDisplay = $archiveIdentityInspection.ArchiveCount

# This same-volume rename is deliberately the final file mutation. Do not add
# assertions, cleanup, report writes, or other filesystem changes below it.
Move-ReleaseCompletionMarkerAtomically `
    $pendingCompletionMarkerPath `
    $releaseCompletionMarkerPath `
    $completionPendingRoot `
    $buildRoot `
    $completionMarkerName `
    $pendingCompletionIntegrity.Sha256 `
    $pendingCompletionIntegrity.Length

Write-Host ("검토본 완료: " + $reviewRoot)
Write-Host ("검토본 ZIP: " + $reviewZip)
Write-Host ("Velopack 업로드 자산: " + $releasesRoot)
Write-Host ("공개 자산 기대 manifest: " + $assetManifestDisplayPath)
Write-Host ("릴리스 준비 완료 marker: " + $completionDisplayPath)
Write-Host ("Binary Build ID: " + $binaryBuildIdDisplay)
Write-Host ("Release Bundle ID: " + $releaseBundleIdDisplay)
Write-Host ("출처 검증한 패키지 아카이브: " + $archiveCountDisplay)
