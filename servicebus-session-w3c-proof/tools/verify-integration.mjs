export function flattenSpans(text) {
  const spans=[];
  for (const line of text.split(/\r?\n/).filter(l=>l.trim())) {
    const data=JSON.parse(line);
    for(const resource of data.resourceSpans ?? []) {
      const service=resource.resource?.attributes?.find(a=>a.key==='service.name')?.value?.stringValue;
      for(const scope of resource.scopeSpans ?? []) for(const span of scope.spans ?? [])
        spans.push({...span,source:scope.scope?.name,service});
    }
  }
  return spans;
}
export function verifyIntegration(manifest, receipts, spans) {
  const checks=[];
  const check=(name,pass)=>checks.push({name,pass:Boolean(pass)});
  check('12 messages were sent',manifest.sent.length===12);
  for(const message of manifest.sent) {
    const received=receipts.filter(r=>r.messageId===message.messageId && r.variant===message.variant);
    check(`${message.messageId}: message consumed`,received.length>=1);
    check(`${message.messageId}: received Diagnostic-Id unchanged`,received.length>=1 && received.every(r=>r.diagnosticId===message.diagnosticId));
    check(`${message.messageId}: session identifier preserved`,received.length>=1 && received.every(r=>(r.sessionId ?? null)===(message.sessionId ?? null)));
    const source=message.kind==='session'?'Azure.Messaging.ServiceBus.ServiceBusSessionProcessor':'Azure.Messaging.ServiceBus.ServiceBusProcessor';
    const related=spans.filter(s=>s.service===`dnc-host-${message.variant}` && s.source===source && hex(s.traceId)===message.traceId);
    if(message.kind==='session' && message.variant==='before') {
      check(`${message.messageId}: old Host has no session processor span`,related.length===0);
    } else {
      check(`${message.messageId}: expected processor span exported`,related.length>0);
      // SDK versions may model the producer as a parent or a link. Preserve and
      // report the actual semantics; either must carry the exact producer context.
      check(`${message.messageId}: exact producer parent or link preserved`,related.some(s=>hex(s.parentSpanId)===message.producerSpanId || (s.links??[]).some(l=>hex(l.traceId)===message.traceId && hex(l.spanId)===message.producerSpanId)));
    }
  }
  // A working regular consumer in BOTH hosts guards against a broken collector
  // falsely appearing to reproduce the old missing-session-span regression.
  for(const variant of ['before','after']) check(`${variant}: regular control exported`,spans.some(s=>s.service===`dnc-host-${variant}` && s.source==='Azure.Messaging.ServiceBus.ServiceBusProcessor'));
  check('Old Host exports no session processor source at all',!spans.some(s=>s.service==='dnc-host-before' && s.source==='Azure.Messaging.ServiceBus.ServiceBusSessionProcessor'));
  return {status:checks.every(c=>c.pass)?'PASS':'FAIL',detail:'Actual AMQP messages, source-built Host pair, isolated Functions worker, local emulator and OTLP collector.',checks};
}
export function hex(value) {
  if(!value)return '';
  return /^[0-9a-f]+$/i.test(value) && [16,32].includes(value.length)?value.toLowerCase():Buffer.from(value,'base64').toString('hex');
}
