# Site

The project website: Astro + [Starlight](https://starlight.astro.build/), static, published to GitHub Pages.
English at `/`, Russian at `/ru/`.

```bash
cd site
npm ci                # Node 22 (see .nvmrc)
npm run dev           # http://localhost:4321/IronMapper/ with live reload
npm run build         # the same build as CI → dist/
npm run check-links   # after a build: internal links and anchors in dist/
```

## Where the content comes from

Nothing on the site is written twice. `scripts/prepare.mjs` runs before `dev` and `build` and generates
`src/content/docs/` (git-ignored) from:

| Source in the repository | Page |
|---|---|
| `docs/<name>.md`, `docs/<name>.ru.md` | `/<name>/`, `/ru/<name>/` |
| `CHANGELOG.md` | `/changelog/` (English; the Russian locale shows it with a "not translated" notice) |
| `README.md`, `README.ru.md`: the block between `<!-- quickstart:begin -->` and `<!-- quickstart:end -->` | the Quick Start on the home page (`src/generated/quickstart.*.md`) |
| `site/pages/**`: the hand-written pages, home (`index.mdx`) and `roadmap.md`, with their `ru/` versions | `/`, `/roadmap/`, … |

The script takes the `# Title` of a doc as the page title, drops the language banner, rewrites relative links
(`configuration.md#anchor` → `/configuration/#anchor`, `../CHANGELOG.md` → `/changelog/`, other repository files →
GitHub) and fails the build when a link points to a missing file or heading, when a doc exists in one language only,
or when the format contract in `CLAUDE.md` ("Site") is broken. Links in `site/pages/` are written as relative paths to
the repository files (`../../docs/configuration.md`), so they are checked the same way.

## Name and addresses

`project.config.mjs` is the only place with the library's name, GitHub organization, NuGet package names and the
site address (`site` + `base`). When the library is renamed, change it there; the texts in `docs/`, README and
CHANGELOG are the source of truth and are changed in the repository, not by the site.

## Layout

```
site/
├── astro.config.mjs        Starlight: locales, sidebar, theme
├── project.config.mjs      name, addresses, packages
├── pages/                  hand-written pages (home, roadmap), en and ru/
├── public/                 favicon
├── scripts/prepare.mjs     repository → src/content/docs (runs before dev/build)
├── scripts/check-links.mjs
└── src/
    ├── content.config.ts
    ├── styles/custom.css
    ├── content/docs/       generated, git-ignored
    └── generated/          generated, git-ignored
```

No analytics and no third-party scripts; search is Pagefind, a static index built with the site. Dependencies are
pinned in `package-lock.json` and updated about once a quarter.
