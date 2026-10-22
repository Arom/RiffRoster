import type { StateSnapshot, PublicUser } from './state';

export interface EventNavigation { activeEventId: string | null; activeDayId: string | null }
type PreferenceStorage = Pick<Storage, 'getItem' | 'setItem'>;
const encode = encodeURIComponent;

export const navigation = {
  event(state: StateSnapshot, eventId: string | null, dayId: string | null = null): EventNavigation {
    const event = state.events.find(item => item.id === eventId) ?? state.events[0];
    return { activeEventId: event?.id ?? null,
      activeDayId: event?.days.find(day => day.id === dayId)?.id ?? event?.days[0]?.id ?? null };
  },
  group(state: StateSnapshot, preferred: string | null): string | null {
    return state.groups.some(group => group.id === preferred) ? preferred : null;
  },
  contextUsers(state: StateSnapshot | null, username: string, groupId: string | null): PublicUser[] {
    if (!state) return [];
    const group = state.groups.find(item => item.id === groupId);
    if (group) return group.members;
    return state.users.filter(user => user.username.localeCompare(username, undefined, { sensitivity: 'accent' }) === 0);
  }
};

function parseNames(raw: string | null): string[] | null {
  try {
    const value: unknown = raw === null ? null : JSON.parse(raw);
    return Array.isArray(value) && value.every(name => typeof name === 'string') ? value : null;
  } catch { return null; }
}
function legacyKey(username: string, groupId: string | null): string {
  return `riffroster.compareUsers.${encode(username.toLocaleLowerCase())}.${encode(groupId || 'personal')}`;
}
export const comparisonPreferences = {
  key(username: string, eventId: string | null, groupId: string | null): string {
    // Versioned namespace and distinct null marker avoid collisions with actual IDs.
    return `riffroster.compareUsers.v2.${encode(username.toLocaleLowerCase())}.${eventId === null ? '-' : `e${encode(eventId)}`}.${groupId === null ? '-' : `g${encode(groupId)}`}`;
  },
  restore(storage: PreferenceStorage, username: string, eventId: string | null, groupId: string | null, available: PublicUser[]): Set<string> {
    let names: string[] | null = null;
    try {
      const raw = storage.getItem(this.key(username, eventId, groupId));
      names = parseNames(raw === null ? storage.getItem(legacyKey(username, groupId)) : raw);
    } catch { /* Storage access can be denied; use the current crew for this visit. */ }
    return new Set(available.filter(user => names === null || names.some(name => name.localeCompare(user.username, undefined, { sensitivity: 'accent' }) === 0)).map(user => user.username));
  },
  save(storage: PreferenceStorage, username: string, eventId: string | null, groupId: string | null, names: ReadonlySet<string>): void {
    try { storage.setItem(this.key(username, eventId, groupId), JSON.stringify([...names])); }
    catch { /* In-memory choices remain usable without browser storage. */ }
  }
};
