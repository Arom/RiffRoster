const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const context = vm.createContext({console});
vm.runInContext(readFileSync(path.join(__dirname, '../../wwwroot/generated/riffroster-web.js'), 'utf8'), context);
const decode = context.RiffRosterWeb.decodeState;
function fixture() {
  const user = {username:'Alice', color:'#FFB000', joinedAt:'2026-08-04T12:00:00.1234567+02:00'};
  return {currentUser:'Alice', isAdmin:false, lastSelectedEventId:null,
    events:[{id:'event', name:'Festival', location:'', startDate:'2026-08-04', endDate:'2026-08-04', days:[{
      id:'day', date:'2026-08-04', label:'Tuesday', performances:[{id:'act', artist:'Band', stage:'Main', startsAt:'2026-08-04T23:30:00+02:00', endsAt:'2026-08-05T00:30:00+02:00'}]
    }]}], users:[user], selections:[{username:'alice', performanceId:'act'}],
    groups:[{id:'group', name:'Crew', createdBy:'Alice', isOwner:true, members:[user]}]};
}
test('state decoder copies public fields and retains overnight and legacy timestamp precision', () => {
  const input = fixture(); input.secret = 'discard'; input.users[0].passwordHash = 'discard';
  const output = decode(input);
  assert.equal(output.users[0].passwordHash, undefined);
  assert.equal(output.secret, undefined);
  assert.equal(output.users[0].joinedAt, input.users[0].joinedAt);
  assert.equal(output.events[0].days[0].performances[0].endsAt, input.events[0].days[0].performances[0].endsAt);
  input.users[0].username = 'Changed'; assert.equal(output.users[0].username, 'Alice');
});
test('state decoder accepts empty events and a stale preferred event', () => {
  const input = fixture(); input.events = []; input.selections = []; input.groups = []; input.lastSelectedEventId = 'deleted';
  assert.equal(decode(input).events.length, 0);
});
test('state decoder refuses invalid fields, dates, duplicate identities and dangling references', () => {
  const cases = [
    s => { delete s.groups; }, s => { s.isAdmin = 'true'; }, s => { s.currentUser = 'Missing'; },
    s => { s.users[0].color = 'red;position:fixed'; }, s => { s.users.push({...s.users[0], username:'ALICE'}); },
    s => { s.events.push(s.events[0]); }, s => { s.selections[0].performanceId = 'missing'; },
    s => { s.selections[0].username = 'missing'; }, s => { s.selections.push(s.selections[0]); },
    s => { s.groups[0].members = []; }, s => { s.events[0].startDate = '2026-02-30'; },
    s => { s.events[0].days[0].performances[0].startsAt = '2026-08-04T23:30:00'; },
    s => { s.events[0].days[0].performances[0].endsAt = '2026-08-04T20:00:00Z'; },
    s => { s.events[0].days[0].performances[0].startsAt = '2026-02-30T20:00:00Z'; }
  ];
  for (const mutate of cases) { const input = fixture(); mutate(input); assert.throws(() => decode(input), /invalid running order/); }
  for (const input of [null, [], {}, 'secret-response']) assert.throws(() => decode(input), /invalid running order/);
});
