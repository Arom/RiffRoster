import type { LoginRequest, CreateEventRequest, CreatePerformanceRequest, SavePushSubscriptionRequest } from './generated/client';
type Present<T> = { [K in keyof T]: NonNullable<T[K]> };
export type SignInRequest = Present<LoginRequest>;
export type EventRequest = CreateEventRequest;
export type PerformanceRequest = CreatePerformanceRequest;
export type SubscriptionRequest = Present<SavePushSubscriptionRequest>;
export class RequestValidationError extends Error {
  constructor() {
    super('The form contains invalid or missing fields. Refresh the page and try again. Nothing was sent.');
    this.name = 'RequestValidationError';
  }
}
function check(condition: boolean): asserts condition { if (!condition) throw new RequestValidationError(); }
function record(value: unknown): Record<string, unknown> {
  check(typeof value === 'object' && value !== null && !Array.isArray(value));
  return value as Record<string, unknown>;
}
function text(value: unknown): string { check(typeof value === 'string'); return value; }
export function requestId(value: unknown): string {
  const id = text(value); check(id.trim().length > 0); return id;
}
function fields<K extends string>(value: unknown, names: readonly K[]): Record<K, string> {
  const row = record(value);
  // Copy only contract fields. Never serialize an untrusted object's toJSON method.
  return Object.fromEntries(names.map(name => [name, text(row[name])])) as Record<K, string>;
}
export const requestBodies = {
  signIn: (value: SignInRequest) => fields(value, ['loginName', 'displayName', 'password', 'accessCode']),
  event: (value: EventRequest) => fields(value, ['name', 'location', 'startDate', 'endDate']),
  performance: (value: PerformanceRequest) => fields(value, ['artist', 'stage', 'startsAt', 'endsAt']),
  group: (value: { name: string }) => fields(value, ['name']),
  join: (value: { joinCode: string }) => fields(value, ['joinCode']),
  endpoint: (value: { endpoint: string }) => fields(value, ['endpoint']),
  bulk(value: { performances: PerformanceRequest[] }) {
    const row = record(value); check(Array.isArray(row.performances));
    return { performances: row.performances.map(act => fields(act, ['artist', 'stage', 'startsAt', 'endsAt'])) };
  },
  subscription(value: SubscriptionRequest) {
    const row = record(value);
    check(typeof row.leadMinutes === 'number' && [10, 20, 30, 60].includes(row.leadMinutes));
    check(typeof row.includeCrewNames === 'boolean');
    return { endpoint: text(row.endpoint), keys: fields(row.keys, ['p256dh', 'auth']),
      leadMinutes: row.leadMinutes, includeCrewNames: row.includeCrewNames };
  }
};
export function jsonRequest(method: 'POST' | 'PUT' | 'DELETE', body: object): RequestInit {
  return { method, body: JSON.stringify(body) };
}
