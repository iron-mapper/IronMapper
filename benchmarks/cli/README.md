# Benchmark CLI — for its developers

How to run it: [Benchmark CLI](../README.md#benchmark-cli-benchps1) in `benchmarks/README.md`. This page is about the
code.

## The rule

**The scripts own every check and all the measurement logic; the CLI only passes flags and shows output.**
`run-quick.ps1` and `run-all.ps1` (and `-SummarizeOnly`) stay the reference: CI, the published results and the
documented commands use them, and everything the CLI does can be done with them. So the CLI:

- passes only the parameters the user set; a parameter left out gets the script's default. The menu shows and
  preselects the defaults, read from the script's `param()` block through the PowerShell AST
  (`Processes/ScriptDefaults.cs`; constants only, nothing is executed). It never passes them;
- prints the exact `pwsh -NoProfile -NonInteractive -File ...` command before a run (`ScriptCommand.Display`), and
  after a menu run the equivalent `./bench.ps1 ...` (`QuickOptions`/`FullOptions.ToBenchCommand`);
- never repeats a check: a pre-flight result is the script's `OK`/`WARN`/`FAIL` line; a run's status is what
  `Get-RunStatus` in `run-quick.ps1` derives from `assemblies.json` (`ExitCode` and `Problems` only, nothing else of
  that file is read); the exit code is the script's;
- decides only what to show and how a run is started and stopped: progress, Ctrl+C (asks, then kills the process
  tree), keeping the machine awake, the menu's choice of a results folder.

When a feature needs a new check or a new measurement, add it to the script (with a flag), then expose the flag here.

## Layout

```
Program.cs              entry point: UTF-8 console, Spectre.Console.Cli commands; no arguments in a terminal → menu
BenchPaths.cs           benchmarks/, the repository, scripts/, artifacts/quick/, results/
Commands/               quick, full, results (show, summarize); Menu and the wizards (QuickWizard, FullWizard);
                        *Settings (flags) → *Options (ToScriptArguments, ToBenchCommand) → *Runner
Monitoring/             IRunMonitor, QuickMonitor (run-quick.ps1), RunAllMonitor (run-all.ps1), Phase (one stage and
                        its log), RunSession (a run with live or plain progress)
Parsing/                QuickOutput, RunAllOutput (stdout lines → events), PreflightCheck, BdnProgress (a
                        BenchmarkDotNet log), LogTail (follows a log another process writes), ScriptError (stderr)
Processes/              ScriptCommand, ScriptProcess (start, decode, kill the tree), ScriptRunner, InterruptHandler
                        (Ctrl+C), SleepGuard, ScriptDefaults, BenchEnvironment (git, frameworks of the suite), Tools
Artifacts/              what a finished run left: BenchmarkCatalog (benchmark classes ↔ -Filter), QuickRun (a run
                        folder), RunStatus (assemblies.json, as Get-RunStatus), ResultsRun (results/*/RESULTS.md),
                        MarkdownDocument/MarkdownTable/MarkdownBlock (summary.md and RESULTS.md)
Ui/                     Terminal (interactive or plain), CheckView, MarkdownView
```

## How a run goes

1. A command (or a wizard) builds `QuickOptions`/`FullOptions`; `ToScriptArguments()` gives the script's parameters.
2. `ScriptCommand(script, arguments, benchmarksDir)` is the invocation. `ScriptProcess` starts it with no console of
   ours, so Ctrl+C reaches only the CLI. On Windows it runs as `cmd /c "chcp 65001 >nul && pwsh ..."`: the hidden
   console otherwise starts with the OEM code page, and pwsh would write dotnet's UTF-8 output into the logs as
   mojibake. On Linux it runs under `setsid` when available. ANSI colours are removed from both streams.
3. Two ways to run it:
   - `ScriptRunner.RunAsync` — no progress view: the output lines go to a callback. Used for `-CheckOnly`
     (the checks are shown as a table afterwards) and `results summarize` (`-SummarizeOnly`).
   - `RunSession.RunAsync(command, monitor, keepAwake)` — a measurement. In a terminal: a live view refreshed every
     250 ms; Ctrl+C stops the view and asks (a second Ctrl+C stops at once). Plain mode (redirected output or
     `--plain`): the script's lines as they come, plus the monitor's progress lines; the first Ctrl+C stops. Stopping
     kills the whole tree and lists the partial results (`IRunMonitor.PartialArtifacts`).
4. After the run the runner reads what the script wrote (`Artifacts/`): the statuses of a quick run and its
   `summary.md`, or the summary table of `RESULTS.md`.

## Where progress comes from

Only from what the scripts and BenchmarkDotNet already write; nothing is measured or estimated by the CLI itself.

| Source | What is read | Where |
|---|---|---|
| stdout of `run-quick.ps1` | `Output: <root>`; `Filter: ...; frameworks: ...; job: ...` (the phases, in the order of the runs); the `OK`/`WARN`/`FAIL` check lines; `[HH:mm:ss] <variant> on <framework>: build`/`run`/`<status>`; `Summary: <path>` | `Parsing/QuickOutput.cs` → `QuickMonitor` |
| stdout of `run-all.ps1` | the mode header (`... — Dry run`); `results: <dir>`; `Expected duration ...: ~N min (Build 1, ...)` (the steps); check lines; `==> Step (~N min) — HH:mm`; `    log: <path>` (written by `Invoke-Dotnet`); `    done in h:mm:ss`; `All done in ... Results: <file>` | `Parsing/RunAllOutput.cs` → `RunAllMonitor` |
| the current stage's log | run-quick: `<root>/<variant>-<framework>/build.log`, `run.log`; run-all: the path of the last `log:` line, and for the Startup step `<results>/raw/startup/bdn-startup.log` | `Monitoring/Phase.cs` + `Parsing/LogTail.cs` (last line, "no log output for N min" after `Phase.HangThreshold`) |
| a BenchmarkDotNet log (`run.log`; `bdn-*.log` of run-all) | `// ***** Found N benchmark(s) in total *****`, `// Benchmark: ...`, `// Launch: i / n`, `// ** Remained N ... (Xh Ym from now) **`, `Global total time` | `Parsing/BdnProgress.cs` (cases done/total, current case, launch, ETA) |
| stderr | the last line; after a failure the message (`ScriptError`: pwsh's concise error view, else the text) | monitors, `ScriptResult.ErrorMessage` |

Unknown lines are ignored everywhere, so new output of a script does not break the CLI; it just is not shown as
progress until a parser knows it. The patterns do not depend on the culture (BenchmarkDotNet's percentage and date
are not read) or on the dash of the header lines (an em dash can arrive mangled; see `ScriptOutputTests`).

## Tests

```bash
cd benchmarks/cli
dotnet test Comparison.Cli.Tests
```

CI runs them on ubuntu and windows in pull requests that change `benchmarks/**` (`.github/workflows/bench-cli.yml`,
not a required check). The parsers and monitors are tested on recorded output in `Comparison.Cli.Tests/Fixtures/`;
`ProcessTests` start real `pwsh` processes (decoding, arguments, killing the tree), so `pwsh` must be on `PATH`.

| Test class | Covers |
|---|---|
| `ScriptOutputTests` | `QuickOutput`, `RunAllOutput`, `ScriptError` on the recorded stdout/stderr |
| `QuickMonitorTests`, `RunAllMonitorTests` | stages, logs followed, final states; the menu's folder and `-Force` choices |
| `BdnProgressTests`, `LogTailTests` | BenchmarkDotNet logs (two cultures), growing and rewritten logs |
| `ArtifactTests` | `assemblies.json` → status (`Get-RunStatus`), run folders, `summary.md`/`RESULTS.md`, `BenchmarkCatalog` against `$runtimeClasses` of `run-all.ps1` |
| `ResultsViewTests` | the `results` views, rendered to a string |
| `ProcessTests` | quoting, `ScriptCommand.Display`, UTF-8 end to end, Ctrl+C, `SleepGuard` |

### Re-recording fixtures

The fixtures are real output, never edited by hand (`Fixtures/README.md` lists each file and the command that made
it). When a script's output changes:

1. Run the command from `Fixtures/README.md` for the affected file and save stdout and stderr separately, e.g.

   ```powershell
   cd benchmarks
   pwsh -NoProfile -File ./scripts/run-quick.ps1 -Dry -Frameworks net10.0 -Filter '*.FlatBenchmark.*' `
       > cli/Comparison.Cli.Tests/Fixtures/run-quick.dry.stdout.txt
   ```

   Logs and run files (`run.log`, `build.log`, `run-info.json`, `summary.md`, `assemblies.json`) are copied from the
   run folder the command printed (`../artifacts/quick/<yyyyMMdd-HHmmss>/`).
2. Update the date (and the machine or culture, if different) in `Fixtures/README.md`; a new fixture gets a row there.
3. Run the tests. A failure is either the change you made (update the expected values in the test) or a parser that
   no longer recognizes a line (fix the parser in `Parsing/`, not the fixture).

The current fixtures come from a ru-RU Windows machine (decimal comma, Russian MSBuild messages); keep at least one
fixture with a non-English culture when re-recording elsewhere.

### Fixtures from Windows, tests on every platform

The tests run on ubuntu and windows (CI), but the fixtures hold what the scripts printed on Windows. **A test never
gives a path or text from a fixture to a function whose result depends on the platform:**

- **Paths** in fixtures use `\` (`log: C:\...\build.log`, `Output: C:\...`). `Path.GetFileName`,
  `GetDirectoryName`, `GetExtension` and the like split only at `/` on Linux and macOS and return the whole path. Use
  `Fixture.FileName(path)` (both separators) for the name of a recorded path. When the CLI itself must work with the
  path (follow a log, list a folder), replace the recorded path by a temporary folder of the test before feeding the
  line, as `QuickMonitorTests` does with `Output:`; on Linux the scripts write `/` and the CLI needs no change. Paths a
  test builds itself come from `Path.Combine` with relative parts or a `Fixture.NewTempDir()`, never a literal
  `C:\...`. Comparing a recorded path as a string (`Assert.Equal(@"C:\...", ...)`, `EndsWith`) is fine.
- **Line endings** of a checked-out fixture depend on the platform (`.gitattributes`: `*.md` CRLF everywhere, the
  other fixtures LF on Linux and macOS, CRLF on Windows), so a test that reads a fixture as one string checks only one
  of them on each machine. Read lines with `Fixture.Lines` (either ending), and test a parser of whole text with both
  endings through `Fixture.Text(name, newline)` (`[InlineData("\r\n")]`, `[InlineData("\n")]`, as in
  `ArtifactTests` and `LogTailTests`). Do not split on `Environment.NewLine` or `'\n'` in a test.
- **Check on Linux** before a PR when tests or fixtures change (the CI job is not a required check):

  ```bash
  # in the repository root, after git add: the index as a Linux checkout (LF), in the .NET SDK image (pwsh included).
  # MSYS_NO_PATHCONV=1: Git Bash on Windows would otherwise turn /src into a Windows path; elsewhere it does nothing.
  tree=$(git write-tree)
  MSYS_NO_PATHCONV=1 docker run --rm -v "$PWD:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash -c "
    git config --global --add safe.directory '*' && mkdir /work &&
    git -C /src archive $tree benchmarks | tar -x -C /work && cd /work/benchmarks/cli &&
    dotnet test Comparison.Cli.Tests -c Release"
  ```

## Adding a flag

A script parameter that the CLI should pass (example: `-Foo` of `run-quick.ps1`):

1. **The script first**: the parameter, its default in `param()` (a constant, so the menu can show it), and its
   effect. Document it where the script is documented (`benchmarks/README.md`).
2. `Commands/QuickCommand.cs`: a property on `QuickSettings` with `[CommandOption("--foo <VALUE>")]` and a
   `[Description]` ending with the script parameter (`(-Foo)`); validation in `Validate()` only for what the CLI needs
   to build the arguments (unknown benchmark names, frameworks the suite does not target) — anything about the
   machine or the run belongs to the script's checks.
3. `Commands/QuickOptions.cs`: the property, `ToScriptArguments()` (passed only when set) and `ToBenchCommand()`.
   For `run-all.ps1`: `FullSettings`/`FullOptions` in `Commands/FullCommand.cs`.
4. The menu, if it should ask: `QuickWizard` (a value parameter shows its default through `ScriptDefaults`) or
   `FullWizard` (`run-all.ps1` has switches only).
5. If the parameter changes the output, the parser and a re-recorded fixture (above).
6. Tests: the arguments and the equivalent command (`ToScriptArguments`, `ToBenchCommand`; as in
   `RunAllMonitorTests.QuickAndDry_GetTheirOwnFolder_FullKeepsTheScriptDefault`), quoting of unusual values in
   `ProcessTests.Arguments_AreQuotedForCopying`; docs: the flags list in `benchmarks/README.md` ("Benchmark CLI").

Lists go to the script as one comma-separated argument: `pwsh -File` passes `a,b` as one string, and the scripts split
it (#44). On Windows `cmd` cannot carry `"` or `%` in an argument; `ScriptProcess.CmdQuote` refuses them.

## Adding a script

1. A path in `BenchPaths` (`ScriptsDir`).
2. Options (`ToScriptArguments`, `ToBenchCommand`), settings and a command in `Commands/`; register it in `Program.cs`
   (`config.AddCommand`, with a description and an example) and, if wanted, in `Menu`.
3. A parser of the script's stdout in `Parsing/` (events as records, unknown lines → `null`) and an `IRunMonitor` in
   `Monitoring/` (stages as `Phase`, logs through `Phase.Follow`, `PartialArtifacts` for Ctrl+C). The script should
   print a line when a stage starts and the path of the log it writes (as `Invoke-Dotnet` does with `log:`); if it
   does not, add that to the script rather than guessing paths in the CLI.
4. If the script writes files the CLI shows afterwards, the reader in `Artifacts/`.
5. Fixtures of its real output (a `-Dry` or `-CheckOnly` run, plus a failing check), rows in `Fixtures/README.md`, and
   tests: a parser test on the fixture, a monitor test that replays it, and the arguments.
6. Docs: `benchmarks/README.md` ("Benchmark CLI"), this file, and the CLI paragraph of the root `CLAUDE.md`.

## Build

`cli/` has its own `Directory.Build.props`, `Directory.Build.targets` (empty) and `Directory.Packages.props`, so the
suite's IronMapper switches (`UseLocalIronMapper`, `IronMapperPackageVersion`) do not reach it, and Spectre.Console
does not appear in the package table of `RESULTS.md` (`Get-PackageVersions`). It is not in
`IronMapper.Comparison.slnx` and not under `src/`, `tests/`, `probes/` or `tools/` (`Clear-BuildOutputs` of the
scripts would delete its build). `bench.ps1`/`bench.sh` build it quietly (`dotnet build -c Release -v q`) and run
`bin/Release/net10.0/Comparison.Cli.dll`. Warnings are errors.

The folder `Comparison.Cli/Artifacts/` is source code, not build output: the root `.gitignore` excludes it from
`artifacts/` explicitly (`git` ignores case on Windows and macOS).
