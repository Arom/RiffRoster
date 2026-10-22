const {test} = require('node:test');
const assert = require('node:assert/strict');
const {readFileSync} = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const context = vm.createContext({console,TextEncoder});
vm.runInContext(readFileSync(path.join(__dirname,'../../wwwroot/generated/riffroster-web.js'),'utf8'),context);
const calendar = context.RiffRosterWeb.routeCalendar;
const stamp = '2026-09-11T12:00:00Z';
function fixture() {
  const act = {id:'act',artist:'Night Band',stage:'Main',startsAt:'2026-08-04T23:30:00-04:00',endsAt:'2026-08-05T00:30:00-04:00'};
  const event = {id:'event',name:'Festival',location:'Town',startDate:'2026-08-04',endDate:'2026-08-04',days:[{id:'day',date:'2026-08-04',label:'Tuesday',performances:[act,{...act,id:'crew-only'}]}]};
  return {currentUser:'Alice',isAdmin:false,lastSelectedEventId:null,events:[event,{...event,id:'other',days:[{...event.days[0],performances:[{...act,id:'other-act'}]}]}],
    users:['Alice','Bob'].map(username=>({username,color:'#FFB000',joinedAt:stamp})),groups:[],
    selections:[{username:'alice',performanceId:'act'},{username:'Bob',performanceId:'crew-only'},{username:'Alice',performanceId:'other-act'}]};
}
test('calendar exports only current account and event with correct overnight UTC times', () => {
  const result = calendar(fixture(),'event',stamp);
  assert.equal(result.count,1);
  assert.match(result.contents,/DTSTART:20260805T033000Z\r\nDTEND:20260805T043000Z/);
  assert.match(result.contents,/DTSTAMP:20260911T120000Z/);
  assert.equal((result.contents.match(/BEGIN:VEVENT/g)||[]).length,1);
  assert.doesNotMatch(result.contents,/crew-only|other-act|Alice|Bob/);
  assert.ok(result.contents.endsWith('END:VCALENDAR\r\n'));
});
test('calendar escapes injected properties and folds Unicode by UTF-8 bytes', () => {
  const state = fixture(); const artist = '演奏🎸'.repeat(30)+';One,Two\\Three\r\nATTENDEE:intruder';
  state.events[0].days[0].performances[0].artist = artist;
  const result = calendar(state,'event',stamp);
  for (const line of result.contents.split('\r\n')) assert.ok(Buffer.byteLength(line,'utf8')<=75);
  const unfolded = result.contents.replace(/\r\n /g,'');
  assert.ok(unfolded.includes('SUMMARY:'+artist.replace(/\\/g,'\\\\').replace(/\r\n/g,'\\n').replace(/;/g,'\\;').replace(/,/g,'\\,')));
  assert.doesNotMatch(unfolded,/\r\nATTENDEE:/);
  assert.doesNotMatch(result.contents,/\uFFFD/);
});
test('calendar identifiers survive edits and unsafe event names produce safe filenames', () => {
  const state = fixture(); const before = calendar(state,'event',stamp);
  state.events[0].name = '../../CON: 🎸'; state.events[0].days[0].performances[0].artist='Renamed';
  const after = calendar(state,'event','2026-09-12T12:00:00Z');
  assert.equal(before.contents.match(/UID:.+/)[0],after.contents.match(/UID:.+/)[0]);
  assert.match(after.filename,/^riffroster-[a-zA-Z0-9-]+-route\.ics$/);
});
test('calendar refuses empty routes, missing events and invalid dates', () => {
  assert.throws(()=>calendar({...fixture(),selections:[]},'event',stamp),/Pick at least one/);
  assert.throws(()=>calendar(fixture(),'missing',stamp),/Choose a running order/);
  assert.throws(()=>calendar(fixture(),'event','invalid'),/date cannot be exported/);
});
