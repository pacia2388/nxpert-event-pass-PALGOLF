export type EventTab = 'Open' | 'Registered' | 'Closed' | 'Ended';

export const EVENT_TABS: EventTab[] = ['Open', 'Registered', 'Closed', 'Ended'];

type TabbedEvent = { endsAt: string; status: string };

export function isEnded(e: { endsAt: string }, now = Date.now()): boolean {
  return new Date(e.endsAt).getTime() < now;
}

export function isClosed(e: { status: string }): boolean {
  return e.status === 'Closed';
}

/** Ended and closed events can be viewed but no longer take registrations. */
export function canRegister(e: TabbedEvent, now = Date.now()): boolean {
  return !isEnded(e, now) && !isClosed(e);
}

/**
 * Each event sits under exactly one tab, checked in this order: past events are Ended, then
 * events the user registered for are Registered (even if registration has since closed), then
 * closed events are Closed, and the rest are Open.
 */
export function eventTab(e: TabbedEvent, registered: boolean, now = Date.now()): EventTab {
  if (isEnded(e, now)) return 'Ended';
  if (registered) return 'Registered';
  return isClosed(e) ? 'Closed' : 'Open';
}
