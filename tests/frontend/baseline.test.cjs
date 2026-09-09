const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

// Exercise the built TypeScript bridge and legacy helpers; DOM boot is suppressed.
function browserHelpers() {
  const context = vm.createContext({
    localStorage: { getItem: () => null }, document: { addEventListener() {} },
    console, URL, setTimeout, clearTimeout
  });
  vm.runInContext(readFileSync(path.join(__dirname, '../../wwwroot/generated/riffroster-web.js'), 'utf8'), context);
  vm.runInContext(readFileSync(path.join(__dirname, '../../wwwroot/app.js'), 'utf8'), context);
  return expression => JSON.parse(JSON.stringify(vm.runInContext(expression, context)));
}

test('time geometry is proportional, offset aware and compact for short acts', () => {
  const run = browserHelpers();
  const result = run(`(() => {
    const acts = [{id:'a', startsAt:'2026-08-04T23:30:00+02:00', endsAt:'2026-08-05T00:30:00+02:00'},
      {id:'b', startsAt:'2026-08-05T00:00:00+02:00', endsAt:'2026-08-05T00:10:00+02:00'}];
    const timeline = buildTimeline(acts);
    return {timeline, a:positionOnTimeline(acts[0], timeline), b:positionOnTimeline(acts[1], timeline), clashes:[...findConflicts(acts)]};
  })()`);
  assert.equal(result.a.height, 127);
  assert.equal(result.b.height, 32);
  assert.equal(result.b.top - result.a.top, 66);
  assert.equal(result.b.compact, true);
  assert.ok(result.timeline.ticks.some(tick => tick.label === '00:00'));
  assert.deepEqual(result.clashes.map(pair => pair.map(act => act.id)), [['a', 'b']]);
});

test('back to back performances do not clash', () => {
  const run = browserHelpers();
  assert.deepEqual(run(`[...findConflicts([
    {id:'a', startsAt:'2026-08-04T20:00:00Z', endsAt:'2026-08-04T21:00:00Z'},
    {id:'b', startsAt:'2026-08-04T21:00:00Z', endsAt:'2026-08-04T22:00:00Z'}])]`), []);
});

test('typed geometry rejects invalid and empty schedules instead of producing NaN layouts', () => {
  const run = browserHelpers();
  assert.throws(() => run('buildTimeline([])'), /empty schedule/);
  assert.throws(() => run(`buildTimeline([{startsAt:'2026-08-04T20:00:00',endsAt:'2026-08-04T21:00:00Z'}])`), /explicit offset/);
  assert.throws(() => run(`buildTimeline([{startsAt:'2026-08-04T21:00:00Z',endsAt:'2026-08-04T20:00:00Z'}])`), /positive duration/);
});

test('typed geometry aligns the same instant across different explicit offsets', () => {
  const run = browserHelpers();
  const result = run(`(() => {
    const acts = [{startsAt:'2026-08-04T20:00:00+02:00',endsAt:'2026-08-04T21:00:00+02:00'},
      {startsAt:'2026-08-04T18:00:00Z',endsAt:'2026-08-04T19:00:00Z'}];
    const timeline = buildTimeline(acts);
    return acts.map(act => positionOnTimeline(act, timeline));
  })()`);
  assert.deepEqual(result[0], result[1]);
});

test('OCR fixture yields deduplicated review rows and repairs common digit mistakes', () => {
  const run = browserHelpers();
  const text = readFileSync(path.join(__dirname, '../fixtures/poster-text.txt'), 'utf8');
  const rows = run(`parsePosterSchedule('', ${JSON.stringify(text)}, {days:[{date:'2026-08-04', label:'Tuesday', performances:[{stage:'Main A'}]}]})`);
  assert.equal(rows.length, 2);
  assert.equal(rows[0].artist, 'Fixture Band');
  assert.equal(rows[0].start, '20:00');
  assert.equal(rows[1].end, '00:15');
  assert.ok(rows.every(row => row.needsReview));
});

test('OCR rejects impossible time tokens and retains missing end for correction', () => {
  const run = browserHelpers();
  assert.deepEqual(run(`findPosterTimes('29:99 Fake act')`), []);
  const parsed = run(`extractPerformancesFromLine({text:'20:00 Fixture Act', left:0, top:0, width:200, height:20, confidence:40})`);
  assert.equal(parsed[0].end, '');
  assert.equal(parsed[0].artist, 'Fixture Act');
  assert.equal(run(`scheduleMinutes('01:00')`), 1500);
});

test('poster payload assigns after midnight to next calendar date without image fields', () => {
  const run = browserHelpers();
  const result = run(`posterRowToPayload({dayDate:'2026-08-04', start:'23:30', end:'00:15', artist:' Fixture ', stage:' Main '})`);
  assert.match(result.startsAt, /^2026-08-04T23:30:00/);
  assert.match(result.endsAt, /^2026-08-05T00:15:00/);
  assert.deepEqual(Object.keys(result).sort(), ['artist', 'endsAt', 'stage', 'startsAt']);
  assert.equal(result.artist, 'Fixture');
});

test('service worker skips API cache and maps push notification data to a deep link', async () => {
  const handlers = {};
  const shown = [];
  const navigated = [];
  const context = vm.createContext({URL, self: {
    addEventListener: (name, handler) => { handlers[name] = handler; },
    location: {origin:'https://riffroster.example.test'},
    registration: {showNotification: async (title, options) => shown.push({title, options})},
    clients: {matchAll: async () => [{navigate: async url => navigated.push(url), focus: async () => {}}]}
  }});
  vm.runInContext(readFileSync(path.join(__dirname, '../../wwwroot/service-worker.js'), 'utf8'), context);
  handlers.fetch({request:{method:'GET', url:'https://riffroster.example.test/api/state'}, respondWith:() => assert.fail('API must not enter generic cache')});
  let pending;
  handlers.push({data:{json:() => ({title:'Fixture reminder', data:{url:'/?event=fixture&performance=act'}})}, waitUntil:p => {pending = p;}});
  await pending;
  assert.equal(shown[0].title, 'Fixture reminder');
  handlers.notificationclick({notification:{data:shown[0].options.data, close() {}}, waitUntil:p => {pending = p;}});
  await pending;
  assert.equal(navigated[0], 'https://riffroster.example.test/?event=fixture&performance=act');
});
