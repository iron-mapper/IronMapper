# IronMapper — Codex guide

IronMapper is a compile-time .NET object mapper. Roslyn generates C# extension methods; the optional
IronMapper.Extensions.DI calls them through a generated delegate registry. Profiles are analyzed, not executed
for mapping. MappingValidator uses reflection; do not claim every API is reflection-free or has zero overhead.

## Product priorities and real-world use

- IronMapper is developed as a free, open-source, full-featured alternative to existing .NET object mappers.
  The goal is a high-quality, functionally complete, reliable, performant and easy-to-use library; this is a product
  direction, not a claim that every feature is already implemented.
- Correctness, public API stability, diagnostics, compatibility and usability take priority over auxiliary
  infrastructure. Never sacrifice correctness or maintainability to improve benchmark numbers.
- Benchmarks, the benchmark CLI and the website support the library. Avoid unnecessary coupling to core and do not
  let their optional tasks block library development. Mandatory quality checks, user documentation and release
  contracts remain required; this priority does not waive any of them.
- IronMapper is used in the independent SaaS hrtbt.ru. It supplies practical scenarios and feedback, not a build,
  test or release dependency. Where possible, reproduce issues found there as standalone library regression tests
  that do not depend on the SaaS.

## Before working

- Read IRONMAPPER_PLAN.md: working rules, accepted decisions, release order, status and done-when criteria.
  Distinguish dated decisions from historical reports and unfinished proposals. If conflicting instructions could
  change a contract or lose a rule, stop and ask the maintainer.
- Update IRONMAPPER_PLAN.md in the same change when task statuses, accepted decisions or done-when criteria change,
  or significant discrepancies are discovered, unless the user excludes the plan or requests analysis only.
  Minor edits that do not affect its content do not require a plan change. Analysis does not authorize edits or builds.
- Inspect Git status first; preserve unrelated work, untracked files, local settings and benchmark raw data.
- Read the applicable nested AGENTS.md before editing. From the root or across directories, explicitly read the
  files below; do not assume all nested instructions were loaded automatically.

| Area | Instructions |
|---|---|
| Runtime, DI, code fixes, generator | src/AGENTS.md; generator also src/IronMapper.Generator/AGENTS.md |
| Tests, snapshots, consumer/AOT probes | tests/AGENTS.md |
| Packaging, MSBuild, CI, release verification | eng/AGENTS.md, including edits under .github/workflows/ |
| Benchmarks and CLI | benchmarks/AGENTS.md; CLI also benchmarks/cli/AGENTS.md |
| README, docs, XML/package descriptions, CHANGELOG | docs/AGENTS.md |
| Website and its source documents | site/AGENTS.md and docs/AGENTS.md |

CLAUDE.md remains a detailed architecture reference. Its automatic staging/destructive-cleanup recipe is not a
Codex procedure. Follow these guides and the user's authorized scope instead of copying that recipe.

## Git and releases

- Do not run git commit, git push or git tag; the maintainer does those. Do not merge, rebase, create or switch
  branches without authorization for that operation. Do not stage automatically; git add is permitted only when
  the task allows it, and never for unrelated work.
- Project workflow: one plan item, one branch, one PR; new branches start from current main. The plan uses fix/,
  feature/, chore/, docs/, release/ (existing performance branches use perf/). Confirm a naming conflict with the
  execution environment before creating a branch. Integrate overlapping branches in order; updating against main
  is for the maintainer or an explicitly authorized task.
- Main changes go through PRs with squash merge. Main CI runs on main pushes and PRs, not ordinary branch pushes.
  Open a PR (draft is acceptable) when PR creation is authorized. Preserve required checks and review protections.
- Release tags v* belong on main after merge; pushing one publishes to NuGet. Tag and package versions must match.
  Do not bypass release-tag protection or infer permission to publish from a development task.
- Never automatically run destructive cleanup, hard resets or equivalents. Packaging/CI still needs a fresh
  checkout or another agreed clean environment; preserve the current tree and ask when none is available.

## Development invariants

- Do not read or copy AutoMapper 15+ source (RPL-1.5). Compare behavior using documentation and AutoMapper 14.x (MIT).
- Add no dependencies to the core IronMapper package. Keep Roslyn packages out of its NuGet runtime dependencies.
  Domain destination types must not need IronMapper-specific constructor/factory attributes.
- Keep TreatWarningsAsErrors, code-style enforcement and tests effective. Do not weaken settings, suppress failures
  or disable tests to get a green build. Follow .editorconfig and .gitattributes.
- Preserve incremental value equality and isolation from Roslyn objects; see the generator guide. Generated mapping
  hot paths have no LINQ, delegates, reflection, dynamic IL or unnecessary allocations. Registry delegates are intentional.
- User configuration problems belong in actionable IMxxxx diagnostics at user code, not compiler errors in .g.cs.
  Generated C# must compile without warnings with the consumer's available APIs and supported language versions.
- Breaking default behavior/API changes belong in major releases; 2.x opt-ins use accepted MSBuild settings.
  Do not invent exceptions beyond explicitly accepted fixes in the plan. Future 3.0 decisions are not instructions
  to implement them now; unresolved API/semantic decisions need the maintainer.

## Verification and documentation

- For a bug fix, first reproduce with a failing test. If it does not reproduce, record that in the plan and report
  it rather than claiming a fix. Prefer tests that compile and run generated code over text-only assertions.
- For implementation work, baseline checks are dotnet build IronMapper.slnx -c Release without warnings and
  dotnet test IronMapper.slnx. Add relevant package/AOT, snapshot, benchmark-smoke or site checks from scoped guides.
  The root solution does not include all probes, benchmark projects or the CLI.
- Read every intended snapshot difference before accepting it; never bulk-accept unread results. Note changed
  generated-code snapshots for a future maintainer-authorized performance run.
- Update EN/RU documentation together and CHANGELOG.md under [Unreleased] in the same behavior-changing PR.
  Claims need code, test or benchmark evidence; remove Known limitations only with their fixes.
- No performance measurements in PR tasks; CI benchmark smoke is the exception. Measurements require a maintainer
  command. Publish numbers only from an authorized full run, never quick/Dry runs or beyond observed variance.
  The plan explicitly provides a 2.1.0 release without a full run: keep 2.0.0 numbers and mark changes unmeasured.
- For instruction-only tasks use text/diff/scope/size checks; do not build or test when the user excludes them.

## Final task report

After every task, report in Russian:

- Brief outcome.
- What changed and why.
- Builds, tests and checks actually performed, with their results.
- Checks not performed and why.
- Remaining problems, risks and limitations.
- Questions and decisions requiring the maintainer, or explicitly state that none remain.
- Suggested repository commit message(s) briefly describing the completed work; do not execute a commit.

Never claim a check passed if it was not run. Keep report detail proportional to the task, while covering these items.

## Repository map

IronMapper.slnx: four src libraries, runtime/generator/code-fix/integration tests and Demo.
PackageConsumer and AotProbe projects under tests/: packed-package checks outside the solution.
benchmarks/IronMapper.Comparison.slnx: independent comparison suite; CLI has a separate build.
site/: Astro Starlight, generated from repository docs and benchmark summaries.
Shared build settings: Directory.Build.props; CI/release/site workflows: .github/workflows/.
