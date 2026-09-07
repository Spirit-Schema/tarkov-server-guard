# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

# Exercises the real Velopack runtime and full packages in a temporary local feed.
# Uses TestVelopackLocator deliberately: never installs, launches, applies an update,
# writes registry/firewall settings, or reads the actual user profile.
param(
    [Parameter(Mandatory = $true)][string]$ReleaseDirectory,
    [Parameter(Mandatory = $true)][string]$VerifiedBuildDirectory,
    [string]$PreviousPackage,
    [string]$Version,
    [ValidateSet('0.8.3', '0.8.5')][string]$PreviousVersion = '0.8.5'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseVerification.ps1')
$verified = Get-VerifiedReleaseBuild -ProjectRoot $projectRoot -BuildDirectory $VerifiedBuildDirectory
$releasePath = [IO.Path]::GetFullPath($ReleaseDirectory)
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = ([Version]$verified.Summary.Metadata.AppVersion).ToString(3) }
if ([string]::IsNullOrWhiteSpace($PreviousPackage)) {
    $PreviousPackage = Join-Path (Split-Path -Parent (Split-Path -Parent $projectRoot)) `
        ('release-archive\v' + $PreviousVersion + '\github-assets\SpiritSchema.TarkovServerGuard-' + $PreviousVersion + '-full.nupkg')
}
$previousHashes = @{
    '0.8.3' = 'BD41A71F5524EB0941B4BB7901B5EE65F54CEFD3148A675FBA12C1096CBEF0F8'
    '0.8.5' = 'EC1D430473389E68AE727C52D7FB34A99EC07FC81D9933D37381C55AEEB9EF55'
}
$previousHash = $previousHashes[$PreviousVersion]
if ((Get-FileHash -LiteralPath $PreviousPackage -Algorithm SHA256).Hash -cne $previousHash) {
    throw 'The previous package does not match the selected archived official release.'
}
$runRoot = Join-Path $projectRoot ('build\offline-update-tests\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$checks = New-Object 'System.Collections.Generic.List[string]'
$report = [pscustomobject]@{
    Status = 'Running'; Mode = 'Offline feed and artifact verification'
    InstalledUpgrade = 'Not run: requires an isolated Windows VM or Windows Sandbox'
    BuildRunId = $verified.Summary.RunId; FromVersion = $PreviousVersion; ToVersion = $Version
    Directory = $runRoot; Checks = $checks
}
function Assert-Update([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $checks.Add($Message)
    Write-Host ('PASS: ' + $Message)
}
function Expand-SafePackage([string]$Package, [string]$Destination) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        foreach ($entry in $archive.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $entry.FullName))
            if (-not $target.StartsWith($Destination + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw ('Unsafe path inside package: ' + $entry.FullName)
            }
        }
    } finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($Package, $Destination)
}
function New-FixtureManager([string]$Name, [string]$InstalledVersion, [string]$Feed) {
    $root = Join-Path $runRoot $Name
    $packages = Join-Path $root 'packages'
    New-Item -ItemType Directory -Force -Path $packages | Out-Null
    $locator = [Velopack.Locators.TestVelopackLocator]::new(
        'SpiritSchema.TarkovServerGuard', $InstalledVersion, $packages, $null)
    return [Velopack.UpdateManager]::new($Feed, $null, $locator)
}
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $feed = Get-Content -Raw -LiteralPath (Join-Path $releasePath 'releases.win.json') | ConvertFrom-Json
    $targets = @($feed.Assets | Where-Object {
        $_.PackageId -ceq 'SpiritSchema.TarkovServerGuard' -and $_.Version -ceq $Version -and $_.Type -eq 'Full'
    })
    Assert-Update ($targets.Count -eq 1) 'The offline feed contains exactly one target full package'
    $target = $targets[0]
    Assert-Update (-not [string]::IsNullOrWhiteSpace($target.NotesMarkdown)) 'The target feed includes release notes'
    Assert-Update ([IO.Path]::GetFileName($target.FileName) -ceq $target.FileName) 'The target package name stays inside the feed directory'
    $candidatePackage = Join-Path $releasePath $target.FileName
    Assert-Update ((Get-FileHash -LiteralPath $candidatePackage -Algorithm SHA256).Hash -ceq $target.SHA256.ToUpperInvariant()) 'The target package matches the feed SHA-256'
    Assert-Update ((Get-Item -LiteralPath $candidatePackage).Length -eq $target.Size) 'The target package matches the feed size'
    $previousRoot = Join-Path $runRoot ('official-' + $PreviousVersion)
    $candidateRoot = Join-Path $runRoot 'candidate'
    Expand-SafePackage $PreviousPackage $previousRoot
    Expand-SafePackage $candidatePackage $candidateRoot
    $previousApp = Join-Path $previousRoot 'lib\app\TarkovServerGuard.exe'
    $candidateApp = Join-Path $candidateRoot 'lib\app\TarkovServerGuard.exe'
    Assert-Update ((Get-Item -LiteralPath $previousApp).VersionInfo.FileVersion -ceq ($PreviousVersion + '.0')) 'The archived application is the selected official executable'
    Assert-Update ((Get-Item -LiteralPath $candidateApp).VersionInfo.FileVersion -ceq ($Version + '.0')) 'The packaged application has the target file version'
    Assert-Update ((Get-FileHash -LiteralPath $candidateApp -Algorithm SHA256).Hash -ceq $verified.Summary.Metadata.AppSHA256) 'The packaged application is byte-identical to the verified build'
    $runtimeRoot = Join-Path $candidateRoot 'lib\app'
    [void][Reflection.Assembly]::LoadFrom((Join-Path $runtimeRoot 'Newtonsoft.Json.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $runtimeRoot 'Velopack.dll'))
    $manager = New-FixtureManager ('from-' + $PreviousVersion) $PreviousVersion $releasePath
    Assert-Update $manager.IsInstalled 'The isolated locator represents an installed application'
    $update = $manager.CheckForUpdates()
    Assert-Update ($null -ne $update -and $update.TargetFullRelease.Version.ToString() -ceq $Version) ('The real runtime offers ' + $Version + ' to the isolated ' + $PreviousVersion + ' installation state')
    $manager.DownloadUpdates($update, $null)
    $download = Join-Path $runRoot ('from-' + $PreviousVersion + '\packages\' + $target.FileName)
    Assert-Update ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ceq $target.SHA256.ToUpperInvariant()) 'The real runtime downloads and verifies the target full package'
    $currentManager = New-FixtureManager 'already-current' $Version $releasePath
    Assert-Update ($null -eq $currentManager.CheckForUpdates()) 'The target version does not offer itself again'
    $badFeed = Join-Path $runRoot 'corrupt-feed'
    New-Item -ItemType Directory -Path $badFeed | Out-Null
    Copy-Item -LiteralPath (Join-Path $releasePath 'releases.win.json') -Destination $badFeed
    [IO.File]::WriteAllBytes((Join-Path $badFeed $target.FileName), [byte[]]@(1, 2, 3, 4))
    $badManager = New-FixtureManager 'corrupt-download' $PreviousVersion $badFeed
    $badUpdate = $badManager.CheckForUpdates()
    $rejected = $false
    try { $badManager.DownloadUpdates($badUpdate, $null) } catch { $rejected = $true }
    Assert-Update $rejected 'The runtime rejects a damaged package before any apply operation'
    $report.Status = 'Passed'
} catch {
    $report.Status = 'Failed'
    $report | Add-Member -NotePropertyName Error -NotePropertyValue $_.Exception.Message
    throw
} finally {
    [IO.File]::WriteAllText((Join-Path $runRoot 'summary.json'), ($report | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    Write-Host ('Offline update verification: ' + (Join-Path $runRoot 'summary.json'))
}
