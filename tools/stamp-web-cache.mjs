import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const controllerPath = resolve(root,'wwwroot/app.js');
const shellVersion = createHash('sha256').update(readFileSync(resolve(root,'wwwroot/generated/shell-features.js'))).digest('hex').slice(0,16);
const controller = readFileSync(controllerPath,'utf8');
const stampedController = controller.replace(/\/generated\/shell-features\.js(?:\?v=[a-f0-9]+)?/g, `/generated/shell-features.js?v=${shellVersion}`);
if (controller !== stampedController) writeFileSync(controllerPath,stampedController);
const assets = ['styles.css', 'app.js', 'generated/riffroster-web.js', 'generated/shell-features.js'];
const indexPath = resolve(root, 'wwwroot/index.html');
const indexSource = readFileSync(indexPath, 'utf8');
let index = indexSource;
for (const asset of assets) {
  const version = createHash('sha256').update(readFileSync(resolve(root, 'wwwroot', asset))).digest('hex').slice(0, 16);
  const escaped = asset.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  index = index.replace(new RegExp(`/${escaped}(?:\\?v=[a-f0-9]+)?(?=")`, 'g'), `/${asset}?v=${version}`);
}
if (index !== indexSource) writeFileSync(indexPath, index);
// Stamp the complete shell, not only the TypeScript entry. No private data enters this hash.
const hash = createHash('sha256');
for (const file of ['wwwroot/index.html', 'wwwroot/app.js', 'wwwroot/styles.css', 'wwwroot/generated/riffroster-web.js'])
  hash.update(readFileSync(resolve(root, file)));
const path = resolve(root, 'wwwroot/service-worker.js');
const originalSource = readFileSync(path, 'utf8');
let source = originalSource;
for (const asset of assets) {
  const version = createHash('sha256').update(readFileSync(resolve(root, 'wwwroot', asset))).digest('hex').slice(0, 16);
  const escaped = asset.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  source = source.replace(new RegExp(`/${escaped}(?:\\?v=[a-f0-9]+)?(?=')`, 'g'), `/${asset}?v=${version}`);
}
const marker = /const CACHE_NAME = '[^']+';/;
if (!marker.test(source)) throw new Error('Missing service worker cache marker.');
hash.update(source.replace(marker, ''));
const next = source.replace(marker, `const CACHE_NAME = 'riffroster-shell-${hash.digest('hex').slice(0, 16)}';`);
if (originalSource !== next) writeFileSync(path, next);
