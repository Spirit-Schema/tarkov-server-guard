# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

# Validate a completed build before reusing it. No application process is started.
function Get-VerifiedReleaseBuild {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$BuildDirectory
    )
    $projectPath = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
    $buildPath = [IO.Path]::GetFullPath($BuildDirectory).TrimEnd('\')
    $verificationPath = Join-Path $buildPath 'build-verification.json'
    $summary = Get-Content -Raw -LiteralPath $verificationPath | ConvertFrom-Json
    if ($summary.Status -ne 'Passed' -or $summary.Metadata.TestsRequested -ne $true -or
        $summary.Metadata.InputsUnchangedDuringBuild -ne $true -or
        $summary.Metadata.CompileWarningCount -ne 0) {
        throw 'Release input must be a fully passed build with unchanged inputs and no compiler warnings.'
    }
    $planned = @($summary.Metadata.PlannedSuites | Sort-Object -Unique)
    $completed = @($summary.Metadata.CompletedSuites | Sort-Object -Unique)
    if ($planned.Count -eq 0 -or ($planned -join '|') -cne ($completed -join '|') -or
        @($summary.Steps | Where-Object { $_.Status -ne 'Passed' }).Count -ne 0) {
        throw 'The build did not complete every planned verification suite.'
    }
    foreach ($suite in $planned) {
        if (@($summary.Steps | Where-Object {
            $_.Name -ceq $suite -and $_.Kind -ne 'Compile' -and $_.Status -eq 'Passed' -and $_.ExitCode -eq 0
        }).Count -ne 1) { throw "Missing or ambiguous successful test step: $suite" }
    }
    $runPath = [IO.Path]::GetFullPath([string]$summary.Directory).TrimEnd('\')
    $runsPath = Join-Path $projectPath 'build\runs'
    if (-not $runPath.StartsWith($runsPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The verification run belongs to a different project.'
    }
    $originalSummary = Join-Path $runPath 'summary.json'
    if ((Get-FileHash -LiteralPath $verificationPath -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath $originalSummary -Algorithm SHA256).Hash) {
        throw 'The copied verification record differs from the original run.'
    }
    $inputs = @($summary.Metadata.Inputs)
    if ($inputs.Count -eq 0) { throw 'The build has no input manifest.' }
    foreach ($inputFile in $inputs) {
        $inputPath = [IO.Path]::GetFullPath([string]$inputFile.Path)
        if (-not $inputPath.StartsWith($projectPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $inputPath -PathType Leaf) -or
            (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash -cne $inputFile.SHA256) {
            throw "A verified build input has changed or is missing: $inputPath"
        }
    }
    $executable = Join-Path $buildPath 'TarkovServerGuard.exe'
    $runExecutable = Join-Path $runPath 'app\TarkovServerGuard.exe'
    foreach ($appFile in @($executable, $runExecutable)) {
        if ((Get-FileHash -LiteralPath $appFile -Algorithm SHA256).Hash -cne $summary.Metadata.AppSHA256) {
            throw "The application does not match the verified executable: $appFile"
        }
    }
    $config = Join-Path $buildPath 'TarkovServerGuard.exe.config'
    if ((Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath (Join-Path $projectPath 'app.config') -Algorithm SHA256).Hash) {
        throw 'The application configuration does not match the verified source.'
    }
    $updateSteps = @($summary.Steps | Where-Object { $_.Name -ceq 'GitHubUpdateTests' -and $_.Kind -eq 'Test' })
    $expectedUpdateTest = Join-Path $runPath 'tests\GitHubUpdateTests.exe'
    if ($updateSteps.Count -ne 1 -or
        -not [string]::Equals([IO.Path]::GetFullPath([string]$updateSteps[0].FilePath),
            $expectedUpdateTest, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $expectedUpdateTest -PathType Leaf)) {
        throw 'The updater test executable is missing from the verified run.'
    }
    return [pscustomobject]@{
        Directory = $buildPath
        RunDirectory = $runPath
        Executable = $executable
        Configuration = $config
        VerificationPath = $verificationPath
        UpdateTestExecutable = $expectedUpdateTest
        Summary = $summary
    }
}
