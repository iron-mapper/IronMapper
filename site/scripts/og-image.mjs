// Renders the Open Graph image (1200×630 PNG) from summary.json: the project name, the tagline and the metric tiles
// of the home page (project.config.mjs → homeMetrics), so the preview card of a shared link carries the same numbers
// as the page. SVG drawn here, rasterized with sharp (a dependency of Astro). Called by prepare.mjs.

import sharp from 'sharp';
import { project } from '../project.config.mjs';
import { model, homeMetrics } from '../src/lib/benchmarks.mjs';

const esc = (s) => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

export async function renderOgImage(summary, file) {
  const m = model(summary);
  const tiles = homeMetrics(m, project.homeMetrics.slice(0, 4), 'en');
  const W = 1200, H = 630, pad = 64;
  const tileW = (W - pad * 2 - 24 * 3) / 4;
  const tileY = 330, tileH = 170;
  const host = project.site.replace(/^https?:\/\//, '') + (project.base || '');

  const tilesSvg = tiles.map((t, i) => {
    const x = pad + i * (tileW + 24);
    const value = t.value ?? '—';
    const size = value.length > 6 ? 40 : 48;
    return `
      <rect x="${x}" y="${tileY}" width="${tileW}" height="${tileH}" rx="12" fill="#1f2937" stroke="#334155"/>
      <text x="${x + 24}" y="${tileY + 78}" font-size="${size}" font-weight="700" fill="#f1f5f9">${esc(value)}</text>
      ${wrap(t.label, 22).slice(0, 2).map((line, j) => `<text x="${x + 24}" y="${tileY + 112 + j * 24}" font-size="18" fill="#cbd5e1">${esc(line)}</text>`).join('')}`;
  }).join('');

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="DejaVu Sans, Segoe UI, Helvetica, Arial, sans-serif">
    <rect width="${W}" height="${H}" fill="#0f172a"/>
    <rect x="0" y="0" width="${W}" height="8" fill="#4a7fb5"/>
    <text x="${pad}" y="150" font-size="84" font-weight="700" fill="#f8fafc">${esc(project.name)}</text>
    ${wrap(project.tagline.en, 70).slice(0, 2).map((line, j) => `<text x="${pad}" y="${212 + j * 38}" font-size="28" fill="#cbd5e1">${esc(line)}</text>`).join('')}
    ${tilesSvg}
    <text x="${pad}" y="${H - 44}" font-size="20" fill="#94a3b8">Benchmarks ${esc(m.runLabel)} · ${esc(m.runDate)} · vs hand-written code · ${esc((m.runtime ?? '').split(' (')[0])}</text>
    <text x="${W - pad}" y="${H - 44}" font-size="20" fill="#94a3b8" text-anchor="end">${esc(host)}</text>
  </svg>`;

  await sharp(Buffer.from(svg)).png().toFile(file);
}

/** Greedy word wrap at about `max` characters per line. */
function wrap(text, max) {
  const words = String(text ?? '').split(/\s+/);
  const lines = [];
  let line = '';
  for (const w of words) {
    if ((line + ' ' + w).trim().length > max && line) { lines.push(line); line = w; }
    else line = (line + ' ' + w).trim();
  }
  if (line) lines.push(line);
  return lines;
}
