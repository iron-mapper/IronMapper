# Documentation contracts

Read this for root README.md/README.ru.md, CHANGELOG.md, XML comments and package metadata too. Website changes
also need ../site/AGENTS.md; performance claims ../benchmarks/AGENTS.md.

- Every claim needs code/test/authorized benchmark evidence. Distinguish shipped releases, unreleased implementation,
  future decisions and unsupported features. Retain Known limitations until fixed. Do not claim universal zero overhead,
  reflection-free APIs or .NET 9 CI coverage that is not present.
- Update EN/RU together and CHANGELOG [Unreleased] in the behavior-changing PR. Explain changed runtime results,
  upgrade actions and warnings-as-errors effects. Keep migration Supported/Partial/Different/Unsupported labels honest:
  runtime evidence is stronger than generated-text assertions.
- Numbers come only from benchmarks/results and authorized full runs, with framework/version/methodology and variance
  context. Never publish quick/Dry numbers or a goal as measured. For 2.1.0 keep labeled 2.0.0 figures and Performance
  unmeasured unless the maintainer explicitly authorizes a new full run.
- Each docs/<name>.md has docs/<name>.ru.md and vice versa. One level-1 title at the start; optional language banner
  follows. Preserve valid repository-relative links/GitHub anchors and site-consumed heading shapes.
- diagnostics EN/RU: every level-2 heading is `## IMxxxx — Title`, with identical code sets. Keep withdrawn code
  sections instead of renaming/removing codes. Align descriptors, AnalyzerReleases.Unshipped.md, locations/severity,
  code-fix tests and docs. New diagnostics need both sections.
- Public diagnostic URLs are https://iron-mapper.ru/diagnostics/imxxxx/ (lowercase, trailing slash), Russian under
  /ru/. DiagnosticHelpLinkTests checks links/source sections. These addresses are a compatibility contract.
- README Quick Start stays a single csharp block between `<!-- quickstart:begin -->` and `<!-- quickstart:end -->`,
  a complete Program.cs. verify-package compiles/runs both languages; the site extracts the same block. No fragments
  or independently maintained generated site copy. README/package links must work on nuget.org.
- Site content derives from docs/README/CHANGELOG/results: edit source, not generated pages. CHANGELOG is English
  with the site's explicit untranslated notice; no separate RU changelog obligation.
- Before releases update site/pages/roadmap.md and site/pages/ru/roadmap.md and dates. Do not rename IronMapper from
  stale roadmap text: accepted 28.10.2026 decision retains the name. Preserve version notes for SDK/implicit usings.
- CLAUDE.md is detailed reference, not authority to revive superseded examples. If an explicit accepted decision
  cannot resolve a contract ambiguity, ask. Do not fix product docs during an instruction-only migration.
