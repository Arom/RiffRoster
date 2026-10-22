const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const {readFileSync} = require('node:fs');
const path = require('node:path');
const context = vm.createContext({console});
vm.runInContext(readFileSync(path.join(__dirname, '../../wwwroot/generated/riffroster-web.js'), 'utf8'), context);
const {scheduleState:reduce, scheduleDateForPerformance:dayFor} = context.RiffRosterWeb;
const act = {id:'act', artist:'Band', stage:'Main', startsAt:'2026-08-04T20:00:00+02:00', endsAt:'2026-08-04T21:00:00+02:00'};
function fixture() {
  const state = {currentUser:'Alice', isAdmin:true, lastSelectedEventId:'one', users:[], groups:[],
    selections:[{username:'Alice',performanceId:'act'}, {username:'Bob',performanceId:'act'}, {username:'Alice',performanceId:'other'}],
    events:[{id:'one', days:[{id:'d1',date:'2026-08-04',performances:[act]}, {id:'d2',date:'2026-08-05',performances:[]}]},
      {id:'two', days:[{id:'other-day',date:'2026-08-04',performances:[{...act,id:'other'}]}]}]};
  function freeze(value) {if (value && typeof value === 'object') {Object.values(value).forEach(freeze);Object.freeze(value);}}
  freeze(state); return state;
}
test('confirmed picks are idempotent and preserve other users and performances', () => {
  const original = fixture();
  const once = reduce.pick(original, {username:'alice',performanceId:'act',selected:true});
  const twice = reduce.pick(once, {username:'alice',performanceId:'act',selected:true});
  assert.equal(twice.selections.length,3);
  const removed = reduce.pick(twice, {username:'ALICE',performanceId:'act',selected:false});
  assert.equal(removed.selections.length,2);
  assert.ok(removed.selections.some(pick => pick.username === 'Bob'));
  assert.equal(original.selections.length,3);
});
test('act edits move stable IDs between days without losing picks or altering another event', () => {
  const state = fixture();
  const saved = {...act,artist:'Renamed',startsAt:'2026-08-06T01:00:00+02:00',endsAt:'2026-08-06T02:00:00+02:00'};
  const result = reduce.savePerformance(state,'one',saved);
  assert.equal(result.dayId,'d2');
  assert.equal(result.state.events[0].days[0].performances.length,0);
  assert.equal(result.state.events[0].days[1].performances[0].id,'act');
  assert.equal(result.state.events[1],state.events[1]);
  assert.equal(result.state.selections,state.selections);
  assert.equal(reduce.savePerformance(result.state,'one',saved).state.events[0].days[1].performances.length,1);
});
test('unknown destination day preserves the entire prior snapshot until reload', () => {
  const state = fixture();
  const result = reduce.savePerformance(state,'one',{...act,startsAt:'2027-01-01T20:00:00Z'});
  assert.equal(result.dayId,null);
  assert.equal(result.state,state);
  assert.equal(reduce.savePerformance(state,'missing',act).state,state);
});
test('act deletion targets the originating event and removes only related picks', () => {
  const state = fixture();
  const result = reduce.deletePerformance(state,'one','act');
  assert.equal(result.events[0].days[0].performances.length,0);
  assert.equal(result.events[1],state.events[1]);
  assert.equal(result.selections.length,1);
  assert.equal(result.selections[0].performanceId,'other');
  assert.equal(state.events[0].days[0].performances.length,1);
});
test('festival days use explicit local dates at the 06:00 boundary and year/leap transitions', () => {
  for (const [startsAt, expected] of [
    ['2026-08-05T05:59:59-10:00','2026-08-04'], ['2026-08-05T06:00:00+14:00','2026-08-05'],
    ['2026-01-01T01:00:00Z','2025-12-31'], ['2024-03-01T01:00:00Z','2024-02-29']
  ]) assert.equal(dayFor({...act,startsAt}),expected);
});
test('event reconciliation keeps a single copy of each stable event ID', () => {
  const state = fixture();
  const saved = {...state.events[0],name:'Renamed'};
  assert.equal(reduce.event(state,saved).events.length,2);
  const added = reduce.event(state,{...saved,id:'new'});
  assert.equal(reduce.event(added,{...saved,id:'new'}).events.length,3);
  assert.equal(state.events.length,2);
});
