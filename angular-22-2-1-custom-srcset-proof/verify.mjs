import {chromium} from 'playwright';
import {createServer} from 'node:http';
import {readFile, mkdir, writeFile} from 'node:fs/promises';
import {resolve, dirname, extname, relative} from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
const root = dirname(fileURLToPath(import.meta.url));
const evidence = resolve(root, 'evidence');
await mkdir(evidence, {recursive:true});
const server = createServer(async (req,res) => {
  try {
    const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    const file = resolve(root, 'versions', pathname.slice(1));
    if (relative(resolve(root, 'versions'), file).startsWith('..')) {res.writeHead(403);res.end();return;}
    const bytes = await readFile(file);
    res.setHeader('Content-Type', {'.html':'text/html','.js':'application/javascript','.png':'image/png','.map':'application/json'}[extname(file)] ?? 'text/plain');
    res.setHeader('Cache-Control', 'no-store');
    res.end(bytes);
  } catch {res.writeHead(404);res.end();}
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;
let browser;
const results=[];
try {
  browser = await chromium.launch({headless:true});
  for (const version of ['22.2.0','22.2.1']) for (const dpr of [1,2]) {
    const context = await browser.newContext({viewport:{width:1000,height:750},deviceScaleFactor:dpr});
    const page = await context.newPage();
    const errors=[], consoleMessages=[], requests=[];
    page.on('pageerror', e => errors.push(String(e)));
    page.on('console', message => consoleMessages.push({type:message.type(),text:message.text()}));
    page.on('request', req => {if(req.resourceType()==='image')requests.push(req.url());});
    await page.goto(`http://127.0.0.1:${port}/${version}/dist/index.html`);
    await page.waitForFunction(v => window.proofReady===v || window.proofError, version);
    await page.waitForFunction(() => Array.from(document.images).length===3 && Array.from(document.images).every(img=>img.complete && img.naturalWidth>0));
    const observed = await page.evaluate(() => ({version:window.proofReady,dpr:devicePixelRatio,
      rows:['custom','attribute','generated'].map(id=>{const img=document.getElementById(id);return {id,srcset:img.getAttribute('srcset'),src:img.getAttribute('src'),currentSrc:img.currentSrc,naturalWidth:img.naturalWidth};})}));
    const custom='images/signed-a.png?token=one 1x, images/signed-b.png?token=two 2x';
    assert.equal(observed.version,version);
    assert.equal(observed.rows[0].srcset,version==='22.2.0'?null:custom);
    assert.equal(observed.rows[1].srcset,custom);
    assert.equal(observed.rows[2].srcset,'images/generated-100.png 1x, images/generated-200.png 2x');
    const signed=dpr===1?'signed-a.png?token=one':'signed-b.png?token=two';
    assert.ok(observed.rows[0].currentSrc.endsWith(version==='22.2.0'?'fallback.png':signed));
    assert.ok(observed.rows[1].currentSrc.endsWith(signed));
    assert.ok(observed.rows[2].currentSrc.endsWith(`generated-${100*dpr}.png`));
    assert.equal(errors.length,0,JSON.stringify(errors));
    await page.screenshot({path:resolve(evidence,`angular-${version}-dpr-${dpr}.png`),fullPage:true});
    results.push({...observed,requests,consoleMessages,errors,checks:'PASS'});
    console.log(`PASS Angular ${version}, DPR ${dpr}: DOM srcset + browser currentSrc`);
    await context.close();
  }
  await writeFile(resolve(evidence,'receipt.json'),JSON.stringify({status:'PASS',atUtc:new Date().toISOString(),browser:browser.version(),node:process.version,scope:'Initial bindings, local PNGs, Chromium DPR 1/2; no LCP, bandwidth, production CDN, dynamic binding or all-browser claims.',results},null,2));
} catch(error) {
  await writeFile(resolve(evidence,'receipt.json'),JSON.stringify({status:'FAIL',atUtc:new Date().toISOString(),error:String(error),completed:results},null,2));
  throw error;
} finally {
  if(browser)await browser.close();
  await new Promise(resolve=>server.close(resolve));
}
