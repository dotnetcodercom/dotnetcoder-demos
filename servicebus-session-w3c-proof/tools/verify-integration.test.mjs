import test from 'node:test';
import assert from 'node:assert/strict';
import {verifyIntegration,flattenSpans,hex} from './verify-integration.mjs';
function fixture(){
 const sent=[],receipts=[],spans=[];
 let n=1;
 for(const variant of ['before','after'])for(const kind of ['regular','session'])for(let index=0;index<3;index++){
  const traceId=(n++).toString(16).padStart(32,'0'),producerSpanId='1234567812345678',messageId=`${variant}-${kind}-${index}`;
  const diagnosticId=`00-${traceId}-${producerSpanId}-01`,sessionId=kind==='session'?'proof-session':null;
  sent.push({variant,kind,index,traceId,producerSpanId,messageId,diagnosticId,sessionId});
  receipts.push({variant,kind,messageId,diagnosticId,sessionId});
  if(variant==='after'||kind==='regular')spans.push({service:`dnc-host-${variant}`,source:kind==='session'?'Azure.Messaging.ServiceBus.ServiceBusSessionProcessor':'Azure.Messaging.ServiceBus.ServiceBusProcessor',traceId,parentSpanId:producerSpanId});
 }
 return {manifest:{sent},receipts,spans};
}
test('Verifier accepts complete synthetic evidence (not a broker run)',()=>{const f=fixture();assert.equal(verifyIntegration(f.manifest,f.receipts,f.spans).status,'PASS');});
test('A dead collector cannot reproduce the regression falsely',()=>{const f=fixture();assert.equal(verifyIntegration(f.manifest,f.receipts,[]).status,'FAIL');});
test('A changed after-session Trace ID is rejected',()=>{const f=fixture();f.spans.find(s=>s.service==='dnc-host-after'&&s.source.endsWith('SessionProcessor')).traceId='f'.repeat(32);assert.equal(verifyIntegration(f.manifest,f.receipts,f.spans).status,'FAIL');});
test('Changed Diagnostic-Id is rejected',()=>{const f=fixture();f.receipts[0].diagnosticId='changed';assert.equal(verifyIntegration(f.manifest,f.receipts,f.spans).status,'FAIL');});
test('A missing processed message is rejected',()=>{const f=fixture();f.receipts.pop();assert.equal(verifyIntegration(f.manifest,f.receipts,f.spans).status,'FAIL');});
test('Changed session ID is rejected',()=>{const f=fixture();f.receipts.find(r=>r.kind==='session').sessionId='other';assert.equal(verifyIntegration(f.manifest,f.receipts,f.spans).status,'FAIL');});
test('Exact producer link is accepted and an unrelated parent is rejected',()=>{const f=fixture();const s=f.spans[0];s.links=[{traceId:s.traceId,spanId:s.parentSpanId}];s.parentSpanId='0'.repeat(16);assert.equal(verifyIntegration(f.manifest,f.receipts,f.spans).status,'PASS');s.links=[];assert.equal(verifyIntegration(f.manifest,f.receipts,f.spans).status,'FAIL');});
test('Collector JSON scopes and hexadecimal/base64 IDs are read',()=>{const t='a'.repeat(32),p='b'.repeat(16);assert.equal(hex(Buffer.from(t,'hex').toString('base64')),t);assert.equal(hex(p),p);const spans=flattenSpans(JSON.stringify({resourceSpans:[{resource:{attributes:[{key:'service.name',value:{stringValue:'test'}}]},scopeSpans:[{scope:{name:'source'},spans:[{traceId:t,spanId:p}]}]}]}));assert.equal(spans[0].source,'source');assert.equal(spans[0].service,'test');});
