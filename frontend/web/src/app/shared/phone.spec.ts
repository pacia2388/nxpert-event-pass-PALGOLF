import {
  formatPhoneNumber,
  isCompletePhone,
  isCompleteStoredPhone,
  joinPhone,
  phoneSupported,
  splitPhone,
} from './phone';

const listed = ['63', '65', '1'];

describe('phone numbers on user forms', () => {
  describe('opening a stored number in the form', () => {
    it('splits a Philippine mobile into its code and the masked number', () => {
      expect(splitPhone('+639171234567', listed)).toEqual({ code: '63', number: '917 123 4567' });
    });

    it('splits a number from another listed country into its code and plain digits', () => {
      expect(splitPhone('+6591234567', listed)).toEqual({ code: '65', number: '91234567' });
    });

    it('starts a blank number on the first listed code', () => {
      expect(splitPhone(null, listed)).toEqual({ code: '63', number: '' });
      expect(splitPhone('', listed)).toEqual({ code: '63', number: '' });
    });

    it('starts a blank number with no code when no country is listed', () => {
      expect(splitPhone(undefined, [])).toEqual({ code: '', number: '' });
    });

    it('keeps an unlisted number whole with no code so the user must pick one', () => {
      expect(splitPhone('+447911123456', listed)).toEqual({ code: '', number: '+447911123456' });
    });
  });

  describe('typing the number part', () => {
    it.each([
      ['a full mobile', '9171234567', '917 123 4567'],
      ['a mobile with a leading 0', '09171234567', '917 123 4567'],
      ['a mobile with the 63 prefix typed again', '639171234567', '917 123 4567'],
      ['a mobile typed with dashes', '917-123-4567', '917 123 4567'],
      ['a partly typed mobile', '9171', '917 1'],
      ['three digits', '917', '917'],
      ['too many digits', '917123456799', '917 123 4567'],
      ['nothing', '', ''],
    ])('masks %s for the Philippines as 9XX XXX XXXX', (_label, raw, shown) => {
      expect(formatPhoneNumber('63', raw)).toBe(shown);
    });

    it('keeps only the digits for other countries', () => {
      expect(formatPhoneNumber('65', '9123 4567')).toBe('91234567');
    });

    it('stops other countries at 15 digits including the code', () => {
      expect(formatPhoneNumber('1', '2025550123456789')).toBe('20255501234567');
      expect(formatPhoneNumber('65', '9123456789012345')).toBe('9123456789012');
    });
  });

  describe('knowing when the number is complete', () => {
    it('accepts a Philippine mobile of 9 followed by 9 digits', () => {
      expect(isCompletePhone('63', '917 123 4567')).toBe(true);
    });

    it.each([
      ['one digit short', '917 123 456'],
      ['not starting with 9', '817 123 4567'],
      ['blank', ''],
    ])('refuses a Philippine number that is %s', (_label, number) => {
      expect(isCompletePhone('63', number)).toBe(false);
    });

    it('accepts another country when code and number make 8 to 15 digits', () => {
      expect(isCompletePhone('65', '123456')).toBe(true);
      expect(isCompletePhone('65', '91234567')).toBe(true);
      expect(isCompletePhone('1', '12345678901234')).toBe(true);
    });

    it('refuses another country below 8 or above 15 digits in all', () => {
      expect(isCompletePhone('65', '12345')).toBe(false);
      expect(isCompletePhone('1', '123456789012345')).toBe(false);
    });

    it('refuses a number with no code picked', () => {
      expect(isCompletePhone('', '91234567')).toBe(false);
    });

    it('refuses a code with no number', () => {
      expect(isCompletePhone('65', '')).toBe(false);
    });
  });

  describe('saving the number', () => {
    it('stores the code and the digits as +<code><number>', () => {
      expect(joinPhone('63', '917 123 4567')).toBe('+639171234567');
      expect(joinPhone('65', '9123 4567')).toBe('+6591234567');
    });

    it('stores nothing when the number is blank', () => {
      expect(joinPhone('63', '')).toBeNull();
      expect(joinPhone('63', '  ')).toBeNull();
    });
  });

  describe('flagging numbers from unlisted countries', () => {
    it('supports a number whose code is listed', () => {
      expect(phoneSupported('+639171234567', listed)).toBe(true);
      expect(phoneSupported('+6591234567', listed)).toBe(true);
    });

    it('flags a number whose code is not listed', () => {
      expect(phoneSupported('+447911123456', listed)).toBe(false);
    });

    it('does not flag a blank number', () => {
      expect(phoneSupported(null, listed)).toBe(true);
      expect(phoneSupported('', [])).toBe(true);
    });
  });

  describe('checking a stored number is a full mobile', () => {
    it.each([
      ['a Philippine mobile', '+639171234567'],
      ['a 10-digit foreign number', '+6591234567'],
      ['the shortest foreign number (8 digits)', '+12345678'],
      ['the longest foreign number (15 digits)', '+123456789012345'],
    ])('accepts %s', (_label, stored) => {
      expect(isCompleteStoredPhone(stored)).toBe(true);
    });

    it.each([
      ['a Philippine number one digit short', '+63917123456'],
      ['a Philippine number one digit long', '+6391712345678'],
      ['a Philippine landline', '+63281234567'],
      ['a foreign number of 7 digits', '+1234567'],
      ['a foreign number of 16 digits', '+1234567890123456'],
      ['a number starting with 0', '+01234567890'],
      ['a number without the plus', '639171234567'],
      ['blank', ''],
      ['missing', null],
    ])('refuses %s', (_label, stored) => {
      expect(isCompleteStoredPhone(stored)).toBe(false);
    });
  });
});
