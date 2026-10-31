# Benchmark CLI

Read root/parent guides, README.md here and affected fixture instructions. CLI wraps scripts, not a second engine.

- Scripts own every check/measurement. Pass only explicitly changed parameters. Menu defaults come from constant
  PowerShell param() values via AST, without execution. Add checks to scripts first, then expose flags. Print the
  exact invocation and menu-equivalent bench command. Preserve script exit status.
- Read only ExitCode/Problems from assemblies.json for status, as Get-RunStatus does. Progress uses script stdout,
  stage log paths and BDN logs, ignoring unknown lines. Do not parse culture-dependent percentages/dates or guess
  paths when scripts can print them.
- Preserve Windows hidden-console UTF-8 handling (chcp 65001 before pwsh), ANSI stripping, Unix process-group handling,
  whole-tree cancellation, exit 130 and interactive/plain Ctrl+C behavior.
- Lists are one comma-separated script argument; quote example lists in single quotes. Preserve bench.ps1's joining
  of unquoted PowerShell arrays, cross-shell printed-command quoting (apostrophes/backslashes) and CmdQuote rejection
  of unsupported characters.
- Own Directory.Build.props/targets/Directory.Packages.props isolate Spectre/CLI from measurement package tables.
  CLI stays outside comparison solution and its src/tests/probes/tools cleanup roots. Comparison.Cli/Artifacts is
  source: preserve its gitignore exception. Warnings remain errors.
- Fixtures are recorded real output; never edit them by hand to pass tests. Re-record documented commands, separate
  stdout/stderr, update Fixtures/README.md provenance and keep a non-English fixture. Re-recording Dry workloads needs
  authorization under parent policy.
- Recorded Windows paths must not enter host-dependent Path methods in cross-platform tests. Use Fixture.FileName
  or replace runtime paths with test temporary folders. Use Fixture.Lines and test whole-text parsers with both
  CRLF/LF via Fixture.Text; never assume Environment.NewLine for fixture text.
- Run `dotnet test benchmarks/cli/Comparison.Cli.Tests` for applicable code changes; process tests need pwsh.
  Test/fixture changes require Linux and Windows validation before PR, via agreed isolation or an explicit reported
  gap. Do not stage user work merely to reproduce the index-based Docker recipe.
- Root/suite test commands do not cover CLI. Preserve separate bench-cli workflow and its path-filtered status.
  Update flags, relevant READMEs, actual fixtures and tests together when CLI behavior changes.
