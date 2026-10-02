import {createRequire} from 'node:module';
import {mkdir, copyFile, writeFile, readFile} from 'node:fs/promises';
import {resolve, dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
const root = dirname(fileURLToPath(import.meta.url));
const versionRoot = process.cwd();
const require = createRequire(resolve(versionRoot, 'package.json'));
const {build} = require('esbuild');
const version = JSON.parse(await readFile(resolve(versionRoot, 'node_modules/@angular/core/package.json'))).version;
await mkdir(resolve(versionRoot, 'dist/images'), {recursive: true});
// Resolve imports from the version's own node_modules, using the same fixture bytes.
await copyFile(resolve(root, 'fixture.ts'), resolve(versionRoot, 'fixture.ts'));
await build({entryPoints: [resolve(versionRoot, 'fixture.ts')], bundle: true,
  format: 'esm', platform: 'browser', outfile: resolve(versionRoot, 'dist/main.js'),
  sourcemap: true, target: 'es2022', tsconfigRaw: {compilerOptions: {experimentalDecorators: true}}});
await writeFile(resolve(versionRoot, 'dist/index.html'), `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Angular ${version} srcset proof</title><style>body{font:16px system-ui;margin:32px;max-width:960px}section{display:inline-block;margin:12px;padding:16px;border:1px solid #ddd}img{display:block}pre{background:#f2f2f2;padding:20px;white-space:pre-wrap}</style></head><body><app-root></app-root><script type="module" src="main.js"></script></body></html>`);
for (const name of ['fallback.png','signed-a.png','signed-b.png','generated-100.png','generated-200.png'])
  await copyFile(resolve(root, 'images', name), resolve(versionRoot, 'dist/images', name));
console.log(`Built original Angular ${version}`);
