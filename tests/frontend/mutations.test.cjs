const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const {readFileSync} = require('node:fs');
const path = require('node:path');
const source = readFileSync(path.join(__dirname, '../../wwwroot/generated/riffroster-web.js'), 'utf8');
function bridge(fetch) { const context = vm.createContext({Headers, fetch, console}); vm.runInContext(source, context); return context.RiffRosterWeb; }
const decoders = bridge().mutationDecoders;
const act = {id:'act', artist:'Band', stage:'Main', startsAt:'2026-08-04T20:00:00Z', endsAt:'2026-08-04T21:00:00Z'};
test('mutation decoders preserve values and reject mismatched identities and invalid counts', () => {
  assert.equal(decoders.toggle({selected:false, performanceId:'act', username:'Alice'}, 'act').selected, false);
  assert.equal(decoders.savePerformance(act, 'act').id, 'act');
  assert.equal(decoders.bulk({created:[act], skippedDuplicates:2}).skippedDuplicates, 2);
  for (const run of [
    () => decoders.toggle({selected:'false', performanceId:'act', username:'Alice'}, 'act'),
    () => decoders.toggle({selected:true, performanceId:'other', username:'Alice'}, 'act'),
    () => decoders.savePerformance(act, 'other'),
    () => decoders.deletePerformance({deletedPerformanceId:'other', deletedArtist:'Band', removedSelections:0}, 'act'),
    () => decoders.bulk({created:[act,act], skippedDuplicates:0}),
    () => decoders.bulk({created:[], skippedDuplicates:-1}),
    () => decoders.leaveGroup({left:false, groupId:'group'}, 'group'),
    () => decoders.groupCode({groupId:'other', joinCode:'code'}, 'group'),
    () => decoders.lastEvent({eventId:null}, 'event')
  ]) assert.throws(run);
});
test('invalid successful mutations advise refresh without retrying, including invalid JSON', async () => {
  for (const json of [async () => null, async () => {throw new SyntaxError('private-value');}]) {
    let calls = 0;
    const api = bridge(async () => { calls++; return {ok:true, status:200, json}; });
    await assert.rejects(api.mutations.toggle('act', {method:'POST'}), error => /may have been saved.*Refresh/.test(error.message) && !error.message.includes('private-value'));
    assert.equal(calls, 1);
  }
});
test('typed mutation methods encode route IDs and retain status errors', async () => {
  let url;
  const api = bridge(async value => { url = value; return {ok:false, status:403, json:async () => ({})}; });
  await assert.rejects(api.mutations.deletePerformance('a/b', 'c?d', {method:'DELETE'}), error => error.status === 403);
  assert.equal(url, '/api/events/a%2Fb/performances/c%3Fd');
});
