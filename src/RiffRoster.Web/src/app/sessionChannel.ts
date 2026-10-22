// Only an invalidation marker crosses tabs: no account, credentials or schedule data.
export function connectSessionChannel(onSessionChange: () => void): { changed(): void; revision(): string | null } {
  const key = 'riffroster.session.changed';
  const seen = new Set<string>();
  function receive(value: unknown): void {
    if (typeof value !== 'string' || !/^\d+-[a-f0-9-]{36}$/.test(value) || seen.has(value)) return;
    seen.add(value);
    // Bound memory for long-lived tabs while deduplicating both transports.
    if (seen.size > 128) seen.delete(seen.values().next().value!);
    onSessionChange();
  }
  let channel: BroadcastChannel | null = null;
  try {
    channel = new BroadcastChannel('riffroster.session');
    channel.onmessage = event => receive(event.data);
  } catch { /* Storage events are the fallback when BroadcastChannel is unavailable. */ }
  window.addEventListener('storage', event => {
    if (event.key === key) receive(event.newValue);
  });
  return {
    revision() {
      try { return localStorage.getItem(key); } catch { return null; }
    },
    changed() {
      let marker: string;
      try { marker = `${Date.now()}-${crypto.randomUUID()}`; }
      catch { return; }
      try { channel?.postMessage(marker); } catch { /* Try storage as well. */ }
      try { localStorage.setItem(key, marker); }
      catch { /* Local authentication still works when both transports are unavailable. */ }
    }
  };
}
