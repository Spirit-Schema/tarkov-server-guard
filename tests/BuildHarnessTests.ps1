# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

param([Parameter(Mandatory = $true)][string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $projectRoot 'tools\BuildHarness.ps1')
$testDirectory = Join-Path $ArtifactDirectory ([Guid]::NewGuid().ToString('N'))
$context = New-BuildRunContext $testDirectory
$testDirectory = $context.Directory
$context.Metadata['Purpose'] = 'Native runner behavior tests. Failed and TimedOut fixture steps are expected and asserted.'
$assertions = 0

function Assert-Harness {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    $script:assertions++
}

function Invoke-Fixture {
    param([string]$Name, [string[]]$FixtureArguments, [int]$TimeoutSeconds = 10)
    Invoke-BuildProcess -Context $context -Name $Name -FilePath $fixtureExe -Arguments $FixtureArguments -WorkingDirectory $testDirectory -TimeoutSeconds $TimeoutSeconds
}

try {
    $fixtureSource = Join-Path $testDirectory 'RunnerFixture.cs'
    $fixtureExe = Join-Path $testDirectory 'RunnerFixture.exe'
    $fixtureCode = @'
using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
internal static class RunnerFixture
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args[0] == "arguments")
        {
            for (int i = 1; i < args.Length; i++)
                Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(args[i])));
            return 0;
        }
        if (args[0] == "flood")
        {
            Console.Write(new string('O', 262144));
            Console.Error.Write(new string('E', 262144));
            return 0;
        }
        if (args[0] == "fail")
        {
            Console.WriteLine("before failure");
            Console.Error.WriteLine("intentional failure");
            return 7;
        }
        if (args[0] == "spawn")
        {
            ProcessStartInfo start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "sleep");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            using (Process child = Process.Start(start))
                Console.WriteLine("child=" + child.Id);
        }
        Console.Out.Flush();
        Thread.Sleep(30000);
        return 0;
    }
}
'@
    [IO.File]::WriteAllText($fixtureSource, $fixtureCode, (New-Object Text.UTF8Encoding($false)))
    $compiler = @(
        'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
        'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    $compileResult = Invoke-BuildProcess -Context $context -Name 'Fixture.Compile' -Kind Compile -FilePath $compiler -Arguments @('/nologo', '/utf8output', '/warn:4', '/warnaserror+', '/target:exe', ('/out:' + $fixtureExe), $fixtureSource) -WorkingDirectory $testDirectory -TimeoutSeconds 30
    Assert-BuildProcessPassed $compileResult

    $argumentValues = @('', 'plain', 'space value', 'quote"here', 'C:\space dir\', 'a\\"b', "first`nsecond", ([string][char]0xD55C + [char]0xAE00))
    $argumentsResult = Invoke-Fixture 'ArgumentRoundTrip' (@('arguments') + $argumentValues)
    Assert-Harness ($argumentsResult.Status -eq 'Passed') 'Argument fixture failed.'
    $expectedArguments = ($argumentValues | ForEach-Object { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($_)) }) -join "`n"
    $actualArguments = [IO.File]::ReadAllText($argumentsResult.StdoutPath).Replace("`r`n", "`n").TrimEnd([char]10)
    Assert-Harness ($actualArguments -ceq $expectedArguments) 'Native argument quoting changed an empty, Unicode, quoted, newline, or trailing-backslash argument.'

    $floodResult = Invoke-Fixture 'ConcurrentOutputCapture' @('flood')
    Assert-Harness ($floodResult.Status -eq 'Passed') 'Large stdout/stderr deadlocked or failed.'
    Assert-Harness ([IO.File]::ReadAllText($floodResult.StdoutPath).Length -eq 262144) 'Standard output was truncated.'
    Assert-Harness ([IO.File]::ReadAllText($floodResult.StderrPath).Length -eq 262144) 'Standard error was truncated.'

    $failureResult = Invoke-Fixture 'ExpectedNonzeroExit' @('fail')
    Assert-Harness ($failureResult.Status -eq 'Failed' -and $failureResult.ExitCode -eq 7) 'A nonzero exit was reported as a pass.'
    Assert-Harness ([IO.File]::ReadAllText($failureResult.StderrPath).Contains('intentional failure')) 'Failure diagnostics were lost.'
    $failureRejected = $false
    try { Assert-BuildProcessPassed $failureResult } catch { $failureRejected = $true }
    Assert-Harness $failureRejected 'The checked process gate accepted a failed process.'

    $timeoutResult = Invoke-Fixture 'ExpectedTimeoutAndChildCleanup' @('spawn') 1
    Assert-Harness ($timeoutResult.Status -eq 'TimedOut') 'A timed-out test was not identified.'
    Assert-Harness ($timeoutResult.DurationSeconds -lt 16) 'Timeout cleanup exceeded its bounded wait.'
    $childMatch = [regex]::Match([IO.File]::ReadAllText($timeoutResult.StdoutPath), 'child=(\d+)')
    Assert-Harness $childMatch.Success 'The timeout fixture did not start its child process.'
    $childAlive = $false
    try {
        $childProcess = [Diagnostics.Process]::GetProcessById([int]$childMatch.Groups[1].Value)
        try { $childAlive = -not $childProcess.HasExited } finally { $childProcess.Dispose() }
    } catch [ArgumentException] { }
    Assert-Harness (-not $childAlive) 'The timed-out fixture left its child process running.'

    $missingResult = Invoke-BuildProcess -Context $context -Name 'ExpectedStartFailure' -FilePath (Join-Path $testDirectory 'missing.exe') -WorkingDirectory $testDirectory -TimeoutSeconds 1
    Assert-Harness ($missingResult.Status -eq 'Failed' -and $null -eq $missingResult.ExitCode) 'A process start failure was misreported.'

    $warningSource = Join-Path $testDirectory 'WarningFixture.cs'
    $warningExe = Join-Path $testDirectory 'WarningFixture.exe'
    [IO.File]::WriteAllText($warningSource, 'internal class WarningFixture { private int unused; private static void Main() {} }', (New-Object Text.UTF8Encoding($false)))
    $warningResult = Invoke-BuildProcess -Context $context -Name 'ExpectedWarningRejection' -Kind Compile -FilePath $compiler -Arguments @('/nologo', '/utf8output', '/warn:4', '/warnaserror+', '/target:exe', ('/out:' + $warningExe), $warningSource) -WorkingDirectory $testDirectory -TimeoutSeconds 30
    Assert-Harness ($warningResult.Status -eq 'Failed') 'Compiler warnings did not fail compilation.'
    Assert-Harness (-not (Test-Path -LiteralPath $warningExe)) 'A warning-rejected build produced an executable.'

    $savedSummary = Get-Content -LiteralPath (Join-Path $testDirectory 'summary.json') -Raw | ConvertFrom-Json
    Assert-Harness ($savedSummary.Steps.Count -eq 7) 'The summary omitted a completed step.'
    Assert-Harness ($savedSummary.Steps[3].ExitCode -eq 7 -and $savedSummary.Steps[4].Status -eq 'TimedOut') 'The summary lost the actual failure and timeout results.'
    $context.Status = 'Passed'
    $context.Metadata['AssertionsPassed'] = $assertions
    Write-Host ('BuildHarnessTests: PASS (' + $assertions + ' assertions)')
} catch {
    $context.Status = 'Failed'
    $context.Metadata['Failure'] = $_.Exception.Message
    Write-Host ('BuildHarnessTests: FAIL: ' + $_.Exception.Message)
    exit 1
} finally {
    $context.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    Save-BuildRunSummary $context
}
exit 0
