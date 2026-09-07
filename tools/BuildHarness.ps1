# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

# This helper supports Windows PowerShell 5.1 and does not require a package restore.
if (-not ('TsgBuildProcessJob' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'BuildProcessJob.cs')
}

function New-BuildRunContext {
    param([Parameter(Mandatory = $true)][string]$Directory)
    $directoryPath = [IO.Path]::GetFullPath($Directory)
    $logsPath = Join-Path $directoryPath 'logs'
    New-Item -ItemType Directory -Force -Path $logsPath | Out-Null
    return [pscustomobject]@{
        RunId = [IO.Path]::GetFileName($directoryPath)
        Directory = $directoryPath
        LogsDirectory = $logsPath
        StartedUtc = [DateTime]::UtcNow.ToString('o')
        CompletedUtc = $null
        Status = 'Running'
        Steps = New-Object 'System.Collections.Generic.List[object]'
        Metadata = @{}
    }
}

function Save-BuildRunSummary {
    param([Parameter(Mandatory = $true)]$Context)
    $summaryPath = Join-Path $Context.Directory 'summary.json'
    $temporaryPath = $summaryPath + '.tmp'
    $json = $Context | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($temporaryPath, $json, (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temporaryPath -Destination $summaryPath -Force
}

function ConvertTo-NativeArgument {
    param([AllowEmptyString()][string]$Value)
    # Windows CRT quoting: backslashes only need doubling before quotes and
    # the closing quote. Always quote, including an empty argument.
    $builder = New-Object Text.StringBuilder
    [void]$builder.Append('"')
    $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $slashes++
            continue
        }
        if ($character -eq '"') {
            [void]$builder.Append(('\' * ($slashes * 2 + 1)))
        } else {
            [void]$builder.Append(('\' * $slashes))
        }
        [void]$builder.Append($character)
        $slashes = 0
    }
    [void]$builder.Append(('\' * ($slashes * 2)))
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-BuildProcess {
    param(
        [Parameter(Mandatory = $true)]$Context,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [ValidateRange(1, 3600)][int]$TimeoutSeconds = 120,
        [ValidateSet('Compile', 'Test', 'Harness')][string]$Kind = 'Test'
    )
    $safeName = [regex]::Replace($Name, '[^A-Za-z0-9._-]', '_')
    $logPrefix = '{0:D2}-{1}' -f ($Context.Steps.Count + 1), $safeName
    $step = [pscustomobject]@{
        Name = $Name
        Kind = $Kind
        Status = 'Running'
        FilePath = $FilePath
        Arguments = @($Arguments)
        WorkingDirectory = $WorkingDirectory
        TimeoutSeconds = $TimeoutSeconds
        StartedUtc = [DateTime]::UtcNow.ToString('o')
        DurationSeconds = 0
        ExitCode = $null
        WarningCount = 0
        StdoutPath = Join-Path $Context.LogsDirectory ($logPrefix + '.stdout.log')
        StderrPath = Join-Path $Context.LogsDirectory ($logPrefix + '.stderr.log')
        Error = $null
    }
    $Context.Steps.Add($step)
    Save-BuildRunSummary $Context
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = New-Object Diagnostics.Process
    $job = $null
    $processStarted = $false
    $standardOutput = ''
    $standardError = ''
    try {
        $process.StartInfo = New-Object Diagnostics.ProcessStartInfo
        $process.StartInfo.FileName = $FilePath
        $process.StartInfo.Arguments = (@($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
        $process.StartInfo.WorkingDirectory = $WorkingDirectory
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        $process.StartInfo.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
        $process.StartInfo.StandardErrorEncoding = New-Object Text.UTF8Encoding($false)
        $process.StartInfo.EnvironmentVariables['TSG_BUILD_RUN_ROOT'] = $Context.Directory
        $job = New-Object TsgBuildProcessJob
        if (-not $process.Start()) { throw 'The process could not be started.' }
        $processStarted = $true
        $job.Attach($process)
        # Drain both pipes concurrently, so a verbose compiler/test cannot
        # deadlock when either OS pipe buffer fills.
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $step.Status = 'TimedOut'
            $step.Error = 'Exceeded ' + $TimeoutSeconds + ' seconds.'
            $job.Dispose()
            if (-not $process.WaitForExit(5000)) { throw 'The timed-out process did not exit after job termination.' }
        }
        # Also close a successful process's job before draining its streams:
        # a leaked child must not keep either redirected pipe open forever.
        $job.Dispose()
        $step.ExitCode = $process.ExitCode
        if ($outputTask.Wait(5000)) {
            $standardOutput = $outputTask.Result
        } else {
            throw 'Standard output did not close after the process exited.'
        }
        if ($errorTask.Wait(5000)) {
            $standardError = $errorTask.Result
        } else {
            throw 'Standard error did not close after the process exited.'
        }
        if ($step.Status -ne 'TimedOut') {
            $step.Status = if ($step.ExitCode -eq 0) { 'Passed' } else { 'Failed' }
            if ($step.ExitCode -ne 0) { $step.Error = 'Process exited with code ' + $step.ExitCode + '.' }
        }
        $step.WarningCount = [regex]::Matches(($standardOutput + "`n" + $standardError), '(?im)\bwarning\s+(?:CS\d+|[A-Z]+\d+)\b').Count
    } catch {
        if ($step.Status -ne 'TimedOut') { $step.Status = 'Failed' }
        $step.Error = (@($step.Error, $_.Exception.Message) | Where-Object { $_ }) -join ' '
    } finally {
        if ($null -ne $job) { $job.Dispose() }
        if ($processStarted -and -not $process.HasExited) {
            $process.Kill()
            [void]$process.WaitForExit(5000)
        }
        $stopwatch.Stop()
        $step.DurationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
        [IO.File]::WriteAllText($step.StdoutPath, $standardOutput, (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($step.StderrPath, $standardError, (New-Object Text.UTF8Encoding($false)))
        $process.Dispose()
        Save-BuildRunSummary $Context
    }
    Write-Host ('[{0}] {1} ({2}s)' -f $step.Status, $Name, $step.DurationSeconds)
    if ($step.Status -ne 'Passed') {
        Write-Host $step.Error
        # Keep the console useful while retaining the complete output in logs.
        (($standardOutput + "`n" + $standardError) -split '\r?\n' | Select-Object -Last 25) | ForEach-Object { Write-Host $_ }
        Write-Host ('Logs: ' + $step.StdoutPath + ' ; ' + $step.StderrPath)
    }
    return $step
}

function Assert-BuildProcessPassed {
    param([Parameter(Mandatory = $true)]$Result)
    if ($Result.Status -ne 'Passed') {
        throw ($Result.Name + ': ' + $Result.Status + '. ' + $Result.Error)
    }
}
