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

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null

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
    ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')),
    ('/win32icon:' + $appIcon),
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
) + $commonReferences + @(
    (Join-Path $sourceRoot 'AppBranding.cs'),
    (Join-Path $sourceRoot 'Program.cs'),
    (Join-Path $sourceRoot 'MainForm.cs'),
    (Join-Path $sourceRoot 'GitHubUpdateService.cs'),
    (Join-Path $sourceRoot 'UpdatePromptForm.cs'),
    (Join-Path $sourceRoot 'UsageNoticeForm.cs'),
    (Join-Path $sourceRoot 'ArenaBlockWarningForm.cs'),
    (Join-Path $sourceRoot 'FirewallRuleManager.cs'),
    (Join-Path $sourceRoot 'BlockedServerMetadataStore.cs'),
    (Join-Path $sourceRoot 'BlockedServersForm.cs'),
    (Join-Path $sourceRoot 'PingKickActionCell.cs'),
    (Join-Path $sourceRoot 'RaidNoteStore.cs'),
    (Join-Path $sourceRoot 'RaidNoteForm.cs'),
    (Join-Path $sourceRoot 'RaidNoteArchiveForm.cs'),
    (Join-Path $sourceRoot 'UserReportMemoStore.cs'),
    (Join-Path $sourceRoot 'UserReportMemoForm.cs'),
    (Join-Path $sourceRoot 'DbIpLiteMmdbReader.cs'),
    (Join-Path $sourceRoot 'DbIpLiteGeoService.cs'),
    (Join-Path $sourceRoot 'ServerReportCore.cs'),
    (Join-Path $sourceRoot 'TarkovLogServices.cs')
)

& $compiler $appArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -Force (Join-Path $projectRoot 'app.config') ($appOutput + '.config')

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
        (Join-Path $sourceRoot 'UpdatePromptForm.cs'),
        (Join-Path $testRoot 'GitHubUpdateTests.cs')
    )

    & $compiler $githubUpdateTestArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $githubUpdateTestOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host ('완료: ' + $appOutput)
