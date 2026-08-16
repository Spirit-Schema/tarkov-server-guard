# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
$projectPath = [IO.Path]::GetFullPath($ProjectRoot)
$scanner = Join-Path $projectPath 'tools\Assert-ReleaseSanitized.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'TSG-ReleaseSanitizationTests-' + [Guid]::NewGuid().ToString('N'))

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Write-TestText([string]$Path, [string]$Value) {
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Value, (New-Object Text.UTF8Encoding($false)))
}

function Get-TextFingerprint([string]$Value) {
    $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($bytes) }
    finally { $sha.Dispose() }
    return 'sha256:' + ([BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
}

function Invoke-Scanner(
    [string]$Path,
    [string]$Label,
    [string]$Mode,
    [string]$Report,
    [string]$Allowlist,
    [hashtable]$Limits) {
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $scanner,
        '-InputPath', $Path,
        '-InputLabel', $Label,
        '-InputMode', $Mode,
        '-ProjectRoot', $projectPath,
        '-ReportPath', $Report)
    if (-not [string]::IsNullOrWhiteSpace($Allowlist)) {
        $arguments += @('-AllowlistPath', $Allowlist)
    }
    if ($null -ne $Limits) {
        foreach ($key in $Limits.Keys | Sort-Object) {
            $arguments += ('-' + $key)
            $arguments += [string]$Limits[$key]
        }
    }
    $captureId = [Guid]::NewGuid().ToString('N')
    $standardOutputPath = Join-Path $temporaryRoot ('scanner-' + $captureId + '.stdout.txt')
    $standardErrorPath = Join-Path $temporaryRoot ('scanner-' + $captureId + '.stderr.txt')
    $quotedArguments = @($arguments | ForEach-Object {
        '"' + ([string]$_).Replace('"', '\"') + '"'
    })
    try {
        $process = Start-Process `
            -FilePath 'powershell.exe' `
            -ArgumentList $quotedArguments `
            -WindowStyle Hidden `
            -Wait `
            -PassThru `
            -RedirectStandardOutput $standardOutputPath `
            -RedirectStandardError $standardErrorPath
        $exitCode = $process.ExitCode
        $output = [IO.File]::ReadAllText($standardOutputPath) +
            [IO.File]::ReadAllText($standardErrorPath)
    }
    finally {
        foreach ($capturePath in @($standardOutputPath, $standardErrorPath)) {
            if (Test-Path -LiteralPath $capturePath -PathType Leaf) {
                Remove-Item -LiteralPath $capturePath -Force -ErrorAction SilentlyContinue
            }
        }
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
        Report = $Report
    }
}

function Assert-Pass($Result, [string]$Message) {
    Assert ($Result.ExitCode -eq 0) ($Message + "`n" + $Result.Output)
    $report = Get-Content -Raw -LiteralPath $Result.Report | ConvertFrom-Json
    Assert ([bool]$report.success) 'Passing scan report did not record success.'
    Assert (@($report.findings).Count -eq 0) 'Passing scan report contains findings.'
}

function Assert-FailureWithoutDisclosure(
    $Result,
    [string]$RawValue,
    [string]$ExpectedRule,
    [string]$Message) {
    Assert ($Result.ExitCode -ne 0) $Message
    Assert (Test-Path -LiteralPath $Result.Report -PathType Leaf) `
        ($Message + ' Failure report was not written.' + "`n" + $Result.Output)
    if (-not [string]::IsNullOrEmpty($RawValue)) {
        Assert (-not $Result.Output.Contains($RawValue)) `
            ('Console output disclosed the raw finding for rule: ' + $ExpectedRule +
                "`n" + $Result.Output.Replace($RawValue, '[REDACTED]'))
    }
    $reportText = Get-Content -Raw -LiteralPath $Result.Report
    if (-not [string]::IsNullOrEmpty($RawValue)) {
        Assert (-not $reportText.Contains($RawValue)) `
            ('JSON report disclosed the raw finding for rule: ' + $ExpectedRule)
    }
    $report = $reportText | ConvertFrom-Json
    Assert (-not [bool]$report.success) 'Failure report incorrectly recorded success.'
    Assert (@($report.findings | Where-Object { $_.rule -eq $ExpectedRule }).Count -gt 0) `
        ('Expected rule was not reported: ' + $ExpectedRule)
    foreach ($finding in @($report.findings)) {
        Assert ($finding.fingerprint -match '^sha256:[0-9a-f]{64}$') `
            'A finding omitted its exact one-way fingerprint.'
    }
}

function New-Zip([string]$Path, [object[]]$Entries) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    $archive = New-Object IO.Compression.ZipArchive(
        $stream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        foreach ($item in $Entries) {
            $entry = $archive.CreateEntry(
                [string]$item.Name,
                [IO.Compression.CompressionLevel]::Optimal)
            if ($null -ne $item.PSObject.Properties['ExternalAttributes']) {
                $entry.ExternalAttributes = [int]$item.ExternalAttributes
            }
            $entryStream = $entry.Open()
            try {
                $bytes = $(if ($item.Data -is [byte[]]) {
                    $item.Data
                } else {
                    (New-Object Text.UTF8Encoding($false)).GetBytes([string]$item.Data)
                })
                $entryStream.Write($bytes, 0, $bytes.Length)
            }
            finally { $entryStream.Dispose() }
        }
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null
    Assert (Test-Path -LiteralPath $scanner -PathType Leaf) 'Release scanner is missing.'

    $normal = Join-Path $temporaryRoot 'normal'
    Write-TestText (Join-Path $normal 'README.txt') `
        'Tarkov Server Guard public release fixture without local data.'
    $normalFileHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (
        Join-Path $normal 'README.txt')).Hash
    $normalResult = Invoke-Scanner $normal 'normal' 'Directory' `
        (Join-Path $temporaryRoot 'normal-report.json') $null $null
    Assert-Pass $normalResult 'Normal release fixture was rejected.'
    Assert (((Get-FileHash -Algorithm SHA256 -LiteralPath (
        Join-Path $normal 'README.txt')).Hash) -ceq $normalFileHash) `
        'Directory input changed during a passing scan.'

    $insideReportRoot = Join-Path $temporaryRoot 'report-inside-input'
    $insideReportInput = Join-Path $insideReportRoot 'input.txt'
    Write-TestText $insideReportInput 'public input'
    $insideHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $insideReportInput).Hash
    $insideReport = Join-Path $insideReportRoot 'scan-report.json'
    $insideResult = Invoke-Scanner $insideReportRoot 'inside-report' 'Directory' `
        $insideReport $null $null
    Assert ($insideResult.ExitCode -ne 0) 'A report path inside the input directory was accepted.'
    Assert (-not (Test-Path -LiteralPath $insideReport)) `
        'The scanner wrote a report into its read-only input.'
    Assert (((Get-FileHash -Algorithm SHA256 -LiteralPath $insideReportInput).Hash) -ceq
        $insideHash) 'Input changed when an unsafe report path was rejected.'

    $invalidUtf8Root = Join-Path $temporaryRoot 'invalid-utf8'
    New-Item -ItemType Directory -Force -Path $invalidUtf8Root | Out-Null
    $invalidUtf8Path = Join-Path $invalidUtf8Root 'invalid.txt'
    [IO.File]::WriteAllBytes($invalidUtf8Path, [byte[]]@(0xC3, 0x28))
    $invalidResult = Invoke-Scanner $invalidUtf8Root 'invalid-utf8' 'Directory' `
        (Join-Path $temporaryRoot 'invalid-utf8-report.json') $null $null
    Assert-FailureWithoutDisclosure $invalidResult $null 'ScannerFailure' `
        'Invalid UTF-8 text was silently accepted.'

    $githubToken = 'gh' + 'p_' + ('A' * 36)
    $tokenRoot = Join-Path $temporaryRoot 'token'
    Write-TestText (Join-Path $tokenRoot 'token.txt') $githubToken
    $tokenResult = Invoke-Scanner $tokenRoot 'token' 'Directory' `
        (Join-Path $temporaryRoot 'token-report.json') $null $null
    Assert-FailureWithoutDisclosure $tokenResult $githubToken 'GitHubToken' `
        'GitHub token fixture passed.'

    $bannedRoot = Join-Path $temporaryRoot 'banned-name'
    Write-TestText (Join-Path $bannedRoot '.env') 'PUBLIC_FIXTURE=true'
    $bannedResult = Invoke-Scanner $bannedRoot 'banned' 'Directory' `
        (Join-Path $temporaryRoot 'banned-report.json') $null $null
    Assert-FailureWithoutDisclosure $bannedResult $null 'BannedFileName' `
        'Banned file-name fixture passed.'

    $privateSegmentRoot = Join-Path $temporaryRoot 'private-segment'
    Write-TestText (Join-Path $privateSegmentRoot 'RaidNotes\memo.txt') 'private memo'
    $privateSegmentResult = Invoke-Scanner $privateSegmentRoot 'private-segment' 'Directory' `
        (Join-Path $temporaryRoot 'private-segment-report.json') $null $null
    Assert-FailureWithoutDisclosure $privateSegmentResult $null 'BannedPathSegment' `
        'Runtime private-data directory fixture passed.'

    $windowsPath = 'C:' + '\Users\ExamplePerson\Documents\private\history.json'
    $pathRoot = Join-Path $temporaryRoot 'windows-path'
    Write-TestText (Join-Path $pathRoot 'path.txt') $windowsPath
    $pathResult = Invoke-Scanner $pathRoot 'path' 'Directory' `
        (Join-Path $temporaryRoot 'path-report.json') $null $null
    Assert-FailureWithoutDisclosure $pathResult $windowsPath 'WindowsUserPath' `
        'Windows absolute user path fixture passed.'

    $projectPathRoot = Join-Path $temporaryRoot 'known-project-path'
    Write-TestText (Join-Path $projectPathRoot 'path.txt') $projectPath
    $projectPathResult = Invoke-Scanner $projectPathRoot 'known-project-path' 'Directory' `
        (Join-Path $temporaryRoot 'known-project-path-report.json') $null $null
    Assert-FailureWithoutDisclosure $projectPathResult $projectPath 'KnownProjectPath' `
        'The exact local project path was accepted or disclosed.'

    $sid = 'S-' + '1-5-21-123456789-223456789-323456789-1001'
    $sidRoot = Join-Path $temporaryRoot 'sid'
    Write-TestText (Join-Path $sidRoot 'sid.txt') $sid
    $sidResult = Invoke-Scanner $sidRoot 'sid' 'Directory' `
        (Join-Path $temporaryRoot 'sid-report.json') $null $null
    Assert-FailureWithoutDisclosure $sidResult $sid 'WindowsSid' 'Windows SID fixture passed.'

    $accountEmail = 'personal.account' + '@gmail.com'
    $accountRoot = Join-Path $temporaryRoot 'account'
    Write-TestText (Join-Path $accountRoot 'account.txt') $accountEmail
    $accountResult = Invoke-Scanner $accountRoot 'account' 'Directory' `
        (Join-Path $temporaryRoot 'account-report.json') $null $null
    Assert-FailureWithoutDisclosure $accountResult $accountEmail 'AccountEmail' `
        'Personal account identifier fixture passed.'

    $exampleEmail = 'fixture.account' + '@example.invalid'
    $exampleAccountRoot = Join-Path $temporaryRoot 'example-account'
    Write-TestText (Join-Path $exampleAccountRoot 'account.txt') $exampleEmail
    $exampleAccountResult = Invoke-Scanner $exampleAccountRoot 'example-account' 'Directory' `
        (Join-Path $temporaryRoot 'example-account-report.json') $null $null
    Assert-FailureWithoutDisclosure $exampleAccountResult $exampleEmail 'AccountEmail' `
        'An example-domain account bypassed the exact allowlist requirement.'

    $credentialValue = 'CorrectHorseBatteryStaple42'
    $credentialText = ('pass' + 'word=') + $credentialValue
    $credentialRoot = Join-Path $temporaryRoot 'credential'
    Write-TestText (Join-Path $credentialRoot 'config.txt') $credentialText
    $credentialResult = Invoke-Scanner $credentialRoot 'credential' 'Directory' `
        (Join-Path $temporaryRoot 'credential-report.json') $null $null
    Assert-FailureWithoutDisclosure $credentialResult $credentialValue `
        'CredentialAssignment' 'Password assignment fixture passed.'

    $placeholderValue = 'placeholder-value-123'
    $placeholderText = ('pass' + 'word=') + $placeholderValue
    $placeholderRoot = Join-Path $temporaryRoot 'placeholder-credential'
    Write-TestText (Join-Path $placeholderRoot 'config.txt') $placeholderText
    $placeholderResult = Invoke-Scanner $placeholderRoot 'placeholder' 'Directory' `
        (Join-Path $temporaryRoot 'placeholder-report.json') $null $null
    Assert-FailureWithoutDisclosure $placeholderResult $placeholderValue `
        'CredentialAssignment' 'A placeholder credential bypassed the exact allowlist requirement.'

    $gameSid = '0123456789abcdef01234567'
    $gameIdentityRoot = Join-Path $temporaryRoot 'game-identity'
    Write-TestText (Join-Path $gameIdentityRoot 'session.txt') ('S' + 'ID: ' + $gameSid)
    $gameIdentityResult = Invoke-Scanner $gameIdentityRoot 'game-identity' 'Directory' `
        (Join-Path $temporaryRoot 'game-identity-report.json') $null $null
    Assert-FailureWithoutDisclosure $gameIdentityResult $gameSid 'GameAccountIdentifier' `
        'A game SID assignment was accepted.'
    $gameAllowlistPath = Join-Path $temporaryRoot 'game-identity-allowlist.json'
    $gameAllowlist = [ordered]@{
        schemaVersion = 1
        entries = @([ordered]@{
            file = 'game-identity/session.txt'
            rule = 'GameAccountIdentifier'
            fingerprint = Get-TextFingerprint $gameSid
            reason = 'Synthetic game SID used only by this exact isolated scanner fixture.'
        })
    }
    [IO.File]::WriteAllText(
        $gameAllowlistPath,
        ($gameAllowlist | ConvertTo-Json -Depth 6),
        (New-Object Text.UTF8Encoding($false)))
    $gameAllowlistedResult = Invoke-Scanner $gameIdentityRoot 'game-identity' 'Directory' `
        (Join-Path $temporaryRoot 'game-identity-allowlisted-report.json') `
        $gameAllowlistPath $null
    Assert-Pass $gameAllowlistedResult 'Exact synthetic game SID allowlist did not pass.'

    $privateMarker = '-----BE' + 'GIN PRIVATE KEY-----'
    $privateRoot = Join-Path $temporaryRoot 'private-key'
    Write-TestText (Join-Path $privateRoot 'private.txt') $privateMarker
    $privateResult = Invoke-Scanner $privateRoot 'private' 'Directory' `
        (Join-Path $temporaryRoot 'private-report.json') $null $null
    Assert-FailureWithoutDisclosure $privateResult $privateMarker 'PrivateKeyMarker' `
        'Private-key fixture passed.'

    $jwt = ('ey' + 'J' + ('a' * 12)) + '.' + ('b' * 12) + '.' + ('c' * 12)
    $jwtRoot = Join-Path $temporaryRoot 'jwt'
    Write-TestText (Join-Path $jwtRoot 'jwt.txt') $jwt
    $jwtResult = Invoke-Scanner $jwtRoot 'jwt' 'Directory' `
        (Join-Path $temporaryRoot 'jwt-report.json') $null $null
    Assert-FailureWithoutDisclosure $jwtResult $jwt 'JwtToken' 'JWT fixture passed.'

    $allowRoot = Join-Path $temporaryRoot 'allow'
    Write-TestText (Join-Path $allowRoot 'allowed.txt') $githubToken
    $allowlistPath = Join-Path $temporaryRoot 'allowlist.json'
    $allowlist = [ordered]@{
        schemaVersion = 1
        entries = @([ordered]@{
            file = 'allow/allowed.txt'
            rule = 'GitHubToken'
            fingerprint = Get-TextFingerprint $githubToken
            reason = 'Synthetic scanner fixture with an exact one-way fingerprint.'
        })
    }
    [IO.File]::WriteAllText(
        $allowlistPath,
        ($allowlist | ConvertTo-Json -Depth 6),
        (New-Object Text.UTF8Encoding($false)))
    $allowResult = Invoke-Scanner $allowRoot 'allow' 'Directory' `
        (Join-Path $temporaryRoot 'allow-report.json') $allowlistPath $null
    Assert-Pass $allowResult 'Exact synthetic-fixture allowlist did not pass.'
    $allowlist.entries[0].fingerprint = 'sha256:' + ('0' * 64)
    [IO.File]::WriteAllText(
        $allowlistPath,
        ($allowlist | ConvertTo-Json -Depth 6),
        (New-Object Text.UTF8Encoding($false)))
    $wrongAllowResult = Invoke-Scanner $allowRoot 'allow' 'Directory' `
        (Join-Path $temporaryRoot 'wrong-allow-report.json') $allowlistPath $null
    Assert-FailureWithoutDisclosure $wrongAllowResult $githubToken 'GitHubToken' `
        'A wrong allowlist fingerprint suppressed a finding.'

    $setupPath = Join-Path $temporaryRoot 'Fixture-Setup.exe'
    $utf16ApiToken = 's' + 'k-' + ('Z' * 28)
    $setupBytes = New-Object Collections.Generic.List[byte]
    $setupBytes.AddRange((New-Object Text.ASCIIEncoding).GetBytes('MZ PUBLIC '))
    $setupBytes.AddRange((New-Object Text.ASCIIEncoding).GetBytes($githubToken))
    $setupBytes.Add(0)
    $setupBytes.AddRange([Text.Encoding]::Unicode.GetBytes($utf16ApiToken))
    [IO.File]::WriteAllBytes($setupPath, $setupBytes.ToArray())
    $setupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $setupPath).Hash
    $setupResult = Invoke-Scanner $setupPath 'setup' 'Setup' `
        (Join-Path $temporaryRoot 'setup-report.json') $null $null
    Assert-FailureWithoutDisclosure $setupResult $githubToken 'GitHubToken' `
        'Setup ASCII-string credential fixture passed.'
    Assert (-not $setupResult.Output.Contains($utf16ApiToken)) `
        'Setup UTF-16 credential was disclosed in output.'
    Assert (((Get-FileHash -Algorithm SHA256 -LiteralPath $setupPath).Hash) -ceq $setupHash) `
        'Setup input changed during a failing scan.'

    $unicodeSetupPath = Join-Path $temporaryRoot 'Unicode-Setup.exe'
    $unicodeSetupBytes = New-Object Collections.Generic.List[byte]
    $unicodeSetupBytes.AddRange((New-Object Text.ASCIIEncoding).GetBytes('MZ PUBLIC '))
    $unicodeSetupBytes.AddRange([Text.Encoding]::Unicode.GetBytes(('검수용한글문자열' * 20000)))
    [IO.File]::WriteAllBytes($unicodeSetupPath, $unicodeSetupBytes.ToArray())
    $unicodeSetupResult = Invoke-Scanner $unicodeSetupPath 'unicode-setup' 'Setup' `
        (Join-Path $temporaryRoot 'unicode-setup-report.json') $null $null
    Assert-Pass $unicodeSetupResult `
        'Arbitrary non-ASCII UTF-16 executable bytes caused a false positive.'

    $zipSlip = Join-Path $temporaryRoot 'zip-slip.zip'
    New-Zip $zipSlip @([pscustomobject]@{ Name = '../escape.txt'; Data = 'escape' })
    $zipSlipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipSlip).Hash
    $zipSlipResult = Invoke-Scanner $zipSlip 'zip-slip' 'Archive' `
        (Join-Path $temporaryRoot 'zip-slip-report.json') $null $null
    Assert-FailureWithoutDisclosure $zipSlipResult $null 'ArchiveUnsafePath' `
        'Zip-slip fixture passed.'
    Assert (((Get-FileHash -Algorithm SHA256 -LiteralPath $zipSlip).Hash) -ceq $zipSlipHash) `
        'Archive input changed during path rejection.'

    $unsafeWindowsZip = Join-Path $temporaryRoot 'unsafe-windows-paths.zip'
    New-Zip $unsafeWindowsZip @(
        [pscustomobject]@{ Name = 'folder/file.txt:secret'; Data = 'ads' },
        [pscustomobject]@{ Name = 'folder/CON.txt'; Data = 'device' },
        [pscustomobject]@{ Name = 'folder/trailing.'; Data = 'dot' },
        [pscustomobject]@{ Name = 'folder/trailing '; Data = 'space' },
        [pscustomobject]@{ Name = '/rooted.txt'; Data = 'rooted' },
        [pscustomobject]@{ Name = 'C:/drive.txt'; Data = 'drive' })
    $unsafeWindowsResult = Invoke-Scanner $unsafeWindowsZip 'unsafe-windows' 'Archive' `
        (Join-Path $temporaryRoot 'unsafe-windows-report.json') $null $null
    Assert-FailureWithoutDisclosure $unsafeWindowsResult $null 'ArchiveUnsafePath' `
        'A Windows ADS, device, ambiguous, rooted, or drive-qualified path was accepted.'

    $symlinkZip = Join-Path $temporaryRoot 'symlink.zip'
    $symlinkAttributes = [BitConverter]::ToInt32(
        [BitConverter]::GetBytes([Convert]::ToUInt32('A1FF0000', 16)), 0)
    New-Zip $symlinkZip @([pscustomobject]@{
        Name = 'link-entry'
        Data = 'target'
        ExternalAttributes = $symlinkAttributes
    })
    $symlinkResult = Invoke-Scanner $symlinkZip 'symlink' 'Archive' `
        (Join-Path $temporaryRoot 'symlink-report.json') $null $null
    Assert-FailureWithoutDisclosure $symlinkResult $null 'ArchiveLinkEntry' `
        'Archive symlink fixture passed.'

    $bombZip = Join-Path $temporaryRoot 'ratio-bomb.zip'
    New-Zip $bombZip @([pscustomobject]@{ Name = 'huge.txt'; Data = ('A' * 1000000) })
    $bombResult = Invoke-Scanner $bombZip 'bomb' 'Archive' `
        (Join-Path $temporaryRoot 'bomb-report.json') $null `
        @{ MaximumCompressionRatio = 20 }
    Assert-FailureWithoutDisclosure $bombResult $null 'ArchiveCompressionRatioLimit' `
        'Compression-ratio bomb fixture passed.'

    $boundedBytes = [byte[]](0..99)
    $entrySizeZip = Join-Path $temporaryRoot 'entry-size.zip'
    New-Zip $entrySizeZip @([pscustomobject]@{ Name = 'bounded.bin'; Data = $boundedBytes })
    $entrySizeResult = Invoke-Scanner $entrySizeZip 'entry-size' 'Archive' `
        (Join-Path $temporaryRoot 'entry-size-report.json') $null `
        @{ MaximumArchiveEntryBytes = 50 }
    Assert-FailureWithoutDisclosure $entrySizeResult $null 'ArchiveEntrySizeLimit' `
        'Per-entry expanded-size fixture passed.'

    $totalSizeZip = Join-Path $temporaryRoot 'total-size.zip'
    New-Zip $totalSizeZip @(
        [pscustomobject]@{ Name = 'first.bin'; Data = [byte[]](0..39) },
        [pscustomobject]@{ Name = 'second.bin'; Data = [byte[]](40..79) })
    $totalSizeResult = Invoke-Scanner $totalSizeZip 'total-size' 'Archive' `
        (Join-Path $temporaryRoot 'total-size-report.json') $null `
        @{ MaximumArchiveExpandedBytes = 60 }
    Assert-FailureWithoutDisclosure $totalSizeResult $null 'ArchiveExpandedSizeLimit' `
        'Total expanded-size fixture passed.'

    $manyZip = Join-Path $temporaryRoot 'too-many.zip'
    New-Zip $manyZip @(
        [pscustomobject]@{ Name = 'file1.txt'; Data = 'ok' },
        [pscustomobject]@{ Name = 'file2.txt'; Data = 'ok' },
        [pscustomobject]@{ Name = 'file3.txt'; Data = 'ok' },
        [pscustomobject]@{ Name = ($githubToken + '.txt'); Data = 'ok' })
    $manyResult = Invoke-Scanner $manyZip 'many' 'Archive' `
        (Join-Path $temporaryRoot 'many-report.json') $null `
        @{ MaximumArchiveEntries = 3 }
    Assert-FailureWithoutDisclosure $manyResult $githubToken 'ArchiveEntryCountLimit' `
        'Too-many-entries fixture passed.'

    $deepZip = Join-Path $temporaryRoot 'too-deep.zip'
    New-Zip $deepZip @([pscustomobject]@{ Name = 'one/two/three/file.txt'; Data = 'ok' })
    $deepResult = Invoke-Scanner $deepZip 'deep' 'Archive' `
        (Join-Path $temporaryRoot 'deep-report.json') $null `
        @{ MaximumArchivePathDepth = 3 }
    Assert-FailureWithoutDisclosure $deepResult $null 'ArchivePathDepthLimit' `
        'Too-deep archive path fixture passed.'

    $secretPathZip = Join-Path $temporaryRoot 'secret-path.zip'
    New-Zip $secretPathZip @([pscustomobject]@{
        Name = ('folder/' + $githubToken + '.txt')
        Data = 'public content'
    })
    $secretPathResult = Invoke-Scanner $secretPathZip 'secret-path' 'Archive' `
        (Join-Path $temporaryRoot 'secret-path-report.json') $null $null
    Assert-FailureWithoutDisclosure $secretPathResult $githubToken 'GitHubToken' `
        'A credential embedded in an archive entry path was accepted or disclosed.'

    $innerZip = Join-Path $temporaryRoot 'inner.zip'
    New-Zip $innerZip @([pscustomobject]@{ Name = 'nested.txt'; Data = $githubToken })
    $outerZip = Join-Path $temporaryRoot 'outer.zip'
    New-Zip $outerZip @([pscustomobject]@{
        Name = 'payload/inner.zip'
        Data = [IO.File]::ReadAllBytes($innerZip)
    })
    $nestedResult = Invoke-Scanner $outerZip 'nested' 'Archive' `
        (Join-Path $temporaryRoot 'nested-report.json') $null $null
    Assert-FailureWithoutDisclosure $nestedResult $githubToken 'GitHubToken' `
        'Nested archive content was not rescanned.'

    $branchA = Join-Path $temporaryRoot 'branch-a.zip'
    $branchB = Join-Path $temporaryRoot 'branch-b.zip'
    New-Zip $branchA @(
        [pscustomobject]@{ Name = 'a1.txt'; Data = 'ok' },
        [pscustomobject]@{ Name = 'a2.txt'; Data = 'ok' })
    New-Zip $branchB @(
        [pscustomobject]@{ Name = 'b1.txt'; Data = 'ok' },
        [pscustomobject]@{ Name = 'b2.txt'; Data = 'ok' })
    $branchingZip = Join-Path $temporaryRoot 'branching.zip'
    New-Zip $branchingZip @(
        [pscustomobject]@{ Name = 'a.zip'; Data = [IO.File]::ReadAllBytes($branchA) },
        [pscustomobject]@{ Name = 'b.zip'; Data = [IO.File]::ReadAllBytes($branchB) })
    $archiveCountResult = Invoke-Scanner $branchingZip 'archive-count' 'Archive' `
        (Join-Path $temporaryRoot 'archive-count-report.json') $null `
        @{ MaximumArchives = 2 }
    Assert-FailureWithoutDisclosure $archiveCountResult $null 'GlobalArchiveCountLimit' `
        'Global nested archive-count fixture passed.'
    $globalEntryResult = Invoke-Scanner $branchingZip 'global-entries' 'Archive' `
        (Join-Path $temporaryRoot 'global-entry-report.json') $null `
        @{ MaximumTotalArchiveEntries = 5 }
    Assert-FailureWithoutDisclosure $globalEntryResult $null `
        'GlobalArchiveEntryCountLimit' 'Global nested entry-count fixture passed.'

    $repositoryAllowlistPath = Join-Path $projectPath 'release-scan-allowlist.json'
    $packageShapeZip = Join-Path $temporaryRoot 'package-shaped-review.zip'
    New-Zip $packageShapeZip @([pscustomobject]@{
        Name = 'source/tests/CoreTests.cs'
        Data = [IO.File]::ReadAllText((Join-Path $projectPath 'tests\CoreTests.cs'))
    })
    $packageShapeResult = Invoke-Scanner $packageShapeZip 'public-review' 'Archive' `
        (Join-Path $temporaryRoot 'package-shaped-review-report.json') `
        $repositoryAllowlistPath $null
    Assert-Pass $packageShapeResult `
        'Package-shaped review ZIP did not honor exact canonical source allowlist paths.'
    $wrongShapeZip = Join-Path $temporaryRoot 'wrong-shaped-review.zip'
    New-Zip $wrongShapeZip @([pscustomobject]@{
        Name = 'other/tests/CoreTests.cs'
        Data = [IO.File]::ReadAllText((Join-Path $projectPath 'tests\CoreTests.cs'))
    })
    $wrongShapeResult = Invoke-Scanner $wrongShapeZip 'public-review' 'Archive' `
        (Join-Path $temporaryRoot 'wrong-shaped-review-report.json') `
        $repositoryAllowlistPath $null
    Assert-FailureWithoutDisclosure $wrongShapeResult $null 'GameAccountIdentifier' `
        'Canonical allowlist matching escaped the exact source subtree.'

    $returnStatusReport = Join-Path $temporaryRoot 'return-status-report.json'
    & $scanner -InputPath $tokenRoot -InputLabel 'return-status-failure' `
        -InputMode 'Directory' -ProjectRoot $projectPath `
        -ReportPath $returnStatusReport -ReturnStatus
    Assert ($LASTEXITCODE -ne 0) `
        'In-process package scanner failure did not return a nonzero status.'
    & $scanner -InputPath $normal -InputLabel 'return-status-pass' `
        -InputMode 'Directory' -ProjectRoot $projectPath `
        -ReportPath $returnStatusReport -ReturnStatus
    Assert ($LASTEXITCODE -eq 0) `
        'In-process package scanner pass did not clear its status.'
    $overwrittenReport = Get-Content -Raw -LiteralPath $returnStatusReport | ConvertFrom-Json
    Assert ([bool]$overwrittenReport.success -and @($overwrittenReport.findings).Count -eq 0) `
        'Atomic report replacement did not publish the final successful report.'

    $development = Get-Content -Raw -LiteralPath (Join-Path $projectPath 'DEVELOPMENT.md')
    Assert ($development.Contains('민감정보 자동 검사')) `
        'Scanner limits and defense-in-depth documentation is missing.'
    $repositoryAllowlist = Get-Content -Raw -LiteralPath $repositoryAllowlistPath |
        ConvertFrom-Json
    Assert ([int]$repositoryAllowlist.schemaVersion -eq 1 -and
        @($repositoryAllowlist.entries).Count -gt 0) `
        'Repository release allowlist is missing or empty.'
    foreach ($entry in @($repositoryAllowlist.entries)) {
        Assert ($entry.file -notmatch '[*?]' -and $entry.rule -notmatch '[*?]' -and
            $entry.fingerprint -match '^sha256:[0-9a-f]{64}$' -and
            -not [string]::IsNullOrWhiteSpace([string]$entry.reason)) `
            'Repository allowlist contains a broad or incomplete entry.'
    }
    Assert (@($repositoryAllowlist.entries | Where-Object {
        $_.rule -eq 'GameAccountIdentifier'
    }).Count -gt 0) 'Synthetic repository game SIDs lack exact allowlist entries.'

    $packageScript = Get-Content -Raw -LiteralPath (Join-Path $projectPath 'package-release.ps1')
    $prepackIndex = $packageScript.IndexOf('release-sanitization-prepack-v')
    $packIndex = $packageScript.IndexOf('& dotnet @vpkArguments')
    $postpackIndex = $packageScript.IndexOf('release-sanitization-postpack-v')
    $reviewIndex = $packageScript.IndexOf('release-sanitization-review-v')
    $provenanceIndex = $packageScript.IndexOf("tools\New-ReleaseProvenance.ps1")
    $publicAssetsIndex = $packageScript.IndexOf('release-sanitization-public-assets-v')
    Assert ($prepackIndex -ge 0 -and $prepackIndex -lt $packIndex -and
        $postpackIndex -gt $packIndex -and $reviewIndex -gt $postpackIndex -and
        $publicAssetsIndex -gt $provenanceIndex) `
        'Release sanitization gates are missing or ordered around the wrong packaging boundary.'
    Assert ($packageScript -match '(?s)\$reviewDocumentFiles\s*=\s*@\(.*?''ROADMAP\.md''.*?\)') `
        'ROADMAP.md is missing from the shared public-document list.'
    Assert ($packageScript.Contains('foreach ($document in $reviewDocumentFiles)') -and
        $packageScript.Contains('foreach ($sourceDocument in $reviewDocumentFiles)')) `
        'The shared document list is not copied into both publish and source review snapshots.'
    Write-Host 'All release sanitization tests passed.'
    $global:LASTEXITCODE = 0
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolved = [IO.Path]::GetFullPath($temporaryRoot)
        $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith(
            $temporaryPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolved) -notmatch
                '^TSG-ReleaseSanitizationTests-[0-9a-f]{32}$') {
            throw 'Unexpected release sanitization fixture cleanup path.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
