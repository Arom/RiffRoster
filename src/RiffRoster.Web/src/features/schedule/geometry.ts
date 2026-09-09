export interface TimedPerformance {
  readonly startsAt: string;
  readonly endsAt: string;
}
export interface Timeline {
  readonly start: number;
  readonly end: number;
  readonly height: number;
  readonly ticks: readonly { top: number; label: string }[];
  readonly pixelsPerMinute: number;
  readonly halfHourOffset: number;
}
export interface TimelinePosition {
  readonly top: number;
  readonly height: number;
  readonly compact: boolean;
}
const minute = 60_000;
const hour = 60 * minute;

function instant(value: string): number {
  const epoch = Date.parse(value);
  if (!Number.isFinite(epoch) || !/(?:Z|[+-]\d{2}:\d{2})$/.test(value))
    throw new RangeError('Timeline timestamps require an explicit offset.');
  return epoch;
}

export function buildTimeline(performances: readonly TimedPerformance[]): Timeline {
  const first = performances[0];
  if (!first) throw new RangeError('An empty schedule has no timeline.');
  const pixelsPerMinute = 2.2;
  const offsetMinutes = offsetMinutesFromIso(first.startsAt);
  const offsetMs = offsetMinutes * minute;
  let earliest = Infinity;
  let latest = -Infinity;
  for (const performance of performances) {
    const start = instant(performance.startsAt);
    const end = instant(performance.endsAt);
    if (end <= start) throw new RangeError('Performances must have positive duration.');
    earliest = Math.min(earliest, start);
    latest = Math.max(latest, end);
  }
  const localStart = Math.floor((earliest + offsetMs) / hour) * hour - 30 * minute;
  const localEnd = Math.ceil((latest + offsetMs) / hour) * hour + 30 * minute;
  const start = localStart - offsetMs;
  const end = localEnd - offsetMs;
  const ticks: { top: number; label: string }[] = [];
  for (let localTick = Math.ceil(localStart / hour) * hour; localTick < localEnd; localTick += hour) {
    const epoch = localTick - offsetMs;
    ticks.push({ top: Math.round(((epoch - start) / minute) * pixelsPerMinute), label: formatTimelineTime(epoch, offsetMinutes) });
  }
  return { start, end, height: Math.round(((end - start) / minute) * pixelsPerMinute), ticks, pixelsPerMinute, halfHourOffset: Math.round(30 * pixelsPerMinute) };
}

export function positionOnTimeline(performance: TimedPerformance, timeline: Timeline): TimelinePosition {
  const startsAt = instant(performance.startsAt);
  const endsAt = instant(performance.endsAt);
  if (endsAt <= startsAt) throw new RangeError('Performances must have positive duration.');
  const top = ((startsAt - timeline.start) / minute) * timeline.pixelsPerMinute;
  const height = Math.max(32, ((endsAt - startsAt) / minute) * timeline.pixelsPerMinute - 5);
  return { top: Math.round(top * 10) / 10, height: Math.round(height * 10) / 10, compact: height < 66 };
}

export function offsetMinutesFromIso(value: string | null | undefined): number {
  const match = (value ?? '').match(/([+-])(\d{2}):(\d{2})$/);
  if (!match) return 0;
  const minutes = Number(match[2]) * 60 + Number(match[3]);
  return match[1] === '-' ? -minutes : minutes;
}

export function formatTimelineTime(epoch: number, offsetMinutes: number): string {
  const local = new Date(epoch + offsetMinutes * minute);
  return `${String(local.getUTCHours()).padStart(2, '0')}:${String(local.getUTCMinutes()).padStart(2, '0')}`;
}
