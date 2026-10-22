import { decodeState, object, string, type StateSnapshot } from './state';
import { ApiError } from './api';
import { generatedApi } from './generated/client';
import { mutationDecoders } from './mutations';

interface Cached { key: string; account: string; savedAt: number; state: StateSnapshot }
interface Intent { key: string; account: string; performanceId: string; selected: boolean; mutationId: string }
const accountKey = (name: string) => name.toLowerCase();
let generation = 0;
function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open('riffroster-offline-v1', 1);
    request.onupgradeneeded = () => {
      request.result.createObjectStore('snapshots', { keyPath: 'key' });
      request.result.createObjectStore('outbox', { keyPath: 'key' });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(new Error('Offline storage is unavailable.'));
  });
}
async function transaction<T>(stores: string[], mode: IDBTransactionMode, run: (tx: IDBTransaction, result: (value: T) => void) => void): Promise<T> {
  const db = await open();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(stores, mode);
    let value: T;
    tx.oncomplete = () => { db.close(); resolve(value); };
    tx.onerror = tx.onabort = () => { db.close(); reject(new Error('Offline storage could not save this change.')); };
    try { run(tx, result => { value = result; }); } catch (error) { tx.abort(); reject(error); }
  });
}
const all = <T>(store: string) => transaction<T[]>([store], 'readonly', (tx, done) => {
  const request = tx.objectStore(store).getAll(); request.onsuccess = () => done(request.result as T[]);
});
export const offline = {
  async cache(state: StateSnapshot): Promise<void> {
    const token = generation;
    const snapshot = decodeState(state);
    await transaction<void>(['snapshots'], 'readwrite', tx => {
      if (token !== generation) return;
      const store = tx.objectStore('snapshots');
      store.clear();
      const account = accountKey(snapshot.currentUser);
      // One row per account/event. Cache only validated public response fields, never credentials.
      for (const event of snapshot.events) {
        const ids = new Set(event.days.flatMap(day => day.performances.map(act => act.id)));
        store.put({ key: JSON.stringify([account,event.id]), account, savedAt: Date.now(), state: {
          ...snapshot, events:[event], selections:snapshot.selections.filter(pick => ids.has(pick.performanceId))
        } } satisfies Cached);
      }
    });
  },
  async restore(username: string): Promise<{ state: StateSnapshot; savedAt: number } | null> {
    const token = generation;
    const rows = (await all<Cached>('snapshots')).filter(row => row.account === accountKey(username));
    if (!rows.length || token !== generation) return null;
    rows.sort((a,b) => b.savedAt-a.savedAt);
    const first = rows[0]!;
    const states = rows.map(row => decodeState(row.state));
    if (states.some(state => accountKey(state.currentUser) !== accountKey(username))) throw new Error('The cached account is invalid.');
    const state = decodeState({ ...first.state, isAdmin:false, events:states.flatMap(item => item.events), selections:states.flatMap(item => item.selections) });
    return {state:await this.overlay(state),savedAt:first.savedAt};
  },
  async queue(username: string, performanceId: string, selected: boolean): Promise<void> {
    const token = generation;
    const account = accountKey(username);
    await transaction<void>(['outbox'], 'readwrite', tx => {
      if (token !== generation) return;
      tx.objectStore('outbox').put({key:JSON.stringify([account,performanceId]),account,performanceId,selected,mutationId:crypto.randomUUID()} satisfies Intent);
    });
  },
  async overlay(state: StateSnapshot): Promise<StateSnapshot> {
    const intents = (await all<Intent>('outbox')).filter(item => item.account === accountKey(state.currentUser));
    const selections = [...state.selections];
    const ids = new Set(state.events.flatMap(event => event.days.flatMap(day => day.performances.map(act => act.id))));
    for (const item of intents) {
      if (!ids.has(item.performanceId) || typeof item.selected !== 'boolean') continue;
      for (let i=selections.length-1;i>=0;i--) if (accountKey(selections[i]!.username) === item.account && selections[i]!.performanceId === item.performanceId) selections.splice(i,1);
      if (item.selected) selections.push({username:state.currentUser,performanceId:item.performanceId});
    }
    return {...state,selections};
  },
  async pending(username: string): Promise<number> {
    return (await all<Intent>('outbox')).filter(item => item.account === accountKey(username)).length;
  },
  async sync(state: StateSnapshot): Promise<{removed:number}> {
    const token = generation;
    const account = accountKey(state.currentUser);
    let removed = 0;
    const intents = (await all<Intent>('outbox')).filter(item => item.account === account);
    for (const item of intents) {
      if (token !== generation) break;
      if (typeof item.performanceId !== 'string' || typeof item.selected !== 'boolean' || typeof item.mutationId !== 'string') throw new Error('A queued change is invalid. Sign out to clear offline data.');
      try {
        const response = object(await generatedApi.putSelectionsPerformanceId(item.performanceId,{selected:item.selected,mutationId:item.mutationId}));
        if (response.mutationId !== item.mutationId || !/^[A-F0-9]{64}$/.test(string(response.eventVersion))) throw new Error('The sync acknowledgement is invalid. Retry when connected.');
        const result = mutationDecoders.toggle(response,item.performanceId);
        if (accountKey(result.username) !== account || result.selected !== item.selected) throw new Error('The session changed. Sign in again before syncing.');
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 404)) throw error;
        removed++;
      }
      await transaction<void>(['outbox'], 'readwrite', tx => {
        if (token !== generation) return;
        const store = tx.objectStore('outbox'); const request = store.get(item.key);
        request.onsuccess = () => { if ((request.result as Intent | undefined)?.mutationId === item.mutationId) store.delete(item.key); };
      });
    }
    return {removed};
  },
  async clear(): Promise<void> {
    generation++;
    await transaction<void>(['snapshots','outbox'], 'readwrite', tx => {
      tx.objectStore('snapshots').clear();tx.objectStore('outbox').clear();
    });
  }
};
