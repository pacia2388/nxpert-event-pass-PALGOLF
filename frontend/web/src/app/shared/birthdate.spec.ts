import { birthdateMax, fromIsoDate, isValidBirthdate, toIsoDate } from './birthdate';

describe('birthdates', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 16, 15, 30));
  });

  afterEach(() => vi.useRealTimers());

  describe('the latest birthdate allowed', () => {
    it('is the start of today', () => {
      expect(birthdateMax()).toEqual(new Date(2026, 10, 16));
    });
  });

  describe('checking a picked birthdate', () => {
    it.each([
      ['the earliest allowed day, 1 Jan 1900', new Date(1900, 0, 1)],
      ['an ordinary birthdate', new Date(1985, 6, 21)],
      ['today', new Date(2026, 10, 16)],
    ])('accepts %s', (_label, picked) => {
      expect(isValidBirthdate(picked)).toBe(true);
    });

    it.each([
      ['the day before 1900', new Date(1899, 11, 31)],
      ['tomorrow', new Date(2026, 10, 17)],
      ['an unreadable date', new Date('not a date')],
      ['nothing picked', null],
      ['a missing value', undefined],
    ])('refuses %s', (_label, picked) => {
      expect(isValidBirthdate(picked)).toBe(false);
    });
  });

  describe('sending a birthdate to the API', () => {
    it('writes the picked day as yyyy-MM-dd', () => {
      expect(toIsoDate(new Date(1990, 0, 5))).toBe('1990-01-05');
    });

    it('keeps the local day even late in the evening', () => {
      expect(toIsoDate(new Date(1990, 11, 31, 23, 59))).toBe('1990-12-31');
    });

    it('sends nothing for a missing or unreadable date', () => {
      expect(toIsoDate(null)).toBeNull();
      expect(toIsoDate(undefined)).toBeNull();
      expect(toIsoDate(new Date('not a date'))).toBeNull();
    });
  });

  describe('reading a birthdate from the API', () => {
    it('reads yyyy-MM-dd as that local day at midnight', () => {
      expect(fromIsoDate('1990-01-05')).toEqual(new Date(1990, 0, 5));
    });

    it('gives back the same text when sent again', () => {
      expect(toIsoDate(fromIsoDate('2000-02-29'))).toBe('2000-02-29');
    });

    it.each([
      ['blank', ''],
      ['missing', null],
      ['without leading zeros', '1990-1-5'],
      ['in day/month/year order', '05/01/1990'],
      ['with a time', '1990-01-05T00:00:00'],
    ])('reads nothing from a value that is %s', (_label, value) => {
      expect(fromIsoDate(value)).toBeNull();
    });
  });
});
