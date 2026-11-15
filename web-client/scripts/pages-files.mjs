// Writes Cloudflare Pages control files into dist/ after `vite build`:
//   _redirects : SPA fallback so deep links (/market/12, /open/<id>) load index.html
//   _headers   : security headers + CSP that only allows this site and the configured API origin (https + wss),
//                long-lived caching for hashed bundles, revalidation for data/i18n.
import { createHash } from 'node:crypto';
import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';

const apiBase = (process.env.VITE_API_BASE ?? '').replace(/\/$/, '');
let connect = "'self'";
if (apiBase) {
  const u = new URL(apiBase);
  const ws = (u.protocol === 'https:' ? 'wss://' : 'ws://') + u.host;
  connect = `'self' ${u.origin} ${ws}`;
}

const csp = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self' 'unsafe-inline'", // React style attributes (CSS variables for rarity/element colours)
  "img-src 'self' data:",
  "font-src 'self'",
  `connect-src ${connect}`,
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join('; ');

writeFileSync('dist/_redirects', '/* /index.html 200\n');
writeFileSync('dist/_headers', `/*
  Content-Security-Policy: ${csp}
  X-Content-Type-Options: nosniff
  Referrer-Policy: strict-origin-when-cross-origin
  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()
  X-Frame-Options: DENY

/assets/*
  Cache-Control: public, max-age=31536000, immutable

/art/*
  Cache-Control: public, max-age=604800

/data/*
  Cache-Control: public, max-age=0, must-revalidate

/i18n/*
  Cache-Control: public, max-age=0, must-revalidate

/sw.js
  Cache-Control: no-cache

/manifest.webmanifest
  Content-Type: application/manifest+json
  Cache-Control: public, max-age=0, must-revalidate
`);
console.log(`pages files written (connect-src ${connect})`);

// ---- service worker: precache list = everything the shell needs (not the 400 pet images) ----
const files = [];
const walk = (d) => readdirSync(d).forEach((n) => { const p = join(d, n); statSync(p).isDirectory() ? walk(p) : files.push('/' + relative('dist', p).split('\\').join('/')); });
walk('dist');
const precache = files.filter((f) => f === '/index.html' || f === '/manifest.webmanifest' || /^\/(assets|data|i18n|icons|art\/ui)\//.test(f)).sort();
const version = createHash('sha256').update(precache.join('\n') + readFileSync('dist/index.html')).digest('hex').slice(0, 12);
writeFileSync('dist/sw.js', readFileSync('scripts/sw-template.js', 'utf8').replace('__VERSION__', version).replace('__PRECACHE__', JSON.stringify(precache)));
console.log(`sw.js written: version ${version}, ${precache.length} precached files`);
