import {readFile, writeFile, readdir} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {buildReport} from './report-en.mjs';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const dirIndex = process.argv.indexOf('--dir');
const directory = dirIndex >= 0 ? path.resolve(process.argv[dirIndex + 1]) : path.join(root, 'artifacts', 'latest');
try {
  const summary = JSON.parse(await readFile(path.join(directory, 'verification.json'), 'utf8'));
  const results = [];
  for (const name of await readdir(directory)) {
    if (!/^(before|after)-(blocks|seed|download-(low|balanced|wide))\.json$/.test(name)) continue;
    results.push(JSON.parse(await readFile(path.join(directory, name), 'utf8')));
  }
  const passed = results.flatMap(r => r.checks ?? []).filter(c => c.pass === true).length;
  if (results.length !== summary.results || passed !== summary.passedChecks) throw new Error('Receipt counts do not match verification.json. Send the complete results folder.');
  if (summary.status === 'PASS' && results.some(r => r.status !== 'PASS')) throw new Error('A receipt contradicts the PASS summary.');
  await writeFile(path.join(directory, 'report.html'), buildReport(summary, results), 'utf8');
  console.log(`English report generated from saved receipts. No tests were rerun.\nStatus: ${summary.status}; checks: ${summary.passedChecks}; cross-version gates: ${summary.crossVersionChecks?.length ?? 0}\nReport: ${path.join(directory, 'report.html')}`);
} catch (error) {
  console.error(`Could not refresh report: ${error.message}`);
  process.exitCode = 1;
}
