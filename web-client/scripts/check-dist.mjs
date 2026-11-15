// Cloudflare Pages rejects any single file over 25 MiB and deployments over 20,000 files.
import { readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
const LIMIT = 25 * 1024 * 1024;
let files = 0, total = 0, biggest = ['', 0];
const walk = (d) => readdirSync(d).forEach((n) => {
  const p = join(d, n); const s = statSync(p);
  if (s.isDirectory()) return walk(p);
  files++; total += s.size; if (s.size > biggest[1]) biggest = [p, s.size];
  if (s.size > LIMIT) { console.error(`over 25 MiB: ${p}`); process.exitCode = 1; }
});
walk('dist');
if (files > 20000) { console.error('too many files for Pages'); process.exitCode = 1; }
console.log(`dist: ${files} files, ${(total / 1048576).toFixed(1)} MiB, largest ${biggest[0]} ${(biggest[1] / 1024).toFixed(0)} KiB`);
