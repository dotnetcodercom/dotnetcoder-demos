import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = path.dirname(fileURLToPath(import.meta.url));
const local = JSON.parse(await readFile(path.join(root, 'evidence/local-checks.json'), 'utf8'));
const proof = JSON.parse(await readFile(path.join(root, 'evidence/laptop-verification.json'), 'utf8'));
assert.equal(proof.status, 'PASS');
assert.equal(proof.sql_server_version, '17.0.1000.7');
assert.deepEqual(proof.checks.existing_optional_int, [2,3,4,5]);
assert.deepEqual(proof.checks.missing_optional_int, [1,6,8]);
assert.deepEqual(proof.checks.nested_flag, [6]);
assert.deepEqual(proof.checks.nested_path, [6,8]);
assert.deepEqual(proof.checks.different_case, []);
assert.deepEqual(proof.checks.sql_null_document, [7]);
assert.equal(proof.checks.transaction_rollback, 'PASS');
assert.equal(proof.checks.final_laptop_verification, 'PASS');
for (const [relative, expected] of Object.entries(local.source_sha256)) {
  const file = path.resolve(root, relative);
  assert.ok(file.startsWith(root + path.sep));
  assert.equal(createHash('sha256').update(await readFile(file)).digest('hex'), expected, relative);
}
const audit = JSON.parse(await readFile(path.join(root, 'evidence/screenshot-qa.json'), 'utf8'));
assert.equal(audit.screenshots.length, 3);
for (const screenshot of audit.screenshots) {
  assert.equal(screenshot.pixel_equality, 'PASS');
  assert.equal(createHash('sha256').update(await readFile(path.join(root, screenshot.output))).digest('hex'), screenshot.output_sha256);
}
console.log('PASS: saved laptop proof, exact result matrix, source binding and three screenshots verified');
console.log('AUDIT ONLY: no restore, build or SQL Server execution performed by this command');
