// Trims the preview build to what it uses (art, data, i18n, brand icons) and renames preview.html to index.html,
// so the folder can be opened from any static host. Fails if a referenced asset folder is missing.
import { existsSync, readdirSync, renameSync, rmSync, statSync } from 'node:fs';
import { join } from 'node:path';

const out = 'dist-preview';
renameSync(join(out, 'preview.html'), join(out, 'index.html'));
const keep = new Set(['index.html', 'assets', 'art', 'data', 'i18n', 'brand']);
for (const f of readdirSync(out)) if (!keep.has(f)) rmSync(join(out, f), { recursive: true, force: true });
rmSync(join(out, 'art/pets/thumb'), { recursive: true, force: true }); // full-size art only (VITE_NO_THUMBS)
for (const need of ['art/pets', 'art/ui', 'data/catalog.json', 'i18n/vi.json', 'i18n/en.json']) {
  if (!existsSync(join(out, need))) { console.error('preview: missing ' + need); process.exit(1); }
}
let files = 0, bytes = 0;
const walk = (d) => { for (const f of readdirSync(d)) { const p = join(d, f); const s = statSync(p); if (s.isDirectory()) walk(p); else { files++; bytes += s.size; } } };
walk(out);
console.log(`preview: ${files} files, ${(bytes / 1048576).toFixed(1)} MiB in ${out}/`);
