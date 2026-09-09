export class ApiError extends Error {
  constructor(message: string, readonly status: number) { super(message); this.name = 'ApiError'; }
}

// Unknown stays at the compatibility boundary until individual endpoint decoders land.
export async function requestJson(url: string, options: RequestInit = {}): Promise<unknown> {
  const headers = new Headers(options.headers);
  if (!headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  const response = await fetch(url, { ...options, headers });
  let data: unknown;
  try { data = await response.json(); } catch {
    if (response.ok && response.status !== 204) throw new ApiError('The server returned an invalid response. Try again.', response.status);
  }
  if (!response.ok) {
    let message = typeof data === 'object' && data !== null && 'message' in data && typeof data.message === 'string'
      ? data.message : `Request failed (${response.status})`;
    if (response.status === 401 && url !== '/api/session') message = 'Your session has expired. Sign in again.';
    if (response.status === 403) message = 'Administrator access is required for that action.';
    if (response.status === 429) message = 'Too many attempts. Wait a minute and try again.';
    throw new ApiError(message, response.status);
  }
  return response.status === 204 ? {} : data;
}
