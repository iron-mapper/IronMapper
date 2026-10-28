#!/usr/bin/env node
// Checks the built site in dist/: every internal link (href under the site's base) points to a page or file that
// exists, and every `#anchor` on an internal link exists on the target page. External links are not fetched.
// Run after `npm run build`; exits with 1 and lists the broken links.

import { readFileSync, readdirSync, existsSync, statSync } from 'node:fs';
import { dirname, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { project } from '../project.config.mjs';

const siteDir = dirname(dirname(fileURLToPath(import.meta.url)));
const dist = join(siteDir, 'dist');
const base = project.base.replace(/\/+$/, '');

if (!existsSync(dist)) {
  console.error('check-links: dist/ does not exist, run `npm run build` first');
  process.exit(1);
}

const htmlFiles = [];
(function walk(dir) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) walk(p);
    else if (e.name.endsWith('.html')) htmlFiles.push(p);
  }
})(dist);

const idsCache = new Map();
function idsOf(file) {
  if (!idsCache.has(file)) {
    const ids = new Set();
    for (const m of readFileSync(file, 'utf8').matchAll(/\s(?:id|name)="([^"]+)"/g)) ids.add(m[1]);
    idsCache.set(file, ids);
  }
  return idsCache.get(file);
}

/** dist file for a site path, or null. */
function fileFor(path) {
  const rel = path.slice(base.length).replace(/^\//, '');
  const candidates = rel === '' ? ['index.html'] : [rel, join(rel, 'index.html'), `${rel}.html`];
  for (const c of candidates) {
    const f = join(dist, c);
    if (existsSync(f) && statSync(f).isFile()) return f;
  }
  return null;
}

const broken = [];
let checked = 0;
for (const file of htmlFiles) {
  const html = readFileSync(file, 'utf8');
  const page = '/' + relative(dist, file).split(sep).join('/');
  for (const m of html.matchAll(/href="([^"]*)"/g)) {
    const href = m[1].replace(/&amp;/g, '&');
    if (/^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith('//')) continue;
    let target;
    let anchor = '';
    if (href.startsWith('#')) {
      target = file;
      anchor = href.slice(1);
    } else {
      const [path, a] = href.split('#', 2);
      anchor = a ?? '';
      const abs = path.startsWith('/') ? path : new URL(path, `http://x${page}`).pathname;
      if (!abs.startsWith(`${base}/`) && abs !== base) {
        broken.push(`${page}: link outside the site base: ${href}`);
        continue;
      }
      target = fileFor(decodeURIComponent(abs));
      if (!target) {
        broken.push(`${page}: no page for ${href}`);
        continue;
      }
    }
    checked++;
    if (anchor && target.endsWith('.html') && !idsOf(target).has(decodeURIComponent(anchor))) {
      broken.push(`${page}: no element #${anchor} on ${href}`);
    }
  }
}

// /llms.txt and /llms-full.txt (starlight-llms-txt): both exist, llms.txt names the project, and every absolute link
// into the site in llms.txt points to a page that exists.
let llmsLinks = 0;
for (const name of ['llms.txt', 'llms-full.txt']) {
  const file = join(dist, name);
  if (!existsSync(file) || statSync(file).size === 0) { broken.push(`/${name}: missing or empty`); continue; }
  const text = readFileSync(file, 'utf8');
  if (!text.startsWith(`# ${project.name}`) && !text.includes(`# ${project.name}\n`)) broken.push(`/${name}: does not start with "# ${project.name}"`);
  if (name === 'llms.txt') {
    for (const m of text.matchAll(/\]\((https?:\/\/[^)\s]+)\)/g)) {
      const url = new URL(m[1]);
      if (url.origin !== new URL(project.site).origin) continue;
      llmsLinks++;
      if (!fileFor(url.pathname)) broken.push(`/llms.txt: no page for ${m[1]}`);
    }
  }
}

if (broken.length) {
  console.error(`check-links: ${broken.length} broken link(s) in ${htmlFiles.length} pages:`);
  for (const b of broken) console.error('  ' + b);
  process.exit(1);
}
console.log(`check-links: ${checked} internal links in ${htmlFiles.length} pages and ${llmsLinks} site links in llms.txt are fine; llms-full.txt present`);
