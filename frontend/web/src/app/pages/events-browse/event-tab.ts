export type EventTab = 'Open' | 'Registered' | 'Ended';

export const EVENT_TABS: EventTab[] = ['Open', 'Registered', 'Ended'];

/** Each event sits under exactly one tab: past events are Ended, whether registered or not. */
export function eventTab(e: { endsAt: string }, registered: boolean, now = Date.now()): EventTab {
  if (new Date(e.endsAt).getTime() < now) return 'Ended';
  return registered ? 'Registered' : 'Open';
}
