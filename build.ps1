# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$OutputDirectory,
    [ValidateRange(1, 3600)][int]$CompileTimeoutSeconds = 120,
    [ValidateRange(1, 3600)][int]$TestTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src'
$testRoot = Join-Path $projectRoot 'tests'
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $projectRoot 'dist'
} elseif ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
}
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $projectRoot ('build\runs\' + $runId)
. (Join-Path $projectRoot 'tools\BuildHarness.ps1')
$context = New-BuildRunContext $runRoot
$context.Metadata['TestsRequested'] = -not $SkipTests
$context.Metadata['OutputDirectory'] = $outputRoot
$context.Metadata['WarningsPolicy'] = 'Compiler warnings are errors (/warn:4 /warnaserror+).'
$appRoot = Join-Path $runRoot 'app'
$buildRoot = Join-Path $runRoot 'tests'
$artifactRoot = Join-Path $runRoot 'artifacts'
$appOutput = Join-Path $appRoot 'TarkovServerGuard.exe'
New-Item -ItemType Directory -Force -Path $appRoot, $buildRoot, $artifactRoot | Out-Null

$commonReferences = @('System.dll', 'System.Core.dll', 'Microsoft.CSharp.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll') |
    ForEach-Object { '/reference:' + $_ }
$windowsFormsReferences = @('/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
$compilerOptions = @('/nologo', '/utf8output', '/platform:anycpu', '/optimize+', '/warn:4', '/warnaserror+')

# Explicit source lists prevent dormant/removed features from entering a build.
$appSourceNames = @(
    'AppLocalization.cs', 'AppLocalizationV084.cs', 'AppPreferencesStore.cs', 'WindowSizeStore.cs', 'ColumnWidthStore.cs', 'ApplicationSettingsForm.cs',
    'UiLocalization.cs', 'AppBranding.cs', 'DataGridViewScrollCorner.cs', 'ResizeGuideDataGridView.cs', 'Program.cs', 'MainForm.cs',
    'GitHubUpdateService.cs', 'ReleaseNotesService.cs', 'UpdatePromptForm.cs', 'PatchNotesForm.cs',
    'UsageNoticeForm.cs', 'LicenseForm.cs', 'ArenaBlockWarningForm.cs', 'FirewallRuleManager.cs',
    'BlockedServerMetadataStore.cs', 'BlockedServerBackup.cs', 'BlockedServerRestorePreviewForm.cs',
    'PartyBlockBundleStore.cs', 'PartyBlockInputForm.cs', 'PartyBlockBundleReleaseForm.cs', 'BlockedServersForm.cs',
    'PingKickActionCell.cs', 'RaidNoteStore.cs', 'RaidNoteForm.cs', 'MemoArchiveBackup.cs',
    'MemoArchiveRestorePreviewForm.cs', 'RaidNoteArchiveForm.cs', 'UserReportMemoStore.cs', 'UserReportMemoForm.cs',
    'DbIpLiteMmdbReader.cs', 'DbIpLiteGeoService.cs', 'ServerReportCore.cs', 'RaidQualityEvidence.cs', 'TarkovLogServices.cs'
)
$coreSources = @('DbIpLiteMmdbReader.cs', 'DbIpLiteGeoService.cs', 'ServerReportCore.cs')
$localizedSources = @('AppLocalization.cs', 'AppLocalizationV084.cs', 'UiLocalization.cs', 'AppBranding.cs')
$legacyMemoSources = @('RaidNoteStore.cs', 'RaidNoteForm.cs', 'MemoArchiveBackup.cs', 'MemoArchiveRestorePreviewForm.cs', 'RaidNoteArchiveForm.cs', 'UserReportMemoStore.cs', 'UserReportMemoForm.cs', 'ColumnWidthStore.cs', 'ResizeGuideDataGridView.cs', 'AppPreferencesStore.cs')

function New-TestSuite {
    param([string]$Name, [string[]]$Sources = @(), [switch]$Forms, [string]$Main, [string[]]$Arguments = @(), [switch]$UiHarness)
    [pscustomobject]@{ Name = $Name; Sources = $Sources; Forms = $Forms.IsPresent; Main = $Main; Arguments = $Arguments; UiHarness = $UiHarness.IsPresent }
}

$suites = @(
    New-TestSuite 'CoreTests' ($coreSources + @('FirewallRuleManager.cs', 'TarkovLogServices.cs'))
    New-TestSuite 'RaidClassificationTests' ($coreSources + @('TarkovLogServices.cs'))
    New-TestSuite 'DbIpLiteGeoTests' $coreSources
    New-TestSuite 'StorageAndBatchTests' @('FirewallRuleManager.cs', 'BlockedServerMetadataStore.cs', 'BlockedServerBackup.cs')
    New-TestSuite 'PartyBlockBundleTests' @('FirewallRuleManager.cs', 'PartyBlockBundleStore.cs') -Main 'TarkovServerReporter.Tests.PartyBlockBundleTests'
    New-TestSuite 'MemoArchiveBackupTests' ($coreSources + @('RaidNoteStore.cs', 'UserReportMemoStore.cs', 'MemoArchiveBackup.cs'))
    New-TestSuite 'UserReportMemoTests' ($localizedSources + $coreSources + $legacyMemoSources) -Forms -UiHarness
    New-TestSuite 'RaidParticipantLogTests' ($coreSources + @('FirewallRuleManager.cs', 'TarkovLogServices.cs'))
    New-TestSuite 'RaidQualityEvidenceTests' ($coreSources + @('RaidQualityEvidence.cs'))
    New-TestSuite 'GitHubUpdateTests' ($localizedSources + @('GitHubUpdateService.cs', 'ReleaseNotesService.cs', 'UpdatePromptForm.cs', 'PatchNotesForm.cs')) -Forms
    New-TestSuite 'ReleaseNotesTests' ($localizedSources + @('ReleaseNotesService.cs', 'PatchNotesForm.cs', 'UpdatePromptForm.cs')) -Forms -Arguments @($appOutput, $artifactRoot)
    New-TestSuite 'LocalizationTests' ($localizedSources + @('AppPreferencesStore.cs')) -Forms
    New-TestSuite 'BlockedServersUiTests' -Forms -Main 'TarkovServerReporter.Tests.BlockedServersUiTests' -Arguments @($appOutput)
    New-TestSuite 'V080UiTests' -Forms -UiHarness -Main 'TarkovServerReporter.Tests.V080UiTests'
    New-TestSuite 'MemoRollbackTests' -Forms -UiHarness -Main 'TarkovServerReporter.Tests.MemoRollbackTests' -Arguments @($appOutput, $artifactRoot)
    New-TestSuite 'LogScanRegressionTests' -Arguments @($appOutput)
    New-TestSuite 'MainResponsivenessTests' -Forms -UiHarness -Main 'TarkovServerReporter.Tests.MainResponsivenessTests' -Arguments @($appOutput)
    New-TestSuite 'ArchiveSearchTests' -Forms -UiHarness -Main 'TarkovServerReporter.Tests.ArchiveSearchTests' -Arguments @($appOutput, $artifactRoot)
    New-TestSuite 'EnglishUiReviewTests' -Forms -UiHarness -Main 'TarkovServerReporter.Tests.EnglishUiReviewTests' -Arguments @($appOutput, $artifactRoot)
    New-TestSuite 'WindowSizeTests' -Forms -UiHarness -Main 'TarkovServerReporter.Tests.WindowSizeTests' -Arguments @($appOutput, $artifactRoot)
    New-TestSuite 'ColumnWidthTests' -Forms -UiHarness -Main 'TarkovServerReporter.Tests.ColumnWidthTests' -Arguments @($appOutput)
)

function Get-InputManifest {
    param([string[]]$Paths)
    @($Paths | Sort-Object -Unique | ForEach-Object {
        [pscustomobject]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    })
}

function Invoke-CSharpCompiler {
    param([string]$Name, [string[]]$Arguments)
    $result = Invoke-BuildProcess -Context $context -Name $Name -FilePath $compiler -Arguments $Arguments -WorkingDirectory $projectRoot -TimeoutSeconds $CompileTimeoutSeconds -Kind Compile
    Assert-BuildProcessPassed $result
}

try {
    Save-BuildRunSummary $context
    Write-Host ('Build run: ' + $runRoot)
    $compiler = @(
        'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
        'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $compiler) { throw '.NET Framework C# compiler (csc.exe) was not found.' }
    $context.Metadata['Compiler'] = $compiler
    $context.Metadata['CompilerFileVersion'] = [Diagnostics.FileVersionInfo]::GetVersionInfo($compiler).FileVersion
    $appIcon = Join-Path $projectRoot 'assets\branding\tarkov-server-guard-tsg.ico'
    $appSources = @($appSourceNames | ForEach-Object { Join-Path $sourceRoot $_ })
    $inputPaths = @($appSources) + @(
        $PSCommandPath, (Join-Path $projectRoot 'tools\BuildHarness.ps1'), (Join-Path $projectRoot 'tools\BuildProcessJob.cs'), $appIcon,
        (Join-Path $projectRoot 'package-release.ps1'), (Join-Path $projectRoot 'tools\ReleaseVerification.ps1'),
        (Join-Path $projectRoot 'tools\Test-OfflineReleaseUpdate.ps1'), (Join-Path $projectRoot 'release-notes-v0.8.5.md'),
        (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'README.en.md'), (Join-Path $projectRoot 'PRIVACY.md'), (Join-Path $projectRoot 'DEVELOPMENT.md'),
        (Join-Path $projectRoot 'app.manifest'), (Join-Path $projectRoot 'app.config'),
        (Join-Path $projectRoot 'LICENSE'), (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md')
    )
    if (-not $SkipTests) {
        $inputPaths += Join-Path $testRoot 'BuildHarnessTests.ps1'
        $inputPaths += Join-Path $testRoot 'ReleaseVerificationTests.ps1'
        $inputPaths += Join-Path $testRoot 'StaUiTestHarness.cs'
        foreach ($suite in $suites) {
            $inputPaths += Join-Path $testRoot ($suite.Name + '.cs')
            $inputPaths += @($suite.Sources | ForEach-Object { Join-Path $sourceRoot $_ })
        }
    }
    $initialManifest = Get-InputManifest $inputPaths
    $context.Metadata['Inputs'] = $initialManifest
    $programSource = Get-Content -Raw -LiteralPath (Join-Path $sourceRoot 'Program.cs')
    $versionMatch = [regex]::Match($programSource, 'AssemblyVersion\("(?<version>\d+\.\d+\.\d+\.\d+)"\)')
    if (-not $versionMatch.Success) { throw 'Program.cs does not contain a four-part AssemblyVersion.' }
    $appVersion = $versionMatch.Groups['version'].Value
    $context.Metadata['AppVersion'] = $appVersion
    $context.Metadata['PlannedSuites'] = if ($SkipTests) { @() } else { @('BuildHarnessTests', 'ReleaseVerificationTests') + @($suites.Name) }

    if (-not $SkipTests) {
        $harnessResult = Invoke-BuildProcess -Context $context -Name 'BuildHarnessTests' -Kind Harness -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Arguments @(
            '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $testRoot 'BuildHarnessTests.ps1'),
            '-ArtifactDirectory', (Join-Path $runRoot 'harness-tests')
        ) -WorkingDirectory $projectRoot -TimeoutSeconds $TestTimeoutSeconds
        Assert-BuildProcessPassed $harnessResult
        $releaseHarnessResult = Invoke-BuildProcess -Context $context -Name 'ReleaseVerificationTests' -Kind Harness -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Arguments @(
            '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $testRoot 'ReleaseVerificationTests.ps1'),
            '-ArtifactDirectory', (Join-Path $runRoot 'release-verification-tests')
        ) -WorkingDirectory $projectRoot -TimeoutSeconds $TestTimeoutSeconds
        Assert-BuildProcessPassed $releaseHarnessResult
    }

    Invoke-CSharpCompiler 'App.Compile' ($compilerOptions + @(
        '/target:winexe', ('/out:' + $appOutput),
        ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')), ('/win32icon:' + $appIcon),
        ('/resource:' + (Join-Path $projectRoot 'LICENSE') + ',TarkovServerReporter.LICENSE.txt'),
        ('/resource:' + (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') + ',TarkovServerReporter.THIRD_PARTY_NOTICES.md')
    ) + $windowsFormsReferences + $commonReferences + $appSources)
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination ($appOutput + '.config') -Force

    if (-not $SkipTests) {
        foreach ($suite in $suites) {
            $testOutput = Join-Path $buildRoot ($suite.Name + '.exe')
            $arguments = $compilerOptions + @('/target:exe', '/debug:pdbonly', ('/out:' + $testOutput)) + $commonReferences
            if ($suite.Forms) { $arguments += $windowsFormsReferences }
            if ($suite.Main) { $arguments += '/main:' + $suite.Main }
            $arguments += @($suite.Sources | ForEach-Object { Join-Path $sourceRoot $_ })
            $arguments += Join-Path $testRoot ($suite.Name + '.cs')
            if ($suite.UiHarness) { $arguments += Join-Path $testRoot 'StaUiTestHarness.cs' }
            Invoke-CSharpCompiler ($suite.Name + '.Compile') $arguments
            $testArguments = @($suite.Arguments)
            if ($suite.Name -eq 'V080UiTests') { $testArguments = @($appOutput, $appVersion) }
            $result = Invoke-BuildProcess -Context $context -Name $suite.Name -Kind Test -FilePath $testOutput -Arguments $testArguments -WorkingDirectory $projectRoot -TimeoutSeconds $TestTimeoutSeconds
            Assert-BuildProcessPassed $result
        }
    }

    $finalManifest = Get-InputManifest $inputPaths
    $initialFingerprint = @($initialManifest | ForEach-Object { $_.Path + ':' + $_.SHA256 }) -join "`n"
    $finalFingerprint = @($finalManifest | ForEach-Object { $_.Path + ':' + $_.SHA256 }) -join "`n"
    if ($initialFingerprint -cne $finalFingerprint) {
        throw 'Build inputs changed during this run. Run the build again after edits finish.'
    }
    $context.Metadata['InputsUnchangedDuringBuild'] = $true
    $context.Metadata['AppSHA256'] = (Get-FileHash -LiteralPath $appOutput -Algorithm SHA256).Hash
    $context.Metadata['CompletedSuites'] = @($context.Steps | Where-Object { $_.Kind -ne 'Compile' -and $_.Status -eq 'Passed' } | ForEach-Object { $_.Name })
    $context.Metadata['CompileWarningCount'] = ($context.Steps | Measure-Object -Property WarningCount -Sum).Sum

    # Dist receives only this run's successfully built app. Test executables,
    # captures and diagnostic logs remain in the isolated run directory.
    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    Copy-Item -LiteralPath $appOutput -Destination (Join-Path $outputRoot 'TarkovServerGuard.exe') -Force
    Copy-Item -LiteralPath ($appOutput + '.config') -Destination (Join-Path $outputRoot 'TarkovServerGuard.exe.config') -Force
    $context.Status = if ($SkipTests) { 'Unverified' } else { 'Passed' }
    $context.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    Save-BuildRunSummary $context
    Copy-Item -LiteralPath (Join-Path $runRoot 'summary.json') -Destination (Join-Path $outputRoot 'build-verification.json') -Force
    if ($SkipTests) {
        Write-Warning 'Build completed WITHOUT tests. This output is unverified and is not a review/release candidate.'
    } else {
        Write-Host ('Verified: ' + $context.Metadata['CompletedSuites'].Count + ' suites; version ' + $appVersion + '; SHA-256 ' + $context.Metadata['AppSHA256'])
    }
    Write-Host ('Output: ' + (Join-Path $outputRoot 'TarkovServerGuard.exe'))
} catch {
    $context.Status = 'Failed'
    $context.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    $context.Metadata['Failure'] = $_.Exception.Message
    Save-BuildRunSummary $context
    Write-Host ('Build failed: ' + $_.Exception.Message)
    Write-Host 'No successful verification is claimed for this run. Existing output may belong to a previous run.'
    exit 1
} finally {
    Save-BuildRunSummary $context
    Copy-Item -LiteralPath (Join-Path $runRoot 'summary.json') -Destination (Join-Path $projectRoot 'build\latest-run.json') -Force
    Write-Host ('Summary: ' + (Join-Path $runRoot 'summary.json'))
}
exit 0
