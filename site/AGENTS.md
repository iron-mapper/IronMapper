# Website

Read root, ../docs/AGENTS.md and README.md here. Astro Starlight builds static English / and Russian /ru/ pages.
No analytics or third-party scripts; search is Pagefind.

- scripts/prepare.mjs runs before dev/build and generates src/content/docs, src/generated and public/og from docs,
  README Quick Start, CHANGELOG, benchmark summary/methodology and site/pages. Edit original sources, not generated
  files. Preserve EN/RU, title, diagnostic heading and link contracts from the docs guide.
- project.config.mjs centralizes library name, addresses, packages and homeMetrics selection. Values come only from
  ../benchmarks/results/summary.json; src/lib/benchmarks.mjs transforms them without hardcoded numbers. Preserve
  schemaVersion validation, rejection of unknown metrics and null = not measured. Coordinate schema changes with producers.
- Preserve source methodology headings in benchmarks/README.md: Methodology and fairness rules, How to read the
  results, Caveats, What is not here, and why. Embed these sections, do not maintain duplicate methodology text.
- Preparation validates relative repository links/GitHub anchors, including site/pages inputs. Diagnostics intro
  and each `## IMxxxx — Title` become separate stable lowercase/trailing-slash helpLink pages. Withdrawn codes retain
  sections in both languages. Preserve source Edit page links and home editUrl:false.
- pages/index.mdx, benchmarks.mdx, roadmap.md and ru versions are hand-written inputs; distinguish generated outputs.
  Update both roadmap dates/content before release; preserve README marker extraction and untranslated CHANGELOG notice.
- Keep build-time English llms.txt, llms-full.txt and llms-small.txt from rendered pages, their reading order,
  summary.json metrics and link/existence checks. Do not create independent copies of product documentation there.
- Use Node from .nvmrc and locked dependencies. For authorized site changes use existing dependencies/task-authorized
  npm ci, then npm run build and npm run check-links in site/. These commands generate files and do not belong in
  read-only audits or this instruction-only migration.
- site.yml is path-filtered: relevant PRs build/check without deployment; relevant main pushes deploy GitHub Pages.
  Skipped checks are not evidence of a valid site. Do not make path-filtered site checks universally required.
  Preserve main-only publishing, domain and HTTPS; no unrelated deployment or hosting changes.
