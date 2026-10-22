const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const {readFileSync} = require('node:fs');
const path = require('node:path');
const source = readFileSync(path.join(__dirname,'../../wwwroot/generated/riffroster-web.js'),'utf8');
test('cross-tab channel sends only an invalidation signal and validates fallback events', () => {
  let listener, receiver, calls = 0;
  const messages = [], writes = [];
  const c = vm.createContext({console, window:{addEventListener:(_,fn) => {listener=fn;}},
    BroadcastChannel:class {constructor(){receiver=this;} postMessage(value){messages.push(value);}},
    localStorage:{setItem:(...args) => writes.push(args)},crypto:{randomUUID:() => '00000000-0000-4000-8000-000000000000'}});
  vm.runInContext(source,c);
  c.RiffRosterWeb.connectSessionChannel(() => calls++).changed();
  assert.equal(messages.length,1);
  assert.equal(messages[0],writes[0][1]);
  receiver.onmessage({data:{username:'ignored'}});
  listener({key:'riffroster.session.changed',newValue:'invalid'});
  assert.equal(calls,0);
  receiver.onmessage({data:messages[0]});
  listener({key:writes[0][0],newValue:writes[0][1]});
  assert.equal(calls,1);
  listener({key:writes[0][0],newValue:'1-11111111-1111-4111-8111-111111111111'});
  assert.equal(calls,2);
});
test('unavailable cross-tab transports do not prevent local sign-out', () => {
  const c = vm.createContext({console,window:{addEventListener(){}},localStorage:{setItem(){throw new Error('Denied');}}});
  vm.runInContext(source,c);
  assert.doesNotThrow(() => c.RiffRosterWeb.connectSessionChannel(() => {}).changed());
});
test('storage fallback delivers session changes without BroadcastChannel', () => {
  let listener, calls = 0;
  const writes = [];
  const c = vm.createContext({console,window:{addEventListener:(_,fn) => {listener=fn;}},
    localStorage:{setItem:(...args) => writes.push(args)},crypto:{randomUUID:() => '00000000-0000-4000-8000-000000000000'}});
  vm.runInContext(source,c);
  c.RiffRosterWeb.connectSessionChannel(() => calls++).changed();
  listener({key:writes[0][0],newValue:writes[0][1]});
  listener({key:writes[0][0],newValue:writes[0][1]});
  assert.equal(calls,1);
});
