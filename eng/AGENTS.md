# Packaging, CI and releases

Read this also for root MSBuild/package/buildTransitive files and .github/workflows changes. Read the root guide
and ../tests/AGENTS.md; benchmark/site changes also need their scoped guides.

- Packaging/build-order/CI changes require a fresh checkout or agreed clean environment: stale bin/obj can hide
  missing build steps. Never automatically stage the user's tree or follow destructive cleanup/reset recipes from
  CLAUDE.md or historical reports. If proposed changes cannot be validated safely in isolation, ask the maintainer.
  Report exact checks and any remaining clean-tree validation gap.
- Baseline: Release build without warnings and solution tests. Then run
  `pwsh ./eng/verify-package.ps1 -PackageDir ./artifacts/packages`: pack both packages, inspect layout/dependencies,
  build/run consumer variants against those packages. Existing binaries/project refs are not package evidence.
- Preserve analyzers/dotnet/cs, runtime targets, symbols, embedded ILLink substitutions and buildTransitive props/targets.
  Roslyn must not become a runtime package dependency. Implicit using stays C#-only and conditional on ImplicitUsings
  and IronMapperImplicitUsings. Preserve no-map, C# 8/9, explicit using/IDE0005, netstandard and DI-only consumer cases.
- Run `pwsh ./eng/verify-aot.ps1 -PackageDir ./artifacts/packages` for relevant implementation changes: packed-package
  net8/net10 AOT/trimmed publication and checked execution. Verify feature switch runtimeconfig and registration
  presence/absence in ILC maps/trimmed assemblies, explicit overrides and transitive DI. Current expectation is zero
  trim/AOT warnings, not the historical IL2026 on AddIronMapper.
- Use CI's PowerShell semantics and inspect the process exit status, not just success text. Example:
  `pwsh -NoProfile -Command './eng/verify-package.ps1 -PackageDir ./artifacts/packages; exit $LASTEXITCODE'`.
  Apply the same wrapper to verify-aot. Expected failures are checked and global LASTEXITCODE reset; successful scripts
  explicitly exit 0 and unexpected failures throw. Preserve this behavior.
- Main CI builds/tests on Ubuntu/Windows with .NET 10; package checks on both; AOT on Ubuntu for net8/net10.
  .NET 9 CI coverage is pending, not guaranteed. Preserve required Build & Test/Pack & verify and AOT/benchmark-smoke
  PR jobs. Ordinary branch pushes without a PR do not trigger main CI.
- CLI and site checks are separate/path-filtered. CLI is outside the comparison solution; build/test it separately.
  Do not make a path-filtered job universally required when unrelated PRs will never receive it. Do not narrow main
  CI with those filters or bypass repository protections.
- Publish is v* tag-driven; tag must match Directory.Build.props Version. Preserve build/test/package verification
  before package/symbol publication. Tags belong on main after squash merge; maintainer handles commit/push/tag.
  Do not publish or change protections during unrelated implementation work.
- Before an authorized release update both site/pages/roadmap.md and site/pages/ru/roadmap.md and their dates,
  CHANGELOG and upgrade notes. For 2.1.0 the plan provides release without full benchmark measurements: retain labeled
  2.0.0 figures and mark Performance unmeasured. A release checklist is not permission to launch measurements.
- Local prerelease test packages need a new version when contents change because NuGet caches by version.
