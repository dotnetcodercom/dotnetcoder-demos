import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.dirname(fileURLToPath(import.meta.url));
const proof = JSON.parse(await readFile(path.join(root, 'evidence/proof.json'), 'utf8'));
assert.equal(proof.sdk, '11.0.100-rc.1.26425.128');
const required = ['before_warning', 'after_restore', 'after_build', 'after_run',
  'multitarget_restore', 'multitarget_build_net10.0', 'multitarget_build_net11.0'];
for (const result of required) assert.equal(proof.results[result], 'PASS', result);
for (const [relative, expected] of Object.entries(proof.sha256)) {
  const resolved = path.resolve(root, relative);
  assert.ok(resolved.startsWith(root + path.sep), 'Source hash path must stay in demo');
  const actual = createHash('sha256').update(await readFile(resolved)).digest('hex');
  assert.equal(actual, expected, relative + ' differs from the locally tested source');
}
const transcript = await readFile(path.join(root, 'evidence/windows-proof-excerpts.txt'), 'utf8');
assert.match(transcript, /warning NU1510:/);
assert.match(transcript, /PASS: NU1510 proof completed/);
console.log('PASS: saved user-supplied Windows proof is present and bound to the demo source');
console.log('AUDIT ONLY: no restore, build, or runtime execution was performed by this command');
