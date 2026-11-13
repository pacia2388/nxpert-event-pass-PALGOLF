import { eventTab } from './event-tab';

describe('event tab', () => {
  const now = new Date('2026-11-13T12:00:00').getTime();
  const upcoming = { endsAt: '2026-11-20T17:00:00' };
  const ended = { endsAt: '2026-11-10T17:00:00' };

  it('puts an upcoming event the user has not registered for under Open', () => {
    expect(eventTab(upcoming, false, now)).toBe('Open');
  });

  it('puts an upcoming event the user registered for under Registered', () => {
    expect(eventTab(upcoming, true, now)).toBe('Registered');
  });

  it('puts a past event under Ended, registered or not', () => {
    expect(eventTab(ended, false, now)).toBe('Ended');
    expect(eventTab(ended, true, now)).toBe('Ended');
  });
});
