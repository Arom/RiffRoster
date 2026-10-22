import type { StateSnapshot, Performance, FestivalEvent } from './state';

const sameName = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;

export function scheduleDateForPerformance(performance: Performance): string {
  const date = performance.startsAt.slice(0, 10);
  if (Number(performance.startsAt.slice(11, 13)) >= 6) return date;
  // The festival day follows the timestamp's explicit local date, not the device timezone.
  const previous = new Date(`${date}T12:00:00Z`);
  previous.setUTCDate(previous.getUTCDate() - 1);
  return previous.toISOString().slice(0, 10);
}

export const scheduleState = {
  pick(state: StateSnapshot, result: { username: string; performanceId: string; selected: boolean }): StateSnapshot {
    const selections = state.selections.filter(selection => !(selection.performanceId === result.performanceId && sameName(selection.username, result.username)));
    if (result.selected) selections.push({ username: result.username, performanceId: result.performanceId });
    return { ...state, selections };
  },
  event(state: StateSnapshot, saved: FestivalEvent): StateSnapshot {
    const exists = state.events.some(event => event.id === saved.id);
    return { ...state, events: exists ? state.events.map(event => event.id === saved.id ? saved : event) : [...state.events, saved] };
  },
  savePerformance(state: StateSnapshot, eventId: string, saved: Performance): { state: StateSnapshot; dayId: string | null } {
    const event = state.events.find(event => event.id === eventId);
    const target = event?.days.find(day => day.date === scheduleDateForPerformance(saved));
    // A newly created day requires a fresh snapshot. Keep the previous state intact until it arrives.
    if (!event || !target) return { state, dayId: null };
    const days = event.days.map(day => {
      const performances = day.performances.filter(act => act.id !== saved.id);
      if (day.id === target.id) performances.push(saved);
      performances.sort((a, b) => Date.parse(a.startsAt) - Date.parse(b.startsAt));
      return { ...day, performances };
    });
    return { state: { ...state, events: state.events.map(item => item.id === eventId ? { ...event, days } : item) }, dayId: target.id };
  },
  deletePerformance(state: StateSnapshot, eventId: string, performanceId: string): StateSnapshot {
    return { ...state,
      events: state.events.map(event => event.id === eventId ? { ...event, days: event.days.map(day => ({ ...day, performances: day.performances.filter(act => act.id !== performanceId) })) } : event),
      selections: state.selections.filter(selection => selection.performanceId !== performanceId)
    };
  }
};
