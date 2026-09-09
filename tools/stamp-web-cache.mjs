import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
// Stamp the complete shell, not only the TypeScript entry. No private data enters this hash.
const hash = createHash('sha256');
for (const file of ['wwwroot/index.html', 'wwwroot/app.js', 'wwwroot/styles.css', 'wwwroot/generated/riffroster-web.js'])
  hash.update(readFileSync(resolve(root, file)));
const path = resolve(root, 'wwwroot/service-worker.js');
const source = readFileSync(path, 'utf8');
const marker = /const CACHE_NAME = '[^']+';/;
if (!marker.test(source)) throw new Error('Missing service worker cache marker.');
hash.update(source.replace(marker, ''));
const next = source.replace(marker, `const CACHE_NAME = 'riffroster-shell-${hash.digest('hex').slice(0, 16)}';`);
if (source !== next) writeFileSync(path, next);
