import { eventTab } from './event-tab';

describe('event tab', () => {
  const now = new Date('2026-11-13T12:00:00').getTime();
  const upcoming = { endsAt: '2026-11-20T17:00:00', status: 'Published' };
  const ended = { endsAt: '2026-11-10T17:00:00', status: 'Published' };
  const closed = { endsAt: '2026-11-20T17:00:00', status: 'Closed' };

  it('puts an upcoming event the user has not registered for under Open', () => {
    expect(eventTab(upcoming, false, now)).toBe('Open');
  });

  it('puts an upcoming event the user registered for under Registered', () => {
    expect(eventTab(upcoming, true, now)).toBe('Registered');
  });

  it('puts a past event under Ended, registered or not', () => {
    expect(eventTab(ended, false, now)).toBe('Ended');
    expect(eventTab(ended, true, now)).toBe('Ended');
    expect(eventTab({ ...closed, endsAt: ended.endsAt }, false, now)).toBe('Ended');
  });

  it('puts an upcoming closed event the user has not registered for under Closed', () => {
    expect(eventTab(closed, false, now)).toBe('Closed');
  });

  it('keeps an upcoming closed event the user registered for under Registered', () => {
    expect(eventTab(closed, true, now)).toBe('Registered');
  });
});
