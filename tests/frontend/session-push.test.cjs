const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const {readFileSync} = require('node:fs');
const path = require('node:path');
const source = readFileSync(path.join(__dirname, '../../wwwroot/generated/riffroster-web.js'), 'utf8');
function bridge(fetch) {const c = vm.createContext({Headers, fetch, console}); vm.runInContext(source,c); return c.RiffRosterWeb;}
const decode = bridge().sessionPushDecoders;
test('session and push decoders require real response fields', () => {
  const session = {user:{username:'Alice',color:'#FFB000',joinedAt:'2026-08-04T12:00:00Z'},isAdmin:false,created:true,claimed:false};
  assert.equal(decode.session(session).user.username,'Alice');
  assert.throws(() => decode.session({...session,isAdmin:'false'}));
  assert.throws(() => decode.subscription({enabled:true}));
  assert.throws(() => decode.subscription({enabled:true,leadMinutes:15,includeCrewNames:false}));
  assert.equal(decode.subscription({enabled:true,leadMinutes:20,includeCrewNames:false}).leadMinutes,20);
  assert.throws(() => decode.test({enabled:true}));
  assert.equal(decode.test({sent:true}).sent,true);
});
test('notification key validation checks encoded length and point prefix', () => {
  const publicKey = Buffer.from([4,...Array(64).fill(1)]).toString('base64url');
  assert.equal(decode.publicKey({publicKey}).publicKey,publicKey);
  for (const key of ['AQID', '!', Buffer.from(Array(65).fill(0)).toString('base64url')]) assert.throws(() => decode.publicKey({publicKey:key}));
});
test('logout requires no-content and never retries unexpected successful responses', async () => {
  for (const status of [200,204,401]) {
    let calls = 0;
    const api = bridge(async () => {calls++; return {status,ok:status<300};}).sessionPush;
    if(status===204) await api.signOut();
    else await assert.rejects(api.signOut(), error => status===401 ? error.status===401 : /Refresh before/.test(error.message));
    assert.equal(calls,1);
  }
});
