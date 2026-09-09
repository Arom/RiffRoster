const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const bundle = readFileSync(path.join(__dirname, '../../wwwroot/generated/riffroster-web.js'), 'utf8');
function client(response, capture = () => {}) {
  const context = vm.createContext({ Headers, console, fetch: async (url, options) => { capture(url, options); return response; } });
  vm.runInContext(bundle, context);
  return context.RiffRosterWeb.requestJson;
}
test('transport preserves JSON, methods, body and Headers overrides', async () => {
  let sent;
  const data = { saved: true };
  const request = client({ ok: true, status: 200, json: async () => data }, (url, options) => { sent = { url, ...options }; });
  assert.equal(await request('/api/example', { method: 'POST', body: '{}', headers: new Headers({ 'X-Test': 'yes' }) }), data);
  assert.equal(sent.method, 'POST');
  assert.equal(sent.body, '{}');
  assert.equal(sent.headers.get('content-type'), 'application/json');
  assert.equal(sent.headers.get('x-test'), 'yes');
});
test('transport handles null errors and preserves authentication status messages', async () => {
  for (const [status, url, payload, expected] of [
    [401, '/api/session', {message:'Wrong password'}, 'Wrong password'],
    [401, '/api/state', null, 'Your session has expired. Sign in again.'],
    [403, '/api/events', {}, 'Administrator access is required for that action.'],
    [429, '/api/session', {}, 'Too many attempts. Wait a minute and try again.'],
    [503, '/api/state', {message:{unsafe:true}}, 'Request failed (503)']
  ]) {
    await assert.rejects(client({ok:false, status, json:async () => payload})(url), error => error.status === status && error.message === expected);
  }
});
test('transport refuses malformed success JSON but accepts no-content responses', async () => {
  const json = async () => { throw new SyntaxError('invalid'); };
  await assert.rejects(client({ok:true, status:200, json})('/api/state'), /invalid response/);
  assert.equal(JSON.stringify(await client({ok:true, status:204, json})('/api/session/logout')), '{}');
});
