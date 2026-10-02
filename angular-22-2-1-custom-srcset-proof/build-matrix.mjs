import {spawnSync} from 'node:child_process';
import {dirname, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
const root=dirname(fileURLToPath(import.meta.url));
function run(executable,args,cwd=root) {
  const result=spawnSync(executable,args,{cwd,stdio:'inherit'});
  if(result.error) throw result.error;
  if(result.status!==0) throw new Error(`${executable} failed with exit ${result.status}`);
}
for(const version of ['22.2.0','22.2.1'])
  run(process.execPath,[resolve(root,'build.mjs')],resolve(root,'versions',version));
// Use the installed Playwright CLI directly; no shell or global npx dependency.
run(process.execPath,[resolve(root,'node_modules/playwright/cli.js'),'install','chromium','--only-shell']);
