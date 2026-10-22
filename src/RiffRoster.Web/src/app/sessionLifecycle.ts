// Coordinates snapshot reads and exclusive cookie-changing operations. It does not retry writes.
export function createSessionLifecycle() {
  let generation = 0;
  let readSequence = 0;
  let authenticationPending = false;
  return {
    invalidate(): void {
      generation++;
      authenticationPending = false;
    },
    isAuthenticationCurrent(token: number): boolean {
      return authenticationPending && token === generation;
    },
    beginAction(): number | null {
      return authenticationPending ? null : generation;
    },
    isActionCurrent(token: number): boolean {
      return !authenticationPending && token === generation;
    },
    beginAuthentication(): number | null {
      if (authenticationPending) return null;
      authenticationPending = true;
      return ++generation;
    },
    finishAuthentication(token: number): void {
      if (token === generation) authenticationPending = false;
    },
    beginRead(): { generation: number; sequence: number } | null {
      return authenticationPending ? null : { generation, sequence: ++readSequence };
    },
    isCurrent(token: { generation: number; sequence: number }): boolean {
      return !authenticationPending && token.generation === generation && token.sequence === readSequence;
    }
  };
}
