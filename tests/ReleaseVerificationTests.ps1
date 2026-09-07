# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param([Parameter(Mandatory = $true)][string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\ReleaseVerification.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetFullPath($ArtifactDirectory)) ([Guid]::NewGuid().ToString('N'))
$runRoot = Join-Path $fixtureRoot 'build\runs\fixture'
$outputRoot = Join-Path $fixtureRoot 'build\verified'
New-Item -ItemType Directory -Force -Path (Join-Path $runRoot 'app'), (Join-Path $runRoot 'tests'), $outputRoot | Out-Null
$encoding = New-Object Text.UTF8Encoding($false)
$source = Join-Path $fixtureRoot 'app.config'
$app = Join-Path $runRoot 'app\TarkovServerGuard.exe'
$updateTest = Join-Path $runRoot 'tests\GitHubUpdateTests.exe'
[IO.File]::WriteAllText($source, 'configuration fixture', $encoding)
[IO.File]::WriteAllText($app, 'executable fixture; never executed', $encoding)
[IO.File]::WriteAllText($updateTest, 'test fixture; never executed', $encoding)
Copy-Item -LiteralPath $app -Destination $outputRoot
Copy-Item -LiteralPath $source -Destination (Join-Path $outputRoot 'TarkovServerGuard.exe.config')
$summary = [pscustomobject]@{
    Status = 'Passed'; Directory = $runRoot
    Steps = @([pscustomobject]@{ Name = 'GitHubUpdateTests'; Kind = 'Test'; Status = 'Passed'; ExitCode = 0; FilePath = $updateTest })
    Metadata = [pscustomobject]@{
        TestsRequested = $true; InputsUnchangedDuringBuild = $true; CompileWarningCount = 0
        PlannedSuites = @('GitHubUpdateTests'); CompletedSuites = @('GitHubUpdateTests')
        Inputs = @([pscustomobject]@{ Path = $source; SHA256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash })
        AppSHA256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
    }
}
$original = $summary | ConvertTo-Json -Depth 10
$assertions = 0
function Write-FixtureRecord {
    $json = $script:summary | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText((Join-Path $runRoot 'summary.json'), $json, $encoding)
    [IO.File]::WriteAllText((Join-Path $outputRoot 'build-verification.json'), $json, $encoding)
}
function Assert-Rejected([string]$Name) {
    $rejected = $false
    try { $null = Get-VerifiedReleaseBuild -ProjectRoot $fixtureRoot -BuildDirectory $outputRoot }
    catch { $rejected = $true }
    if (-not $rejected) { throw "The release verifier accepted $Name." }
    $script:assertions++
    Write-Host ('PASS: rejects ' + $Name)
    $script:summary = $original | ConvertFrom-Json
    Write-FixtureRecord
}
Write-FixtureRecord
$valid = Get-VerifiedReleaseBuild -ProjectRoot $fixtureRoot -BuildDirectory $outputRoot
if ($valid.UpdateTestExecutable -cne $updateTest) { throw 'The successful verifier did not select this run updater test.' }
$assertions++
Write-Host 'PASS: accepts complete, matching build and selects its updater test'
$summary.Status = 'Unverified'; Write-FixtureRecord
Assert-Rejected 'unverified build'
$summary.Metadata.TestsRequested = $false; Write-FixtureRecord
Assert-Rejected 'skipped tests'
$summary.Metadata.CompileWarningCount = 1; Write-FixtureRecord
Assert-Rejected 'compiler warnings'
$summary.Metadata.PlannedSuites += 'MissingSuite'; Write-FixtureRecord
Assert-Rejected 'missing test suite'
$summary.Steps[0].ExitCode = 2; Write-FixtureRecord
Assert-Rejected 'failed step hidden behind passed metadata'
$summary.Metadata.Inputs = @(); Write-FixtureRecord
Assert-Rejected 'missing input manifest'
$summary.Metadata.Inputs[0].SHA256 = 'altered'; Write-FixtureRecord
Assert-Rejected 'changed source input'
$summary.Metadata.AppSHA256 = 'altered'; Write-FixtureRecord
Assert-Rejected 'changed executable'
$summary.Steps[0].FilePath = Join-Path $fixtureRoot 'GitHubUpdateTests.exe'; Write-FixtureRecord
Assert-Rejected 'stale updater test path'
[IO.File]::AppendAllText((Join-Path $outputRoot 'build-verification.json'), ' ', $encoding)
Assert-Rejected 'copied record differing from original run'
[IO.File]::WriteAllText((Join-Path $outputRoot 'TarkovServerGuard.exe.config'), 'changed configuration', $encoding)
Assert-Rejected 'changed configuration'
Copy-Item -LiteralPath $source -Destination (Join-Path $outputRoot 'TarkovServerGuard.exe.config') -Force
Move-Item -LiteralPath $updateTest -Destination ($updateTest + '.held')
Assert-Rejected 'missing updater test executable'
Write-Host ('All release verification tests passed: ' + $assertions + ' assertions.')
