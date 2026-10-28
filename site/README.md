# Site

The project website: Astro + [Starlight](https://starlight.astro.build/), static, published to GitHub Pages at
[iron-mapper.ru](https://iron-mapper.ru). English at `/`, Russian at `/ru/`.

```bash
cd site
npm ci                # Node 22 (see .nvmrc)
npm run dev           # http://localhost:4321/ with live reload
npm run build         # the same build as CI → dist/
npm run check-links   # after a build: internal links and anchors in dist/
```

## Where the content comes from

Nothing on the site is written twice. `scripts/prepare.mjs` runs before `dev` and `build` and generates
`src/content/docs/` (git-ignored) from:

| Source in the repository | Page |
|---|---|
| `docs/<name>.md`, `docs/<name>.ru.md` | `/<name>/`, `/ru/<name>/` |
| `docs/diagnostics.md`, `.ru.md` | `/diagnostics/` (the intro, codes of its table linked) and one page per `## IMxxxx — Title`: `/diagnostics/imxxxx/`, the address for `helpLinkUri` |
| `CHANGELOG.md` | `/changelog/` (English; the Russian locale shows it with a "not translated" notice) |
| `README.md`, `README.ru.md`: the block between `<!-- quickstart:begin -->` and `<!-- quickstart:end -->` | the Quick Start on the home page (`src/generated/quickstart.*.md`) |
| `benchmarks/results/summary.json` (written by `benchmarks/scripts/generate-report.ps1`) | the metric tiles of the home page, every table and chart of `/benchmarks/`, the Open Graph image `public/og/og.png` |
| `benchmarks/README.md`: the sections Methodology, How to read the results, Caveats, What is not here | the second half of `/benchmarks/` (English; the Russian page summarizes and links) |
| `site/pages/**`: the hand-written pages, home (`index.mdx`), `benchmarks.mdx`, `roadmap.md`, with their `ru/` versions | `/`, `/benchmarks/`, `/roadmap/`, … |

The script takes the `# Title` of a doc as the page title, drops the language banner, rewrites relative links
(`configuration.md#anchor` → `/configuration/#anchor`, `diagnostics.md#im0002--…` → `/diagnostics/im0002/`,
`../CHANGELOG.md` → `/changelog/`, other repository files → GitHub) and fails the build when a link points to a missing file or heading, when a doc exists in one language only,
or when the format contract in `CLAUDE.md` ("Site") is broken. Links in `site/pages/` are written as relative paths to
the repository files (`../../docs/configuration.md`), so they are checked the same way. Every generated page carries an
"Edit page" link to its source file on GitHub (a visitor without write access is offered a fork and a pull request, as
on GitHub itself); the hand-written home pages set `editUrl: false`.

## Benchmark numbers

`src/lib/benchmarks.mjs` turns `summary.json` into what the pages show and holds no number itself. The build stops when
the file's `schemaVersion` is not the one it reads (`SUMMARY_SCHEMA_VERSION` in `scripts/prepare.mjs`), so a format
change in the suite cannot silently render wrong numbers. Which metric tiles the home page shows is the list
`homeMetrics` in `project.config.mjs` (keys of `metrics` in `benchmarks.mjs`); an unknown key fails the build. A value
the run did not measure is `null` in the JSON and "not measured" on the page.

## Name and addresses

`project.config.mjs` is the only place with the library's name, GitHub organization, NuGet package names and the
site address (`site` = `https://iron-mapper.ru`, `base` = `''`; on `<owner>.github.io/<repo>/` it would be
`'/<repo>'`). The texts in `docs/`, README and CHANGELOG are the source of truth and are changed in the repository,
not by the site.

## Layout

```
site/
├── astro.config.mjs        Starlight: locales, sidebar, theme
├── project.config.mjs      name, addresses, packages
├── pages/                  hand-written pages (home, benchmarks, roadmap), en and ru/
├── public/                 favicon; og/ is generated (git-ignored)
├── scripts/prepare.mjs     repository → src/content/docs, src/generated, public/og (runs before dev/build)
├── scripts/og-image.mjs    Open Graph image from summary.json
├── scripts/check-links.mjs
└── src/
    ├── content.config.ts
    ├── components/         Metrics, SummaryTable, ScenarioTable, Charts, BarChart, RunInfo
    ├── lib/benchmarks.mjs  summary.json → values, metric definitions
    ├── styles/custom.css
    ├── content/docs/       generated, git-ignored
    └── generated/          generated, git-ignored
```

## Publishing

`.github/workflows/site.yml`: a push to `main` that changes `site/**`, `docs/**`, `benchmarks/results/**`,
`benchmarks/README.md`, `CHANGELOG.md`, the READMEs or the workflow runs `npm ci`, `npm run build`, `npm run check-links`
and deploys `site/dist` to GitHub Pages (repository settings: Pages → Source = GitHub Actions, custom domain
`iron-mapper.ru`, Enforce HTTPS). A pull request with the same paths gets the build and the link check only. Nothing
is deployed from any other branch; a broken link or a contract violation fails the build before the deploy.

## llms.txt

For AI tools a user points at the site, [`starlight-llms-txt`](https://github.com/delucis/starlight-llms-txt) (pinned
in `package.json`, configured in `astro.config.mjs`) writes three files at build time, English only:
`/llms.txt` (the project in a paragraph and links to the key pages), `/llms-full.txt` (every English page as Markdown
without the site navigation, in reading order: home, guides, diagnostics, benchmarks, then release notes) and
`/llms-small.txt` (the same with asides and details collapsed). They are built from the rendered pages, so the
benchmark numbers in them are the `summary.json` values like everywhere else. `npm run check-links` checks that the
first two exist and that every site link in `llms.txt` has a page.

No analytics and no third-party scripts; search is Pagefind, a static index built with the site. Dependencies are
pinned in `package-lock.json` and updated about once a quarter.
