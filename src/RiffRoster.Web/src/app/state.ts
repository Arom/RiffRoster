import { generatedApi } from './generated/client';

export interface Performance { id: string; artist: string; stage: string; startsAt: string; endsAt: string }
export interface FestivalDay { id: string; date: string; label: string; performances: Performance[] }
export interface FestivalEvent { id: string; name: string; location: string; startDate: string; endDate: string; days: FestivalDay[] }
export interface PublicUser { username: string; color: string; joinedAt: string }
export interface Selection { username: string; performanceId: string }
export interface Group { id: string; name: string; createdBy: string; isOwner: boolean; members: PublicUser[] }
export interface StateSnapshot {
  currentUser: string; isAdmin: boolean; lastSelectedEventId: string | null;
  events: FestivalEvent[]; users: PublicUser[]; selections: Selection[]; groups: Group[];
  eventVersions?: Record<string, string>;
}

export class StateValidationError extends Error {
  constructor() { super('The server returned an invalid running order. Refresh to try again.'); this.name = 'StateValidationError'; }
}
function requireValid(condition: boolean): asserts condition { if (!condition) throw new StateValidationError(); }
export function object(value: unknown): Record<string, unknown> {
  requireValid(typeof value === 'object' && value !== null && !Array.isArray(value));
  return value as Record<string, unknown>;
}
export function string(value: unknown, allowEmpty = false): string {
  requireValid(typeof value === 'string' && (allowEmpty || value.trim().length > 0)); return value;
}
export function boolean(value: unknown): boolean { requireValid(typeof value === 'boolean'); return value; }
function list<T>(value: unknown, decode: (item: unknown) => T): T[] {
  requireValid(Array.isArray(value)); return value.map(decode);
}
function date(value: unknown): string {
  const result = string(value);
  requireValid(/^\d{4}-\d{2}-\d{2}$/.test(result));
  const epoch = Date.parse(`${result}T00:00:00Z`);
  requireValid(Number.isFinite(epoch) && new Date(epoch).toISOString().slice(0, 10) === result);
  return result;
}
function timestamp(value: unknown): string {
  const result = string(value);
  requireValid(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(result));
  date(result.slice(0, 10));
  requireValid(Number.isFinite(Date.parse(result)) && Number(result.slice(11, 13)) < 24);
  return result;
}
function unique(values: readonly string[]): Set<string> {
  const result = new Set(values); requireValid(result.size === values.length); return result;
}
const usernameKey = (name: string) => name.toLowerCase();
export function user(value: unknown): PublicUser {
  const row = object(value); const color = string(row.color);
  // Public colors are server-generated hex values and also feed legacy inline styles.
  requireValid(/^#[\da-f]{6}$/i.test(color));
  return { username: string(row.username), color, joinedAt: timestamp(row.joinedAt) };
}
export function decodePerformance(value: unknown): Performance {
  const row = object(value);
  const startsAt = timestamp(row.startsAt); const endsAt = timestamp(row.endsAt);
  requireValid(Date.parse(endsAt) > Date.parse(startsAt));
  return { id: string(row.id), artist: string(row.artist), stage: string(row.stage), startsAt, endsAt };
}
export function decodeEvent(value: unknown): FestivalEvent {
  const row = object(value); const startDate = date(row.startDate); const endDate = date(row.endDate);
  requireValid(startDate <= endDate);
  const days = list(row.days, value => {
    const day = object(value);
    return { id: string(day.id), date: date(day.date), label: string(day.label), performances: list(day.performances, decodePerformance) };
  });
  unique(days.map(day => day.id));
  requireValid(days.every(day => day.date >= startDate && day.date <= endDate));
  return { id: string(row.id), name: string(row.name), location: string(row.location, true), startDate, endDate, days };
}

export function decodeState(value: unknown): StateSnapshot {
  const row = object(value);
  const currentUser = string(row.currentUser); const isAdmin = boolean(row.isAdmin);
  const lastSelectedEventId = row.lastSelectedEventId === null ? null : string(row.lastSelectedEventId);
  const events = list(row.events, decodeEvent); unique(events.map(item => item.id));
  const performanceIds = unique(events.flatMap(item => item.days.flatMap(day => day.performances.map(act => act.id))));
  const users = list(row.users, user); const userNames = unique(users.map(item => usernameKey(item.username)));
  requireValid(userNames.has(usernameKey(currentUser)));
  const selections = list(row.selections, value => {
    const selection = object(value);
    return { username: string(selection.username), performanceId: string(selection.performanceId) };
  });
  unique(selections.map(selection => JSON.stringify([usernameKey(selection.username), selection.performanceId])));
  requireValid(selections.every(selection => userNames.has(usernameKey(selection.username)) && performanceIds.has(selection.performanceId)));
  const groups = list(row.groups, value => {
    const group = object(value); const members = list(group.members, user);
    const names = unique(members.map(member => usernameKey(member.username)));
    requireValid(names.has(usernameKey(currentUser)) && members.every(member => userNames.has(usernameKey(member.username))));
    return { id: string(group.id), name: string(group.name), createdBy: string(group.createdBy), isOwner: boolean(group.isOwner), members };
  });
  unique(groups.map(group => group.id));
  // A stale preferred event is tolerated: the existing controller chooses the first available event.
  const result: StateSnapshot = { currentUser, isAdmin, lastSelectedEventId, events, users, selections, groups };
  if (row.eventVersions !== undefined) {
    const versions = object(row.eventVersions);
    result.eventVersions = Object.fromEntries(events.map(event => {
      const version = string(versions[event.id]);
      requireValid(/^[A-F0-9]{64}$/.test(version));
      return [event.id, version];
    }));
  }
  return result;
}

export async function fetchState(): Promise<StateSnapshot> { return decodeState(await generatedApi.getState()); }
