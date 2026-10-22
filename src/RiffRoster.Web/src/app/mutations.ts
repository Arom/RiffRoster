import { requestBodies, requestId, type EventRequest, type PerformanceRequest } from './requests';
import { ApiError } from './api';
import { generatedApi } from './generated/client';
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
  transferOwner(value: unknown, id: string, username: string) {
    const result = group(object(value).group);
    matching(result.id, id);
    check(result.createdBy.toLowerCase() === username.toLowerCase());
    check(result.members.some(member => member.username.toLowerCase() === result.createdBy.toLowerCase()));
    return {group:result};
  },
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
async function mutate<T>(send: () => Promise<unknown>, decode: (value: unknown) => T): Promise<T> {
  let value: unknown;
  try { value = await send(); } catch (error) {
    if (error instanceof ApiError && error.status >= 200 && error.status < 300) throw new MutationResponseError();
    throw error;
  }
  // Never retry mutations automatically: a malformed response can follow a committed write.
  try { return decode(value); } catch { throw new MutationResponseError(); }
}
export const mutations = {
  transferOwner: async (id: string, username: string) => mutate(() => generatedApi.putGroupsGroupIdOwner(requestId(id), {username:requestId(username)}), value => mutationDecoders.transferOwner(value,id,username)),
  toggle: async (id: string) => mutate(() => generatedApi.postSelectionsToggle({performanceId:requestId(id)}), value => mutationDecoders.toggle(value, id)),
  createEvent: async (body: EventRequest) => mutate(() => generatedApi.postEvents(requestBodies.event(body)), mutationDecoders.createEvent),
  deleteEvent: async (id: string) => mutate(() => generatedApi.deleteEventsEventId(requestId(id)), value => mutationDecoders.deleteEvent(value, id)),
  savePerformance: async (eventId: string, id: string | null, body: PerformanceRequest) => mutate(() => id === null
    ? generatedApi.postEventsEventIdPerformances(requestId(eventId),requestBodies.performance(body))
    : generatedApi.putEventsEventIdPerformancesPerformanceId(requestId(eventId),requestId(id),requestBodies.performance(body)), value => mutationDecoders.savePerformance(value, id)),
  deletePerformance: async (eventId: string, id: string) => mutate(() => generatedApi.deleteEventsEventIdPerformancesPerformanceId(requestId(eventId),requestId(id)), value => mutationDecoders.deletePerformance(value, id)),
  bulk: async (id: string, body: { performances: PerformanceRequest[] }) => mutate(() => generatedApi.postEventsEventIdPerformancesBulk(requestId(id),requestBodies.bulk(body)), mutationDecoders.bulk),
  createGroup: async (body: { name: string }) => mutate(() => generatedApi.postGroups(requestBodies.group(body)), mutationDecoders.createGroup),
  joinGroup: async (body: { joinCode: string }) => mutate(() => generatedApi.postGroupsJoin(requestBodies.join(body)), mutationDecoders.joinGroup),
  leaveGroup: async (id: string) => mutate(() => generatedApi.postGroupsGroupIdLeave(requestId(id)), value => mutationDecoders.leaveGroup(value, id)),
  groupCode: async (id: string) => mutate(() => generatedApi.postGroupsGroupIdJoinCode(requestId(id)), value => mutationDecoders.groupCode(value, id)),
  lastEvent: async (id: string) => mutate(() => generatedApi.putAccountLastEvent({eventId:requestId(id)}), value => mutationDecoders.lastEvent(value, id))
};
