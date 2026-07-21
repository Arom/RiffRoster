const appState = {
  data: null,
  username: localStorage.getItem('riffroster.username') || '',
  loginName: localStorage.getItem('riffroster.loginName') || '',
  isAdmin: false,
  activeEventId: null,
  activeDayId: null,
  activeGroupId: null,
  activeView: 'schedule',
  search: '',
  visibleStages: new Set(),
  compareUsers: new Set(),
  posterImport: {
    file: null,
    imageUrl: null,
    rows: [],
    rawText: '',
    worker: null,
    processing: false,
    nextRowId: 1,
    runId: 0
  },
  serviceWorkerRegistrationPromise: null,
  pushSubscription: null,
  toastTimer: null
};

const PUSH_LEAD_KEY = 'riffroster.pushLeadMinutes';
const PUSH_CREW_NAMES_KEY = 'riffroster.pushCrewNames';
const COMPARE_USERS_KEY = 'riffroster.compareUsers';
const ACTIVE_GROUP_KEY = 'riffroster.activeGroup';

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];

document.addEventListener('DOMContentLoaded', () => {
  bindEvents();
  applyStoredNotificationPreferences();
  $('#login-name').value = appState.loginName;
  $('#display-name').value = appState.username;
  registerServiceWorker();
  loadApp();
});

function bindEvents() {
  $('#login-form').addEventListener('submit', login);
  $('#event-select').addEventListener('change', changeEvent);
  $('#group-select').addEventListener('change', changeGroup);
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
  $('#delete-event-form').addEventListener('submit', deleteEvent);
  $('#performance-form').addEventListener('submit', createPerformance);
  $('#create-group-form').addEventListener('submit', createGroup);
  $('#join-group-form').addEventListener('submit', joinGroup);
  $('#poster-file').addEventListener('change', posterFileChanged);
  $('#poster-import-rows').addEventListener('input', posterRowChanged);
  $('#poster-import-rows').addEventListener('change', posterRowChanged);
  $('#poster-import-modal').addEventListener('close', resetPosterImport);
  $('#notification-lead').addEventListener('change', notificationPreferenceChanged);
  $('#notification-crew-names').addEventListener('change', notificationPreferenceChanged);

  document.addEventListener('click', event => {
    const actionNode = event.target.closest('[data-action]');
    if (actionNode) handleAction(actionNode.dataset.action, actionNode);
    if (!event.target.closest('[data-action="toggle-attendee-names"]')) closeAttendeeNames();

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
  const loginName = formData.get('loginName').trim();
  const displayName = formData.get('displayName').trim();
  const password = formData.get('password');
  const accessCode = formData.get('accessCode');
  $('#login-error').textContent = '';
  try {
    const session = await api('/api/session', { method: 'POST', body: JSON.stringify({ loginName, displayName, password, accessCode }) });
    appState.username = session.user.username;
    appState.loginName = loginName;
    appState.isAdmin = session.isAdmin;
    localStorage.setItem('riffroster.username', session.user.username);
    localStorage.setItem('riffroster.loginName', loginName);
    $('#account-password').value = '';
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
    const preferredEventId = appState.data.lastSelectedEventId;
    appState.activeEventId = preferredEventId && eventById(preferredEventId)
      ? preferredEventId
      : appState.data.events[0]?.id || null;
    const activeEvent = currentEvent();
    appState.activeDayId = activeEvent?.days.some(day => day.id === appState.activeDayId) ? appState.activeDayId : activeEvent?.days[0]?.id;
    restoreActiveGroup();
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
  $('#login-name').value = appState.loginName;
  $('#display-name').value = appState.username;
  window.scrollTo(0, 0);
}

function showApp() {
  $('#login-screen').hidden = true;
  $('#app').hidden = false;
  window.scrollTo(0, 0);
}

function renderAll() {
  renderHeader();
  renderGroupContext();
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

  const users = contextUsers();
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

function renderGroupContext() {
  const groups = appState.data.groups || [];
  $('#group-select').innerHTML = [
    `<option value="">Personal route</option>`,
    ...groups.map(group => `<option value="${escapeHtml(group.id)}" ${group.id === appState.activeGroupId ? 'selected' : ''}>${escapeHtml(group.name)}</option>`)
  ].join('');
  const group = activeGroup();
  $('#group-context').innerHTML = group
    ? `<strong>${group.members.length} ${plural(group.members.length, 'member')}</strong><span>${group.members.map(member => escapeHtml(member.username)).join(' · ')}</span>`
    : '<strong>Just you</strong><span>Create or join a group when you want to compare routes.</span>';
  renderGroupMemberships();
}

function performanceHtml(performance, selected, clash, timelinePosition = null) {
  const attendees = usersForPerformance(performance.id);
  const otherCount = attendees.filter(user => !sameName(user.username, appState.username)).length;
  const isShared = selected && otherCount > 0;
  const end = formatTime(performance.endsAt);
  const attendeeAvatars = attendees.slice(0, 4).reverse().map(user => avatarHtml(user, true)).join('');
  const attendeeNames = attendees.map(user => user.username).join(', ');
  const expandableAttendees = appState.activeView === 'mine' && attendees.length > 0;
  const attendeeBadges = expandableAttendees
    ? `<button type="button" class="attendance-avatars attendance-avatars--expandable" data-action="toggle-attendee-names" aria-expanded="false" aria-label="Show attendee names for ${escapeHtml(performance.artist)}">${attendeeAvatars}</button>
       <span class="attendance-names-popover" hidden>${escapeHtml(attendeeNames)}</span>`
    : `<div class="attendance-avatars">${attendeeAvatars}</div>`;
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
      ${attendeeBadges}
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
  const users = contextUsers();
  if (!activeGroup()) {
    $('#people-picker').innerHTML = `<div class="personal-compare-note"><b>Personal mode</b><span>Join or create a group to compare schedules with friends.</span><button class="button button--ghost button--compact" type="button" data-action="open-groups-modal">Manage groups</button></div>`;
    return;
  }
  $('#people-picker').innerHTML = users.map(user => `
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
    persistLastSelectedEvent().catch(error => toast(`Festival created, but your selection could not be saved: ${error.message}`, true));
    toast(`${created.name} is ready`);
  } catch (error) { errorNode.textContent = error.message; }
}

function openDeleteEventModal() {
  if (!appState.isAdmin) return toast('Administrator access is required', true);
  const festivalEvent = currentEvent();
  if (!festivalEvent) return toast('There is no festival to delete', true);

  const performanceCount = festivalEvent.days.reduce((total, day) => total + day.performances.length, 0);
  $('#delete-event-name').textContent = festivalEvent.name;
  $('#delete-event-summary').textContent = `${performanceCount} ${plural(performanceCount, 'act')} and every pick linked to them will be removed.`;
  $('.modal-error', $('#delete-event-form')).textContent = '';
  $('#delete-event-modal').showModal();
}

async function deleteEvent(event) {
  event.preventDefault();
  if (!appState.isAdmin) return toast('Administrator access is required', true);
  const festivalEvent = currentEvent();
  if (!festivalEvent) return;

  const form = event.currentTarget;
  const submitButton = $('button[type="submit"]', form);
  const errorNode = $('.modal-error', form);
  errorNode.textContent = '';
  submitButton.disabled = true;
  try {
    const result = await api(`/api/events/${encodeURIComponent(festivalEvent.id)}`, { method: 'DELETE' });
    $('#delete-event-modal').close();
    await loadApp();
    toast(`${result.deletedEventName} deleted`);
  } catch (error) {
    errorNode.textContent = error.message;
  } finally {
    submitButton.disabled = false;
  }
}

async function createGroup(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const errorNode = $('.modal-error', form);
  errorNode.textContent = '';
  try {
    const name = new FormData(form).get('name').trim();
    const result = await api('/api/groups', { method: 'POST', body: JSON.stringify({ name }) });
    appState.activeGroupId = result.group.id;
    form.reset();
    $('#new-group-code').textContent = result.joinCode;
    $('#join-code-result').hidden = false;
    await refreshAfterGroupChange();
    toast(`${result.group.name} created`);
  } catch (error) { errorNode.textContent = error.message; }
}

async function joinGroup(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const errorNode = $('.modal-error', form);
  errorNode.textContent = '';
  try {
    const joinCode = new FormData(form).get('joinCode').trim();
    const result = await api('/api/groups/join', { method: 'POST', body: JSON.stringify({ joinCode }) });
    appState.activeGroupId = result.group.id;
    form.reset();
    await refreshAfterGroupChange();
    toast(result.alreadyMember ? `${result.group.name} selected` : `Joined ${result.group.name}`);
  } catch (error) { errorNode.textContent = error.message; }
}

async function leaveGroup(groupId) {
  const group = appState.data.groups.find(item => item.id === groupId);
  if (!group || !window.confirm(`Leave ${group.name}? Your personal picks will be kept.`)) return;
  try {
    await api(`/api/groups/${encodeURIComponent(groupId)}/leave`, { method: 'POST' });
    if (appState.activeGroupId === groupId) appState.activeGroupId = null;
    await refreshAfterGroupChange();
    toast(`Left ${group.name}`);
  } catch (error) { toast(error.message, true); }
}

async function refreshAfterGroupChange() {
  persistActiveGroup();
  appState.data = await api('/api/state');
  restoreActiveGroup();
  initializeCompareUsers();
  renderAll();
}

function renderGroupMemberships() {
  const container = $('#group-memberships');
  if (!container || !appState.data) return;
  const groups = appState.data.groups || [];
  container.innerHTML = groups.length ? groups.map(group => `
    <article class="group-membership ${group.id === appState.activeGroupId ? 'active' : ''}">
      <button type="button" class="group-membership-main" data-action="select-group" data-group-id="${escapeHtml(group.id)}">
        <b>${escapeHtml(group.name)}</b>
        <span>${group.members.length} ${plural(group.members.length, 'member')} · ${group.members.map(member => escapeHtml(member.username)).join(', ')}</span>
      </button>
      <div class="group-membership-actions">
        ${group.isOwner ? `<button type="button" class="text-button" data-action="new-group-code" data-group-id="${escapeHtml(group.id)}">New join code</button>` : ''}
        <button type="button" class="text-button" data-action="leave-group" data-group-id="${escapeHtml(group.id)}">Leave</button>
      </div>
    </article>`).join('') : '<div class="empty-compact">No groups yet. Your running order and personal route still work independently.</div>';
}

function openGroupsModal() {
  $('#join-code-result').hidden = true;
  $('#new-group-code').textContent = '';
  $$('.modal-error', $('#groups-modal')).forEach(node => { node.textContent = ''; });
  renderGroupMemberships();
  $('#groups-modal').showModal();
}

async function copyGroupCode() {
  const code = $('#new-group-code').textContent;
  if (!code) return;
  try {
    await navigator.clipboard.writeText(code);
    toast('Join code copied');
  } catch { toast(`Join code: ${code}`); }
}

async function issueGroupCode(groupId) {
  try {
    const result = await api(`/api/groups/${encodeURIComponent(groupId)}/join-code`, { method: 'POST' });
    $('#new-group-code').textContent = result.joinCode;
    $('#join-code-result').hidden = false;
    toast('New join code ready');
  } catch (error) { toast(error.message, true); }
}

function selectGroup(groupId) {
  appState.activeGroupId = groupId;
  persistActiveGroup();
  initializeCompareUsers();
  renderAll();
  $('#groups-modal').close();
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
  if (action === 'toggle-attendee-names') toggleAttendeeNames(node);
  if (action === 'sign-out') signOut();
  if (action === 'open-event-modal') openEventModal();
  if (action === 'open-delete-event-modal') openDeleteEventModal();
  if (action === 'open-performance-modal') openPerformanceModal();
  if (action === 'open-poster-import') openPosterImport();
  if (action === 'close-poster-import') $('#poster-import-modal').close();
  if (action === 'start-poster-ocr') startPosterOcr();
  if (action === 'add-poster-row') addPosterRow();
  if (action === 'remove-poster-row') removePosterRow(node.dataset.rowId);
  if (action === 'import-poster-rows') importPosterRows();
  if (action === 'open-notification-modal') openNotificationModal();
  if (action === 'enable-notifications') enableNotifications();
  if (action === 'disable-notifications') disableNotifications();
  if (action === 'test-notification') sendTestNotification();
  if (action === 'open-groups-modal') openGroupsModal();
  if (action === 'copy-group-code') copyGroupCode();
  if (action === 'select-group') selectGroup(node.dataset.groupId);
  if (action === 'new-group-code') issueGroupCode(node.dataset.groupId);
  if (action === 'leave-group') leaveGroup(node.dataset.groupId);
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

async function changeEvent(event) {
  appState.activeEventId = event.target.value;
  appState.activeDayId = currentEvent()?.days[0]?.id;
  appState.visibleStages.clear();
  renderAll();
  try {
    await persistLastSelectedEvent();
  } catch (error) {
    toast(`Could not save your festival selection: ${error.message}`, true);
  }
}

async function persistLastSelectedEvent() {
  if (!appState.activeEventId) return;
  const result = await api('/api/account/last-event', {
    method: 'PUT',
    body: JSON.stringify({ eventId: appState.activeEventId })
  });
  if (appState.data) appState.data.lastSelectedEventId = result.eventId;
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
  persistCompareUsers();
  renderComparePicker();
  renderCompare();
}

function changeGroup(event) {
  appState.activeGroupId = event.target.value || null;
  persistActiveGroup();
  initializeCompareUsers();
  renderHeader();
  renderGroupContext();
  renderSchedule();
  renderComparePicker();
  renderCompare();
}

function initializeCompareUsers() {
  const availableUsers = contextUsers();
  const storedUsers = readStoredCompareUsers();
  if (storedUsers) {
    appState.compareUsers = new Set(availableUsers
      .filter(user => storedUsers.some(name => sameName(name, user.username)))
      .map(user => user.username));
    persistCompareUsers();
    return;
  }

  appState.compareUsers = new Set(availableUsers.map(user => user.username));
  persistCompareUsers();
}

function compareUsersStorageKey() {
  return `${COMPARE_USERS_KEY}.${encodeURIComponent(appState.username.toLocaleLowerCase())}.${encodeURIComponent(appState.activeGroupId || 'personal')}`;
}

function readStoredCompareUsers() {
  try {
    const stored = localStorage.getItem(compareUsersStorageKey());
    if (stored === null) return null;
    const users = JSON.parse(stored);
    return Array.isArray(users) && users.every(user => typeof user === 'string') ? users : null;
  } catch {
    return null;
  }
}

function persistCompareUsers() {
  try {
    localStorage.setItem(compareUsersStorageKey(), JSON.stringify([...appState.compareUsers]));
  } catch { /* Compare choices can still work for this visit when browser storage is unavailable. */ }
}

function toggleAttendeeNames(button) {
  const willExpand = button.getAttribute('aria-expanded') !== 'true';
  closeAttendeeNames();
  if (!willExpand) return;

  const performance = button.closest('.performance');
  const names = $('.attendance-names-popover', performance);
  if (!names) return;
  button.setAttribute('aria-expanded', 'true');
  performance.classList.add('attendee-names-expanded');
  names.hidden = false;
}

function closeAttendeeNames() {
  $$('.attendance-avatars--expandable[aria-expanded="true"]').forEach(button => {
    button.setAttribute('aria-expanded', 'false');
    const performance = button.closest('.performance');
    performance?.classList.remove('attendee-names-expanded');
    const names = performance && $('.attendance-names-popover', performance);
    if (names) names.hidden = true;
  });
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

function openPosterImport() {
  if (!appState.isAdmin) return toast('Administrator access is required', true);
  const activeEvent = currentEvent();
  if (!activeEvent) return;
  resetPosterImport();
  $('#poster-import-event-label').textContent = `Importing into ${activeEvent.name}`;
  $('#poster-import-modal').showModal();
}

function resetPosterImport() {
  const state = appState.posterImport;
  state.runId++;
  if (state.worker) state.worker.terminate().catch(() => {});
  if (state.imageUrl) URL.revokeObjectURL(state.imageUrl);
  state.file = null;
  state.imageUrl = null;
  state.rows = [];
  state.rawText = '';
  state.worker = null;
  state.processing = false;
  state.nextRowId = 1;

  const fileInput = $('#poster-file');
  fileInput.value = '';
  fileInput.disabled = false;
  $('.poster-file-picker span').textContent = 'Choose poster';
  $('#poster-preview').removeAttribute('src');
  $('#poster-preview').hidden = true;
  $('#poster-preview-empty').hidden = false;
  $('#poster-ocr-start').disabled = true;
  $('#poster-ocr-start').textContent = 'Read poster';
  $('#poster-review').hidden = true;
  $('#poster-import-rows').innerHTML = '';
  $('#poster-raw-text').textContent = '';
  $('#poster-import-error').textContent = '';
  updatePosterOcrStatus('idle', 'Choose a poster to begin', 'Extraction may take around a minute on a phone.', 0, false);
}

function posterFileChanged(event) {
  const file = event.target.files?.[0];
  const errorNode = $('#poster-import-error');
  errorNode.textContent = '';
  if (!file) return;
  if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
    event.target.value = '';
    errorNode.textContent = 'Choose a PNG, JPEG or WebP image.';
    return;
  }
  if (file.size > 25 * 1024 * 1024) {
    event.target.value = '';
    errorNode.textContent = 'That poster is larger than 25 MB. Export a smaller copy and try again.';
    return;
  }

  const state = appState.posterImport;
  if (state.imageUrl) URL.revokeObjectURL(state.imageUrl);
  state.file = file;
  state.imageUrl = URL.createObjectURL(file);
  state.rows = [];
  state.rawText = '';
  $('.poster-file-picker span').textContent = file.name;
  $('#poster-preview').src = state.imageUrl;
  $('#poster-preview').hidden = false;
  $('#poster-preview-empty').hidden = true;
  $('#poster-ocr-start').disabled = false;
  $('#poster-ocr-start').textContent = 'Read poster';
  $('#poster-review').hidden = true;
  updatePosterOcrStatus('idle', 'Poster ready on this device', 'Press Read poster to extract acts, dates, stages and times.', 0, false);
}

async function startPosterOcr() {
  const state = appState.posterImport;
  if (!state.file || state.processing) return;
  if (!window.Tesseract) {
    $('#poster-import-error').textContent = 'The OCR engine did not load. Refresh the page and try again.';
    return;
  }

  const runId = ++state.runId;
  state.processing = true;
  state.rows = [];
  $('#poster-import-error').textContent = '';
  $('#poster-review').hidden = true;
  $('#poster-file').disabled = true;
  $('#poster-ocr-start').disabled = true;
  $('#poster-ocr-start').textContent = 'Reading…';
  updatePosterOcrStatus('working', 'Preparing the poster', 'Improving contrast for small timetable text.', 2, true);

  try {
    const preparedImage = await prepareImageForOcr(state.file);
    if (runId !== state.runId) return;
    state.worker = await Tesseract.createWorker('eng', Tesseract.OEM.LSTM, {
      workerPath: '/vendor/tesseract/worker.min.js',
      corePath: '/vendor/tesseract/core',
      langPath: '/vendor/tesseract/lang',
      logger: message => updatePosterOcrProgress(message, runId),
      errorHandler: error => console.warn('Poster OCR worker error.', error)
    });
    if (runId !== state.runId) return;
    await state.worker.setParameters({
      tessedit_pageseg_mode: Tesseract.PSM.SPARSE_TEXT,
      preserve_interword_spaces: '1'
    });
    const result = await state.worker.recognize(preparedImage.blob, { rotateAuto: true }, { text: true, tsv: true });
    if (runId !== state.runId) return;

    state.rawText = result.data.text || '';
    state.rows = parsePosterSchedule(result.data.tsv || '', state.rawText, currentEvent(), preparedImage.classifyTextColor);
    $('#poster-raw-text').textContent = state.rawText || 'No text was recognized.';
    renderPosterRows();
    $('#poster-review').hidden = false;
    const detail = state.rows.length
      ? 'Compare each row with the original poster and correct anything that looks wrong.'
      : 'No schedule rows were found. You can add rows manually or try a clearer image.';
    updatePosterOcrStatus('ready', `${state.rows.length} ${plural(state.rows.length, 'act')} extracted`, detail, 100, false);
  } catch (error) {
    if (runId === state.runId) {
      console.warn('Poster OCR failed.', error);
      $('#poster-import-error').textContent = 'The poster could not be read. Try a sharper, higher-resolution image.';
      updatePosterOcrStatus('error', 'Could not read this poster', 'The image remains on this device and can still be used for manual entry.', 0, false);
    }
  } finally {
    if (state.worker) await state.worker.terminate().catch(() => {});
    if (runId === state.runId) {
      state.worker = null;
      state.processing = false;
      $('#poster-file').disabled = false;
      $('#poster-ocr-start').disabled = !state.file;
      $('#poster-ocr-start').textContent = 'Read again';
    }
  }
}

function updatePosterOcrProgress(message, runId) {
  if (runId !== appState.posterImport.runId) return;
  const labels = {
    'loading tesseract core': 'Loading the on-device OCR engine',
    'initializing tesseract': 'Starting the on-device OCR engine',
    'loading language traineddata': 'Loading the English recognition model',
    'initializing api': 'Preparing text recognition',
    'recognizing text': 'Reading timetable text'
  };
  const progress = Math.round((message.progress || 0) * 100);
  updatePosterOcrStatus('working', labels[message.status] || 'Reading the poster', `${progress}% complete. Keep this window open.`, progress, true);
}

function updatePosterOcrStatus(state, title, detail, progress, showProgress) {
  $('#poster-ocr-status').dataset.state = state;
  $('#poster-ocr-status-title').textContent = title;
  $('#poster-ocr-status-detail').textContent = detail;
  $('#poster-ocr-progress').value = progress;
  $('#poster-ocr-progress').hidden = !showProgress;
}

async function prepareImageForOcr(file) {
  const bitmap = await createImageBitmap(file);
  try {
    const longestEdge = Math.max(bitmap.width, bitmap.height);
    let scale = Math.min(2, 2800 / longestEdge);
    const scaledPixels = bitmap.width * bitmap.height * scale * scale;
    if (scaledPixels > 10_000_000) scale *= Math.sqrt(10_000_000 / scaledPixels);
    const width = Math.max(1, Math.round(bitmap.width * scale));
    const height = Math.max(1, Math.round(bitmap.height * scale));
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d', { willReadFrequently: true });
    context.drawImage(bitmap, 0, 0, width, height);
    const image = context.getImageData(0, 0, width, height);
    const sourcePixels = new Uint8ClampedArray(image.data);
    let luminanceTotal = 0;
    for (let index = 0; index < image.data.length; index += 4)
      luminanceTotal += image.data[index] * .299 + image.data[index + 1] * .587 + image.data[index + 2] * .114;
    const shouldInvert = luminanceTotal / (image.data.length / 4) < 118;
    for (let index = 0; index < image.data.length; index += 4) {
      let luminance = image.data[index] * .299 + image.data[index + 1] * .587 + image.data[index + 2] * .114;
      if (shouldInvert) luminance = 255 - luminance;
      luminance = Math.max(0, Math.min(255, (luminance - 128) * 1.45 + 128));
      image.data[index] = luminance;
      image.data[index + 1] = luminance;
      image.data[index + 2] = luminance;
    }
    context.putImageData(image, 0, 0);
    const blob = await new Promise((resolve, reject) => canvas.toBlob(
      blob => blob ? resolve(blob) : reject(new Error('Could not prepare the poster image.')),
      'image/png'
    ));
    return {
      blob,
      classifyTextColor: box => classifyPosterTextColor(sourcePixels, width, height, box)
    };
  } finally {
    bitmap.close();
  }
}

function classifyPosterTextColor(pixels, width, height, box) {
  const left = Math.max(0, Math.floor(box.left));
  const top = Math.max(0, Math.floor(box.top));
  const right = Math.min(width, Math.ceil(box.left + box.width));
  const bottom = Math.min(height, Math.ceil(box.top + box.height));
  const stride = Math.max(1, Math.floor(Math.max(right - left, bottom - top) / 90));
  let yellow = 0;
  let white = 0;
  for (let y = top; y < bottom; y += stride) {
    for (let x = left; x < right; x += stride) {
      const index = (y * width + x) * 4;
      const red = pixels[index];
      const green = pixels[index + 1];
      const blue = pixels[index + 2];
      if (red > 115 && green > 75 && red > blue * 1.35 && green > blue * 1.18) yellow++;
      else if (red > 125 && green > 125 && blue > 125 && Math.max(red, green, blue) - Math.min(red, green, blue) < 58) white++;
    }
  }
  if (yellow >= 5 && yellow > white * .18) return 'yellow';
  if (white >= 5) return 'white';
  return null;
}

function parsePosterSchedule(tsv, rawText, festivalEvent, classifyTextColor = null) {
  let lines = parseTsvLines(tsv);
  if (!lines.length) {
    lines = rawText.split(/\r?\n/).map((text, index) => ({
      text: text.trim(), left: 0, top: index * 20, width: 1000, height: 18, confidence: 40
    })).filter(line => line.text);
  }

  const dayHeadings = findPosterDayHeadings(lines, festivalEvent);
  const knownStages = [...new Set((festivalEvent?.days || []).flatMap(day => day.performances.map(performance => performance.stage)))];
  const stageHeadings = findPosterStageHeadings(lines, knownStages);
  const rows = [];
  const seen = new Set();
  const spatialPerformances = extractSpatialPosterPerformances(parseTsvWords(tsv), festivalEvent, classifyTextColor);
  const linePerformances = spatialPerformances.length ? [] : lines.flatMap(extractPerformancesFromLine);

  for (const performance of [...spatialPerformances, ...linePerformances]) {
      const dayMatch = inferPosterHeading(performance, dayHeadings, 1);
      const stageMatch = inferPosterHeading(performance, stageHeadings, .2);
      const dayDate = dayMatch?.value || currentDay()?.date || festivalEvent?.days[0]?.date || '';
      const availableStages = [...new Set([...knownStages, ...stageHeadings.map(heading => heading.value)])];
      const mainA = availableStages.find(stage => normalizePosterText(stage).replace('STAGE', '').replace(/\s/g, '') === 'MAINA');
      const mainB = availableStages.find(stage => normalizePosterText(stage).replace('STAGE', '').replace(/\s/g, '') === 'MAINB');
      let stage = stageMatch?.value || knownStages[0] || 'Stage 1';
      const matchedMainStage = stageMatch && (stageMatch.value === mainA || stageMatch.value === mainB);
      if (matchedMainStage && performance.colorHint === 'yellow') stage = mainA;
      if (matchedMainStage && performance.colorHint === 'white') stage = mainB;
      const key = `${dayDate}|${stage.toLocaleLowerCase()}|${performance.start}|${performance.artist.toLocaleLowerCase()}`;
      if (seen.has(key)) continue;
      seen.add(key);
      rows.push({
        id: appState.posterImport.nextRowId++,
        dayDate,
        stage,
        start: performance.start,
        end: performance.end,
        artist: performance.artist,
        needsReview: performance.confidence < 72 || !performance.end || !dayMatch || !stageMatch ||
          ((stage === mainA || stage === mainB) && !performance.colorHint)
      });
  }

  const dayIndex = new Map((festivalEvent?.days || []).map((day, index) => [day.date, index]));
  return rows.sort((a, b) =>
    (dayIndex.get(a.dayDate) ?? 999) - (dayIndex.get(b.dayDate) ?? 999) ||
    scheduleMinutes(a.start) - scheduleMinutes(b.start) ||
    a.stage.localeCompare(b.stage));
}

function parseTsvWords(tsv) {
  const records = tsv.split(/\r?\n/);
  if (!records.length) return [];
  const defaultHeader = ['level', 'page_num', 'block_num', 'par_num', 'line_num', 'word_num', 'left', 'top', 'width', 'height', 'conf', 'text'];
  const hasHeader = records[0].split('\t')[0] === 'level';
  const header = hasHeader ? records[0].split('\t') : defaultHeader;
  const indexOf = name => header.indexOf(name);
  const words = [];
  for (const record of records.slice(hasHeader ? 1 : 0)) {
    if (!record.trim()) continue;
    const fields = record.split('\t');
    if (Number(fields[indexOf('level')]) !== 5) continue;
    const text = fields.slice(indexOf('text')).join('\t').trim();
    if (!text) continue;
    words.push({
      text,
      left: Number(fields[indexOf('left')]) || 0,
      top: Number(fields[indexOf('top')]) || 0,
      width: Number(fields[indexOf('width')]) || 0,
      height: Number(fields[indexOf('height')]) || 0,
      confidence: Number(fields[indexOf('conf')]) || 0
    });
  }
  return words;
}

function extractSpatialPosterPerformances(words, festivalEvent, classifyTextColor) {
  if (!words.length) return [];
  const timeWords = words.map(word => ({ word, parsed: parsePosterTimeToken(word.text) })).filter(item => item.parsed);
  const primaryTimes = timeWords.filter(item => item.parsed.hasSeparator);
  const medianHeight = median(words.map(word => word.height).filter(Boolean)) || 14;
  const verticalTolerance = Math.max(7, medianHeight * .75);
  const pairDistance = Math.max(70, medianHeight * 8);
  const pageWidth = Math.max(...words.map(word => word.left + word.width));
  const regularDayCount = Math.max(2, Math.min(7, (festivalEvent?.days.length || 5) - 1));
  const columnSpan = pageWidth / regularDayCount;
  const consumedEnds = new Set();
  const performances = [];

  const timeBands = [];
  for (const item of [...primaryTimes].sort((a, b) => wordCenterY(a.word) - wordCenterY(b.word))) {
    const centerY = wordCenterY(item.word);
    let band = timeBands.find(candidate => Math.abs(candidate.centerY - centerY) <= verticalTolerance);
    if (!band) {
      band = { centerY, items: [] };
      timeBands.push(band);
    }
    band.items.push(item);
    band.centerY = band.items.reduce((sum, candidate) => sum + wordCenterY(candidate.word), 0) / band.items.length;
  }
  const orderedStarts = timeBands.flatMap(band => band.items.sort((a, b) => a.word.left - b.word.left));
  for (const start of orderedStarts) {
    if (consumedEnds.has(start.word)) continue;
    const startCenterY = wordCenterY(start.word);
    const end = timeWords
      .filter(candidate => candidate.word !== start.word && !consumedEnds.has(candidate.word))
      .filter(candidate => candidate.word.left > start.word.left + start.word.width)
      .filter(candidate => Math.abs(wordCenterY(candidate.word) - startCenterY) <= verticalTolerance)
      .filter(candidate => candidate.word.left - (start.word.left + start.word.width) <= pairDistance)
      .sort((a, b) => a.word.left - b.word.left)[0] || null;
    if (end) consumedEnds.add(end.word);

    const artistStart = end ? end.word.left + end.word.width : start.word.left + start.word.width;
    const nextStart = primaryTimes
      .filter(candidate => candidate.word !== start.word && candidate.word !== end?.word && !consumedEnds.has(candidate.word))
      .filter(candidate => candidate.word.left > artistStart)
      .filter(candidate => Math.abs(wordCenterY(candidate.word) - startCenterY) <= verticalTolerance)
      .sort((a, b) => a.word.left - b.word.left)[0] || null;
    const boundary = Math.min(nextStart?.word.left ?? Number.POSITIVE_INFINITY, start.word.left + columnSpan);
    const artistWords = words
      .filter(word => word !== start.word && word !== end?.word)
      .filter(word => word.left >= artistStart - 3 && word.left < boundary)
      .filter(word => Math.abs(wordCenterY(word) - startCenterY) <= verticalTolerance)
      .sort((a, b) => a.left - b.left);
    const artist = cleanPosterArtist(artistWords.map(word => word.text).join(' '));
    if (!artist || !/[\p{L}\p{N}]/u.test(artist)) continue;
    const confidenceWords = [start.word, ...(end ? [end.word] : []), ...artistWords];
    const artistBox = artistWords.length ? {
      left: Math.min(...artistWords.map(word => word.left)),
      top: Math.min(...artistWords.map(word => word.top)),
      width: Math.max(...artistWords.map(word => word.left + word.width)) - Math.min(...artistWords.map(word => word.left)),
      height: Math.max(...artistWords.map(word => word.top + word.height)) - Math.min(...artistWords.map(word => word.top))
    } : null;
    performances.push({
      artist: artist.slice(0, 160),
      start: start.parsed.value,
      end: end?.parsed.value || '',
      left: start.word.left,
      top: Math.min(...confidenceWords.map(word => word.top)),
      width: Math.max(40, boundary - start.word.left),
      height: Math.max(...confidenceWords.map(word => word.top + word.height)) - Math.min(...confidenceWords.map(word => word.top)),
      confidence: confidenceWords.reduce((sum, word) => sum + word.confidence, 0) / confidenceWords.length,
      colorHint: artistBox && classifyTextColor ? classifyTextColor(artistBox) : null
    });
  }
  return performances;
}

function parsePosterTimeToken(value) {
  const token = String(value).trim();
  const separated = token.match(/^([0-2OIl]?[0-9OIl])\s*[:.;]\s*([0-5OIls][0-9OIls])$/i);
  const compact = token.match(/^([0-2OIl]?[0-9OIl])([0-5OIls][0-9OIls])$/i);
  const match = separated || compact;
  if (!match) return null;
  const hours = Number(normalizeOcrDigits(match[1]));
  const minutes = Number(normalizeOcrDigits(match[2]));
  if (hours > 23 || minutes > 59) return null;
  return { value: `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`, hasSeparator: Boolean(separated) };
}

function wordCenterY(word) {
  return word.top + word.height / 2;
}

function median(values) {
  if (!values.length) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

function parseTsvLines(tsv) {
  const records = tsv.split(/\r?\n/);
  if (!records.length) return [];
  const defaultHeader = ['level', 'page_num', 'block_num', 'par_num', 'line_num', 'word_num', 'left', 'top', 'width', 'height', 'conf', 'text'];
  const hasHeader = records[0].split('\t')[0] === 'level';
  const header = hasHeader ? records[0].split('\t') : defaultHeader;
  const indexOf = name => header.indexOf(name);
  const groups = new Map();

  for (const record of records.slice(hasHeader ? 1 : 0)) {
    if (!record.trim()) continue;
    const fields = record.split('\t');
    if (Number(fields[indexOf('level')]) !== 5) continue;
    const text = fields.slice(indexOf('text')).join('\t').trim();
    if (!text) continue;
    const key = ['page_num', 'block_num', 'par_num', 'line_num'].map(name => fields[indexOf(name)]).join(':');
    const word = {
      text,
      left: Number(fields[indexOf('left')]) || 0,
      top: Number(fields[indexOf('top')]) || 0,
      width: Number(fields[indexOf('width')]) || 0,
      height: Number(fields[indexOf('height')]) || 0,
      confidence: Number(fields[indexOf('conf')]) || 0
    };
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(word);
  }

  return [...groups.values()].map(words => {
    words.sort((a, b) => a.left - b.left);
    const left = Math.min(...words.map(word => word.left));
    const top = Math.min(...words.map(word => word.top));
    const right = Math.max(...words.map(word => word.left + word.width));
    const bottom = Math.max(...words.map(word => word.top + word.height));
    return {
      text: words.map(word => word.text).join(' '),
      left,
      top,
      width: right - left,
      height: bottom - top,
      confidence: words.reduce((sum, word) => sum + word.confidence, 0) / words.length
    };
  }).sort((a, b) => a.top - b.top || a.left - b.left);
}

function extractPerformancesFromLine(line) {
  const matches = findPosterTimes(line.text);
  if (!matches.length) return [];
  const results = [];
  const pairCount = Math.max(1, Math.floor(matches.length / 2));
  for (let pair = 0; pair < pairCount; pair++) {
    const start = matches[pair * 2];
    const end = matches[pair * 2 + 1];
    const next = matches[pair * 2 + 2];
    const artistStart = end?.end ?? start.end;
    const artistEnd = next?.index ?? line.text.length;
    const artist = cleanPosterArtist(line.text.slice(artistStart, artistEnd));
    if (!artist || !/[\p{L}\p{N}]/u.test(artist)) continue;
    const ratio = line.text.length ? start.index / line.text.length : 0;
    results.push({
      artist: artist.slice(0, 160),
      start: start.value,
      end: end?.value || '',
      left: line.left + line.width * ratio,
      top: line.top,
      width: Math.max(40, line.width / pairCount),
      height: line.height,
      confidence: line.confidence
    });
  }
  return results;
}

function findPosterTimes(text) {
  const matches = [];
  const expression = /(^|[\s|])(?:([0-2OIl]?[0-9OIl])\s*[:.;]\s*([0-5OIl][0-9OIl])|([0-2OIl]?[0-9OIl])([0-5OIl][0-9OIl]))(?=$|[\s|])/gi;
  let match;
  while ((match = expression.exec(text)) !== null) {
    const prefixLength = match[1].length;
    const tokenIndex = match.index + prefixLength;
    if (!matches.length && tokenIndex > 8) continue;
    if (matches.length && /[\p{L}\p{N}]/u.test(text.slice(matches.at(-1).end, tokenIndex))) break;
    const hours = Number(normalizeOcrDigits(match[2] || match[4]));
    const minutes = Number(normalizeOcrDigits(match[3] || match[5]));
    if (hours > 23 || minutes > 59) continue;
    matches.push({
      index: tokenIndex,
      end: expression.lastIndex,
      value: `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`
    });
    if (matches.length === 2) break;
  }
  return matches;
}

function normalizeOcrDigits(value) {
  return String(value).replace(/[Oo]/g, '0').replace(/[Il|]/g, '1').replace(/[Ss]/g, '5');
}

function cleanPosterArtist(value) {
  return value
    .replace(/^[\s|~_\-–—>→:;.,]+/, '')
    .replace(/[\s|~_\-–—>→:;.,]+$/, '')
    .replace(/\s{2,}/g, ' ')
    .trim();
}

function findPosterDayHeadings(lines, festivalEvent) {
  const headings = [];
  for (const line of lines.filter(item => findPosterTimes(item.text).length === 0)) {
    for (const day of festivalEvent?.days || []) {
      const normalized = normalizePosterText(line.text);
      const date = new Date(`${day.date}T00:00:00Z`);
      const weekday = new Intl.DateTimeFormat('en', { weekday: 'short', timeZone: 'UTC' }).format(date).toUpperCase();
      const [, month, dateOfMonth] = day.date.split('-');
      const datePattern = new RegExp(`(^|\\D)0?${Number(dateOfMonth)}[./-]0?${Number(month)}(\\D|$)`);
      const label = normalizePosterText(day.label).slice(0, 3);
      const tokens = normalized.split(' ');
      if (!tokens.includes(weekday) && !tokens.includes(label) && !datePattern.test(line.text)) continue;
      headings.push({ value: day.date, left: line.left, top: line.top, width: line.width, height: line.height });
    }
  }
  return headings;
}

function findPosterStageHeadings(lines, knownStages) {
  const headings = [];
  const noTimes = lines.filter(item => findPosterTimes(item.text).length === 0);
  for (const line of noTimes) {
    const normalized = normalizePosterText(line.text);
    for (const stage of knownStages) {
      const stageText = normalizePosterText(stage).replace(/\bSTAGE\b/g, '').replace(/\s+/g, ' ').trim();
      const searchable = normalized.replace(/STAGE/g, '').replace(/\s+/g, ' ').trim();
      const searchableCompact = searchable.replace(/\s/g, '');
      const stageCompact = stageText.replace(/\s/g, '');
      const matchIndex = stageText.includes(' ')
        ? searchableCompact.indexOf(stageCompact)
        : searchable.split(' ').findIndex(token => token === stageText);
      if (stageText.length < 3 || matchIndex < 0) continue;
      const ratio = searchable.length ? Math.min(1, matchIndex / searchable.length) : .5;
      headings.push({ value: stage, left: line.left + line.width * ratio, top: line.top, width: Math.max(60, line.width / Math.max(1, knownStages.length)), height: line.height });
    }

    if (knownStages.length) continue;
    for (const match of normalized.matchAll(/MAIN\s*STAGE\s*([A-Z0-9])/g)) {
      const ratio = normalized.length ? match.index / normalized.length : .5;
      headings.push({ value: `Main ${match[1]}`, left: line.left + line.width * ratio, top: line.top, width: Math.max(60, line.width / 3), height: line.height });
    }
    const namedStage = ['OBSCURE', 'OCTAGON', 'KAL'].find(name => normalized.split(' ').includes(name));
    if (namedStage)
      headings.push({ value: namedStage === 'KAL' ? 'KAL' : titleCase(namedStage), left: line.left, top: line.top, width: line.width, height: line.height });
    else if (/\b(STAGE|TENT|ARENA|HALL|DOME)\b/.test(normalized))
      headings.push({ value: cleanPosterArtist(line.text).slice(0, 100), left: line.left, top: line.top, width: line.width, height: line.height });
  }
  return headings;
}

function titleCase(value) {
  return String(value).toLocaleLowerCase().replace(/\b\p{L}/gu, letter => letter.toLocaleUpperCase());
}

function inferPosterHeading(item, headings, horizontalWeight) {
  const itemCenter = item.left + item.width / 2;
  return headings
    .filter(heading => heading.top <= item.top + 4)
    .map(heading => {
      const headingCenter = heading.left + heading.width / 2;
      const verticalDistance = item.top - heading.top;
      return { ...heading, score: verticalDistance + Math.abs(itemCenter - headingCenter) * horizontalWeight };
    })
    .sort((a, b) => a.score - b.score)[0] || null;
}

function normalizePosterText(value) {
  return String(value).normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[^A-Z0-9]+/gi, ' ').trim().toUpperCase();
}

function renderPosterRows() {
  const state = appState.posterImport;
  const eventDays = currentEvent()?.days || [];
  const stages = [...new Set([...stagesForEvent(), ...state.rows.map(row => row.stage).filter(Boolean)])];
  $('#poster-stage-options').innerHTML = stages.map(stage => `<option value="${escapeHtml(stage)}"></option>`).join('');
  $('#poster-import-rows').innerHTML = state.rows.map(row => `
    <tr data-row-id="${row.id}" class="${row.needsReview ? 'needs-review' : ''}">
      <td><select data-field="dayDate" aria-label="Festival day for ${escapeHtml(row.artist)}">${eventDays.map(day => `<option value="${day.date}" ${day.date === row.dayDate ? 'selected' : ''}>${escapeHtml(day.label)}</option>`).join('')}</select></td>
      <td><input data-field="stage" list="poster-stage-options" value="${escapeHtml(row.stage)}" aria-label="Stage for ${escapeHtml(row.artist)}"></td>
      <td><input data-field="start" type="time" value="${escapeHtml(row.start)}" aria-label="Start time for ${escapeHtml(row.artist)}"></td>
      <td><input data-field="end" type="time" value="${escapeHtml(row.end)}" aria-label="End time for ${escapeHtml(row.artist)}"></td>
      <td><input data-field="artist" value="${escapeHtml(row.artist)}" aria-label="Artist name"></td>
      <td><button class="poster-row-remove" type="button" data-action="remove-poster-row" data-row-id="${row.id}" aria-label="Remove ${escapeHtml(row.artist)}">×</button></td>
    </tr>`).join('');
  updatePosterValidation();
}

function posterRowChanged(event) {
  const input = event.target.closest('[data-field]');
  if (!input) return;
  const rowNode = input.closest('[data-row-id]');
  const row = appState.posterImport.rows.find(item => item.id === Number(rowNode.dataset.rowId));
  if (!row) return;
  row[input.dataset.field] = input.value;
  row.needsReview = false;
  rowNode.classList.remove('needs-review');
  updatePosterValidation();
}

function addPosterRow() {
  const day = currentDay() || currentEvent()?.days[0];
  appState.posterImport.rows.push({
    id: appState.posterImport.nextRowId++,
    dayDate: day?.date || '',
    stage: stagesForEvent()[0] || 'Stage 1',
    start: '12:00',
    end: '12:45',
    artist: '',
    needsReview: true
  });
  renderPosterRows();
  $('#poster-import-rows tr:last-child input[data-field="artist"]')?.focus();
}

function removePosterRow(rowId) {
  appState.posterImport.rows = appState.posterImport.rows.filter(row => row.id !== Number(rowId));
  renderPosterRows();
}

function updatePosterValidation() {
  const rows = appState.posterImport.rows;
  let validCount = 0;
  rows.forEach(row => {
    const errors = validatePosterRow(row);
    if (!Object.keys(errors).length) validCount++;
    const rowNode = $(`#poster-import-rows [data-row-id="${row.id}"]`);
    if (!rowNode) return;
    $$('[data-field]', rowNode).forEach(input => {
      const invalid = Boolean(errors[input.dataset.field]);
      input.classList.toggle('invalid', invalid);
      input.setAttribute('aria-invalid', String(invalid));
      input.title = errors[input.dataset.field] || '';
    });
  });

  $('#poster-row-count').textContent = `${rows.length} extracted ${plural(rows.length, 'act')}`;
  const invalidCount = rows.length - validCount;
  $('#poster-import-validation').textContent = !rows.length
    ? 'Add at least one corrected row to import.'
    : invalidCount
      ? `${invalidCount} ${plural(invalidCount, 'row')} still ${invalidCount === 1 ? 'needs' : 'need'} attention.`
      : `${validCount} ${plural(validCount, 'act')} ready to import.`;
  $('#poster-import-submit').disabled = !rows.length || invalidCount > 0 || appState.posterImport.processing;
}

function validatePosterRow(row) {
  const errors = {};
  const eventDayDates = new Set((currentEvent()?.days || []).map(day => day.date));
  if (!eventDayDates.has(row.dayDate)) errors.dayDate = 'Choose a valid festival day.';
  if (!row.stage.trim()) errors.stage = 'Enter a stage.';
  if (!/^\d{2}:\d{2}$/.test(row.start)) errors.start = 'Enter a start time.';
  if (!/^\d{2}:\d{2}$/.test(row.end)) errors.end = 'Enter an end time.';
  if (!row.artist.trim()) errors.artist = 'Enter an artist.';
  if (!Object.keys(errors).length) {
    const payload = posterRowToPayload(row);
    const duration = new Date(payload.endsAt) - new Date(payload.startsAt);
    if (duration <= 0 || duration > 24 * 60 * 60 * 1000) errors.end = 'Choose an end time within 24 hours of the start.';
  }
  return errors;
}

function posterRowToPayload(row) {
  const startMinutes = timeMinutes(row.start);
  const endMinutes = timeMinutes(row.end);
  const startOffset = startMinutes < 6 * 60 ? 1 : 0;
  let endOffset = endMinutes < 6 * 60 ? 1 : 0;
  if (endOffset < startOffset || (endOffset === startOffset && endMinutes <= startMinutes)) endOffset = startOffset + 1;
  return {
    artist: row.artist.trim(),
    stage: row.stage.trim(),
    startsAt: localDateTimeToOffsetIso(`${addIsoDays(row.dayDate, startOffset)}T${row.start}`),
    endsAt: localDateTimeToOffsetIso(`${addIsoDays(row.dayDate, endOffset)}T${row.end}`)
  };
}

async function importPosterRows() {
  const state = appState.posterImport;
  const invalidRows = state.rows.filter(row => Object.keys(validatePosterRow(row)).length);
  if (!state.rows.length || invalidRows.length) return updatePosterValidation();
  const button = $('#poster-import-submit');
  const errorNode = $('#poster-import-error');
  errorNode.textContent = '';
  button.disabled = true;
  button.textContent = 'Importing…';
  try {
    const result = await api(`/api/events/${encodeURIComponent(appState.activeEventId)}/performances/bulk`, {
      method: 'POST',
      body: JSON.stringify({ performances: state.rows.map(posterRowToPayload) })
    });
    $('#poster-import-modal').close();
    await loadApp();
    const duplicateMessage = result.skippedDuplicates ? `; ${result.skippedDuplicates} exact ${plural(result.skippedDuplicates, 'duplicate')} skipped` : '';
    toast(`${result.created.length} ${plural(result.created.length, 'act')} imported${duplicateMessage}`);
  } catch (error) {
    errorNode.textContent = error.message;
    button.disabled = false;
    button.textContent = 'Import corrected acts';
  }
}

function timeMinutes(value) {
  const [hours, minutes] = value.split(':').map(Number);
  return hours * 60 + minutes;
}

function scheduleMinutes(value) {
  const minutes = timeMinutes(value);
  return minutes < 6 * 60 ? minutes + 24 * 60 : minutes;
}

function addIsoDays(value, days) {
  const date = new Date(`${value}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
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
function activeGroup() { return appState.data?.groups?.find(group => group.id === appState.activeGroupId) || null; }
function contextUsers() {
  const group = activeGroup();
  if (group) return group.members;
  const me = userByName(appState.username);
  return me ? [me] : [];
}
function userByName(name) { return appState.data?.users.find(user => sameName(user.username, name)); }
function selectionsForUser(name) { return appState.data?.selections.filter(selection => sameName(selection.username, name)) || []; }
function userHasPick(username, performanceId) { return appState.data.selections.some(selection => selection.performanceId === performanceId && sameName(selection.username, username)); }
function usersForPerformance(id) {
  const visibleNames = new Set(contextUsers().map(user => user.username.toLocaleLowerCase()));
  return appState.data.selections
    .filter(selection => selection.performanceId === id && visibleNames.has(selection.username.toLocaleLowerCase()))
    .map(selection => userByName(selection.username))
    .filter(Boolean);
}
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

function activeGroupStorageKey() {
  return `${ACTIVE_GROUP_KEY}.${encodeURIComponent(appState.username.toLocaleLowerCase())}`;
}

function restoreActiveGroup() {
  let stored = null;
  try { stored = localStorage.getItem(activeGroupStorageKey()); } catch { /* Personal mode remains available. */ }
  const preferred = appState.activeGroupId || stored;
  appState.activeGroupId = appState.data?.groups?.some(group => group.id === preferred) ? preferred : null;
  persistActiveGroup();
}

function persistActiveGroup() {
  try {
    if (appState.activeGroupId) localStorage.setItem(activeGroupStorageKey(), appState.activeGroupId);
    else localStorage.removeItem(activeGroupStorageKey());
  } catch { /* The selected group can still work for this visit. */ }
}

async function signOut() {
  try {
    await disconnectPushForSignOut();
    await api('/api/session/logout', { method: 'POST' });
  } catch (error) {
    if (error.status !== 401) toast(error.message, true);
  } finally {
    appState.data = null;
    appState.isAdmin = false;
    $('#account-password').value = '';
    $('#access-code').value = '';
    showLogin();
    $('#account-password').focus();
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
    if (response.status === 401 && url !== '/api/session') message = 'Your session has expired. Sign in again.';
    if (response.status === 403) message = 'Administrator access is required for that action.';
    if (response.status === 429) message = 'Too many attempts. Wait a minute and try again.';
    const error = new Error(message);
    error.status = response.status;
    throw error;
  }
  return data;
}
