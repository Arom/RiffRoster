import { type KeyboardEvent } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { flushSync } from 'react-dom';

export interface DayTab {
  readonly id: string;
  readonly label: string;
  readonly dateLabel: string;
}

const roots = new WeakMap<HTMLElement, Root>();

function moveFocus(event: KeyboardEvent<HTMLButtonElement>, index: number, count: number) {
  let next: number;
  switch (event.key) {
    case 'ArrowRight': next = (index + 1) % count; break;
    case 'ArrowLeft': next = (index - 1 + count) % count; break;
    case 'Home': next = 0; break;
    case 'End': next = count - 1; break;
    default: return;
  }
  event.preventDefault();
  const target = event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next];
  target?.focus();
  // Existing document event delegation owns day selection and schedule/crew synchronization.
  target?.click();
}

export function renderDayTabs(container: HTMLElement, days: readonly DayTab[], activeId: string | null): void {
  let root = roots.get(container);
  if (!root) {
    root = createRoot(container);
    roots.set(container, root);
  }
  const activeIndex = Math.max(0, days.findIndex(day => day.id === activeId));
  flushSync(() => root.render(<>{days.map((day, index) => (
    <button key={day.id} type="button" className={`day-tab ${day.id === activeId ? 'active' : ''}`}
      data-day-id={day.id} role="tab" aria-selected={day.id === activeId}
      aria-controls="schedule-grid" tabIndex={index === activeIndex ? 0 : -1}
      onKeyDown={event => moveFocus(event, index, days.length)}>
      <span>{day.label}</span><small>{day.dateLabel}</small>
    </button>
  ))}</>));
}
