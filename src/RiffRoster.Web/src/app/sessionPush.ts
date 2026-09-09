import { ApiError, requestJson } from './api';
import { object, string, boolean, user } from './state';
import { MutationResponseError } from './mutations';

export const sessionPushDecoders = {
  session(value: unknown) {
    const row = object(value);
    return { user: user(row.user), isAdmin: boolean(row.isAdmin), created: boolean(row.created), claimed: boolean(row.claimed) };
  },
  publicKey(value: unknown) {
    const publicKey = string(object(value).publicKey);
    // Uncompressed P-256 public key: 65 bytes, base64url encoded, first byte 0x04.
    if (!/^B[A-P][A-Za-z0-9_-]{84}[AEIMQUYcgkosw048]=?$/.test(publicKey)) throw new Error('Invalid public key');
    return { publicKey };
  },
  subscription(value: unknown) {
    const row = object(value);
    if (row.enabled !== true || typeof row.leadMinutes !== 'number' || ![10, 20, 30, 60].includes(row.leadMinutes)) throw new Error('Invalid subscription');
    return { enabled: true, leadMinutes: row.leadMinutes, includeCrewNames: boolean(row.includeCrewNames) };
  },
  test(value: unknown) {
    if (object(value).sent !== true) throw new Error('Invalid notification result');
    return { sent: true };
  }
};
async function validated<T>(url: string, options: RequestInit, decode: (value: unknown) => T, mutation = true): Promise<T> {
  let value: unknown;
  try { value = await requestJson(url, options); } catch (error) {
    if (mutation && error instanceof ApiError && error.status >= 200 && error.status < 300) throw new MutationResponseError();
    throw error;
  }
  try { return decode(value); } catch {
    if (mutation) throw new MutationResponseError();
    throw new Error('The server returned invalid notification settings. Refresh to try again.');
  }
}
async function noContent(url: string, options: RequestInit): Promise<void> {
  const headers = new Headers(options.headers);
  if (!headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  const response = await fetch(url, { ...options, headers });
  if (response.status === 204) return;
  if (response.ok) throw new MutationResponseError();
  // Keep authentication failure status available to existing cleanup handlers.
  throw new ApiError(`Request failed (${response.status})`, response.status);
}
export const sessionPush = {
  signIn: (options: RequestInit) => validated('/api/session', options, sessionPushDecoders.session),
  signOut: () => noContent('/api/session/logout', { method: 'POST' }),
  publicKey: () => validated('/api/push/public-key', {}, sessionPushDecoders.publicKey, false),
  subscribe: (options: RequestInit) => validated('/api/push/subscriptions', options, sessionPushDecoders.subscription),
  unsubscribe: (options: RequestInit) => noContent('/api/push/subscriptions', options),
  test: (options: RequestInit) => validated('/api/push/test', options, sessionPushDecoders.test)
};
