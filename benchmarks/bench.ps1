#Requires -Version 7
<#
.SYNOPSIS
    Benchmark CLI (cli/Comparison.Cli): an interactive front end for scripts/run-quick.ps1 and scripts/run-all.ps1.

.DESCRIPTION
    Builds the CLI (incremental, quiet; only its own output is shown) and runs it with the given arguments. The exit
    code is the CLI's, which is the script's. Without arguments in a terminal it opens a menu; `./bench.ps1 --help`
    lists the commands. The scripts can always be run directly; the CLI prints the equivalent command before each run.

.EXAMPLE
    ./bench.ps1
.EXAMPLE
    ./bench.ps1 results
.EXAMPLE
    ./bench.ps1 quick --filter 'Nested,InPlace' --frameworks 'net10.0,net8.0'
#>
$ErrorActionPreference = 'Stop'
# pwsh reads an unquoted list (--filter Nested,InPlace) as an array and would pass its elements to dotnet as separate
# arguments ("Unknown command 'InPlace'"). Joined back with commas, the command works with or without the quotes.
$cliArgs = @(foreach ($arg in $args) { if ($arg -is [array]) { $arg -join ',' } else { $arg } })
$cli = Join-Path $PSScriptRoot 'cli' 'Comparison.Cli'
$build = & dotnet build (Join-Path $cli 'Comparison.Cli.csproj') -c Release -v q -nologo -tl:off 2>&1
if ($LASTEXITCODE -ne 0) {
    $build | Out-Host
    Write-Error "Building the benchmark CLI failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}
# Ctrl+C reaches this pwsh too: it waits for the CLI (which decides whether to stop the run), then stops the script.
# The exit code is set in finally, which still runs then, so it stays the CLI's (130 after a stopped run).
try {
    & dotnet (Join-Path $cli 'bin' 'Release' 'net10.0' 'Comparison.Cli.dll') @cliArgs
}
finally {
    exit $LASTEXITCODE
}
