#!/usr/bin/env node
// Builds the site's content from the repository: docs/*.md (+ *.ru.md), CHANGELOG.md, the README Quick Start and
// the hand-written pages in site/pages/. Runs before `astro dev` and `astro build` (see package.json). Writes only
// into site/src/content/docs/ and site/src/generated/ (both in .gitignore); never changes the sources.
//
// Contract with the repository (checked here; a violation fails the build with the file and line):
//   - every docs/<name>.md has a docs/<name>.ru.md and vice versa;
//   - a doc starts with exactly one `# Title` line (the page title) and has no other level-1 heading;
//   - an optional language banner `> 📖 **[Русская версия](<name>.ru.md)**` / `> 🌐 **[English version](<name>.md)**`
//     follows the title and is dropped (the site has a language switcher);
//   - README.md and README.ru.md contain the Quick Start between `<!-- quickstart:begin -->` and
//     `<!-- quickstart:end -->`, once;
//   - docs/diagnostics.md and docs/diagnostics.ru.md describe the same set of codes, one `## IMxxxx — Title` each;
//   - relative links between these files point to existing files and existing headings.
// Links are rewritten for the site: `configuration.md#anchor` → `<base>/configuration/#anchor`,
// `x.ru.md` → `<base>/ru/x/`, `../CHANGELOG.md` → `<base>/changelog/`, any other file of the repository →
// its page on GitHub. Heading anchors are the ones GitHub and Starlight both produce (github-slugger).

import { readFileSync, writeFileSync, mkdirSync, rmSync, readdirSync, existsSync } from 'node:fs';
import { dirname, join, posix, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import GithubSlugger from 'github-slugger';
import { project, blobUrl } from '../project.config.mjs';

const siteDir = dirname(dirname(fileURLToPath(import.meta.url)));
const repoDir = dirname(siteDir);
const contentDir = join(siteDir, 'src', 'content', 'docs');
const generatedDir = join(siteDir, 'src', 'generated');
const base = project.base.replace(/\/+$/, '');

const LOCALES = /** @type {const} */ (['en', 'ru']);
const CHANGELOG_SLUG = 'changelog';

/** @typedef {{ repoPath: string, locale: 'en' | 'ru', slug: string, lines: string[], headings: Set<string> }} Page */

class ContractError extends Error {}

function fail(file, line, message) {
  const where = line ? `${file}:${line}` : file;
  throw new ContractError(`${where}: ${message}`);
}

function readRepo(repoPath) {
  const full = join(repoDir, repoPath);
  if (!existsSync(full)) fail(repoPath, 0, 'file does not exist');
  return readFileSync(full, 'utf8').replace(/\r\n/g, '\n');
}

// ---------------------------------------------------------------------------------------------------------------------
// Markdown helpers
// ---------------------------------------------------------------------------------------------------------------------

/** Calls `fn(line, index, inFence)` for every line; fences are ``` or ~~~ blocks. */
function eachLine(lines, fn) {
  let fence = null;
  lines.forEach((line, i) => {
    const m = line.match(/^(\s*)(`{3,}|~{3,})/);
    if (m && (fence === null || line.trim().startsWith(fence))) {
      const wasInFence = fence !== null;
      fence = wasInFence ? null : m[2];
      fn(line, i, true);
      return;
    }
    fn(line, i, fence !== null);
  });
}

/** Text of a heading as Starlight's rehype-slug sees it: inline code, emphasis and link markup removed. */
function headingText(markdown) {
  return markdown
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/`([^`]*)`/g, '$1')
    .replace(/(\*\*|__)(.*?)\1/g, '$2')
    .replace(/(\*|_)(.*?)\1/g, '$2')
    .trim();
}

/** Anchor ids of the headings of a markdown text, in order, numbered like github-slugger does for duplicates. */
function collectHeadings(lines) {
  const slugger = new GithubSlugger();
  const set = new Set();
  eachLine(lines, (line, _i, inFence) => {
    if (inFence) return;
    const m = line.match(/^#{1,6}\s+(.*?)\s*#*\s*$/);
    if (m) set.add(slugger.slug(headingText(m[1])));
  });
  return set;
}

/** Repository path of a link target relative to the file that contains it (posix). */
function resolveTarget(fromRepoPath, target) {
  return posix.normalize(posix.join(posix.dirname(fromRepoPath), target));
}

// ---------------------------------------------------------------------------------------------------------------------
// Pages of the site
// ---------------------------------------------------------------------------------------------------------------------

/** Which repository files become pages, and where. Returns {locale, slug} or null (→ link to GitHub). */
function pageFor(repoPath) {
  let m = repoPath.match(/^docs\/([a-z0-9.-]+?)(\.ru)?\.md$/);
  if (m) return { locale: m[2] ? 'ru' : 'en', slug: m[1] };
  if (repoPath === 'CHANGELOG.md') return { locale: 'en', slug: CHANGELOG_SLUG };
  if (repoPath === 'README.md') return { locale: 'en', slug: '' };
  if (repoPath === 'README.ru.md') return { locale: 'ru', slug: '' };
  m = repoPath.match(/^site\/pages\/(ru\/)?([a-z0-9.-]+)\.mdx?$/);
  if (m) return { locale: m[1] ? 'ru' : 'en', slug: m[2] === 'index' ? '' : m[2] };
  return null;
}

function sitePath(locale, slug, anchor = '') {
  const prefix = locale === 'ru' ? '/ru' : '';
  const path = slug ? `${base}${prefix}/${slug}/` : `${base}${prefix}/`;
  return path + anchor;
}

/** Heading ids of a repository file (cached), for link validation. */
const headingCache = new Map();
function headingsOf(repoPath) {
  if (!headingCache.has(repoPath)) headingCache.set(repoPath, collectHeadings(readRepo(repoPath).split('\n')));
  return headingCache.get(repoPath);
}

/**
 * Rewrites the link targets of a markdown file for the site and validates the internal ones.
 * `ownHeadings` are the ids of the file itself (for `#anchor` links).
 */
function rewriteLinks(repoPath, lines, ownHeadings, pages = false) {
  const out = [];
  eachLine(lines, (line, i, inFence) => {
    if (inFence) {
      out.push(line);
      return;
    }
    let result = '';
    let last = 0;
    // Inline links and images `](target)`; in the hand-written pages also `href="target"` of components and
    // `link: target` of the hero frontmatter. Matches inside inline code are skipped.
    const re = pages
      ? /(\]\(|href="|^\s*link:\s+)([^)\s"]+)(\s+"[^"]*")?(\)|"|$)/g
      : /(\]\()([^)\s]+)(\s+"[^"]*")?(\))/g;
    let m;
    while ((m = re.exec(line)) !== null) {
      const before = line.slice(0, m.index);
      const inInlineCode = (before.split('`').length - 1) % 2 === 1;
      if (inInlineCode) continue;
      const target = m[2];
      const rewritten = rewriteTarget(repoPath, i + 1, target, ownHeadings);
      if (rewritten !== target) {
        result += line.slice(last, m.index) + `${m[1]}${rewritten}${m[3] ?? ''}${m[4]}`;
        last = m.index + m[0].length;
      }
    }
    out.push(result + line.slice(last));
  });
  return out;
}

function rewriteTarget(repoPath, lineNo, target, ownHeadings) {
  if (/^[a-z][a-z0-9+.-]*:/i.test(target) || target.startsWith('//')) return target; // http:, mailto:, …
  const [path, anchorPart] = target.split('#', 2);
  const anchor = anchorPart === undefined ? '' : `#${anchorPart}`;
  if (path === '') {
    if (!ownHeadings.has(anchorPart)) fail(repoPath, lineNo, `link to a heading that does not exist: ${target}`);
    return target;
  }
  if (path.startsWith('/')) fail(repoPath, lineNo, `absolute path in a link, use a relative one: ${target}`);
  const targetRepoPath = resolveTarget(repoPath, path);
  if (targetRepoPath.startsWith('../')) fail(repoPath, lineNo, `link leaves the repository: ${target}`);
  if (!existsSync(join(repoDir, targetRepoPath))) fail(repoPath, lineNo, `link to a file that does not exist: ${target}`);
  const page = pageFor(targetRepoPath);
  if (!page) return blobUrl(targetRepoPath) + anchor;
  if (anchorPart !== undefined && /\.md$/.test(targetRepoPath) && !headingsOf(targetRepoPath).has(anchorPart)) {
    fail(repoPath, lineNo, `link to a heading that does not exist in ${targetRepoPath}: #${anchorPart}`);
  }
  return sitePath(page.locale, page.slug, anchor);
}

function frontmatter(fields) {
  const yaml = Object.entries(fields)
    .filter(([, v]) => v !== undefined)
    .map(([k, v]) => `${k}: ${typeof v === 'string' ? JSON.stringify(v) : v}`)
    .join('\n');
  return `---\n${yaml}\n---\n`;
}

function pageSlug(locale, slug) {
  return locale === 'ru' ? `ru/${slug}` : slug;
}

function writePage(locale, slug, text) {
  const file = join(contentDir, locale === 'ru' ? 'ru' : '', slug ? `${slug}.md` : 'index.md');
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, text);
  return relative(siteDir, file).split(sep).join('/');
}

// ---------------------------------------------------------------------------------------------------------------------
// docs/*.md
// ---------------------------------------------------------------------------------------------------------------------

/** Takes the title out of a doc, drops the language banner, checks the heading contract. Returns {title, body}. */
function splitDoc(repoPath, lines) {
  let titleLine = -1;
  const h1s = [];
  eachLine(lines, (line, i, inFence) => {
    if (!inFence && /^#\s/.test(line)) h1s.push(i);
  });
  const firstText = lines.findIndex((l) => l.trim() !== '');
  if (h1s.length === 0 || h1s[0] !== firstText) fail(repoPath, firstText + 1, 'the first line must be the `# Title` of the page');
  if (h1s.length > 1) fail(repoPath, h1s[1] + 1, 'a second level-1 heading; a page has one title, use `##` for sections');
  titleLine = h1s[0];
  const title = headingText(lines[titleLine].replace(/^#\s+/, ''));
  const body = lines.slice(titleLine + 1);
  // Language banner: a blockquote right after the title that links to the other language.
  const bannerIndex = body.findIndex((l) => l.trim() !== '');
  if (bannerIndex >= 0 && /^>\s.*\[(Русская версия|English version)\]\(/.test(body[bannerIndex])) {
    body.splice(bannerIndex, 1);
  }
  return { title, body };
}

function prepareDocs() {
  const docsDir = join(repoDir, 'docs');
  const files = readdirSync(docsDir).filter((f) => f.endsWith('.md'));
  const names = new Set(files.map((f) => f.replace(/(\.ru)?\.md$/, '')));
  const written = [];
  for (const name of [...names].sort()) {
    for (const locale of LOCALES) {
      const repoPath = `docs/${name}${locale === 'ru' ? '.ru' : ''}.md`;
      if (!existsSync(join(repoDir, repoPath))) {
        fail(repoPath, 0, `missing: every doc exists in both languages (docs/${name}.md and docs/${name}.ru.md)`);
      }
      const lines = readRepo(repoPath).split('\n');
      const { title, body } = splitDoc(repoPath, lines);
      const rewritten = rewriteLinks(repoPath, body, headingsOf(repoPath));
      // `slug` keeps the file name as the address (Astro would turn `upgrading-to-2.1` into `upgrading-to-21`).
      const text = frontmatter({ title, slug: pageSlug(locale, name), editUrl: blobUrl(repoPath) }) + rewritten.join('\n');
      written.push(writePage(locale, name, text));
    }
  }
  checkDiagnosticsContract();
  return written;
}

/** Both diagnostics docs describe the same codes, one `## IMxxxx — Title` section each. */
function checkDiagnosticsContract() {
  const codesOf = (repoPath) => {
    const codes = [];
    eachLine(readRepo(repoPath).split('\n'), (line, i, inFence) => {
      if (inFence || !/^##\s/.test(line)) return;
      const m = line.match(/^##\s+(IM\d{4})\s+—\s+\S/);
      if (!m) fail(repoPath, i + 1, 'every `##` heading of the diagnostics doc must be `## IMxxxx — Title`');
      if (codes.includes(m[1])) fail(repoPath, i + 1, `${m[1]} is described twice`);
      codes.push(m[1]);
    });
    return codes;
  };
  const en = codesOf('docs/diagnostics.md');
  const ru = codesOf('docs/diagnostics.ru.md');
  if (en.join() !== ru.join()) {
    fail('docs/diagnostics.ru.md', 0, `codes differ from docs/diagnostics.md: en [${en}] vs ru [${ru}]`);
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// CHANGELOG.md, README Quick Start, site/pages
// ---------------------------------------------------------------------------------------------------------------------

function prepareChangelog() {
  const repoPath = 'CHANGELOG.md';
  const lines = readRepo(repoPath).split('\n');
  const { title, body } = splitDoc(repoPath, lines);
  const rewritten = rewriteLinks(repoPath, body, headingsOf(repoPath));
  // English only: the Russian locale shows this page with Starlight's "not translated" notice.
  return [writePage('en', CHANGELOG_SLUG, frontmatter({ title, slug: CHANGELOG_SLUG, editUrl: blobUrl(repoPath) }) + rewritten.join('\n'))];
}

/** The Quick Start of the README: the fenced block between the markers, as a markdown file to import. */
function prepareQuickStart() {
  mkdirSync(generatedDir, { recursive: true });
  const written = [];
  for (const locale of LOCALES) {
    const repoPath = locale === 'ru' ? 'README.ru.md' : 'README.md';
    const text = readRepo(repoPath);
    const begin = text.split('<!-- quickstart:begin -->');
    if (begin.length !== 2) fail(repoPath, 0, 'expected exactly one `<!-- quickstart:begin -->` marker');
    const end = begin[1].split('<!-- quickstart:end -->');
    if (end.length !== 2) fail(repoPath, 0, 'expected exactly one `<!-- quickstart:end -->` marker after begin');
    const block = end[0].trim();
    if (!/^```csharp\n[\s\S]*\n```$/.test(block)) fail(repoPath, 0, 'the Quick Start between the markers must be one ```csharp block');
    const file = join(generatedDir, `quickstart.${locale}.md`);
    writeFileSync(file, `---\ntitle: Quick Start\n---\n${block}\n`);
    written.push(relative(siteDir, file).split(sep).join('/'));
  }
  return written;
}

/** Hand-written pages of the site (home, roadmap): copied with the same link rewriting as the docs. */
function preparePages() {
  const pagesDir = join(siteDir, 'pages');
  const written = [];
  const walk = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (/\.mdx?$/.test(entry.name)) {
        const repoPath = relative(repoDir, full).split(sep).join('/');
        const page = pageFor(repoPath);
        if (!page) fail(repoPath, 0, 'unexpected file in site/pages (lowercase name, .md or .mdx, optionally under ru/)');
        const lines = readRepo(repoPath).split('\n');
        const rewritten = rewriteLinks(repoPath, lines, collectHeadings(lines), true);
        const target = join(contentDir, page.locale === 'ru' ? 'ru' : '', page.slug ? `${page.slug}${entry.name.slice(entry.name.lastIndexOf('.'))}` : entry.name);
        mkdirSync(dirname(target), { recursive: true });
        writeFileSync(target, rewritten.join('\n'));
        written.push(relative(siteDir, target).split(sep).join('/'));
      }
    }
  };
  walk(pagesDir);
  return written;
}

// ---------------------------------------------------------------------------------------------------------------------

try {
  rmSync(contentDir, { recursive: true, force: true });
  rmSync(generatedDir, { recursive: true, force: true });
  mkdirSync(contentDir, { recursive: true });
  const written = [...prepareDocs(), ...prepareChangelog(), ...prepareQuickStart(), ...preparePages()];
  console.log(`prepare: ${written.length} files written to site/src/ from docs/, CHANGELOG.md, README and site/pages/`);
} catch (e) {
  if (e instanceof ContractError) {
    console.error(`prepare: ${e.message}`);
    process.exit(1);
  }
  throw e;
}
