// All browser subscription access and cookie-changing requests use this origin-wide lock.
export async function withPushOperation<T>(operation: (locked: boolean) => Promise<T>): Promise<T> {
  const locks = typeof navigator === 'undefined' ? undefined : navigator.locks;
  if (locks) return locks.request('riffroster.push-acquisition', () => operation(true));
  return operation(false);
}

// Coordinate acquisition and stale cleanup across cooperating same-origin tabs.
export async function acquirePushSubscription(
  manager: Pick<PushManager, 'getSubscription' | 'subscribe'>,
  options: PushSubscriptionOptionsInit,
  isCurrent: () => boolean,
  lockHeld?: boolean
): Promise<PushSubscription | null> {
  async function acquire(canCleanUp: boolean): Promise<PushSubscription | null> {
    if (!isCurrent()) return null;
    const existing = await manager.getSubscription();
    if (!isCurrent()) return null;
    if (existing) return existing;
    const created = await manager.subscribe(options);
    if (isCurrent()) return created;
    // Without a shared lock, another tab may already be using the returned subscription.
    if (canCleanUp) {
      try { await created.unsubscribe(); } catch { /* Browser cleanup is best effort. */ }
    }
    return null;
  }
  if (lockHeld !== undefined) return acquire(lockHeld);
  return withPushOperation(acquire);
}
