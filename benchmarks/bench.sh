#!/usr/bin/env sh
# Benchmark CLI (cli/Comparison.Cli): an interactive front end for scripts/run-quick.ps1 and scripts/run-all.ps1.
# Builds the CLI (incremental, quiet) and runs it with the given arguments; the exit code is the CLI's (the script's).
# See bench.ps1 and benchmarks/README.md, "Benchmark CLI".
set -eu
dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
cli="$dir/cli/Comparison.Cli"
if ! out=$(dotnet build "$cli/Comparison.Cli.csproj" -c Release -v q -nologo -tl:off 2>&1); then
    printf '%s\n' "$out" >&2
    echo "Building the benchmark CLI failed." >&2
    exit 1
fi
exec dotnet "$cli/bin/Release/net10.0/Comparison.Cli.dll" "$@"
