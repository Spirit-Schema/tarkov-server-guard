# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [switch]$SkipTests,
    [string]$OutputDirectory
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

function Invoke-CSharpCompiler([string[]]$Arguments) {
    & $compiler $Arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Invoke-TestExecutable([string]$Path, [string[]]$Arguments = @()) {
    & $Path $Arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null

$commonReferences = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:Microsoft.CSharp.dll',
    '/reference:System.Net.Http.dll',
    '/reference:System.Web.Extensions.dll'
)
$windowsFormsReferences = @(
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
)

$appOutput = Join-Path $distRoot 'TarkovServerGuard.exe'
$appArguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    ('/out:' + $appOutput),
    ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')),
    ('/win32icon:' + $appIcon),
    ('/resource:' + (Join-Path $projectRoot 'LICENSE') + ',TarkovServerReporter.LICENSE.txt'),
    ('/resource:' + (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') + ',TarkovServerReporter.THIRD_PARTY_NOTICES.md')
) + $windowsFormsReferences + $commonReferences + @(
    (Join-Path $sourceRoot 'AppBranding.cs'),
    (Join-Path $sourceRoot 'DataGridViewScrollCorner.cs'),
    (Join-Path $sourceRoot 'Program.cs'),
    (Join-Path $sourceRoot 'MainForm.cs'),
    (Join-Path $sourceRoot 'GitHubUpdateService.cs'),
    (Join-Path $sourceRoot 'ReleaseNotesService.cs'),
    (Join-Path $sourceRoot 'UpdatePromptForm.cs'),
    (Join-Path $sourceRoot 'PatchNotesForm.cs'),
    (Join-Path $sourceRoot 'UsageNoticeForm.cs'),
    (Join-Path $sourceRoot 'LicenseForm.cs'),
    (Join-Path $sourceRoot 'ArenaBlockWarningForm.cs'),
    (Join-Path $sourceRoot 'FirewallRuleManager.cs'),
    (Join-Path $sourceRoot 'BlockedServerMetadataStore.cs'),
    (Join-Path $sourceRoot 'BlockedServerBackup.cs'),
    (Join-Path $sourceRoot 'BlockedServerRestorePreviewForm.cs'),
    (Join-Path $sourceRoot 'BlockedServersForm.cs'),
    (Join-Path $sourceRoot 'PingKickActionCell.cs'),
    (Join-Path $sourceRoot 'RaidNoteStore.cs'),
    (Join-Path $sourceRoot 'RaidNoteForm.cs'),
    (Join-Path $sourceRoot 'MemoArchiveBackup.cs'),
    (Join-Path $sourceRoot 'MemoArchiveRestorePreviewForm.cs'),
    (Join-Path $sourceRoot 'RaidNoteArchiveForm.cs'),
    (Join-Path $sourceRoot 'UserReportMemoStore.cs'),
    (Join-Path $sourceRoot 'UserReportMemoForm.cs'),
    (Join-Path $sourceRoot 'DbIpLiteMmdbReader.cs'),
    (Join-Path $sourceRoot 'DbIpLiteGeoService.cs'),
    (Join-Path $sourceRoot 'ServerReportCore.cs'),
    (Join-Path $sourceRoot 'TarkovLogServices.cs')
)

Invoke-CSharpCompiler $appArguments
Copy-Item -Force (Join-Path $projectRoot 'app.config') ($appOutput + '.config')

if (-not $SkipTests) {
    $coreTestOutput = Join-Path $buildRoot 'CoreTests.exe'
    Invoke-CSharpCompiler (@(
        '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/warn:4',
        ('/out:' + $coreTestOutput)
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'DbIpLiteMmdbReader.cs'),
        (Join-Path $sourceRoot 'DbIpLiteGeoService.cs'),
        (Join-Path $sourceRoot 'ServerReportCore.cs'),
        (Join-Path $sourceRoot 'FirewallRuleManager.cs'),
        (Join-Path $sourceRoot 'TarkovLogServices.cs'),
        (Join-Path $testRoot 'CoreTests.cs')
    ))
    Invoke-TestExecutable $coreTestOutput

    $dbIpTestOutput = Join-Path $buildRoot 'DbIpLiteGeoTests.exe'
    Invoke-CSharpCompiler (@(
        '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/warn:4',
        ('/out:' + $dbIpTestOutput)
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'DbIpLiteMmdbReader.cs'),
        (Join-Path $sourceRoot 'DbIpLiteGeoService.cs'),
        (Join-Path $sourceRoot 'ServerReportCore.cs'),
        (Join-Path $testRoot 'DbIpLiteGeoTests.cs')
    ))
    Invoke-TestExecutable $dbIpTestOutput

    $storageTestOutput = Join-Path $buildRoot 'StorageAndBatchTests.exe'
    Invoke-CSharpCompiler (@(
        '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/warn:4',
        ('/out:' + $storageTestOutput)
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'FirewallRuleManager.cs'),
        (Join-Path $sourceRoot 'BlockedServerMetadataStore.cs'),
        (Join-Path $sourceRoot 'BlockedServerBackup.cs'),
        (Join-Path $testRoot 'StorageAndBatchTests.cs')
    ))
    Invoke-TestExecutable $storageTestOutput

    $memoArchiveBackupTestOutput = Join-Path $buildRoot 'MemoArchiveBackupTests.exe'
    Invoke-CSharpCompiler (@(
        '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/warn:4',
        ('/out:' + $memoArchiveBackupTestOutput)
    ) + $commonReferences + @(
        (Join-Path $sourceRoot 'DbIpLiteMmdbReader.cs'),
        (Join-Path $sourceRoot 'DbIpLiteGeoService.cs'),
        (Join-Path $sourceRoot 'ServerReportCore.cs'),
        (Join-Path $sourceRoot 'RaidNoteStore.cs'),
        (Join-Path $sourceRoot 'UserReportMemoStore.cs'),
        (Join-Path $sourceRoot 'MemoArchiveBackup.cs'),
        (Join-Path $testRoot 'MemoArchiveBackupTests.cs')
    ))
    Invoke-TestExecutable $memoArchiveBackupTestOutput

    $memoTestOutput = Join-Path $buildRoot 'UserReportMemoTests.exe'
    Invoke-CSharpCompiler (@(
        '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/warn:4',
        ('/out:' + $memoTestOutput)
    ) + $windowsFormsReferences + $commonReferences + @(
        (Join-Path $sourceRoot 'AppBranding.cs'),
        (Join-Path $sourceRoot 'DbIpLiteMmdbReader.cs'),
        (Join-Path $sourceRoot 'DbIpLiteGeoService.cs'),
        (Join-Path $sourceRoot 'ServerReportCore.cs'),
        (Join-Path $sourceRoot 'RaidNoteStore.cs'),
        (Join-Path $sourceRoot 'RaidNoteForm.cs'),
        (Join-Path $sourceRoot 'MemoArchiveBackup.cs'),
        (Join-Path $sourceRoot 'MemoArchiveRestorePreviewForm.cs'),
        (Join-Path $sourceRoot 'RaidNoteArchiveForm.cs'),
        (Join-Path $sourceRoot 'UserReportMemoStore.cs'),
        (Join-Path $sourceRoot 'UserReportMemoForm.cs'),
        (Join-Path $testRoot 'UserReportMemoTests.cs')
    ))
    Invoke-TestExecutable $memoTestOutput

    $githubUpdateTestOutput = Join-Path $buildRoot 'GitHubUpdateTests.exe'
    Invoke-CSharpCompiler (@(
        '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/warn:4',
        ('/out:' + $githubUpdateTestOutput)
    ) + $windowsFormsReferences + $commonReferences + @(
        (Join-Path $sourceRoot 'AppBranding.cs'),
        (Join-Path $sourceRoot 'GitHubUpdateService.cs'),
        (Join-Path $sourceRoot 'ReleaseNotesService.cs'),
        (Join-Path $sourceRoot 'UpdatePromptForm.cs'),
        (Join-Path $sourceRoot 'PatchNotesForm.cs'),
        (Join-Path $testRoot 'GitHubUpdateTests.cs')
    ))
    Invoke-TestExecutable $githubUpdateTestOutput

    $releaseNotesTestOutput = Join-Path $buildRoot 'ReleaseNotesTests.exe'
    Invoke-CSharpCompiler (@(
        '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/warn:4',
        ('/out:' + $releaseNotesTestOutput)
    ) + $windowsFormsReferences + $commonReferences + @(
        (Join-Path $sourceRoot 'AppBranding.cs'),
        (Join-Path $sourceRoot 'ReleaseNotesService.cs'),
        (Join-Path $sourceRoot 'PatchNotesForm.cs'),
        (Join-Path $sourceRoot 'UpdatePromptForm.cs'),
        (Join-Path $testRoot 'ReleaseNotesTests.cs')
    ))
    Invoke-TestExecutable $releaseNotesTestOutput

    $blockedServersUiTestOutput = Join-Path $buildRoot 'BlockedServersUiTests.exe'
    Invoke-CSharpCompiler (@(
        '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/warn:4',
        '/main:TarkovServerReporter.Tests.BlockedServersUiTests',
        ('/out:' + $blockedServersUiTestOutput)
    ) + $windowsFormsReferences + $commonReferences + @(
        (Join-Path $testRoot 'BlockedServersUiTests.cs')
    ))
    Invoke-TestExecutable $blockedServersUiTestOutput @($appOutput)

    $programSource = Get-Content -Raw -LiteralPath (Join-Path $sourceRoot 'Program.cs')
    $versionMatch = [regex]::Match(
        $programSource,
        'AssemblyVersion\("(?<version>\d+\.\d+\.\d+)\.\d+"\)')
    if (-not $versionMatch.Success) {
        throw 'Program.cs에서 3자리 애플리케이션 버전을 찾지 못했습니다.'
    }

    $v080UiTestOutput = Join-Path $buildRoot 'V080UiTests.exe'
    Invoke-CSharpCompiler (@(
        '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/warn:4',
        '/main:TarkovServerReporter.Tests.V080UiTests',
        ('/out:' + $v080UiTestOutput)
    ) + $windowsFormsReferences + $commonReferences + @(
        (Join-Path $testRoot 'V080UiTests.cs')
    ))
    Invoke-TestExecutable $v080UiTestOutput @(
        $appOutput,
        $versionMatch.Groups['version'].Value)
}

Write-Host ('완료: ' + $appOutput)
