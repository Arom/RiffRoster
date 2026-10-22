const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const {readFileSync} = require('node:fs');
const path = require('node:path');
const c = vm.createContext({console});
vm.runInContext(readFileSync(path.join(__dirname, '../../wwwroot/generated/riffroster-web.js'), 'utf8'),c);
const {navigation:n,comparisonPreferences:p} = c.RiffRosterWeb;
const users = [{username:'Alice'},{username:'Bob'}];
const state = {events:[{id:'one',days:[{id:'d1'},{id:'d2'}]},{id:'two',days:[]}],users,groups:[{id:'crew',members:users}]};
function storage() {const data = new Map(); return {getItem:key => data.get(key) ?? null,setItem:(key,value) => data.set(key,value)};}
test('event navigation retains a valid day and recovers from deleted events and empty schedules', () => {
  assert.equal(n.event(state,'one','d2').activeDayId,'d2');
  assert.equal(n.event(state,'missing','d2').activeEventId,'one');
  assert.equal(n.event(state,'one','invalid').activeDayId,'d1');
  assert.equal(n.event(state,'two','d1').activeDayId,null);
  assert.equal(n.event({...state,events:[]},'one').activeEventId,null);
});
test('crew context excludes nonmembers and falls back to the signed-in user', () => {
  assert.equal(n.group(state,'missing'),null);
  assert.equal(n.group(state,'crew'),'crew');
  assert.deepEqual(Array.from(n.contextUsers(state,'alice',null),user => user.username),['Alice']);
  assert.equal(n.contextUsers(state,'Alice','crew').length,2);
  assert.equal(n.contextUsers(null,'Alice','crew').length,0);
});
test('comparison preferences isolate accounts, events and crews, including explicit empty choices', () => {
  const store = storage();
  p.save(store,'Alice','one','crew',new Set());
  p.save(store,'Alice','two','crew',new Set(['Bob']));
  assert.equal(p.restore(store,'Alice','one','crew',users).size,0);
  assert.deepEqual([...p.restore(store,'Alice','two','crew',users)],['Bob']);
  assert.equal(p.restore(store,'Bob','one','crew',users).size,2);
  assert.equal(p.restore(store,'Alice','one','other',users).size,2);
  assert.notEqual(p.key('Alice',null,null),p.key('Alice','-','-'));
});
test('legacy choices migrate per event while stale members are filtered and casing is normalized', () => {
  const store = storage();
  store.setItem('riffroster.compareUsers.alice.crew',JSON.stringify(['bob','Departed']));
  const choice = p.restore(store,'Alice','one','crew',users);
  assert.deepEqual([...choice],['Bob']);
  p.save(store,'Alice','one','crew',choice);
  store.setItem('riffroster.compareUsers.alice.crew',JSON.stringify(['Alice']));
  assert.deepEqual([...p.restore(store,'Alice','one','crew',users)],['Bob']);
  assert.deepEqual([...p.restore(store,'Alice','two','crew',users)],['Alice']);
});
test('corrupt or unavailable storage falls back without preventing navigation', () => {
  const store = storage();
  for (const raw of ['{','[null]','{}','42']) {
    store.setItem(p.key('Alice','one','crew'),raw);
    assert.equal(p.restore(store,'Alice','one','crew',users).size,2);
  }
  const denied = {getItem() {throw new Error('Denied');},setItem() {throw new Error('Denied');}};
  assert.equal(p.restore(denied,'Alice','one','crew',users).size,2);
  assert.doesNotThrow(() => p.save(denied,'Alice','one','crew',new Set()));
});
