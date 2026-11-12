import { scheduleLines } from './schedule-range';

describe('event schedule lines', () => {
  it('shows the start and the end on their own lines', () => {
    expect(scheduleLines('2026-11-12T09:00:00', '2026-11-14T17:30:00')).toEqual([
      'Starts: Thu, Nov 12 2026, 9:00 AM',
      'Ends: Sat, Nov 14 2026, 5:30 PM',
    ]);
  });

  it('still shows the full end date for a same-day event', () => {
    expect(scheduleLines('2026-11-12T09:00:00', '2026-11-12T17:00:00')).toEqual([
      'Starts: Thu, Nov 12 2026, 9:00 AM',
      'Ends: Thu, Nov 12 2026, 5:00 PM',
    ]);
  });

  it('shows only the start when there is no end', () => {
    expect(scheduleLines('2026-11-12T09:00:00', null)).toEqual(['Starts: Thu, Nov 12 2026, 9:00 AM']);
  });
});
