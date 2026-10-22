import { decodeState, type StateSnapshot } from '../../app/state';

// RFC 5545 TEXT escaping and UTF-8 content-line folding.
const escapeText = (value: string) => value.replace(/\\/g, '\\\\').replace(/\r\n|\r|\n/g, '\\n')
  .replace(/;/g, '\\;').replace(/,/g, '\\,').replace(/[\u0000-\u0008\u000b-\u001f\u007f]/g, '');
function fold(line: string): string {
  const encoder = new TextEncoder();
  let output = ''; let bytes = 0;
  for (const character of line) {
    const size = encoder.encode(character).length;
    if (bytes + size > 75) { output += '\r\n '; bytes = 1; }
    output += character; bytes += size;
  }
  return output;
}
function utc(value: string): string {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime()) || date.getUTCFullYear() < 1 || date.getUTCFullYear() > 9999)
    throw new Error('This date cannot be exported to a calendar.');
  return date.toISOString().replace(/[-:]/g, '').replace(/\.\d{3}Z$/, 'Z');
}

export function routeCalendar(input: StateSnapshot, eventId: string, now = new Date().toISOString()): { contents: string; filename: string; count: number } {
  const state = decodeState(input);
  const event = state.events.find(item => item.id === eventId);
  if (!event) throw new Error('Choose a running order before exporting.');
  const ids = new Set(state.selections.filter(pick => pick.username.toLowerCase() === state.currentUser.toLowerCase()).map(pick => pick.performanceId));
  const acts = event.days.flatMap(day => day.performances).filter(act => ids.has(act.id))
    .sort((a,b) => Date.parse(a.startsAt) - Date.parse(b.startsAt) || a.id.localeCompare(b.id));
  if (!acts.length) throw new Error('Pick at least one act in this running order before exporting.');
  const stamp = utc(now);
  const lines = ['BEGIN:VCALENDAR','VERSION:2.0','PRODID:-//RiffRoster//Personal Route//EN','CALSCALE:GREGORIAN'];
  for (const act of acts) lines.push('BEGIN:VEVENT',
    `UID:${encodeURIComponent(event.id)}:${encodeURIComponent(act.id)}@riffroster`,
    `DTSTAMP:${stamp}`, `DTSTART:${utc(act.startsAt)}`, `DTEND:${utc(act.endsAt)}`,
    `SUMMARY:${escapeText(act.artist)}`, `LOCATION:${escapeText([act.stage,event.location].filter(Boolean).join(' - '))}`,
    `DESCRIPTION:${escapeText(`${event.name}\nPersonal route export. This file does not update automatically.`)}`,
    'END:VEVENT');
  lines.push('END:VCALENDAR');
  const name = event.name.normalize('NFKD').replace(/[^a-zA-Z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0,60) || 'festival';
  return {contents:lines.map(fold).join('\r\n')+'\r\n',filename:`riffroster-${name}-route.ics`,count:acts.length};
}
