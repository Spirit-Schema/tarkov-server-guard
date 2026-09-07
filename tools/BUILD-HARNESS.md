# Local build verification

Run from the repository root in Windows PowerShell 5.1 or PowerShell 7:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory build\test-v0.8.4.3
```

The normal command compiles the application with warnings as errors, runs the
native runner checks and all 18 product regression suites, and copies the app
to the requested output directory only after every suite has passed. It does
not package, install, or publish a release. No dependency download is required.

Each invocation gets a new `build/runs/<UTC time>-<random ID>/` directory:

- `app/`: the exact application tested during this run.
- `tests/`: compiled test executables and diagnostic PDB files, separate from
  the deliverable.
- `logs/`: complete stdout/stderr for every compiler and test invocation.
- `artifacts/`: screenshots and other evidence produced by product UI tests.
- `harness-tests/`: synthetic runner fixtures, including intentional failures.
- `summary.json`: status, timings, native exit codes, timeout details, input
  hashes, compiler version, and the tested application's SHA-256.

`build/latest-run.json` records the latest completed attempt, including failed
attempts. A delivered app has its own `build-verification.json` alongside it.
Check that its `Status` is `Passed` and its `Metadata.AppSHA256` matches the
executable. A previous output directory can remain after a failed attempt;
its existence is not evidence that the new attempt passed.

Compilers have a 120-second limit and test suites a 180-second limit. A native
Windows Job Object owns each spawned process and its descendants, so timeout
or runner termination does not leave test children running. Both output pipes
are drained asynchronously to avoid deadlocks. The first failed step stops
the build with exit code 1 and leaves logs available for diagnosis. Input
files are hashed before and after testing; edits during a run reject delivery.

Limits can be changed explicitly with `-CompileTimeoutSeconds` and
`-TestTimeoutSeconds`. Investigate the recorded failure before raising them.
`-SkipTests` is only a development compile shortcut: its summary is marked
`Unverified`, and it is not a completed review or release verification.

To check the runner independently:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\tests\BuildHarnessTests.ps1 -ArtifactDirectory build\harness-selftest
```

This deliberately runs a nonzero-exit fixture, a timed-out process with a
child, a nonexistent executable, and a warning-rejected compilation. Those
fixture steps must fail; the enclosing suite passes only if their outcomes,
cleanup, logs, and summary are correct. It also checks argument preservation
and captures large simultaneous stdout/stderr streams without truncation.

The legacy memo and main-window UI suites use `tests/StaUiTestHarness.cs`'s STA message
loop. It runs queued callbacks, disposes open forms, completes teardown and
propagates failures, while the outer native runner supplies the timeout.

When adding a product suite, add its explicit source list to `$suites` in
`build.ps1`. The same manifest drives compilation and input fingerprinting.
The removed operation-note workflow suites are intentionally absent; legacy
memo behavior is covered by the archive/report suites and `MemoRollbackTests`.
