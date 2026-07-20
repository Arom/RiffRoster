const appState = {
  data: null,
  username: localStorage.getItem('riffroster.username') || '',
  isAdmin: false,
  activeEventId: null,
  activeDayId: null,
  activeView: 'schedule',
  search: '',
  visibleStages: new Set(),
  compareUsers: new Set(),
  serviceWorkerRegistrationPromise: null,
  pushSubscription: null,
  toastTimer: null
};

const PUSH_LEAD_KEY = 'riffroster.pushLeadMinutes';
const PUSH_CREW_NAMES_KEY = 'riffroster.pushCrewNames';

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];

document.addEventListener('DOMContentLoaded', () => {
  bindEvents();
  applyStoredNotificationPreferences();
  $('#username').value = appState.username;
  registerServiceWorker();
  loadApp();
});

function bindEvents() {
  $('#login-form').addEventListener('submit', login);
  $('#event-select').addEventListener('change', changeEvent);
  $('#search-input').addEventListener('input', event => {
    appState.search = event.target.value.trim().toLowerCase();
    renderSchedule();
  });
  $('#compare-day').addEventListener('change', event => {
    appState.activeDayId = event.target.value;
    syncDayControls();
    renderCompare();
  });
  $('#event-form').addEventListener('submit', createEvent);
  $('#performance-form').addEventListener('submit', createPerformance);
  $('#notification-lead').addEventListener('change', notificationPreferenceChanged);
  $('#notification-crew-names').addEventListener('change', notificationPreferenceChanged);

  document.addEventListener('click', event => {
    const actionNode = event.target.closest('[data-action]');
    if (actionNode) handleAction(actionNode.dataset.action, actionNode);

    const viewNode = event.target.closest('[data-view]');
    if (viewNode) changeView(viewNode.dataset.view);

    const dayNode = event.target.closest('[data-day-id]');
    if (dayNode) {
      appState.activeDayId = dayNode.dataset.dayId;
      syncDayControls();
      renderSchedule();
    }

    const stageNode = event.target.closest('[data-stage]');
    if (stageNode) toggleStage(stageNode.dataset.stage);

    const personNode = event.target.closest('[data-person]');
    if (personNode) toggleCompareUser(personNode.dataset.person);
  });
}

async function login(event) {
  event.preventDefault();
  const formData = new FormData(event.currentTarget);
  const username = formData.get('username').trim();
  const accessCode = formData.get('accessCode');
  $('#login-error').textContent = '';
  try {
    const session = await api('/api/session', { method: 'POST', body: JSON.stringify({ username, accessCode }) });
    appState.username = session.user.username;
    appState.isAdmin = session.isAdmin;
    localStorage.setItem('riffroster.username', session.user.username);
    $('#access-code').value = '';
    await loadApp();
  } catch (error) {
    $('#login-error').textContent = error.message;
  }
}

async function loadApp() {
  try {
    appState.data = await api('/api/state');
    appState.username = appState.data.currentUser;
    appState.isAdmin = appState.data.isAdmin;
    localStorage.setItem('riffroster.username', appState.username);
    appState.activeEventId = appState.activeEventId && eventById(appState.activeEventId) ? appState.activeEventId : appState.data.events[0]?.id;
    const activeEvent = currentEvent();
    appState.activeDayId = activeEvent?.days.some(day => day.id === appState.activeDayId) ? appState.activeDayId : activeEvent?.days[0]?.id;
    initializeCompareUsers();
    showApp();
    renderAll();
    focusPerformanceFromNotification();
    refreshNotificationState({ sync: true }).catch(error =>
      console.warn('RiffRoster could not refresh notification settings.', error));
  } catch (error) {
    showLogin();
    if (error.status !== 401) $('#login-error').textContent = `Could not load the running order: ${error.message}`;
  }
}

function showLogin() {
  $('#login-screen').hidden = false;
  $('#app').hidden = true;
  $('#username').value = appState.username;
  window.scrollTo(0, 0);
}

function showApp() {
  $('#login-screen').hidden = true;
  $('#app').hidden = false;
  window.scrollTo(0, 0);
}

function renderAll() {
  renderHeader();
  renderAdminState();
  renderDays();
  renderStageFilters();
  renderSchedule();
  renderComparePicker();
  renderCompare();
  updateViewVisibility();
}

function renderHeader() {
  const activeEvent = currentEvent();
  const eventActCount = activeEvent?.days.reduce((sum, day) => sum + day.performances.length, 0) || 0;
  $('#event-select').innerHTML = appState.data.events.map(event =>
    `<option value="${escapeHtml(event.id)}" ${event.id === appState.activeEventId ? 'selected' : ''}>${escapeHtml(event.name)}</option>`
  ).join('');
  $('#event-meta').innerHTML = activeEvent
    ? `<strong>${escapeHtml(formatDateRange(activeEvent))}</strong> &nbsp;·&nbsp; ${escapeHtml(activeEvent.location)} &nbsp;·&nbsp; ${eventActCount} ${plural(eventActCount, 'act')}`
    : '';

  const users = appState.data.users;
  $('#crew-stack').innerHTML = users.slice(0, 5).reverse().map(avatarHtml).join('');
  const me = userByName(appState.username);
  $('#profile-button').textContent = initials(appState.username);
  $('#profile-button').style.background = me?.color || '#ffb000';
  const roleLabel = appState.isAdmin ? 'Administrator' : 'Member';
  $('#profile-button').setAttribute('aria-label', `Signed in as ${appState.username}, ${roleLabel}. Click to sign out.`);
  $('#profile-button').title = `${appState.username} · ${roleLabel} · Sign out`;
  $('#my-count').textContent = selectionsForUser(appState.username).length;
}

function renderAdminState() {
  $$('[data-admin-only]').forEach(node => { node.hidden = !appState.isAdmin; });
  $('#admin-badge').hidden = !appState.isAdmin;
}

function renderDays() {
  const activeEvent = currentEvent();
  const days = activeEvent?.days || [];
  $('#day-tabs').innerHTML = days.map(day => `
    <button class="day-tab ${day.id === appState.activeDayId ? 'active' : ''}" data-day-id="${day.id}" role="tab" aria-selected="${day.id === appState.activeDayId}">
      <span>${escapeHtml(day.label)}</span><small>${formatShortDate(day.date)}</small>
    </button>`).join('');
  $('#compare-day').innerHTML = days.map(day =>
    `<option value="${day.id}" ${day.id === appState.activeDayId ? 'selected' : ''}>${escapeHtml(day.label)} · ${formatShortDate(day.date)}</option>`
  ).join('');
}

function renderStageFilters() {
  const stages = stagesForEvent();
  if (!appState.visibleStages.size || [...appState.visibleStages].some(stage => !stages.includes(stage))) {
    appState.visibleStages = new Set(stages);
  }
  $('#stage-filters').innerHTML = stages.map((stage, index) => `
    <button class="stage-chip ${appState.visibleStages.has(stage) ? 'active' : ''}" data-stage="${escapeHtml(stage)}" style="--stage-color:${stageColor(stage, index)}">
      ${escapeHtml(stage)}
    </button>`).join('');
}

function renderSchedule() {
  const day = currentDay();
  const container = $('#schedule-grid');
  if (!day) {
    container.innerHTML = emptyState('No festival day selected', 'Create a running order or choose another one to get started.');
    return;
  }

  const allStages = stagesForEvent();
  const query = appState.search;
  const myIds = new Set(selectionsForUser(appState.username).map(selection => selection.performanceId));
  let performances = day.performances.filter(performance =>
    appState.visibleStages.has(performance.stage) &&
    (!query || performance.artist.toLowerCase().includes(query)) &&
    (appState.activeView !== 'mine' || myIds.has(performance.id))
  );

  const visibleStageNames = allStages.filter(stage => appState.visibleStages.has(stage) && performances.some(performance => performance.stage === stage));
  const selectedToday = day.performances.filter(performance => myIds.has(performance.id));
  const conflicts = findConflicts(selectedToday);
  const conflictIds = new Set(conflicts.flatMap(pair => pair.map(performance => performance.id)));

  $('#schedule-summary').innerHTML = appState.activeView === 'mine'
    ? `<strong>${selectedToday.length} picks</strong> on ${escapeHtml(day.label)}${conflicts.length ? ` · <span style="color:var(--red)">${conflicts.length} clash${conflicts.length === 1 ? '' : 'es'}</span>` : ' · no clashes'}`
    : `<strong>${day.performances.length} ${plural(day.performances.length, 'act')}</strong> across ${allStages.length} ${plural(allStages.length, 'stage')} · aligned by time so overlaps are easy to spot`;

  if (!performances.length) {
    const isBlankEvent = day.performances.length === 0;
    container.innerHTML = emptyState(
      isBlankEvent ? 'This day is wide open' : appState.activeView === 'mine' ? 'No picks yet' : 'No acts found',
      isBlankEvent ? 'Add the first act to begin building this running order.' : appState.activeView === 'mine' ? 'Switch to the running order and star a few bands.' : 'Try another search or stage filter.',
      isBlankEvent && appState.isAdmin ? 'open-performance-modal' : null
    );
    return;
  }

  const timeline = buildTimeline(performances);
  const laneCount = visibleStageNames.length;
  const shellMinWidth = 68 + laneCount * 240;
  const tickLabels = timeline.ticks.map(tick =>
    `<time class="timeline-tick" style="top:${tick.top}px">${tick.label}</time>`
  ).join('');
  const gridLines = timeline.ticks.map(tick =>
    `<span class="timeline-gridline" style="top:${tick.top}px" aria-hidden="true"></span>`
  ).join('');

  const headings = visibleStageNames.map(stage => {
    const count = performances.filter(performance => performance.stage === stage).length;
    const color = stageColor(stage, allStages.indexOf(stage));
    return `<header class="timeline-stage-heading" style="--stage-color:${color}">
      <h3>${escapeHtml(stage)}</h3><span>${count} ${plural(count, 'act')}</span>
    </header>`;
  }).join('');

  const lanes = visibleStageNames.map(stage => {
    const color = stageColor(stage, allStages.indexOf(stage));
    const stagePerformances = performances.filter(performance => performance.stage === stage);
    return `<section class="timeline-lane" style="--stage-color:${color}" aria-label="${escapeHtml(stage)}">
      ${stagePerformances.map(performance => performanceHtml(
        performance,
        myIds.has(performance.id),
        conflictIds.has(performance.id),
        positionOnTimeline(performance, timeline)
      )).join('')}
    </section>`;
  }).join('');

  container.innerHTML = `<div class="timeline-swipe-hint">Swipe sideways to compare stages <span aria-hidden="true">↔</span></div>
    <div class="schedule-timeline">
      <div class="timeline-shell" style="min-width:${shellMinWidth}px;--lane-count:${laneCount};--timeline-height:${timeline.height}px;--timeline-half-hour:${timeline.halfHourOffset}px">
        <div class="timeline-header">
          <div class="timeline-corner">TIME</div>
          <div class="timeline-stage-headings">${headings}</div>
        </div>
        <div class="timeline-body">
          <div class="timeline-time-axis">${tickLabels}</div>
          <div class="timeline-lanes">${gridLines}${lanes}</div>
        </div>
      </div>
    </div>`;
}

function performanceHtml(performance, selected, clash, timelinePosition = null) {
  const attendees = usersForPerformance(performance.id);
  const otherCount = attendees.filter(user => !sameName(user.username, appState.username)).length;
  const isShared = selected && otherCount > 0;
  const end = formatTime(performance.endsAt);
  let attendanceText = 'No picks yet';
  if (selected && otherCount) attendanceText = `<strong>You + ${otherCount}</strong> going`;
  else if (selected) attendanceText = 'Only you — so far';
  else if (attendees.length) attendanceText = `${attendees.length} friend${attendees.length === 1 ? '' : 's'} going`;

  const timelineClass = timelinePosition ? `timeline-performance ${timelinePosition.compact ? 'performance--compact' : ''}` : '';
  const timelineStyle = timelinePosition ? ` style="top:${timelinePosition.top}px;height:${timelinePosition.height}px"` : '';
  return `<article class="performance ${timelineClass} ${selected ? 'selected' : ''} ${isShared ? 'shared' : ''} ${clash ? 'clash' : ''}" data-performance-id="${performance.id}" title="${escapeHtml(performance.artist)} · ${formatTime(performance.startsAt)}–${end}"${timelineStyle}>
    <div class="performance-time">${formatTime(performance.startsAt)} <span>→ ${end}</span></div>
    <h4>${escapeHtml(performance.artist)}</h4>
    <button class="pick-button" data-action="toggle-pick" data-id="${performance.id}" aria-label="${selected ? 'Remove' : 'Add'} ${escapeHtml(performance.artist)} ${selected ? 'from' : 'to'} your route" aria-pressed="${selected}">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z"/></svg>
    </button>
    <div class="attendance">
      <div class="attendance-avatars">${attendees.slice(0, 4).reverse().map(user => avatarHtml(user, true)).join('')}</div>
      <span class="attendance-label">${attendanceText}</span>
    </div>
    ${clash ? '<span class="clash-label">CLASH IN YOUR ROUTE</span>' : ''}
  </article>`;
}

function buildTimeline(performances) {
  const minute = 60 * 1000;
  const hour = 60 * minute;
  const pixelsPerMinute = 2.2;
  const offsetMinutes = offsetMinutesFromIso(performances[0]?.startsAt);
  const offsetMs = offsetMinutes * minute;
  const earliest = Math.min(...performances.map(performance => new Date(performance.startsAt).getTime()));
  const latest = Math.max(...performances.map(performance => new Date(performance.endsAt).getTime()));
  const localEarliest = earliest + offsetMs;
  const localLatest = latest + offsetMs;
  const localStart = Math.floor(localEarliest / hour) * hour - 30 * minute;
  const localEnd = Math.ceil(localLatest / hour) * hour + 30 * minute;
  const start = localStart - offsetMs;
  const end = localEnd - offsetMs;
  const height = Math.round(((end - start) / minute) * pixelsPerMinute);
  const ticks = [];

  for (let localTick = Math.ceil(localStart / hour) * hour; localTick < localEnd; localTick += hour) {
    const epoch = localTick - offsetMs;
    ticks.push({
      top: Math.round(((epoch - start) / minute) * pixelsPerMinute),
      label: formatTimelineTime(epoch, offsetMinutes)
    });
  }

  return {
    start,
    end,
    height,
    ticks,
    pixelsPerMinute,
    halfHourOffset: Math.round(30 * pixelsPerMinute)
  };
}

function positionOnTimeline(performance, timeline) {
  const minute = 60 * 1000;
  const startsAt = new Date(performance.startsAt).getTime();
  const endsAt = new Date(performance.endsAt).getTime();
  const top = ((startsAt - timeline.start) / minute) * timeline.pixelsPerMinute;
  const naturalHeight = ((endsAt - startsAt) / minute) * timeline.pixelsPerMinute - 5;
  const height = Math.max(32, naturalHeight);
  return {
    top: Math.round(top * 10) / 10,
    height: Math.round(height * 10) / 10,
    compact: height < 66
  };
}

function offsetMinutesFromIso(value) {
  const match = String(value || '').match(/([+-])(\d{2}):(\d{2})$/);
  if (!match) return 0;
  const minutes = Number(match[2]) * 60 + Number(match[3]);
  return match[1] === '-' ? -minutes : minutes;
}

function formatTimelineTime(epoch, offsetMinutes) {
  const local = new Date(epoch + offsetMinutes * 60 * 1000);
  return `${String(local.getUTCHours()).padStart(2, '0')}:${String(local.getUTCMinutes()).padStart(2, '0')}`;
}

function renderComparePicker() {
  $('#people-picker').innerHTML = appState.data.users.map(user => `
    <button class="person-chip ${appState.compareUsers.has(user.username) ? 'active' : ''} ${sameName(user.username, appState.username) ? 'current' : ''}" data-person="${escapeHtml(user.username)}" style="--person-color:${user.color}" aria-pressed="${appState.compareUsers.has(user.username)}">
      ${avatarHtml(user, true)} ${escapeHtml(user.username)}
    </button>`).join('');
}

function renderCompare() {
  if (!appState.data) return;
  const selectedUsers = [...appState.compareUsers].map(userByName).filter(Boolean);
  const day = currentDay();
  const performances = day?.performances || [];
  const counts = new Map(performances.map(performance => [performance.id, selectedUsers.filter(user => userHasPick(user.username, performance.id)).length]));
  const shared = performances.filter(performance => selectedUsers.length >= 2 && counts.get(performance.id) >= 2).sort(byStart);
  const unanimous = shared.filter(performance => counts.get(performance.id) === selectedUsers.length);
  const picked = performances.filter(performance => counts.get(performance.id) > 0);
  const solo = picked.filter(performance => counts.get(performance.id) === 1);

  $('#compare-stats').innerHTML = [
    [unanimous.length, 'FULL-CREW MATCHES'],
    [shared.length, 'SHARED PICKS'],
    [solo.length, 'SOLO DETOURS']
  ].map(([number, label]) => `<div class="compare-stat"><b>${number}</b><span>${label}</span></div>`).join('');

  if (selectedUsers.length < 2) {
    $('#shared-route').innerHTML = '<div class="empty-compact">Choose at least two people above to map a shared route.</div>';
    $('#solo-routes').innerHTML = '<div class="empty-compact">Solo detours will appear here.</div>';
    return;
  }

  $('#shared-route').innerHTML = shared.length ? shared.map(performance => {
    const attendees = selectedUsers.filter(user => userHasPick(user.username, performance.id));
    const attendeeNames = attendees.map(user => sameName(user.username, appState.username) ? 'You' : user.username).join(', ');
    return `<div class="route-item">
      <time class="route-item__time">${formatTime(performance.startsAt)}</time>
      <div class="route-item__track"></div>
      <div class="route-item__act">
        <b>${escapeHtml(performance.artist)}</b>
        <small>${escapeHtml(performance.stage)}<span class="route-match-count"> · ${attendees.length === selectedUsers.length ? 'full crew' : `${attendees.length} of ${selectedUsers.length}`}</span></small>
        <div class="route-attendee-badges" aria-label="Going: ${escapeHtml(attendeeNames)}">
          ${attendees.map(user => `<span class="route-attendee-badge" style="--person-color:${user.color}"><i></i>${sameName(user.username, appState.username) ? 'You' : escapeHtml(user.username)}</span>`).join('')}
        </div>
      </div>
      <div class="attendance-avatars">${attendees.slice().reverse().map(user => avatarHtml(user, true)).join('')}</div>
    </div>`;
  }).join('') : '<div class="empty-compact">No shared picks on this day yet. Someone needs to cross into another route.</div>';

  $('#solo-routes').innerHTML = selectedUsers.map(user => {
    const userSolo = solo.filter(performance => userHasPick(user.username, performance.id));
    if (!userSolo.length) return '';
    return `<div class="detour-group">
      <div class="detour-owner">${avatarHtml(user, true)} ${escapeHtml(user.username)}</div>
      <div class="detour-picks">${userSolo.map(performance => `<span class="detour-pick" style="--person-color:${user.color}"><time>${formatTime(performance.startsAt)}</time>${escapeHtml(performance.artist)}</span>`).join('')}</div>
    </div>`;
  }).join('') || '<div class="empty-compact">No one is splitting off—this crew is unusually aligned.</div>';
}

async function togglePick(performanceId) {
  try {
    const result = await api('/api/selections/toggle', {
      method: 'POST',
      body: JSON.stringify({ performanceId })
    });
    if (result.selected) appState.data.selections.push({ username: appState.username, performanceId });
    else appState.data.selections = appState.data.selections.filter(selection => !(selection.performanceId === performanceId && sameName(selection.username, appState.username)));
    renderHeader();
    renderSchedule();
    renderCompare();
    toast(result.selected ? 'Added to your route' : 'Removed from your route');
  } catch (error) { toast(error.message, true); }
}

async function createEvent(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const data = Object.fromEntries(new FormData(form));
  const errorNode = $('.modal-error', form);
  errorNode.textContent = '';
  try {
    const created = await api('/api/events', { method: 'POST', body: JSON.stringify(data) });
    appState.data.events.push(created);
    appState.activeEventId = created.id;
    appState.activeDayId = created.days[0]?.id;
    appState.visibleStages.clear();
    form.closest('dialog').close();
    form.reset();
    renderAll();
    toast(`${created.name} is ready`);
  } catch (error) { errorNode.textContent = error.message; }
}

async function createPerformance(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const formData = Object.fromEntries(new FormData(form));
  const errorNode = $('.modal-error', form);
  errorNode.textContent = '';
  const payload = {
    artist: formData.artist,
    stage: formData.stage,
    startsAt: localDateTimeToOffsetIso(formData.startsAt),
    endsAt: localDateTimeToOffsetIso(formData.endsAt)
  };
  try {
    const created = await api(`/api/events/${encodeURIComponent(appState.activeEventId)}/performances`, { method: 'POST', body: JSON.stringify(payload) });
    const createdDate = created.startsAt.slice(0, 10);
    const createdHour = Number(created.startsAt.slice(11, 13));
    const scheduleDate = createdHour < 6
      ? localDateString(new Date(`${createdDate}T12:00:00`).setDate(new Date(`${createdDate}T12:00:00`).getDate() - 1))
      : createdDate;
    const targetDay = currentEvent().days.find(day => day.date === scheduleDate);
    if (targetDay) {
      targetDay.performances.push(created);
      targetDay.performances.sort(byStart);
      appState.activeDayId = targetDay.id;
    } else {
      await loadApp();
    }
    form.closest('dialog').close();
    form.reset();
    renderAll();
    toast(`${created.artist} added`);
  } catch (error) { errorNode.textContent = error.message; }
}

function handleAction(action, node) {
  if (action === 'toggle-pick') togglePick(node.dataset.id);
  if (action === 'sign-out') signOut();
  if (action === 'open-event-modal') openEventModal();
  if (action === 'open-performance-modal') openPerformanceModal();
  if (action === 'open-notification-modal') openNotificationModal();
  if (action === 'enable-notifications') enableNotifications();
  if (action === 'disable-notifications') disableNotifications();
  if (action === 'test-notification') sendTestNotification();
  if (action === 'close-modal') node.closest('dialog').close();
  if (action === 'view-schedule') changeView('schedule');
}

function changeView(view) {
  appState.activeView = view;
  updateViewVisibility();
  if (view === 'compare') renderCompare();
  else renderSchedule();
  window.scrollTo({ top: 0, behavior: 'instant' });
}

function updateViewVisibility() {
  const compare = appState.activeView === 'compare';
  $('#compare-view').hidden = !compare;
  $('#schedule-view').hidden = compare;
  $$('[data-view]').forEach(node => node.classList.toggle('active', node.dataset.view === appState.activeView));
}

function changeEvent(event) {
  appState.activeEventId = event.target.value;
  appState.activeDayId = currentEvent()?.days[0]?.id;
  appState.visibleStages.clear();
  renderAll();
}

function toggleStage(stage) {
  if (appState.visibleStages.has(stage) && appState.visibleStages.size > 1) appState.visibleStages.delete(stage);
  else appState.visibleStages.add(stage);
  renderStageFilters();
  renderSchedule();
}

function toggleCompareUser(username) {
  if (appState.compareUsers.has(username)) appState.compareUsers.delete(username);
  else appState.compareUsers.add(username);
  renderComparePicker();
  renderCompare();
}

function initializeCompareUsers() {
  const validNames = new Set(appState.data.users.map(user => user.username));
  appState.compareUsers = new Set([...appState.compareUsers].filter(name => validNames.has(name)));
  if (!appState.compareUsers.size) {
    appState.compareUsers.add(appState.username);
    appState.data.users.filter(user => !sameName(user.username, appState.username)).slice(0, 2).forEach(user => appState.compareUsers.add(user.username));
  }
}

function openEventModal() {
  if (!appState.isAdmin) return toast('Administrator access is required', true);
  const form = $('#event-form');
  const today = new Date();
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
  form.elements.startDate.value = localDateString(start);
  form.elements.endDate.value = localDateString(start);
  $('.modal-error', form).textContent = '';
  $('#event-modal').showModal();
}

function openPerformanceModal() {
  if (!appState.isAdmin) return toast('Administrator access is required', true);
  const activeEvent = currentEvent();
  if (!activeEvent) return;
  const form = $('#performance-form');
  const day = currentDay() || activeEvent.days[0];
  const baseDate = day?.date || activeEvent.startDate;
  $('#performance-event-label').textContent = `${activeEvent.name} · ${day?.label || ''}`;
  form.elements.startsAt.value = `${baseDate}T12:00`;
  form.elements.endsAt.value = `${baseDate}T12:45`;
  $('.modal-error', form).textContent = '';
  $('#performance-modal').showModal();
}

function applyStoredNotificationPreferences() {
  const storedLead = Number(localStorage.getItem(PUSH_LEAD_KEY));
  $('#notification-lead').value = [10, 20, 30, 60].includes(storedLead) ? String(storedLead) : '20';
  $('#notification-crew-names').checked = localStorage.getItem(PUSH_CREW_NAMES_KEY) !== 'false';
}

function notificationPreferences() {
  return {
    leadMinutes: Number($('#notification-lead').value),
    includeCrewNames: $('#notification-crew-names').checked
  };
}

function saveNotificationPreferences() {
  const preferences = notificationPreferences();
  localStorage.setItem(PUSH_LEAD_KEY, String(preferences.leadMinutes));
  localStorage.setItem(PUSH_CREW_NAMES_KEY, String(preferences.includeCrewNames));
  return preferences;
}

function supportsPushNotifications() {
  return window.isSecureContext &&
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    'Notification' in window;
}

function isIosDevice() {
  return /iphone|ipad|ipod/i.test(navigator.userAgent) ||
    (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
}

function isStandaloneApp() {
  return window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
}

async function registerServiceWorker() {
  if (!('serviceWorker' in navigator)) return null;
  try {
    if (!appState.serviceWorkerRegistrationPromise)
      appState.serviceWorkerRegistrationPromise = navigator.serviceWorker.register('/service-worker.js');
    return await appState.serviceWorkerRegistrationPromise;
  } catch (error) {
    appState.serviceWorkerRegistrationPromise = null;
    console.warn('RiffRoster could not register its service worker.', error);
    return null;
  }
}

async function openNotificationModal() {
  $('#notification-error').textContent = '';
  $('#notification-modal').showModal();
  await refreshNotificationState();
}

async function refreshNotificationState({ sync = false } = {}) {
  const iosInstallRequired = isIosDevice() && !isStandaloneApp();
  $('#ios-install-note').hidden = !iosInstallRequired;

  if (!supportsPushNotifications()) {
    renderNotificationStatus('unavailable', 'Not supported on this device', 'Try an up-to-date version of Chrome, Edge, Firefox, or an installed iPhone Home Screen app.');
    return;
  }
  if (iosInstallRequired) {
    renderNotificationStatus('install', 'Home Screen install required', 'Apple only enables Web Push for installed Home Screen web apps.');
    return;
  }
  if (Notification.permission === 'denied') {
    renderNotificationStatus('denied', 'Notifications are blocked', 'Allow notifications for RiffRoster in this device’s browser or system settings.');
    return;
  }

  const registration = await registerServiceWorker();
  if (!registration) {
    renderNotificationStatus('unavailable', 'Reminders are unavailable', 'The background service could not start on this device.');
    return;
  }

  let subscription;
  try {
    subscription = await registration.pushManager.getSubscription();
  } catch (error) {
    console.warn('RiffRoster could not read this browser subscription.', error);
    renderNotificationStatus('unavailable', 'Reminders are unavailable', 'This browser could not access its notification subscription. Try again shortly.');
    return;
  }
  appState.pushSubscription = subscription;
  if (!subscription) {
    renderNotificationStatus('off', 'Off on this device', 'Enable reminders to receive alerts for your selected performances.');
    return;
  }

  if (sync && appState.data) {
    try {
      await persistPushSubscription(subscription);
    } catch (error) {
      console.warn('RiffRoster could not refresh this push subscription.', error);
    }
  }
  renderNotificationStatus('on', 'On for this device', `${notificationPreferences().leadMinutes} minutes before each selected act.`);
}

function renderNotificationStatus(status, title, detail) {
  const statusNode = $('#notification-status');
  statusNode.dataset.status = status;
  statusNode.innerHTML = `<span></span><div><b>${escapeHtml(title)}</b><small>${escapeHtml(detail)}</small></div>`;
  const enabled = status === 'on';
  $('#enable-notifications').hidden = enabled || ['unavailable', 'install', 'denied'].includes(status);
  $('#test-notification').hidden = !enabled;
  $('#disable-notifications').hidden = !enabled;
  $('#notification-indicator').classList.toggle('active', enabled);
  $('#notification-lead').disabled = ['unavailable', 'install'].includes(status);
  $('#notification-crew-names').disabled = ['unavailable', 'install'].includes(status);
}

async function enableNotifications() {
  const errorNode = $('#notification-error');
  errorNode.textContent = '';
  if (!supportsPushNotifications()) return refreshNotificationState();
  if (isIosDevice() && !isStandaloneApp()) return refreshNotificationState();

  try {
    const permission = await Notification.requestPermission();
    if (permission !== 'granted') {
      await refreshNotificationState();
      return;
    }

    const registration = await registerServiceWorker();
    if (!registration) throw new Error('The background service could not start.');
    const keyResponse = await api('/api/push/public-key');
    const existing = await registration.pushManager.getSubscription();
    const subscription = existing || await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(keyResponse.publicKey)
    });
    saveNotificationPreferences();
    await persistPushSubscription(subscription);
    appState.pushSubscription = subscription;
    await refreshNotificationState();
    toast('Concert reminders enabled');
  } catch (error) {
    errorNode.textContent = error.message;
  }
}

async function persistPushSubscription(subscription) {
  const serialized = subscription.toJSON();
  if (!serialized.keys?.p256dh || !serialized.keys?.auth)
    throw new Error('The browser did not provide notification encryption keys.');
  return api('/api/push/subscriptions', {
    method: 'POST',
    body: JSON.stringify({
      endpoint: subscription.endpoint,
      keys: serialized.keys,
      ...notificationPreferences()
    })
  });
}

async function notificationPreferenceChanged() {
  saveNotificationPreferences();
  if (!appState.pushSubscription) return;
  try {
    await persistPushSubscription(appState.pushSubscription);
    renderNotificationStatus('on', 'On for this device', `${notificationPreferences().leadMinutes} minutes before each selected act.`);
    toast('Reminder preferences saved');
  } catch (error) {
    $('#notification-error').textContent = error.message;
  }
}

async function disableNotifications() {
  const subscription = appState.pushSubscription;
  if (!subscription) return refreshNotificationState();
  $('#notification-error').textContent = '';
  try {
    await api('/api/push/subscriptions', {
      method: 'DELETE',
      body: JSON.stringify({ endpoint: subscription.endpoint })
    });
  } catch (error) {
    console.warn('RiffRoster could not remove the server-side push subscription.', error);
  }

  try {
    await subscription.unsubscribe();
    appState.pushSubscription = null;
    await refreshNotificationState();
    toast('Concert reminders turned off');
  } catch (error) {
    $('#notification-error').textContent = 'This browser could not turn notifications off. Try again.';
    console.warn('RiffRoster could not unsubscribe this browser.', error);
  }
}

async function sendTestNotification() {
  if (!appState.pushSubscription) return;
  $('#notification-error').textContent = '';
  try {
    await api('/api/push/test', {
      method: 'POST',
      body: JSON.stringify({ endpoint: appState.pushSubscription.endpoint })
    });
    toast('Test notification sent');
  } catch (error) {
    $('#notification-error').textContent = error.message;
    if (error.status === 410) await refreshNotificationState();
  }
}

function urlBase64ToUint8Array(value) {
  const padding = '='.repeat((4 - value.length % 4) % 4);
  const base64 = (value + padding).replace(/-/g, '+').replace(/_/g, '/');
  const raw = window.atob(base64);
  return Uint8Array.from([...raw].map(character => character.charCodeAt(0)));
}

function focusPerformanceFromNotification() {
  const performanceId = new URLSearchParams(window.location.search).get('performance');
  if (!performanceId || !appState.data) return;
  for (const festivalEvent of appState.data.events) {
    for (const day of festivalEvent.days) {
      if (!day.performances.some(performance => performance.id === performanceId)) continue;
      appState.activeEventId = festivalEvent.id;
      appState.activeDayId = day.id;
      appState.activeView = 'schedule';
      appState.visibleStages.clear();
      renderAll();
      window.history.replaceState({}, '', window.location.pathname);
      window.requestAnimationFrame(() => {
        const target = $$('.performance').find(node => node.dataset.performanceId === performanceId);
        if (!target) return;
        target.classList.add('notification-target');
        target.scrollIntoView({ behavior: 'smooth', block: 'center' });
        window.setTimeout(() => target.classList.remove('notification-target'), 5000);
      });
      return;
    }
  }
}

function currentEvent() { return eventById(appState.activeEventId); }
function eventById(id) { return appState.data?.events.find(event => event.id === id); }
function currentDay() { return currentEvent()?.days.find(day => day.id === appState.activeDayId); }
function userByName(name) { return appState.data?.users.find(user => sameName(user.username, name)); }
function selectionsForUser(name) { return appState.data?.selections.filter(selection => sameName(selection.username, name)) || []; }
function userHasPick(username, performanceId) { return appState.data.selections.some(selection => selection.performanceId === performanceId && sameName(selection.username, username)); }
function usersForPerformance(id) { return appState.data.selections.filter(selection => selection.performanceId === id).map(selection => userByName(selection.username)).filter(Boolean); }
function stagesForEvent() { return [...new Set((currentEvent()?.days || []).flatMap(day => day.performances.map(performance => performance.stage)))]; }
function sameName(a, b) { return a?.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0; }
function byStart(a, b) { return new Date(a.startsAt) - new Date(b.startsAt); }

function findConflicts(performances) {
  const sorted = [...performances].sort(byStart);
  const conflicts = [];
  for (let i = 0; i < sorted.length; i++) {
    for (let j = i + 1; j < sorted.length; j++) {
      if (new Date(sorted[j].startsAt) >= new Date(sorted[i].endsAt)) break;
      conflicts.push([sorted[i], sorted[j]]);
    }
  }
  return conflicts;
}

function syncDayControls() {
  $$('.day-tab').forEach(button => {
    button.classList.toggle('active', button.dataset.dayId === appState.activeDayId);
    button.setAttribute('aria-selected', button.dataset.dayId === appState.activeDayId);
  });
  $('#compare-day').value = appState.activeDayId;
}

function formatTime(value) {
  const match = String(value).match(/T(\d{2}):(\d{2})/);
  return match ? `${match[1]}:${match[2]}` : '';
}

function formatShortDate(value) {
  return new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${value}T00:00:00Z`));
}

function formatDateRange(event) {
  const options = { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' };
  const start = new Intl.DateTimeFormat('en-GB', options).format(new Date(`${event.startDate}T00:00:00Z`));
  const end = new Intl.DateTimeFormat('en-GB', options).format(new Date(`${event.endDate}T00:00:00Z`));
  return `${start} — ${end}`;
}

function localDateString(date) {
  date = new Date(date);
  const offset = date.getTimezoneOffset();
  return new Date(date.getTime() - offset * 60000).toISOString().slice(0, 10);
}

function localDateTimeToOffsetIso(value) {
  const date = new Date(value);
  const offset = -date.getTimezoneOffset();
  const sign = offset >= 0 ? '+' : '-';
  const hours = String(Math.floor(Math.abs(offset) / 60)).padStart(2, '0');
  const minutes = String(Math.abs(offset) % 60).padStart(2, '0');
  return `${value}:00${sign}${hours}:${minutes}`;
}

function initials(name) {
  return name.split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase();
}

function plural(count, word) { return count === 1 ? word : `${word}s`; }

async function signOut() {
  try {
    await disconnectPushForSignOut();
    await api('/api/session/logout', { method: 'POST' });
  } catch (error) {
    if (error.status !== 401) toast(error.message, true);
  } finally {
    appState.data = null;
    appState.isAdmin = false;
    $('#access-code').value = '';
    showLogin();
    $('#access-code').focus();
  }
}

async function disconnectPushForSignOut() {
  try {
    const registration = await registerServiceWorker();
    const subscription = appState.pushSubscription || await registration?.pushManager.getSubscription();
    if (!subscription) return;
    try {
      await api('/api/push/subscriptions', {
        method: 'DELETE',
        body: JSON.stringify({ endpoint: subscription.endpoint })
      });
    } catch { /* Signing out must still succeed if the server is temporarily unavailable. */ }
    await subscription.unsubscribe();
    appState.pushSubscription = null;
    $('#notification-indicator').classList.remove('active');
  } catch { /* Notification cleanup is best effort during sign-out. */ }
}

function avatarHtml(user, small = false) {
  return `<span class="avatar ${small ? 'avatar--small' : ''}" style="background:${user.color}" title="${escapeHtml(user.username)}">${escapeHtml(initials(user.username))}</span>`;
}

function stageColor(stage, index) {
  const known = { 'Main A': '#ffb000', 'Main B': '#ff6257', 'Obscure': '#8fb8ff', 'Octagon': '#70d6a7' };
  const palette = ['#ffb000', '#ff6257', '#8fb8ff', '#70d6a7', '#d59cff', '#f47fc4'];
  return known[stage] || palette[index % palette.length];
}

function emptyState(title, copy, action = null) {
  return `<div class="empty-state"><div><h3>${escapeHtml(title)}</h3><p>${escapeHtml(copy)}</p>${action ? `<button class="button button--primary" data-action="${action}">Add the first act</button>` : ''}</div></div>`;
}

function toast(message, isError = false) {
  const node = $('#toast');
  node.textContent = message;
  node.style.background = isError ? '#ff6257' : '#ffb000';
  node.classList.add('show');
  clearTimeout(appState.toastTimer);
  appState.toastTimer = setTimeout(() => node.classList.remove('show'), 2200);
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[character]);
}

async function api(url, options = {}) {
  const response = await fetch(url, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) }
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    let message = data.message || `Request failed (${response.status})`;
    if (response.status === 401) message = url === '/api/session' ? 'That access code is not valid.' : 'Your session has expired. Enter the access code again.';
    if (response.status === 403) message = 'Administrator access is required for that action.';
    const error = new Error(message);
    error.status = response.status;
    throw error;
  }
  return data;
}
