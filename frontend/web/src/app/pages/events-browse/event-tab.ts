export type EventTab = 'Open' | 'Registered' | 'Closed' | 'Ended';

export const EVENT_TABS: EventTab[] = ['Open', 'Registered', 'Closed', 'Ended'];

export function isEnded(e: { endsAt: string }, now = Date.now()): boolean {
  return new Date(e.endsAt).getTime() < now;
}

/**
 * Each event sits under exactly one tab, checked in this order: past events are Ended, then
 * events the user registered for are Registered (even if registration has since closed), then
 * closed events are Closed, and the rest are Open.
 */
export function eventTab(
  e: { endsAt: string; status: string },
  registered: boolean,
  now = Date.now(),
): EventTab {
  if (isEnded(e, now)) return 'Ended';
  if (registered) return 'Registered';
  return e.status === 'Closed' ? 'Closed' : 'Open';
}
