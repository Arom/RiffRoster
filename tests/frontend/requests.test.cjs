const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const {readFileSync} = require('node:fs');
const path = require('node:path');
const source = readFileSync(path.join(__dirname, '../../wwwroot/generated/riffroster-web.js'), 'utf8');
function bridge(fetch) { const c = vm.createContext({Headers, fetch, console}); vm.runInContext(source, c); return c.RiffRosterWeb; }
const act = {artist:'Band', stage:'Main', startsAt:'2026-08-04T20:00:00+02:00', endsAt:'2026-08-04T21:00:00+02:00'};
const event = {name:'Festival', location:'', startDate:'2026-08-04', endDate:'2026-08-05'};
const login = {loginName:'alice', displayName:'Alice', password:'  synthetic password  ', accessCode:'synthetic-code'};
const push = {endpoint:'https://example.invalid/push', keys:{p256dh:'synthetic-key', auth:'synthetic-auth'}, leadMinutes:20, includeCrewNames:false};

test('endpoint methods match server verbs, paths and bodies without caller transport options', async () => {
  let sent;
  const {mutations:m, sessionPush:s} = bridge(async (url, options) => {
    sent = {url, method:options.method, body:options.body === undefined ? undefined : JSON.parse(options.body)};
    assert.equal(options.headers.get('Content-Type'), 'application/json');
    return {ok:false, status:400, json:async () => ({message:'Server validation'})};
  });
  const cases = [
    [() => m.toggle('a/b'), 'POST', '/api/selections/toggle', {performanceId:'a/b'}],
    [() => m.createEvent(event), 'POST', '/api/events', event],
    [() => m.deleteEvent('a/b'), 'DELETE', '/api/events/a%2Fb', undefined],
    [() => m.savePerformance('a/b', null, act), 'POST', '/api/events/a%2Fb/performances', act],
    [() => m.savePerformance('a/b', 'c?d', act), 'PUT', '/api/events/a%2Fb/performances/c%3Fd', act],
    [() => m.deletePerformance('a/b', 'c?d'), 'DELETE', '/api/events/a%2Fb/performances/c%3Fd', undefined],
    [() => m.bulk('a/b', {performances:[act]}), 'POST', '/api/events/a%2Fb/performances/bulk', {performances:[act]}],
    [() => m.createGroup({name:'Crew'}), 'POST', '/api/groups', {name:'Crew'}],
    [() => m.joinGroup({joinCode:'code'}), 'POST', '/api/groups/join', {joinCode:'code'}],
    [() => m.leaveGroup('a/b'), 'POST', '/api/groups/a%2Fb/leave', undefined],
    [() => m.groupCode('a/b'), 'POST', '/api/groups/a%2Fb/join-code', undefined],
    [() => m.lastEvent('a/b'), 'PUT', '/api/account/last-event', {eventId:'a/b'}],
    [() => s.signIn(login), 'POST', '/api/session', login],
    [() => s.subscribe(push), 'POST', '/api/push/subscriptions', push],
    [() => s.unsubscribe({endpoint:push.endpoint}), 'DELETE', '/api/push/subscriptions', {endpoint:push.endpoint}],
    [() => s.test({endpoint:push.endpoint}), 'POST', '/api/push/test', {endpoint:push.endpoint}]
  ];
  for (const [run, method, url, body] of cases) {
    await assert.rejects(run(), error => error.status === 400);
    assert.deepEqual(sent, {url, method, body});
  }
});

test('malformed JavaScript bridge input fails before fetch without exposing values', async () => {
  let calls = 0;
  const {mutations:m, sessionPush:s} = bridge(async () => {calls++; throw new Error('Unexpected fetch');});
  for (const run of [
    () => m.toggle(null), () => m.deleteEvent(' '), () => m.savePerformance('event', undefined, act),
    () => m.createEvent({...event, name:{private:'secret'}}), () => m.createGroup(null),
    () => m.joinGroup({joinCode:12}), () => m.bulk('event', {performances:[act, {...act, stage:null}]}),
    () => m.bulk('event', {performances:null}), () => s.signIn({...login, password:null}),
    () => s.subscribe({...push, keys:{p256dh:'secret'}}), () => s.subscribe({...push, leadMinutes:15}),
    () => s.subscribe({...push, includeCrewNames:'false'}), () => s.unsubscribe({}), () => s.test(null)
  ]) await assert.rejects(run(), error => error.name === 'RequestValidationError' && /Nothing was sent/.test(error.message) && !error.message.includes('secret'));
  assert.equal(calls, 0);
});

test('request serialization discards unknown fields and never invokes caller toJSON', async () => {
  const sent = [];
  const {mutations:m, sessionPush:s} = bridge(async (_, options) => {
    sent.push(JSON.parse(options.body));
    return {ok:false, status:400, json:async () => ({})};
  });
  const toJSON = () => {throw new Error('Must not serialize the source object');};
  await assert.rejects(s.signIn({...login, isAdmin:true, toJSON}));
  await assert.rejects(s.subscribe({...push, keys:{...push.keys, private:'discard', toJSON}, toJSON}));
  await assert.rejects(m.bulk('event', {performances:[{...act, id:'discard', poster:'discard', toJSON}], toJSON}));
  assert.deepEqual(sent, [login, push, {performances:[act]}]);
});
