import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile,copyFile,chmod,rm} from 'node:fs/promises';
import {existsSync} from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import crypto from 'node:crypto';
import {report} from './report.mjs';
import {flattenSpans,verifyIntegration} from './verify-integration.mjs';
const root=path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const modeIndex=process.argv.indexOf('--mode');
const mode=modeIndex>=0?process.argv[modeIndex+1]:'component';
if(!['component','integration','all'].includes(mode))throw new Error('Use --mode component|integration|all');
if(Number(process.versions.node.split('.')[0])<22)throw new Error('Node.js 22 or later is required');
const runId=`${Date.now()}-${crypto.randomBytes(4).toString('hex')}`;
const out=path.join(root,'artifacts',runId);
const latest=path.join(root,'artifacts','latest');
const dotnet=process.env.DNC_DOTNET ?? 'dotnet';
const result={schemaVersion:1,runId,mode,timestampUtc:new Date().toISOString(),status:'RUNNING',component:[],gates:[],integration:{status:'PENDING_DOCKER_RUN'}};
const lines=[];
const files=new Set();
let dockerStarted=false;
const dockerEnv={...process.env,DNC_OUTPUT_DIR:out.replace(/\\/g,'/'),MSSQL_SA_PASSWORD:`Dnc!${crypto.randomBytes(14).toString('hex')}7A`,DOTNET_CLI_TELEMETRY_OPTOUT:'1'};
await mkdir(out,{recursive:true});
await chmod(out,0o777).catch(()=>{});
function log(s){console.log(s);lines.push(s);}
async function execute(command,args,{cwd=root,env=process.env,timeout=30*60*1000}={}){
  log(`> ${path.basename(command)} ${args.join(' ')}`);
  return new Promise((resolve,reject)=>{
    const child=spawn(command,args,{cwd,env:{...env,DOTNET_CLI_TELEMETRY_OPTOUT:'1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE:'1'},shell:false});
    let output='';
    for(const stream of [child.stdout,child.stderr])stream.on('data',chunk=>{const s=chunk.toString();output+=s;process.stdout.write(s);lines.push(s);});
    const timer=setTimeout(()=>{child.kill();reject(new Error(`${command} timed out`));},timeout);
    child.on('error',e=>{clearTimeout(timer);reject(e);});
    child.on('close',code=>{clearTimeout(timer);code===0?resolve(output):reject(new Error(`${command} exited ${code}. See console.log.`));});
  });
}
const composeArgs=['compose','-p','dnc-servicebus-w3c-proof','-f',path.join(root,'docker','compose.yaml')];
const compose=(args,opts={})=>execute('docker',[...composeArgs,...args],{env:dockerEnv,...opts});
async function jsonFile(file){return JSON.parse(await readFile(file,'utf8'));}
async function save(name,value){await writeFile(path.join(out,name),typeof value==='string'?value:JSON.stringify(value,null,2));files.add(name);}
async function component(){
  await save('dotnet-info.txt',await execute(dotnet,['--info']));
  const pins=await jsonFile(path.join(root,'sources.json'));
  for(const source of pins.sources){
    log(`Actual Host configuration: ${source.variant}, ${source.tag}, ${source.commit}`);
    const archive=path.join(root,source.archive);
    if(crypto.createHash('sha256').update(await readFile(archive)).digest('hex')!==source.archiveSha256)throw new Error(`Vendor archive hash mismatch: ${source.variant}`);
    const hostRoot=path.join(root,'artifacts','sources',source.variant);
    const probeRoot=path.join(root,'artifacts','probes',source.variant);
    await mkdir(hostRoot,{recursive:true});await mkdir(probeRoot,{recursive:true});
    await execute('tar',['-xzf',archive,'--strip-components=1','-C',hostRoot]);
    // SDK selection alone is relaxed for a machine with SDK 11 RC installed.
    // No Host telemetry/configuration source is edited.
    await copyFile(path.join(root,'global.json'),path.join(hostRoot,'global.json'));
    for(const file of ['HostProbe.csproj','Program.cs'])await copyFile(path.join(root,'src','HostProbe',file),path.join(probeRoot,file));
    const shippedLock=path.join(root,'locks',`HostProbe-${source.variant}.json`);
    if(existsSync(shippedLock))await copyFile(shippedLock,path.join(probeRoot,'packages.lock.json'));
    // The probe is outside the extracted Host tree, so NuGet does not inherit
    // that tree's official feeds for transitive Host dependencies. Pass its
    // original config explicitly; a warm local cache can otherwise hide this.
    const nugetConfig=path.join(hostRoot,'NuGet.config');
    if(!existsSync(nugetConfig))throw new Error(`Official Host NuGet.config missing: ${source.variant}`);
    await execute(dotnet,['restore',path.join(probeRoot,'HostProbe.csproj'),'--locked-mode','--configfile',nugetConfig,`-p:HostSourceRoot=${hostRoot}`,'-p:ContinuousIntegrationBuild=false']);
    const filename=`${source.variant}.json`;
    await execute(dotnet,['run','--project',path.join(probeRoot,'HostProbe.csproj'),'-c','Release','--no-restore',`-p:HostSourceRoot=${hostRoot}`,'-p:ContinuousIntegrationBuild=false','--',source.variant,path.join(out,filename)]);
    files.add(filename);
    const probe=await jsonFile(path.join(out,filename));
    result.component.push({...probe,sourceTag:source.tag,sourceCommit:source.commit,archiveSha256:source.archiveSha256});
    if(!probe.pass)throw new Error(`${source.variant} configuration checks failed`);
  }
  const [before,after]=result.component;
  const session='Azure.Messaging.ServiceBus.ServiceBusSessionProcessor';
  const regular='Azure.Messaging.ServiceBus.ServiceBusProcessor';
  const check=(name,pass)=>result.gates.push({name,pass});
  check('Old Host excludes all three session spans',before.cases.filter(c=>c.source===session).every(c=>c.traceId===null));
  check('Fixed Host preserves all three session trace IDs and parents',after.cases.filter(c=>c.source===session).every(c=>c.traceId===c.expectedTraceId && c.parentSpanId===c.expectedParentSpanId));
  check('Ordinary processor works in both Hosts',[before,after].every(r=>r.cases.filter(c=>c.source===regular).every(c=>c.traceId===c.expectedTraceId)));
  check('No unknown source accidentally enabled',[before,after].every(r=>r.cases.filter(c=>c.source==='DNC.UnknownSource').every(c=>c.traceId===null)));
  if(!result.gates.every(c=>c.pass))throw new Error('Cross-release gates failed');
  log(`COMPONENT PASS: ${before.checks.length+after.checks.length} checks + ${result.gates.length} gates. Broker test is separate.`);
}
async function optionalText(file){try{return await readFile(file,'utf8');}catch(e){if(e.code==='ENOENT')return '';throw e;}}
async function integration(){
  if(process.env.ACCEPT_EULA!=='Y')throw new Error('Run Verify-ServiceBus.ps1 -Mode Integration or All to read and explicitly accept the emulator licenses.');
  const info=await execute('docker',['info','--format','{{.OSType}}/{{.Architecture}}'],{timeout:30000});
  if(!info.includes('linux'))throw new Error('Switch Docker Desktop to Linux containers');
  await save('docker-info.txt',info);
  for(const name of ['receipts-before.jsonl','receipts-after.jsonl','traces.jsonl'])await save(name,'');
  // The fixed project name owns only this demo's disposable containers.
  // Abort if a previous instance is active, so another run is never overwritten.
  const previous=await compose(['ps','-q']);
  if(previous.trim())throw new Error('An earlier demo is active. Run Stop-ServiceBus.ps1 first.');
  await compose(['build','before','after','sender']);
  dockerStarted=true;
  await compose(['up','-d','sql','emulator','azurite','collector','before','after']);
  log('Waiting for the local emulator health endpoint (up to four minutes)...');
  let ready=false;
  for(let i=0;i<120;i++){
    try{const response=await fetch('http://127.0.0.1:5301/health',{signal:AbortSignal.timeout(1500)});if(response.ok){ready=true;break;}}catch{}
    await new Promise(r=>setTimeout(r,2000));
    if(i%15===0)log('Still waiting for SQL / Service Bus emulator...');
  }
  if(!ready)throw new Error('Emulator did not become healthy');
  await compose(['run','--rm','--no-deps','sender',runId,'/proof/manifest.json']);files.add('manifest.json');
  log('Waiting for 12 Functions receipts and OTLP exports...');
  let receipts=[],spans=[],manifest=await jsonFile(path.join(out,'manifest.json'));
  for(let i=0;i<120;i++){
    const texts=await Promise.all(['receipts-before.jsonl','receipts-after.jsonl'].map(n=>optionalText(path.join(out,n))));
    try{receipts=texts.flatMap(t=>t.split(/\r?\n/).filter(Boolean).map(l=>JSON.parse(l)));spans=flattenSpans(await optionalText(path.join(out,'traces.jsonl')));}catch{await new Promise(r=>setTimeout(r,1000));continue;}
    result.integration=verifyIntegration(manifest,receipts,spans);
    if(result.integration.status==='PASS')break;
    await new Promise(r=>setTimeout(r,2000));
    if(i%15===0)log(`Receipts: ${receipts.length}/12; spans exported: ${spans.length}`);
  }
  await save('integration.json',result.integration);
  await save('images.json',await compose(['images','--format','json']));
  if(result.integration.status!=='PASS')throw new Error('Actual Service Bus integration checks failed; see integration.json and docker.log');
  log(`INTEGRATION PASS: ${result.integration.checks.length} checks.`);
}
try{
  if(mode!=='integration')await component();
  if(mode!=='component')await integration();
  result.status=mode==='component'?'COMPONENT_PASS_INTEGRATION_PENDING':'PASS';
}catch(e){result.status='FAIL';result.failure=e.stack ?? String(e);log(result.failure);process.exitCode=1;}
finally{
  if(dockerStarted){
    try{await save('docker.log',await compose(['logs','--no-color']));}catch(e){log(String(e));}
    try{await compose(['down','--remove-orphans'],{timeout:60000});}catch(e){result.cleanupError=String(e);log(result.cleanupError);}
  }
  await save('result.json',result);await save('report.html',report(result));
  // Never write the ephemeral SQL password into the shared evidence log.
  await save('console.log',lines.join('\n').replaceAll(dockerEnv.MSSQL_SA_PASSWORD,'[LOCAL_PASSWORD_REDACTED]'));
  await rm(latest,{recursive:true,force:true});
  await mkdir(latest,{recursive:true});
  // latest/report points only to files generated in this run, no stale PASS data.
  const index={runId,runDirectory:out,files:[...files]};
  for(const file of files)await copyFile(path.join(out,file),path.join(latest,file));
  await writeFile(path.join(latest,'index.json'),JSON.stringify(index,null,2));
  log(`Result: ${result.status}\nReport: ${path.join(latest,'report.html')}`);
}
