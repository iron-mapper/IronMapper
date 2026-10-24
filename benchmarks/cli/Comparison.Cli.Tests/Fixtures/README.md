# Fixtures

Real output of the scripts and of BenchmarkDotNet 0.15.8, recorded on Windows 11 (ru-RU culture: `83,3 %`, Russian
MSBuild messages, a Cyrillic power plan name) on 2026-10-23/24. Do not edit them by hand: re-record when a script's
output changes.

| File | Source |
|---|---|
| `run-quick.dry.stdout.txt` | `run-quick.ps1 -Dry -Frameworks net10.0 -Filter '*.FlatBenchmark.*'`, stdout |
| `run-quick.checkonly-fail.stdout.txt`, `.stderr.txt` | `run-quick.ps1 -CheckOnly -Frameworks net10.0 -Branch no-such-branch` (pre-flight FAIL, exit code 1) |
| `run-all.checkonly.stdout.txt`, `.stderr.txt` | `run-all.ps1 -CheckOnly` with too little free memory (FAIL, exit code 1) |
| `bdn-flat-dry.run.log` | `run.log` of the published variant of that run: `dotnet run ... --filter '*.FlatBenchmark.*' --job Dry` |
| `bdn-two-classes-dry.run.log` | the same with `--filter '*.FlatBenchmark.*' '*.InPlaceBenchmark.*'`: two classes, one "Found 12 benchmark(s) in total" |
| `build.ru.log` | `build.log` of that run (localized MSBuild) |
| `run-info.json`, `summary.md`, `assemblies.published.json` | the run folder `artifacts/quick/20261024-113550` |
| `RESULTS.2.0.0.head.md` | `benchmarks/results/2.0.0/RESULTS.md` up to its summary table |
| `run-all.dry.stdout.txt` | `./bench.ps1 full --mode dry --local --results-dir ../artifacts/bench-cli-dry --force > file` (plain output: run-all.ps1's stdout line by line, with the `log:` lines, plus the CLI's own command, progress and summary lines) |
