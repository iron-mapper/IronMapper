# Tests and snapshots

Read the root guide; generator changes need ../src/IronMapper.Generator/AGENTS.md, consumers/AOT ../eng/AGENTS.md.
Tests are required for implementation changes, not instruction-only work when the user excludes execution.

## Coverage

- IronMapper.Tests compiles with Generator as Analyzer (ReferenceOutputAssembly=false) and executes generated code;
  keep warnings-as-errors effective. Prefer runtime evidence over emitted-text assertions for behavior claims.
- Generator.Tests uses in-memory compilations, diagnostics, snapshots, equality and caching. Prefer
  GeneratorTestHelper.RunGeneratorCompileAndRun / CompileProject. Check compiler/consumer warnings too, not only IM diagnostics.
- Pipeline changes need ModelEqualityTests, named TrackingNames steps, cached source after unrelated/location edits,
  diagnostic appearance/disappearance after edits versus fresh runs. The compilation-bound reporting output is
  intentionally exempt from source-output caching expectations.
- CodeFixes.Tests uses CSharpCodeFixTest with Generator via GetSourceGenerators: test actual locations, properties,
  severity and document changes, not message-text parsing.
- Integration.Tests covers Generator + DI and multi-assembly Application/Domain. Preserve type-name architecture
  coverage and not-yet-initialized assembly tests. Do not accidentally load Application or run its module initializer
  before MapperRegistryAssemblyTests; retain Cecil-based architecture loading where needed.
- Tests running through the process-wide registry/feature switch belong in RegistryCollection. Preserve nonparallel
  assembly-registration tests and isolation from tests that initialize modules or disable the switch.
- PackageConsumer* and AotProbe* are outside IronMapper.slnx. Consumers reference packed packages, not source projects.
  Keep internal/public mapping, implicit-usings/language settings, registration settings, README programs, expected
  failing diagnostic builds and netstandard variants in verify-package.
- AOT/trimmed probes cover extensions, IMapper, runtime types, collections, original exceptions, switch overrides
  and transitive DI/maps in separate libraries on net8/net10. Require zero unexpected IL warnings and checked execution.
  Windows AOT needs Visual Studio C++ tools; report missing tooling rather than weakening checks/installing unasked.

## Snapshot workflow

- GeneratedCodeSnapshotTests uses Verify.SourceGenerators in IronMapper.Generator.Tests/Snapshots. For intentional
  generation changes run the focused filter, then relevant broader checks:
  `dotnet test tests/IronMapper.Generator.Tests --filter FullyQualifiedName~GeneratedCodeSnapshotTests`.
- Read every received/verified diff before accepting it. Explain intended changes and investigate unrelated ones.
  Never bulk-rename unread received files or accept snapshots just to pass tests.
- Verified .cs/.txt snapshots are committed with LF; received files are ignored failed-run outputs. Preserve version
  scrubbing to {version}. A new scenario fails until its initial snapshot is reviewed and accepted.
  DiffEngine_Disabled=true disables the GUI diff launcher when needed.
- Scenarios must compile; behavioral changes need appropriate runtime/regression coverage. Preserve compatibility
  baselines, including NotNestedMapping_SameCodeAs200.
- Note changed generated-code snapshots for a future authorized full performance run. Snapshot acceptance does not
  authorize timing benchmarks or replacing release numbers with quick/Dry results.
