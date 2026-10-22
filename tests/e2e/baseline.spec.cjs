const { test, expect } = require('@playwright/test');
const { readFileSync } = require('node:fs');
const path = require('node:path');

async function login(page, name, admin = false) {
  await page.goto('/');
  await page.locator('#login-name').fill(`private-${name}`);
  await page.locator('#display-name').fill(name);
  await page.locator('#account-password').fill('fixture-password-only');
  await page.locator('#access-code').fill(admin ? 'admin-local-only' : 'friends-local');
  await page.locator('#login-form button[type="submit"]').click();
  await expect(page.locator('#app')).toBeVisible();
}

test('login, personal pick, route and reload retain selection', async ({page}) => {
  await login(page, 'JourneyAlice');
  const pick = page.locator('.pick-button').first();
  const id = await pick.getAttribute('data-id');
  await pick.click();
  await expect(page.locator(`[data-id="${id}"].pick-button`)).toHaveAttribute('aria-pressed', 'true');
  await page.locator('.mobile-nav [data-view="mine"]').click();
  await expect(page.locator('#my-count')).toHaveText('1');
  await page.reload();
  await expect(page.locator(`[data-id="${id}"].pick-button`)).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#group-context')).toContainText('Just you');
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.evaluate(() => RiffRosterWeb.offline.cache(appState.data));
  await page.context().setOffline(true);
  await page.locator(`[data-id="${id}"].pick-button`).click();
  await expect(page.locator('#sync-status')).toContainText('1 queued pick');
  await page.reload();
  await expect(page.locator('#app')).toBeVisible();
  await page.locator('#profile-button').click();
  await expect(page.locator('#profile-modal')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.locator(`[data-id="${id}"].pick-button`)).toHaveAttribute('aria-pressed','false');
  let lostResponse = false;
  await page.route('**/api/selections/*', async route => {
    if (route.request().method() === 'PUT' && !lostResponse) {
      lostResponse = true;
      await route.fetch();
      return route.abort('failed');
    }
    return route.continue();
  });
  await page.context().setOffline(false);
  await expect.poll(() => lostResponse).toBe(true);
  await expect(page.locator('#sync-status')).toContainText('1 queued pick');
  await page.unroute('**/api/selections/*');
  await page.locator('#sync-retry').click();
  await expect(page.locator('#sync-status')).toContainText('All picks synced');
  await page.reload();
  await expect(page.locator(`[data-id="${id}"].pick-button`)).toHaveAttribute('aria-pressed','false');
  // A newer explicit action must survive an older acknowledgement during reconnect.
  await page.context().setOffline(true);
  await page.locator(`[data-id="${id}"].pick-button`).click();
  await expect(page.locator('#sync-status')).toContainText('1 queued pick');
  let release;
  let held = false;
  const gate = new Promise(resolve => { release = resolve; });
  await page.route('**/api/selections/*', async route => {
    const response = await route.fetch();
    held = true;
    await gate;
    await route.fulfill({response});
  });
  await page.context().setOffline(false);
  await expect.poll(() => held).toBe(true);
  await page.locator(`[data-id="${id}"].pick-button`).click();
  await expect(page.locator(`[data-id="${id}"].pick-button`)).toHaveAttribute('aria-pressed','false');
  release();
  await expect.poll(() => page.evaluate(() => RiffRosterWeb.offline.pending(appState.username))).toBe(1);
  await page.unroute('**/api/selections/*');
  await page.locator('#sync-retry').click();
  await expect(page.locator('#sync-status')).toContainText('All picks synced');
  await page.reload();
  await expect(page.locator(`[data-id="${id}"].pick-button`)).toHaveAttribute('aria-pressed','false');
});

test('React day tabs support keyboard navigation and preserve timeline alignment', async ({page}, testInfo) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await login(page, 'KeyboardDayUser');
  const tabs = page.locator('#day-tabs [role="tab"]');
  await expect(tabs).toHaveCount(5);
  await tabs.first().focus();
  await tabs.first().press('ArrowRight');
  await expect(tabs.nth(1)).toBeFocused();
  await expect(tabs.nth(1)).toHaveAttribute('aria-selected', 'true');
  await expect(tabs.first()).toHaveAttribute('tabindex', '-1');
  await expect(page.locator('#compare-day')).toHaveValue(await tabs.nth(1).getAttribute('data-day-id'));
  await tabs.nth(1).press('End');
  await expect(tabs.last()).toBeFocused();
  await expect(tabs.last()).toHaveAttribute('aria-selected', 'true');
  await tabs.last().press('ArrowRight');
  await expect(tabs.first()).toBeFocused();
  await expect(tabs.first()).toHaveAttribute('aria-selected', 'true');
  await expect(page.locator('.timeline-time-axis')).toBeVisible();
  expect(await page.locator('.timeline-performance').first().evaluate(node => Number.parseFloat(node.style.height))).toBeGreaterThan(0);
  expect(errors).toEqual([]);
  expect(await tabs.evaluateAll(nodes => nodes.every(node => {
    const label = node.querySelector('span').getBoundingClientRect();
    const button = node.getBoundingClientRect();
    return label.left >= button.left && label.right <= button.right;
  }))).toBe(true);
  await page.screenshot({path:testInfo.outputPath('day-navigation-mobile.png')});
  await page.setViewportSize({width:1440, height:900});
  await page.screenshot({path:testInfo.outputPath('day-navigation-desktop.png')});
});

test('the typed frontend bundle is available offline without caching authenticated API data', async ({page, context}) => {
  await login(page, 'OfflineShellUser');
  await page.evaluate(() => navigator.serviceWorker.ready);
  await expect.poll(() => page.evaluate(() => Boolean(navigator.serviceWorker.controller))).toBe(true);
  await context.setOffline(true);
  const result = await page.evaluate(async () => {
    const response = await fetch(document.querySelector('script[src*="generated/riffroster-web.js"]').src);
    const script = await response.text();
    let apiFailed = false;
    try { await fetch('/api/state'); } catch { apiFailed = true; }
    return {ok:response.ok, isScript:script.includes('RiffRosterWeb'), apiFailed};
  });
  expect(result).toEqual({ok:true, isScript:true, apiFailed:true});
  await context.setOffline(false);
});

test('admin creates and switches events, edits stable ID and confirms deletion', async ({page}) => {
  await login(page, 'JourneyAdmin', true);
  await page.locator('[data-action="open-event-modal"]:visible').first().click();
  const form = page.locator('#event-form');
  await form.locator('[name="name"]').fill('Browser fixture festival');
  await form.locator('[name="location"]').fill('Synthetic venue');
  await form.locator('[name="startDate"]').fill('2026-08-04');
  await form.locator('[name="endDate"]').fill('2026-08-04');
  await form.getByRole('button', {name:'Create running order'}).click();
  await expect(page.locator('#event-select option:checked')).toHaveText('Browser fixture festival');
  const eventId = await page.locator('#event-select').inputValue();
  await page.locator('[data-action="open-performance-modal"]:visible').first().click();
  const act = page.locator('#performance-form');
  await act.locator('[name="artist"]').fill('Browser fixture act');
  await act.locator('[name="stage"]').fill('Main');
  await act.locator('[name="startsAt"]').fill('2026-08-04T20:00');
  await act.locator('[name="endsAt"]').fill('2026-08-04T21:00');
  await page.locator('#performance-submit').click();
  const id = await page.locator('.pick-button').getAttribute('data-id');
  await page.locator('.pick-button').click();
  await expect(page.locator('.pick-button')).toHaveAttribute('aria-pressed', 'true');
  await page.locator('[data-action="open-edit-performance"]').click();
  await act.locator('[name="artist"]').fill('Renamed fixture act');
  await page.locator('#performance-submit').click();
  await expect(page.locator(`[data-id="${id}"].pick-button`)).toHaveAttribute('aria-pressed', 'true');
  await page.locator('#event-select').selectOption('brutal-assault-2026');
  await page.locator('#event-select').selectOption(eventId);
  await page.reload();
  await expect(page.locator('#event-select')).toHaveValue(eventId);
  await page.locator('[data-action="open-edit-performance"]').click();
  page.once('dialog', async dialog => {
    expect(dialog.message()).toContain('Renamed fixture act');
    expect(dialog.message()).toContain('pick');
    await dialog.accept();
  });
  await page.locator('#performance-delete').click();
  await expect(page.locator(`[data-performance-id="${id}"]`)).toHaveCount(0);
  await expect(page.locator('#my-count')).toHaveText('0');
});

test('create crew, second account joins and compares shared picks', async ({page, browser}) => {
  await login(page, 'CrewOwner');
  await page.locator('.pick-button').first().click();
  await page.locator('[data-action="open-groups-modal"]:visible').first().click();
  await page.locator('#create-group-form [name="name"]').fill('Browser crew');
  await page.locator('#create-group-form button').click();
  await expect(page.locator('#new-group-code')).not.toBeEmpty();
  const code = await page.locator('#new-group-code').textContent();
  const other = await browser.newContext({baseURL:'http://127.0.0.1:5189', viewport:{width:390,height:844}});
  try {
    const friend = await other.newPage();
    await login(friend, 'CrewFriend');
    await friend.locator('.pick-button').first().click();
    await friend.locator('[data-action="open-groups-modal"]:visible').first().click();
    await friend.locator('#join-group-form [name="joinCode"]').fill(code);
    await friend.locator('#join-group-form button').click();
    await expect(friend.locator('#group-memberships')).toContainText('Browser crew');
    await friend.locator('#groups-modal [data-action="close-modal"]').click();
    await friend.locator('.mobile-nav [data-view="compare"]').click();
    await expect(friend.locator('#people-picker')).toContainText('CrewOwner');
    await expect(friend.locator('#people-picker')).toContainText('CrewFriend');
    await expect(friend.locator('#shared-route')).toContainText('Mordloch');
    const firstEvent = await friend.locator('#event-select').inputValue();
    const secondEvent = await friend.locator('#event-select option').evaluateAll((options, current) => options.find(option => option.value !== current).value, firstEvent);
    const ownerChip = friend.locator('#people-picker [data-person="CrewOwner"]');
    await ownerChip.click();
    await expect(ownerChip).toHaveAttribute('aria-pressed', 'false');
    await friend.locator('#event-select').selectOption(secondEvent);
    await expect(ownerChip).toHaveAttribute('aria-pressed', 'true');
    await friend.locator('#event-select').selectOption(firstEvent);
    await expect(ownerChip).toHaveAttribute('aria-pressed', 'false');
    await friend.reload();
    await friend.locator('.mobile-nav [data-view="compare"]').click();
    await expect(ownerChip).toHaveAttribute('aria-pressed', 'false');
    await page.reload();
    await page.locator('[data-action="open-groups-modal"]:visible').first().click();
    await page.locator('[data-owner-target]').selectOption('CrewFriend');
    await page.evaluate(() => { document.documentElement.style.fontSize='200%'; });
    await page.locator('[data-action="transfer-owner"]').focus();
    await page.screenshot({path:'work/phase4-owner-transfer-text-zoom.png'});
    await page.evaluate(() => { document.documentElement.style.fontSize=''; });
    page.once('dialog', dialog => dialog.accept());
    await page.locator('[data-action="transfer-owner"]').click();
    await expect(page.locator('[data-action="new-group-code"]')).toHaveCount(0);
    await expect(page.locator('#group-memberships')).toContainText('CrewOwner');
    await friend.reload();
    await friend.locator('[data-action="open-groups-modal"]:visible').first().click();
    await expect(friend.locator('[data-action="new-group-code"]')).toBeVisible();
    await expect(friend.locator('[data-owner-target] option[value="CrewOwner"]')).toHaveCount(1);
  } finally { await other.close(); }
});

test('poster OCR review sends structured rows and revokes the preview on publication', async ({page}) => {
  const text = readFileSync(path.join(__dirname, '../fixtures/poster-text.txt'), 'utf8');
  await page.addInitScript({content:`
    window.__terminated = 0;
    window.Tesseract = {OEM:{LSTM:1}, PSM:{SPARSE_TEXT:11}, createWorker:async () => ({
      setParameters:async () => {}, recognize:async () => ({data:{text:${JSON.stringify(text)}, tsv:''}}),
      terminate:async () => {window.__terminated++;}
    })};` });
  await page.addInitScript(() => {
    window.__revoked = [];
    const revoke = URL.revokeObjectURL.bind(URL);
    URL.revokeObjectURL = url => { window.__revoked.push(url); revoke(url); };
  });
  await login(page, 'PosterAdmin', true);
  const mutations = [];
  page.on('request', request => { if (request.method() !== 'GET') mutations.push(request); });
  await page.locator('[data-action="open-poster-import"]').click();
  await page.locator('#poster-file').setInputFiles(path.join(__dirname, '../../wwwroot/icons/icon-192.png'));
  await expect(page.locator('#poster-preview')).toHaveAttribute('src', /^blob:/);
  const url = await page.locator('#poster-preview').getAttribute('src');
  await page.locator('#poster-ocr-start').click();
  await expect(page.locator('#poster-import-rows tr')).toHaveCount(2);
  await expect(page.locator('#poster-preview')).toBeVisible();
  await page.locator('#poster-import-rows [data-field="artist"]').first().fill('Corrected fixture');
  const submitted = page.waitForRequest(request => request.url().endsWith('/performances/bulk'));
  await page.locator('#poster-import-submit').click();
  const request = await submitted;
  expect(Object.keys(request.postDataJSON())).toEqual(['performances']);
  expect(request.postDataJSON().performances[0].artist).toBe('Corrected fixture');
  expect(request.postData()).not.toMatch(/data:image|base64|blob:|synthetic-key/);
  await expect(page.locator('#poster-import-modal')).not.toBeVisible();
  expect(await page.evaluate(() => window.__revoked)).toContain(url);
  expect(await page.evaluate(() => window.__terminated)).toBe(1);
  expect(mutations.every(item => item.headers()['content-type']?.includes('application/json'))).toBe(true);
  expect(await page.evaluate(() => JSON.stringify({...localStorage}))).not.toContain('data:image');
});

test('mocked notification permission is contextual and supports enable, test and disable', async ({page}) => {
  await page.addInitScript(() => {
    window.__permissionRequests = 0;
    let permission = 'default';
    Object.defineProperty(Notification, 'permission', {get:() => permission});
    Notification.requestPermission = async () => {window.__permissionRequests++; return permission = 'granted';};
    let subscription = null;
    const registration = {pushManager:{
      getSubscription:async () => subscription,
      subscribe:async () => subscription = {
        endpoint:'https://push.example.test/mock',
        toJSON:() => ({endpoint:'https://push.example.test/mock', keys:{p256dh:'fixture-key',auth:'fixture-auth'}}),
        unsubscribe:async () => {subscription = null; return true;}
      }
    }};
    navigator.serviceWorker.register = async () => registration;
  });
  await page.route('**/api/push/**', route => {
    const request = route.request();
    if (request.method() === 'DELETE') return route.fulfill({status:204});
    const body = request.url().endsWith('public-key') ? {publicKey:Buffer.from([4, ...Array(64).fill(1)]).toString('base64url')}
      : request.url().endsWith('/test') ? {sent:true}
      : {enabled:true, leadMinutes:request.postDataJSON().leadMinutes, includeCrewNames:request.postDataJSON().includeCrewNames};
    return route.fulfill({contentType:'application/json', body:JSON.stringify(body)});
  });
  await login(page, 'NotifyUser');
  expect(await page.evaluate(() => window.__permissionRequests)).toBe(0);
  await page.locator('[data-action="open-notification-modal"]').click();
  await expect(page.locator('#notification-status')).toHaveAttribute('data-status', 'off');
  await page.locator('#enable-notifications').click();
  await expect(page.locator('#notification-status')).toHaveAttribute('data-status', 'on');
  expect(await page.evaluate(() => window.__permissionRequests)).toBe(1);
  const sent = page.waitForRequest('**/api/push/test');
  await page.locator('#test-notification').click();
  expect((await sent).postDataJSON().endpoint).toBe('https://push.example.test/mock');
  await page.locator('#disable-notifications').click();
  await expect(page.locator('#notification-status')).toHaveAttribute('data-status', 'off');
  let lateSaves = 0;
  page.on('request', request => {
    if (request.url().endsWith('/api/push/subscriptions') && request.method() === 'POST') lateSaves++;
  });
  await page.evaluate(async () => {
    const registration = await registerServiceWorker();
    registration.pushManager.subscribe = () => new Promise(resolve => {
      window.releaseLateSubscription = () => resolve({
        endpoint:'https://push.example.test/late',
        unsubscribe:async () => {window.lateSubscriptionRemoved = true;return true;}
      });
    });
    window.pendingEnable = enableNotifications();
  });
  await expect.poll(() => page.evaluate(() => typeof window.releaseLateSubscription)).toBe('function');
  await page.evaluate(() => {
    window.pendingRefresh = refreshNotificationState({sync:true});
    window.pendingSignOut = signOut();
  });
  await page.evaluate(async () => {
    window.releaseLateSubscription();
    await Promise.all([window.pendingEnable, window.pendingRefresh, window.pendingSignOut]);
  });
  expect(await page.evaluate(() => window.lateSubscriptionRemoved)).toBe(true);
  expect(lateSaves).toBe(0);
  await expect(page.locator('#app')).toBeHidden();
});

test('React cards retain keyboard focus and attendee controls across updates', async ({page}) => {
  await login(page, 'CardFocusUser');
  const pick = page.locator('.pick-button').first();
  const id = await pick.getAttribute('data-id');
  await pick.focus();
  await pick.press('Enter');
  await expect(pick).toHaveAttribute('aria-pressed', 'true');
  await expect(pick).toBeFocused();
  await page.locator('.mobile-nav [data-view="mine"]').click();
  const card = page.locator(`[data-performance-id="${id}"]`);
  const attendees = card.locator('.attendance-avatars--expandable');
  await attendees.click();
  await expect(attendees).toHaveAttribute('aria-expanded', 'true');
  await expect(card.locator('.attendance-names-popover')).toBeVisible();
  await expect(card.locator('.attendance-names-popover')).toContainText('CardFocusUser');
  await attendees.click();
  await expect(card.locator('.attendance-names-popover')).toBeHidden();
  await card.locator('.pick-button').click();
  await expect(page.locator('#schedule-grid .empty-state')).toContainText('No picks yet');
  await page.locator('.mobile-nav [data-view="schedule"]').click();
  await expect(page.locator(`[data-performance-id="${id}"] .pick-button`)).toHaveAttribute('aria-pressed', 'false');
});

test('invalid state is rejected before rendering and a later valid load recovers', async ({page}) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/api/state', route => route.fulfill({status:200, contentType:'application/json', body:JSON.stringify({currentUser:'private-marker', events:null})}));
  await page.goto('/');
  await expect(page.locator('#login-error')).toContainText('invalid running order');
  await expect(page.locator('#app')).toBeHidden();
  await expect(page.locator('#login-error')).not.toContainText('private-marker');
  await page.unroute('**/api/state');
  await login(page, 'StateRecoveryUser');
  await expect(page.locator('.timeline-performance').first()).toBeVisible();
  const sibling = await page.context().newPage();
  await sibling.goto('/');
  await expect(sibling.locator('#app')).toBeVisible();
  let releaseSnapshot;
  const heldSnapshot = new Promise(resolve => { releaseSnapshot = resolve; });
  await page.route('**/api/state', async route => {
    const response = await route.fetch();
    releaseSnapshot({route, response});
  });
  await page.evaluate(() => { window.pendingSnapshot = loadApp(); });
  const held = await heldSnapshot;
  let releasePick;
  const heldPick = new Promise(resolve => { releasePick = resolve; });
  await page.route('**/api/selections/toggle', async route => {
    const response = await route.fetch();
    releasePick({route, response});
  });
  await page.evaluate(() => { window.pendingPick = togglePick(document.querySelector('.pick-button').dataset.id); });
  const pick = await heldPick;
  await page.evaluate(() => signOut());
  await expect(sibling.locator('#app')).toBeHidden();
  await expect(sibling.locator('#login-screen')).toBeVisible();
  await sibling.close();
  await expect(page.locator('#app')).toBeHidden();
  await held.route.fulfill({response:held.response});
  await pick.route.fulfill({response:pick.response});
  await page.evaluate(() => window.pendingSnapshot);
  await page.evaluate(() => window.pendingPick);
  expect(await page.evaluate(() => appState.data)).toBeNull();
  await expect(page.locator('#app')).toBeHidden();
  await expect(page.locator('#login-screen')).toBeVisible();
  expect(errors).toEqual([]);
});

test('a mismatched pick result leaves the route unchanged and does not retry', async ({page}) => {
  await login(page, 'MutationGuardUser');
  let calls = 0;
  await page.route('**/api/selections/toggle', route => {
    calls++;
    return route.fulfill({status:200, contentType:'application/json', body:JSON.stringify({selected:true, performanceId:'wrong-act', username:'MutationGuardUser'})});
  });
  const pick = page.locator('.pick-button').first();
  await pick.click();
  await expect(page.locator('#toast')).toContainText('Refresh before trying again');
  await expect(pick).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('#my-count')).toHaveText('0');
  expect(calls).toBe(1);
});

test('login uses versioned assets and handles a missing form field without an uncaught error', async ({page}) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/app.js', route => route.fulfill({contentType:'application/javascript',body:'throw new Error("stale unversioned script loaded")'}));
  await page.goto('/');
  await expect(page.locator('script[src^="/app.js?v="]')).toHaveCount(1);
  await page.locator('#login-name').fill('missing-field-user');
  await page.locator('#display-name').fill('MissingField');
  await page.locator('#account-password').fill('fixture-password-only');
  await page.locator('#access-code').fill('friends-local');
  await page.locator('#login-name').evaluate(input => input.removeAttribute('name'));
  await page.locator('#login-form button[type="submit"]').click();
  await expect(page.locator('#login-error')).toContainText('Please refresh');
  expect(errors).toEqual([]);
  await page.reload();
  await expect(page.locator('#login-name')).toHaveAttribute('name', 'loginName');
  await expect(page.locator('#login-error')).toBeEmpty();
  expect(errors).toEqual([]);
});
