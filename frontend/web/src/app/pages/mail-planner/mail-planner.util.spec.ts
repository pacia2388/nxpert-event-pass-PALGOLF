import { PlanSend } from './mail-planner.model';
import { dayKey, shift, sortedSends, startOfToday } from './mail-planner.util';

function send(id: number, at: Date): PlanSend {
  return {
    id,
    at,
    subject: `Mail ${id}`,
    body: '',
    preview: '',
    images: 0,
    status: 'Scheduled',
    recipientCount: 0,
    lastError: null,
  };
}

describe('mail planner calendar', () => {
  describe('grouping by day', () => {
    it('gives the same key to two times on the same day', () => {
      expect(dayKey(new Date(2026, 10, 16, 0, 0))).toBe(dayKey(new Date(2026, 10, 16, 23, 59)));
    });

    it('gives different keys to different days', () => {
      expect(dayKey(new Date(2026, 10, 16))).not.toBe(dayKey(new Date(2026, 10, 17)));
      expect(dayKey(new Date(2026, 1, 11))).not.toBe(dayKey(new Date(2026, 11, 1)));
    });
  });

  describe('today', () => {
    beforeEach(() => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 10, 16, 15, 30, 45, 500));
    });

    afterEach(() => vi.useRealTimers());

    it('starts at local midnight', () => {
      expect(startOfToday()).toEqual(new Date(2026, 10, 16));
    });
  });

  describe('moving a send time', () => {
    it('moves by whole days keeping the time', () => {
      expect(shift(new Date(2026, 10, 30, 14, 45), 1)).toEqual(new Date(2026, 11, 1, 14, 45));
    });

    it('moves back across a month end', () => {
      expect(shift(new Date(2026, 2, 1, 8, 0), -1)).toEqual(new Date(2026, 1, 28, 8, 0));
    });

    it('sets the hour on the hour when one is given', () => {
      expect(shift(new Date(2026, 10, 30, 14, 45, 30), 1, 9)).toEqual(new Date(2026, 11, 1, 9));
    });

    it('can set only the hour on the same day', () => {
      expect(shift(new Date(2026, 10, 16, 14, 45), 0, 18)).toEqual(new Date(2026, 10, 16, 18));
    });

    it('leaves the original time unchanged', () => {
      const original = new Date(2026, 10, 16, 14, 45);
      shift(original, 3, 9);
      expect(original).toEqual(new Date(2026, 10, 16, 14, 45));
    });
  });

  describe('a plan days in send order', () => {
    it('orders the sends from earliest to latest', () => {
      const sends = [
        send(1, new Date(2026, 10, 20, 9)),
        send(2, new Date(2026, 10, 18, 9)),
        send(3, new Date(2026, 10, 18, 7)),
      ];

      expect(sortedSends(sends).map((s) => s.id)).toEqual([3, 2, 1]);
    });

    it('leaves the plan own list in its order', () => {
      const sends = [send(1, new Date(2026, 10, 20)), send(2, new Date(2026, 10, 18))];
      sortedSends(sends);
      expect(sends.map((s) => s.id)).toEqual([1, 2]);
    });
  });
});
