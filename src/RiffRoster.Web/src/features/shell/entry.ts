import type { FestivalEvent } from '../../app/state';
function element<K extends keyof HTMLElementTagNameMap>(tag: K, text: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag); node.textContent = text; return node;
}
export function profile(container: HTMLElement, username: string, admin: boolean, signOut: () => void): void {
  const heading = element('h2', 'Your profile'); heading.id = 'profile-title';
  const name = element('p', username);
  const role = element('p', admin ? 'Administrator' : 'Member');
  const info = element('p', 'Your display name is visible to your crew. Sign out to clear offline schedules and queued picks on this device.');
  const button = element('button', 'Sign out'); button.type = 'button'; button.className = 'button button--primary'; button.onclick = signOut;
  container.replaceChildren(heading,name,role,info,button);
}
export function eventSearch(container: HTMLElement, events: readonly FestivalEvent[], select: (id: string) => void): void {
  const heading = element('h2','Find a running order'); heading.id = 'event-search-title';
  const label = element('label','Event name or location');
  const input = element('input',''); input.type = 'search'; input.id = 'event-query'; label.htmlFor = input.id;
  const results = element('div',''); results.className = 'event-results'; results.setAttribute('aria-live','polite');
  function render(): void {
    const query = input.value.trim().toLocaleLowerCase();
    const matches = events.filter(event => `${event.name} ${event.location}`.toLocaleLowerCase().includes(query));
    results.replaceChildren(...matches.map(event => {
      const preview = element('article','');
      preview.append(element('h3',event.name),element('p',`${event.location} · ${event.startDate} to ${event.endDate}`));
      const acts = event.days.flatMap(day => day.performances);
      const stages = new Set(acts.map(act => act.stage));
      preview.append(element('p',`${event.days.length} days · ${stages.size} stages · ${acts.length} acts`));
      const artists = [...new Set(acts.map(act => act.artist))];
      preview.append(element('p',artists.slice(0,6).join(', ') + (artists.length > 6 ? '…' : '')));
      const button = element('button',`Open ${event.name}`);
      button.type = 'button'; button.className = 'event-result'; button.onclick = () => select(event.id);
      preview.append(button); return preview;
    }));
    if (!matches.length) results.append(element('p','No matching running orders. Try another name or location.'));
  }
  input.oninput = render; container.replaceChildren(heading,label,input,results); render(); input.focus();
}
