import { spawn, spawnSync } from 'node:child_process';
import { mkdir, readFile, writeFile, copyFile, readdir, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import net from 'node:net';
import crypto from 'node:crypto';
import { buildReport } from './report-en.mjs';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const runId = crypto.randomBytes(16).toString('hex');
const runDir = path.join(root, 'artifacts', `${new Date().toISOString().replace(/[:.]/g, '-')}-${runId.slice(0, 8)}`);
const port = 10080;
const startupTimeoutMs = 120_000;
const dotnet = process.env.DNC_DOTNET ?? 'dotnet';
const localAccount = 'dncproof:MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=';
const labels = ['before', 'after'];
const profiles = ['low', 'balanced', 'wide'];
const projects = { before: 'Before', after: 'After' };
let emulator;
let status = 'FAIL';
let failure;
let emulatorLogs = '';
let emulatorError;
let emulatorClosed = false;
let emulatorCloseCode;
let emulatorCloseSignal;
const startup = { runnerVersion: '1.0.2', node: process.version, platform: process.platform, arch: process.arch,
  host: '127.0.0.1', port, timeoutMs: startupTimeoutMs, status: 'NOT_STARTED' };
const consoleLines = [];
const results = [];

function log(message) { console.log(message); consoleLines.push(message); }
function execute(command, args, options = {}) {
  const r = spawnSync(command, args, { cwd: root, encoding: 'utf8', timeout: 240_000, ...options });
  if (r.stdout) { process.stdout.write(r.stdout); consoleLines.push(r.stdout); }
  if (r.stderr) { process.stderr.write(r.stderr); consoleLines.push(r.stderr); }
  if (r.error || r.status !== 0) throw new Error(`${command} failed (${r.status ?? 'not started'}): ${r.error?.message ?? ''}`);
  return r.stdout.trim();
}
function portOpen() {
  return new Promise(resolve => {
    const socket = net.connect({ host: '127.0.0.1', port });
    socket.setTimeout(500);
    socket.once('connect', () => { socket.destroy(); resolve(true); });
    socket.once('error', () => { socket.destroy(); resolve(false); });
    socket.once('timeout', () => { socket.destroy(); resolve(false); });
  });
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

async function invoke(label, mode, extra = []) {
  const name = `${label}-${mode}${extra.includes('--profile') ? `-${extra[extra.indexOf('--profile') + 1]}` : ''}`;
  const out = path.join(runDir, `${name}.json`);
  const dll = path.join(root, 'src', projects[label], 'bin', 'Release', 'net10.0', `${projects[label]}.dll`);
  execute(dotnet, [dll, '--mode', mode, '--label', label, '--run', runId, '--out', out, ...extra]);
  const receipt = JSON.parse(await readFile(out, 'utf8'));
  results.push(receipt);
  if (receipt.status !== 'PASS') throw new Error(`${name} did not pass`);
}

async function render(summary) {
  await writeFile(path.join(runDir, 'report.html'), buildReport(summary, results, runId));
}

try {
  await mkdir(runDir, { recursive: true });
  if (Number(process.versions.node.split('.')[0]) < 22) throw new Error('Node.js 22 or newer is required.');
  log('=== DNC Azure Blob 12.29.2 vs 12.30.0 local proof ===');
  log(`Runner 1.0.2 | Node ${process.version} | ${process.platform}/${process.arch}`);
  const sdk = execute(dotnet, ['--version']);
  if (Number(sdk.split('.')[0]) < 10) throw new Error('.NET SDK 10 or newer is required.');
  const env = { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' };
  const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
  execute(npm, [existsSync(path.join(root, 'package-lock.json')) ? 'ci' : 'install', '--no-audit', '--no-fund'], { shell: process.platform === 'win32' });
  if (await portOpen()) throw new Error('Local port 10080 is in use. Close the process using that port and run again. The script will not use or stop it.');
  const azuriteDir = path.join(runDir, 'azurite');
  await mkdir(azuriteDir);
  const bin = path.join(root, 'node_modules', 'azurite', 'dist', 'src', 'blob', 'main.js');
  if (!existsSync(bin)) throw new Error(`Azurite entry point is missing: ${bin}`);
  const debugFile = path.join(runDir, 'azurite-debug.log');
  const emulatorArgs = [bin, '--blobHost', '127.0.0.1', '--blobPort', String(port), '--location', azuriteDir, '--silent', '--disableTelemetry', '--debug', debugFile];
  // Official opt-out: prevents Application Insights initialization and its metadata probes.
  startup.status = 'STARTING';
  startup.executable = process.execPath;
  startup.args = emulatorArgs;
  startup.startedUtc = new Date().toISOString();
  const started = Date.now();
  log('Starting Azurite; allowing up to 120 seconds. Output is saved even on failure.');
  emulator = spawn(process.execPath, emulatorArgs, { cwd: root, env: { ...process.env, AZURITE_ACCOUNTS: localAccount }, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
  startup.pid = emulator.pid ?? null;
  const capture = b => { const text = b.toString(); emulatorLogs += text; process.stdout.write(text); };
  emulator.stdout.on('data', capture);
  emulator.stderr.on('data', capture);
  emulator.on('error', error => { emulatorError = error; });
  emulator.on('close', (code, signal) => { emulatorClosed = true; emulatorCloseCode = code; emulatorCloseSignal = signal; });
  let ready = false;
  let nextProgressMs = 10_000;
  while (Date.now() - started < startupTimeoutMs) {
    if (emulatorError) throw emulatorError;
    if (emulatorClosed || emulator.exitCode !== null || emulator.signalCode !== null) {
      throw new Error(`Azurite stopped before opening port ${port} (exit=${emulator.exitCode ?? emulatorCloseCode}, signal=${emulator.signalCode ?? emulatorCloseSignal ?? 'none'}):\n${emulatorLogs || '(No stdout/stderr. See azurite-startup.json.)'}`);
    }
    if (await portOpen()) { ready = true; break; }
    const elapsed = Date.now() - started;
    if (elapsed >= nextProgressMs) { log(`Waiting for Azurite: ${Math.floor(elapsed / 1000)} seconds; PID ${emulator.pid ?? 'not started'}.`); nextProgressMs = elapsed + 10_000; }
    await delay(500);
  }
  startup.elapsedMs = Date.now() - started;
  if (!ready) {
    startup.status = 'TIMEOUT';
    throw new Error(`Azurite did not open 127.0.0.1:${port} within 120 seconds. Send azurite-console.txt, azurite-startup.json and azurite-debug.log if present.\n${emulatorLogs || '(Azurite produced no stdout/stderr.)'}`);
  }
  startup.status = 'READY';
  log('Azurite running on 127.0.0.1:10080 (local only).');
  for (const label of labels) execute(dotnet, ['build', `src/${projects[label]}/${projects[label]}.csproj`, '-c', 'Release', '--nologo', '-v', 'minimal', '-p:RestoreLockedMode=true'], { env });
  const fixture = path.join(runDir, 'fixture-64MiB.bin');
  for (const label of labels) {
    log(`\n--- ${label.toUpperCase()}: block IDs, resume, commit order, ETag ---`);
    await invoke(label, 'blocks');
    await invoke(label, 'seed', ['--fixture', fixture]);
    for (const profile of profiles) {
      log(`\n--- ${label.toUpperCase()}: DownloadToAsync ${profile}, fresh process ---`);
      await invoke(label, 'download', ['--profile', profile, '--fixture', fixture]);
    }
  }
  // This gate is cross-version rather than a comparison of memory observations.
  const before = results.find(x => x.mode === 'blocks' && x.label === 'before');
  const after = results.find(x => x.mode === 'blocks' && x.label === 'after');
  if (before.sdkBlockIds.overlap !== 8 || after.sdkBlockIds.overlap !== 0) throw new Error('Cross-version block ID comparison did not match the release claim.');
  const hashes = new Set(results.filter(x => x.mode === 'download').map(x => x.downloadSha256));
  if (hashes.size !== 1) throw new Error('Cross-version downloaded hashes differ.');
  status = 'PASS';
} catch (error) {
  failure = error;
  console.error(error);
  consoleLines.push(String(error));
} finally {
  if (emulator && !emulatorClosed && emulator.exitCode === null && emulator.signalCode === null && emulator.pid) {
    const stopped = new Promise(resolve => emulator.once('close', resolve));
    emulator.kill();
    await Promise.race([stopped, delay(3000)]);
  }
  startup.exitCode = emulator?.exitCode ?? emulatorCloseCode ?? null;
  startup.signal = emulator?.signalCode ?? emulatorCloseSignal ?? null;
  startup.spawnError = emulatorError ? String(emulatorError) : null;
  if (failure && startup.status === 'STARTING') startup.status = 'FAILED';
  await writeFile(path.join(runDir, 'azurite-console.txt'), emulatorLogs || '(Azurite produced no stdout/stderr.)\n');
  await writeFile(path.join(runDir, 'azurite-startup.json'), JSON.stringify(startup, null, 2));
  const summary = { status, utc: new Date().toISOString(), candidateId: 'DNC-CYCLE-20261001-C03', runId,
    sdkVersions: ['12.29.2', '12.30.0'], azuriteVersion: '3.37.0',
    results: results.length, passedChecks: results.flatMap(x => x.checks).filter(c => c.pass).length,
    crossVersionChecks: status === 'PASS' ? ['old IDs reused / new IDs disjoint', 'all six download SHA256 values equal'] : [],
    evidenceScope: 'LOCAL_AZURITE_ONLY', windowsVerification: process.platform === 'win32' ? status : 'PENDING_USER',
    error: failure ? String(failure) : null, wordPressChanged: false, cloudResourcesCreated: false,
    azuriteTelemetry: 'DISABLED_BY_OFFICIAL_CLI_FLAG', dotnetCliTelemetry: 'DISABLED_FOR_BUILD' };
  await writeFile(path.join(runDir, 'verification.json'), JSON.stringify(summary, null, 2));
  await writeFile(path.join(runDir, 'console.txt'), consoleLines.join('\n'));
  await render(summary);
  const latest = path.join(root, 'artifacts', 'latest');
  await rm(latest, { recursive: true, force: true });
  await mkdir(latest, { recursive: true });
  // Only copy small evidence, not the 64 MiB fixture or emulator storage.
  for (const name of await readdir(runDir)) if (/\.(json|html|txt|log)$/.test(name)) await copyFile(path.join(runDir, name), path.join(latest, name));
  log(`\n=== ${status}: ${summary.passedChecks} checks; ${summary.crossVersionChecks.length} cross-version gates ===`);
  log(`Report: ${path.join(latest, 'report.html')}`);
  log(`Evidence: ${runDir}`);
  process.exitCode = status === 'PASS' ? 0 : 1;
}
