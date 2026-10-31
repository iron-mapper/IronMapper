# Comparison suite

Read the root guide and current README.md/Directory.Build.props/targets/Directory.Packages.props. CLI work also
needs cli/AGENTS.md and cli/README.md. BENCHMARK_SPEC.md is the original implementation specification: old net10-only,
no-CI/project limits and dotnet run -p measurement examples do not describe today's suite. Do not restore superseded
behavior. If historical stage/scope instructions affect a request ambiguously, ask before widening the task.

## Permission and fairness

- No timing measurements (full, quick, partial or executing BenchmarkDotNet for disassembly) in PR tasks. Only the
  maintainer or an explicit maintainer command authorizes measurements. CI smoke is the exception: build local sources,
  equivalence tests on net10/net8 and one Dry iteration of every runtime scenario, excluding StartupBenchmark.
  If the suite changes, perform equivalent local smoke checks when execution is authorized.
- The plan provides 2.1.0 without a full run: do not claim numeric goals achieved; keep labeled 2.0.0 figures until
  an authorized full run. Quick/Dry is not release evidence. Historical “before release” checklists are not permission.
- Participants must perform equivalent work; Manual is baseline. Mark unsupported/different behavior in the feature
  matrix instead of measuring different work. Deterministic data (seed 42) and configuration/compilation belong in
  setup, outside measured runtime calls. Null/reference-identity differences belong in separate feature tests;
  do not compare collection copying against sharing the same-element collection reference.
- Preserve current README methodology, setup and per-scenario fairness/caveats. Distinguish direct versus DI paths
  and keep Manual_DI startup baseline. Disclose unmeasured Mapster.Tool. No AutoMapper 15+ source reading/copying;
  AUTOMAPPER_LICENSE_KEY stays in the environment, never committed/logged.

## Build and measured source

- Independent IronMapper.Comparison.slnx outside root solution. Own props intentionally avoid root packaging/version
  imports. Do not spread benchmark dependencies to core. Existing suite TreatWarningsAsErrors=false is deliberate,
  not permission to weaken other projects. Preserve style checks except generated timed BuildProbe projects.
- Suite src/tests target net10/net8; probes/tools net10. Default IronMapper is the published package in central props
  (currently 1.1.3). Local build/equivalence commands can use -p:UseLocalIronMapper=true, for example:
  `dotnet test benchmarks/IronMapper.Comparison.slnx -c Release -p:UseLocalIronMapper=true`.
- Authorized measurements need an inherited environment variable, not merely parent dotnet run -p: BenchmarkDotNet
  child builds otherwise silently use the package. PowerShell: `$env:UseLocalIronMapper = 'true'`; Bash:
  `export UseLocalIronMapper=true`. Restore task env changes afterward, specify -f for runtime multi-target projects,
  and verify actual package/source versions and child-build artifacts.
- run-all -UseLocalIronMapper uses project refs for build/tests/runtime, locally packed versioned packages for
  startup/AOT/size/build-time. Do not measure source rebuild time or PDB/XML copying as consumer package cost.
  Keep exact central competitor versions and provenance: machine, OS, SDK/runtime, commit and package source.
- FeatureMatrixTests writes results only with IRONMAPPER_WRITE_FEATURE_MATRIX=true; ordinary tests skip that writer.
  Do not enable it just for validation or overwrite release data with a test run.

## Results and runners

- Published numbers require an authorized full release-code run, same-session comparisons and respect for variance.
  Do not claim superiority within scatter or tune implementation to a target number.
- Preserve frozen results/baseline-1.1.3, per-run directories, raw data and run-info.json. Root result files copy the
  latest release report. Generate RESULTS.md/comparison/summary.json via scripts, not hand edits. Record separately
  remeasured scenarios honestly; report generation uses the recorded run commit, not current HEAD.
- Quick A/B goes to artifacts/quick with provenance, validity/source checks and both published/local framework
  variants. Quick/Dry CLI choices must not silently overwrite published results.
- Scripts own checks/measurement logic. run-benchmarks remains the existing maintainer release runner; proposals to
  move its checks/add flags/delete it are pending work, not migration instructions. Check actual supported parameters.
  Even Dry/CheckOnly can build/write artifacts; they are not read-only audit commands.
- summary.json/schemaVersion feeds the site. Coordinate schema readers/checks and read ../site/AGENTS.md and
  ../docs/AGENTS.md before changing result schema, source methodology headings or public performance claims.
