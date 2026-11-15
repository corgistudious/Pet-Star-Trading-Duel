/* Pet Star service worker (generated at build time by scripts/pages-files.mjs; do not edit dist/sw.js).
 * - Precaches the app shell (index.html, hashed bundles, catalog, i18n, UI art, icons) for fast start and an
 *   offline shell. The game itself needs the server: API calls are never cached or answered offline.
 * - Pet artwork is cached on first view (cache-first, immutable file names per art version).
 * - Only same-origin GET requests are handled; the API is on another origin and always goes to the network.
 */
const VERSION = '__VERSION__';
const SHELL = `petstar-shell-${VERSION}`;
const ART = 'petstar-art-v1';
const PRECACHE = __PRECACHE__;
// Hosts may send `Vary: Origin`; module scripts are requested with an Origin header, so ignore Vary when matching.
const MATCH = { ignoreVary: true };

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(SHELL).then((c) => c.addAll(PRECACHE)));
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    for (const k of await caches.keys()) if (k.startsWith('petstar-shell-') && k !== SHELL) await caches.delete(k);
    await self.clients.claim();
  })());
});

self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'SKIP_WAITING') self.skipWaiting();
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return; // API / WebSocket / other origins: untouched

  if (req.mode === 'navigate') {
    // network first so deploys show up immediately; cached shell when offline
    event.respondWith(fetch(req).catch(() => caches.match('/index.html', MATCH)));
    return;
  }
  if (url.pathname.startsWith('/art/pets/')) {
    event.respondWith(caches.open(ART).then(async (c) => {
      const hit = await c.match(req, MATCH);
      if (hit) return hit;
      const res = await fetch(req);
      if (res.ok) c.put(req, res.clone());
      return res;
    }));
    return;
  }
  if (url.pathname.startsWith('/data/') || url.pathname.startsWith('/i18n/')) {
    // stale-while-revalidate: game data and texts update without a new deploy of the shell
    event.respondWith(caches.open(SHELL).then(async (c) => {
      const hit = await c.match(req, MATCH);
      const net = fetch(req).then((res) => { if (res.ok) c.put(req, res.clone()); return res; }).catch(() => hit);
      return hit || net;
    }));
    return;
  }
  event.respondWith(caches.match(req, MATCH).then((hit) => hit || fetch(req)));
});
