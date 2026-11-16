import {
  addDays,
  calendarDays,
  CalendarEvent,
  dateKey,
  dayEvents,
  wallDate,
  weekBlocks,
} from './calendar.util';

function event(title: string, startsAt: string, endsAt: string): CalendarEvent {
  return {
    id: 1,
    title,
    description: '',
    startsAt,
    endsAt,
    venue: 'Wack Wack Golf and Country Club',
    publicSlug: title.toLowerCase(),
    status: 'Published',
    format: 'InPerson',
    isRegistered: false,
    isInvited: false,
    canManage: false,
  };
}

const titles = (events: CalendarEvent[]) => events.map((e) => e.title);
const keys = (days: Date[]) => days.map(dateKey);

describe('event calendar', () => {
  describe('Philippine wall-clock dates', () => {
    it('reads an event time as the same wall-clock time, whatever the viewer time zone', () => {
      expect(wallDate('2026-11-14T17:30:00').toISOString()).toBe('2026-11-14T17:30:00.000Z');
    });

    it('reads a time without seconds', () => {
      expect(wallDate('2026-11-14T17:30').toISOString()).toBe('2026-11-14T17:30:00.000Z');
    });

    it('reads a plain date as midnight', () => {
      expect(wallDate('2026-11-14').toISOString()).toBe('2026-11-14T00:00:00.000Z');
    });

    it('ignores fractions and offsets after the seconds', () => {
      expect(wallDate('2026-11-14T17:30:45.123+08:00').toISOString()).toBe(
        '2026-11-14T17:30:45.000Z',
      );
    });

    it('names a day as yyyy-MM-dd', () => {
      expect(dateKey(wallDate('2026-01-05T23:45:00'))).toBe('2026-01-05');
    });
  });

  describe('moving between days', () => {
    it('moves to the next day at midnight', () => {
      expect(addDays(wallDate('2026-11-14T17:30:00'), 1).toISOString()).toBe(
        '2026-11-15T00:00:00.000Z',
      );
    });

    it('crosses into the next month and year', () => {
      expect(dateKey(addDays(wallDate('2026-11-30'), 1))).toBe('2026-12-01');
      expect(dateKey(addDays(wallDate('2026-12-31'), 1))).toBe('2027-01-01');
    });

    it('moves back across a leap day', () => {
      expect(dateKey(addDays(wallDate('2028-03-01'), -1))).toBe('2028-02-29');
    });
  });

  describe('days shown in a view', () => {
    it('shows six full weeks for a month, starting on the Sunday on or before the 1st', () => {
      const days = calendarDays(wallDate('2026-12-18T10:00:00'), 'month');

      expect(days).toHaveLength(42);
      expect(keys(days.slice(0, 3))).toEqual(['2026-11-29', '2026-11-30', '2026-12-01']);
      expect(dateKey(days[41])).toBe('2027-01-09');
    });

    it('starts the month on the 1st when the 1st is a Sunday', () => {
      const days = calendarDays(wallDate('2026-11-16'), 'month');

      expect(dateKey(days[0])).toBe('2026-11-01');
      expect(dateKey(days[41])).toBe('2026-12-12');
    });

    it('shows Sunday to Saturday around the day for a week', () => {
      expect(keys(calendarDays(wallDate('2026-11-18T10:00:00'), 'week'))).toEqual([
        '2026-11-15',
        '2026-11-16',
        '2026-11-17',
        '2026-11-18',
        '2026-11-19',
        '2026-11-20',
        '2026-11-21',
      ]);
    });
  });

  describe('events on a day', () => {
    const day = wallDate('2026-11-14');

    it('lists events that touch the day, earliest first', () => {
      const events = [
        event('Awards dinner', '2026-11-14T18:00:00', '2026-11-14T21:00:00'),
        event('Practice round', '2026-11-14T07:00:00', '2026-11-14T11:00:00'),
        event('Night golf', '2026-11-13T20:00:00', '2026-11-14T02:00:00'),
        event('Golf week', '2026-11-12T08:00:00', '2026-11-16T17:00:00'),
      ];

      expect(titles(dayEvents(events, day))).toEqual([
        'Golf week',
        'Night golf',
        'Practice round',
        'Awards dinner',
      ]);
    });

    it('leaves out an event that ends exactly at the start of the day', () => {
      const events = [event('Late round', '2026-11-13T21:00:00', '2026-11-14T00:00:00')];

      expect(dayEvents(events, day)).toEqual([]);
    });

    it('leaves out an event that starts exactly at the end of the day', () => {
      const events = [event('Early round', '2026-11-15T00:00:00', '2026-11-15T03:00:00')];

      expect(dayEvents(events, day)).toEqual([]);
    });
  });

  describe('placing events in the day column of a week view', () => {
    const day = wallDate('2026-11-14');
    const place = (events: CalendarEvent[]) =>
      weekBlocks(events, day).map((b) => ({
        title: b.event.title,
        top: b.top,
        height: b.height,
        column: b.column,
        columns: b.columns,
      }));

    it('places an event by minutes from midnight and its length in minutes', () => {
      expect(place([event('Clinic', '2026-11-14T09:00:00', '2026-11-14T10:30:00')])).toEqual([
        { title: 'Clinic', top: 540, height: 90, column: 0, columns: 1 },
      ]);
    });

    it('gives a very short event a minimum height of 18 minutes', () => {
      expect(place([event('Photo op', '2026-11-14T09:00:00', '2026-11-14T09:05:00')])).toEqual([
        { title: 'Photo op', top: 540, height: 18, column: 0, columns: 1 },
      ]);
    });

    it('cuts an event from the day before at midnight', () => {
      expect(place([event('Night golf', '2026-11-13T22:00:00', '2026-11-14T01:00:00')])).toEqual([
        { title: 'Night golf', top: 0, height: 60, column: 0, columns: 1 },
      ]);
    });

    it('cuts an event running into the next day at midnight', () => {
      expect(place([event('Night golf', '2026-11-14T23:00:00', '2026-11-15T02:00:00')])).toEqual([
        { title: 'Night golf', top: 1380, height: 60, column: 0, columns: 1 },
      ]);
    });

    it('keeps back-to-back events in one full-width column', () => {
      const events = [
        event('Clinic', '2026-11-14T09:00:00', '2026-11-14T10:00:00'),
        event('Putting', '2026-11-14T10:00:00', '2026-11-14T11:00:00'),
      ];

      expect(place(events).map((b) => [b.title, b.column, b.columns])).toEqual([
        ['Clinic', 0, 1],
        ['Putting', 0, 1],
      ]);
    });

    it('puts overlapping events side by side, reusing a column once it frees up', () => {
      const events = [
        event('Clinic', '2026-11-14T09:00:00', '2026-11-14T10:00:00'),
        event('Putting', '2026-11-14T09:30:00', '2026-11-14T10:30:00'),
        event('Chipping', '2026-11-14T10:00:00', '2026-11-14T11:00:00'),
        event('Lunch', '2026-11-14T13:00:00', '2026-11-14T14:00:00'),
      ];

      expect(place(events).map((b) => [b.title, b.column, b.columns])).toEqual([
        ['Clinic', 0, 2],
        ['Putting', 1, 2],
        ['Chipping', 0, 2],
        ['Lunch', 0, 1],
      ]);
    });

    it('gives three events that all overlap a column each', () => {
      const events = [
        event('Tournament', '2026-11-14T09:00:00', '2026-11-14T12:00:00'),
        event('Clinic', '2026-11-14T10:00:00', '2026-11-14T11:00:00'),
        event('Putting', '2026-11-14T10:30:00', '2026-11-14T11:30:00'),
      ];

      expect(place(events).map((b) => [b.title, b.column, b.columns])).toEqual([
        ['Tournament', 0, 3],
        ['Clinic', 1, 3],
        ['Putting', 2, 3],
      ]);
    });

    it('places nothing on a day without events', () => {
      expect(weekBlocks([], day)).toEqual([]);
    });
  });
});
