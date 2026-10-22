const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const {readFileSync} = require('node:fs');
const path = require('node:path');
const c = vm.createContext({console,navigator:{locks:{request:(_,run) => run()}}});
vm.runInContext(readFileSync(path.join(__dirname,'../../wwwroot/generated/riffroster-web.js'),'utf8'),c);
const acquire = c.RiffRosterWeb.acquirePushSubscription;
test('outer operation owns the lock through persistence without nested acquisition', async () => {
  let held = false, requests = 0;
  const context = vm.createContext({console,navigator:{locks:{request:async (_,run) => {
    assert.equal(held,false); held=true; requests++;
    try {return await run();} finally {held=false;}
  }}}});
  vm.runInContext(readFileSync(path.join(__dirname,'../../wwwroot/generated/riffroster-web.js'),'utf8'),context);
  const api = context.RiffRosterWeb;
  await api.withPushOperation(async locked => {
    const subscription = await api.acquirePushSubscription({getSubscription:async () => null,subscribe:async () => ({id:'test'})},{},() => true,locked);
    assert.equal(subscription.id,'test');
    await Promise.resolve();
    assert.equal(held,true);
  });
  assert.equal(requests,1);
  assert.equal(held,false);
});
test('operation callback failures propagate instead of retrying without the lock', async () => {
  let calls = 0;
  await assert.rejects(c.RiffRosterWeb.withPushOperation(async () => {calls++;throw new Error('Failed');}),/Failed/);
  assert.equal(calls,1);
});
test('shared lock keeps another tab from reusing a stale subscription before cleanup', async () => {
  let tail = Promise.resolve(), subscription = null, creations = 0, current = true, release;
  const locks = {request:(_,run) => {
    const result = tail.then(run); tail = result.catch(() => {}); return result;
  }};
  const source = readFileSync(path.join(__dirname,'../../wwwroot/generated/riffroster-web.js'),'utf8');
  function tab() {const context = vm.createContext({console,navigator:{locks}});vm.runInContext(source,context);return context.RiffRosterWeb.acquirePushSubscription;}
  const manager = {getSubscription:async () => subscription,subscribe:async () => {
    const id = ++creations;
    if (id === 1) await new Promise(resolve => {release=resolve;});
    return subscription = {id,unsubscribe:async () => {subscription=null;return true;}};
  }};
  const first = tab()(manager,{},() => current);
  while (!release) await Promise.resolve();
  const second = tab()(manager,{},() => true);
  current = false; release();
  assert.equal(await first,null);
  assert.equal((await second).id,2);
  assert.equal(subscription.id,2);
});
test('without cross-tab locks stale creation is not removed and never returned to be saved', async () => {
  const context = vm.createContext({console});
  vm.runInContext(readFileSync(path.join(__dirname,'../../wwwroot/generated/riffroster-web.js'),'utf8'),context);
  let current = true;
  const result = await context.RiffRosterWeb.acquirePushSubscription({getSubscription:async () => null,subscribe:async () => {
    current=false;return {unsubscribe:() => {throw new Error('May belong to another tab');}};
  }},{},() => current);
  assert.equal(result,null);
});
test('existing subscriptions are reused without creating or removing them', async () => {
  const existing = {unsubscribe:() => {throw new Error('Must preserve');}};
  assert.equal(await acquire({getSubscription:async () => existing,subscribe:() => {throw new Error('Must reuse');}}, {}, () => true),existing);
});
test('session expiry during creation removes the late subscription before returning', async () => {
  let current = true, removed = 0;
  const result = await acquire({getSubscription:async () => null,subscribe:async () => {
    current = false; return {unsubscribe:async () => {removed++;return true;}};
  }}, {}, () => current);
  assert.equal(result,null);
  assert.equal(removed,1);
});
test('session expiry during lookup does not create or remove a subscription', async () => {
  let current = true;
  const result = await acquire({getSubscription:async () => {current=false;return null;},subscribe:() => {throw new Error('Must not create');}}, {}, () => current);
  assert.equal(result,null);
});
test('failed stale cleanup stays silent and active creation failures remain visible', async () => {
  let current = true;
  assert.equal(await acquire({getSubscription:async () => null,subscribe:async () => {
    current=false;return {unsubscribe:async () => {throw new Error('Browser denied');}};
  }}, {}, () => current),null);
  await assert.rejects(acquire({getSubscription:async () => null,subscribe:async () => {throw new Error('Creation failed');}}, {}, () => true), /Creation failed/);
});
