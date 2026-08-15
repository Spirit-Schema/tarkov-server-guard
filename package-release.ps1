# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [string]$Version = '0.7.4',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw '릴리스 버전은 1.2.3 같은 3자리 SemVer 형식이어야 합니다.'
}

$projectRoot = $PSScriptRoot
$buildRoot = Join-Path $projectRoot 'build'
$publishRoot = Join-Path $buildRoot ("publish-v" + $Version)
$releasesRoot = Join-Path $buildRoot ("Releases-v" + $Version)
$releasesStageRoot = Join-Path $buildRoot ("Releases-v" + $Version + '-staging')
$reviewRoot = Join-Path $projectRoot ("review\TarkovServerGuard-v" + $Version)
$releaseNotes = Join-Path $projectRoot ("release-notes-v" + $Version + '.md')
$appIcon = Join-Path $projectRoot 'assets\branding\tarkov-server-guard-tsg.ico'

if (-not (Test-Path -LiteralPath $appIcon -PathType Leaf)) {
    throw "앱 아이콘을 찾지 못했습니다: $appIcon"
}

function Reset-ProjectChild([string]$Path, [string]$ExpectedParent) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parentPath = [IO.Path]::GetFullPath($ExpectedParent)
    if (-not $fullPath.StartsWith(
        $parentPath + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "예상하지 않은 빌드 정리 경로입니다: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $fullPath | Out-Null
    return $fullPath
}

function Write-Utf8NoBom([string]$Path, [string[]]$Lines) {
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllLines($Path, $Lines, $encoding)
}

$publishRoot = Reset-ProjectChild $publishRoot $buildRoot
$releasesStageRoot = Reset-ProjectChild $releasesStageRoot $buildRoot

$dependencies = & (Join-Path $projectRoot 'tools\Prepare-Velopack.ps1')
if ($dependencies -is [array]) { $dependencies = $dependencies[-1] }

$buildParameters = @{ OutputDirectory = $publishRoot }
if ($SkipTests) { $buildParameters.SkipTests = $true }
& (Join-Path $projectRoot 'build.ps1') @buildParameters
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$builtExecutable = Join-Path $publishRoot 'TarkovServerGuard.exe'
$expectedFileVersion = $Version + '.0'
$actualFileVersion = (Get-Item -LiteralPath $builtExecutable).VersionInfo.FileVersion
if (-not [string]::Equals(
    $actualFileVersion,
    $expectedFileVersion,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "소스 파일 버전($actualFileVersion)과 패키지 버전($Version)이 일치하지 않습니다."
}

Copy-Item -Force -LiteralPath $dependencies.VelopackDll `
    -Destination (Join-Path $publishRoot 'Velopack.dll')
Copy-Item -Force -LiteralPath $dependencies.NewtonsoftJsonDll `
    -Destination (Join-Path $publishRoot 'Newtonsoft.Json.dll')
foreach ($document in @(
    'README.md',
    'PRIVACY.md',
    'TROUBLESHOOTING.md',
    'LICENSING.md',
    'PUBLICATION_SCOPE.md',
    'CONTRIBUTING.md',
    'DEVELOPMENT.md',
    'LICENSE',
    'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -Force -LiteralPath (Join-Path $projectRoot $document) `
        -Destination (Join-Path $publishRoot $document)
}
Copy-Item -Force -LiteralPath (Join-Path $projectRoot 'LICENSE') `
    -Destination (Join-Path $publishRoot 'LICENSE.txt')
$installerLicense = Join-Path $buildRoot 'TarkovServerGuard-Installer-License.md'
Copy-Item -Force -LiteralPath (Join-Path $projectRoot 'LICENSE') `
    -Destination $installerLicense
$publishBrandingRoot = Join-Path $publishRoot 'assets\branding'
New-Item -ItemType Directory -Force -Path $publishBrandingRoot | Out-Null
Copy-Item -Force `
    -LiteralPath (Join-Path $projectRoot 'assets\branding\tarkov-server-guard-tsg-icon-master.png') `
    -Destination (Join-Path $publishBrandingRoot 'tarkov-server-guard-tsg-icon-master.png')

if (-not $SkipTests) {
    $updateTestRoot = Join-Path $buildRoot 'update-runtime-test'
    $updateTestRoot = Reset-ProjectChild $updateTestRoot $buildRoot
    foreach ($file in @('GitHubUpdateTests.exe')) {
        Copy-Item -Force -LiteralPath (Join-Path $buildRoot $file) `
            -Destination (Join-Path $updateTestRoot $file)
    }
    Copy-Item -Force -LiteralPath $dependencies.VelopackDll `
        -Destination (Join-Path $updateTestRoot 'Velopack.dll')
    Copy-Item -Force -LiteralPath $dependencies.NewtonsoftJsonDll `
        -Destination (Join-Path $updateTestRoot 'Newtonsoft.Json.dll')
    & (Join-Path $updateTestRoot 'GitHubUpdateTests.exe')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$vpkArguments = @(
    $dependencies.VpkDll,
    'pack',
    '--packId', 'SpiritSchema.TarkovServerGuard',
    '--packVersion', $Version,
    '--packDir', $publishRoot,
    '--mainExe', 'TarkovServerGuard.exe',
    '--packTitle', 'Tarkov Server Guard',
    '--packAuthors', 'Spirit-Schema',
    '--icon', $appIcon,
    '--instLicense', $installerLicense,
    '--outputDir', $releasesStageRoot,
    '--instLocation', 'PerUser',
    # The bootstrap call is intentionally reflection-based so raw developer
    # builds still run without Velopack.dll; runtime binding is tested above.
    '--skipVeloAppCheck'
)
if (Test-Path -LiteralPath $releaseNotes) {
    $vpkArguments += @('--releaseNotes', $releaseNotes)
}
& dotnet @vpkArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

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

Copy-Item -Force -Path (Join-Path $publishRoot '*') -Destination $reviewRoot -Recurse
$velopackReviewRoot = Join-Path $reviewRoot 'Velopack-Release'
New-Item -ItemType Directory -Force -Path $velopackReviewRoot | Out-Null
Copy-Item -Force -Path (Join-Path $releasesRoot '*') `
    -Destination $velopackReviewRoot -Recurse

$sourceReviewRoot = Join-Path $reviewRoot 'source'
New-Item -ItemType Directory -Force -Path $sourceReviewRoot | Out-Null
Copy-Item -Force -Path (Join-Path $projectRoot 'src') `
    -Destination $sourceReviewRoot -Recurse
Copy-Item -Force -Path (Join-Path $projectRoot 'tests') `
    -Destination $sourceReviewRoot -Recurse
foreach ($sourceFile in @(
    'app.config',
    'app.manifest',
    'build.ps1',
    'package-release.ps1')) {
    Copy-Item -Force -LiteralPath (Join-Path $projectRoot $sourceFile) `
        -Destination (Join-Path $sourceReviewRoot $sourceFile)
}
foreach ($sourceDocument in @(
    'README.md',
    'PRIVACY.md',
    'TROUBLESHOOTING.md',
    'LICENSING.md',
    'PUBLICATION_SCOPE.md',
    'CONTRIBUTING.md',
    'DEVELOPMENT.md',
    'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -Force -LiteralPath (Join-Path $projectRoot $sourceDocument) `
        -Destination (Join-Path $sourceReviewRoot $sourceDocument)
}
Copy-Item -Force -LiteralPath (Join-Path $projectRoot 'LICENSE') `
    -Destination (Join-Path $sourceReviewRoot 'LICENSE')
Copy-Item -Force -LiteralPath (Join-Path $projectRoot 'LICENSE') `
    -Destination (Join-Path $sourceReviewRoot 'LICENSE.txt')
if (Test-Path -LiteralPath $releaseNotes) {
    Copy-Item -Force -LiteralPath $releaseNotes `
        -Destination (Join-Path $sourceReviewRoot (Split-Path -Leaf $releaseNotes))
}
Copy-Item -Force -Path (Join-Path $projectRoot 'tools') `
    -Destination $sourceReviewRoot -Recurse
$brandingReviewRoot = Join-Path $sourceReviewRoot 'assets\branding'
New-Item -ItemType Directory -Force -Path $brandingReviewRoot | Out-Null
$reviewBrandingFiles = @(
    'tarkov-server-guard-tsg.ico',
    'tarkov-server-guard-tsg-icon-master.png',
    'tarkov-server-guard-tsg-icon-size-preview.png'
)
foreach ($brandingFile in $reviewBrandingFiles) {
    Copy-Item -Force -LiteralPath `
        (Join-Path $projectRoot ('assets\branding\' + $brandingFile)) `
        -Destination $brandingReviewRoot
}

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

if (Test-Path -LiteralPath $releasesStageRoot) {
    Remove-Item -LiteralPath $releasesStageRoot -Recurse -Force
}

Write-Host ("검토본 완료: " + $reviewRoot)
Write-Host ("검토본 ZIP: " + $reviewZip)
Write-Host ("Velopack 업로드 자산: " + $releasesRoot)
