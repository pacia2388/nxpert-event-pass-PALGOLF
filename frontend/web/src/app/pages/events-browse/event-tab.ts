export type EventTab = 'Open' | 'Registered' | 'Ended';

export const EVENT_TABS: EventTab[] = ['Open', 'Registered', 'Ended'];

export function isEnded(e: { endsAt: string }, now = Date.now()): boolean {
  return new Date(e.endsAt).getTime() < now;
}

/** Each event sits under exactly one tab: past events are Ended, whether registered or not. */
export function eventTab(e: { endsAt: string }, registered: boolean, now = Date.now()): EventTab {
  if (isEnded(e, now)) return 'Ended';
  return registered ? 'Registered' : 'Open';
}
