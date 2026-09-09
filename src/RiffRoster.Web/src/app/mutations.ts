import { ApiError, requestJson } from './api';
import { object, string, boolean, user, decodeEvent, decodePerformance, type Group } from './state';

export class MutationResponseError extends Error {
  constructor() {
    super('The server returned an invalid result. Your change may have been saved. Refresh before trying again.');
    this.name = 'MutationResponseError';
  }
}
function check(condition: boolean): asserts condition { if (!condition) throw new MutationResponseError(); }
function count(value: unknown): number { check(typeof value === 'number' && Number.isSafeInteger(value) && value >= 0); return value; }
function matching(value: unknown, expected: string): string { const id = string(value); check(id === expected); return id; }
function group(value: unknown): Group {
  const row = object(value); check(Array.isArray(row.members));
  const members = row.members.map(user);
  check(members.length > 0 && new Set(members.map(member => member.username.toLowerCase())).size === members.length);
  return { id: string(row.id), name: string(row.name), createdBy: string(row.createdBy), isOwner: boolean(row.isOwner), members };
}
export const mutationDecoders = {
  toggle(value: unknown, id: string) {
    const row = object(value);
    return { selected: boolean(row.selected), performanceId: matching(row.performanceId, id), username: string(row.username) };
  },
  createEvent: decodeEvent,
  savePerformance(value: unknown, id: string | null) {
    const result = decodePerformance(value); if (id !== null) matching(result.id, id); return result;
  },
  deleteEvent(value: unknown, id: string) {
    const row = object(value);
    return { deletedEventId: matching(row.deletedEventId, id), deletedEventName: string(row.deletedEventName),
      removedPerformances: count(row.removedPerformances), removedSelections: count(row.removedSelections),
      nextEventId: row.nextEventId === null ? null : string(row.nextEventId) };
  },
  deletePerformance(value: unknown, id: string) {
    const row = object(value);
    return { deletedPerformanceId: matching(row.deletedPerformanceId, id), deletedArtist: string(row.deletedArtist), removedSelections: count(row.removedSelections) };
  },
  bulk(value: unknown) {
    const row = object(value); check(Array.isArray(row.created)); const created = row.created.map(decodePerformance);
    check(new Set(created.map(act => act.id)).size === created.length);
    return { created, skippedDuplicates: count(row.skippedDuplicates) };
  },
  createGroup(value: unknown) { const row = object(value); return { group: group(row.group), joinCode: string(row.joinCode) }; },
  joinGroup(value: unknown) { const row = object(value); return { group: group(row.group), alreadyMember: boolean(row.alreadyMember) }; },
  leaveGroup(value: unknown, id: string) { const row = object(value); check(row.left === true); return { left: true, groupId: matching(row.groupId, id) }; },
  groupCode(value: unknown, id: string) { const row = object(value); return { groupId: matching(row.groupId, id), joinCode: string(row.joinCode) }; },
  lastEvent(value: unknown, id: string) { return { eventId: matching(object(value).eventId, id) }; }
};
async function mutate<T>(url: string, options: RequestInit, decode: (value: unknown) => T): Promise<T> {
  let value: unknown;
  try { value = await requestJson(url, options); } catch (error) {
    if (error instanceof ApiError && error.status >= 200 && error.status < 300) throw new MutationResponseError();
    throw error;
  }
  // Never retry mutations automatically: a malformed response can follow a committed write.
  try { return decode(value); } catch { throw new MutationResponseError(); }
}
const eventPath = (id: string) => `/api/events/${encodeURIComponent(id)}`;
const groupPath = (id: string) => `/api/groups/${encodeURIComponent(id)}`;
export const mutations = {
  toggle: (id: string, options: RequestInit) => mutate('/api/selections/toggle', options, value => mutationDecoders.toggle(value, id)),
  createEvent: (options: RequestInit) => mutate('/api/events', options, mutationDecoders.createEvent),
  deleteEvent: (id: string, options: RequestInit) => mutate(eventPath(id), options, value => mutationDecoders.deleteEvent(value, id)),
  savePerformance: (eventId: string, id: string | null, options: RequestInit) => mutate(`${eventPath(eventId)}/performances${id === null ? '' : `/${encodeURIComponent(id)}`}`, options, value => mutationDecoders.savePerformance(value, id)),
  deletePerformance: (eventId: string, id: string, options: RequestInit) => mutate(`${eventPath(eventId)}/performances/${encodeURIComponent(id)}`, options, value => mutationDecoders.deletePerformance(value, id)),
  bulk: (id: string, options: RequestInit) => mutate(`${eventPath(id)}/performances/bulk`, options, mutationDecoders.bulk),
  createGroup: (options: RequestInit) => mutate('/api/groups', options, mutationDecoders.createGroup),
  joinGroup: (options: RequestInit) => mutate('/api/groups/join', options, mutationDecoders.joinGroup),
  leaveGroup: (id: string, options: RequestInit) => mutate(`${groupPath(id)}/leave`, options, value => mutationDecoders.leaveGroup(value, id)),
  groupCode: (id: string, options: RequestInit) => mutate(`${groupPath(id)}/join-code`, options, value => mutationDecoders.groupCode(value, id)),
  lastEvent: (id: string, options: RequestInit) => mutate('/api/account/last-event', options, value => mutationDecoders.lastEvent(value, id))
};
