import { type CSSProperties } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { flushSync } from 'react-dom';
import { type Timeline, type TimelinePosition } from './geometry';

interface Attendee { readonly username: string; readonly color: string; readonly initials: string }
interface Card {
  readonly id: string; readonly artist: string; readonly start: string; readonly end: string;
  readonly selected: boolean; readonly clash: boolean; readonly admin: boolean;
  readonly expandable: boolean; readonly otherCount: number; readonly attendees: readonly Attendee[];
  readonly position: TimelinePosition;
}
interface Lane { readonly stage: string; readonly color: string; readonly cards: readonly Card[] }
type Model = { readonly empty: { title: string; copy: string; action: string | null } }
  | { readonly timeline: Timeline; readonly lanes: readonly Lane[] };
type Variables = CSSProperties & { [key: `--${string}`]: string | number };
const roots = new WeakMap<HTMLElement, Root>();

function PerformanceCard({ card }: { card: Card }) {
  const avatars = card.attendees.slice(0, 4).reverse().map(user =>
    <span key={user.username} className="avatar avatar--small" style={{ background: user.color }} title={user.username}>{user.initials}</span>);
  const attendance = card.selected
    ? card.otherCount ? <><strong>You + {card.otherCount}</strong> going</> : 'Only you — so far'
    : card.attendees.length ? `${card.attendees.length} friend${card.attendees.length === 1 ? '' : 's'} going` : 'No picks yet';
  return <article className={`performance timeline-performance ${card.position.compact ? 'performance--compact' : ''} ${card.selected ? 'selected' : ''} ${card.selected && card.otherCount > 0 ? 'shared' : ''} ${card.clash ? 'clash' : ''}`}
    data-performance-id={card.id} title={`${card.artist} · ${card.start}–${card.end}`} style={{ top: card.position.top, height: card.position.height }}>
    <div className="performance-time">{card.start} <span>→ {card.end}</span></div><h4>{card.artist}</h4>
    {card.admin && <button className="performance-admin-button" data-action="open-edit-performance" data-id={card.id} aria-label={`Edit ${card.artist}`} title="Edit act">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m4 16-1 5 5-1L19 9l-4-4L4 16Zm9-9 4 4" /></svg>
    </button>}
    <button className="pick-button" data-action="toggle-pick" data-id={card.id} aria-label={`${card.selected ? 'Remove' : 'Add'} ${card.artist} ${card.selected ? 'from' : 'to'} your route`} aria-pressed={card.selected}>
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" /></svg>
    </button>
    <div className="attendance">
      {card.expandable ? <><button type="button" className="attendance-avatars attendance-avatars--expandable" data-action="toggle-attendee-names" aria-expanded="false" aria-label={`Show attendee names for ${card.artist}`}>{avatars}</button>
        <span className="attendance-names-popover" hidden>{card.attendees.map(user => user.username).join(', ')}</span></> : <div className="attendance-avatars">{avatars}</div>}
      <span className="attendance-label">{attendance}</span>
    </div>
    {card.clash && <span className="clash-label">CLASH IN YOUR ROUTE</span>}
  </article>;
}

function Schedule({ model }: { model: Model }) {
  if ('empty' in model) return <div className="empty-state"><div><h3>{model.empty.title}</h3><p>{model.empty.copy}</p>
    {model.empty.action && <button className="button button--primary" data-action={model.empty.action}>Add the first act</button>}</div></div>;
  const { timeline, lanes } = model;
  const style: Variables = { minWidth: 68 + lanes.length * 240, '--lane-count': lanes.length, '--timeline-height': `${timeline.height}px`, '--timeline-half-hour': `${timeline.halfHourOffset}px` };
  return <><div className="timeline-swipe-hint">Swipe sideways to compare stages <span aria-hidden="true">↔</span></div>
    <div className="schedule-timeline"><div className="timeline-shell" style={style}>
      <div className="timeline-header"><div className="timeline-corner">TIME</div><div className="timeline-stage-headings">
        {lanes.map(lane => <header key={lane.stage} className="timeline-stage-heading" style={{ '--stage-color': lane.color } as Variables}><h3>{lane.stage}</h3><span>{lane.cards.length} {lane.cards.length === 1 ? 'act' : 'acts'}</span></header>)}
      </div></div>
      <div className="timeline-body"><div className="timeline-time-axis">{timeline.ticks.map(tick => <time key={tick.top} className="timeline-tick" style={{ top: tick.top }}>{tick.label}</time>)}</div>
        <div className="timeline-lanes">{timeline.ticks.map(tick => <span key={tick.top} className="timeline-gridline" style={{ top: tick.top }} aria-hidden="true" />)}
          {lanes.map(lane => <section key={lane.stage} className="timeline-lane" style={{ '--stage-color': lane.color } as Variables} aria-label={lane.stage}>{lane.cards.map(card => <PerformanceCard key={card.id} card={card} />)}</section>)}
        </div>
      </div>
    </div></div></>;
}

export function renderSchedule(container: HTMLElement, model: Model): void {
  let root = roots.get(container);
  if (!root) { root = createRoot(container); roots.set(container, root); }
  flushSync(() => root.render(<Schedule model={model} />));
}
