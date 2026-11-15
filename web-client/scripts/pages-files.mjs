// Writes Cloudflare Pages control files into dist/ after `vite build`:
//   _redirects : SPA fallback so deep links (/market/12, /open/<id>) load index.html
//   _headers   : security headers + CSP that only allows this site and the configured API origin (https + wss),
//                long-lived caching for hashed bundles, revalidation for data/i18n.
import { writeFileSync } from 'node:fs';

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
`);
console.log(`pages files written (connect-src ${connect})`);
