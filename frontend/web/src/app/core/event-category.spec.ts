import { categoryAgeWarning } from './event-category';

describe('event category age warning', () => {
  const today = new Date(2026, 10, 13); // 13 Nov 2026

  it('warns someone under 65 joining a Senior event', () => {
    expect(categoryAgeWarning('Senior', '1970-05-01', today)).toBe(
      'This event is for seniors (65 and older). You are 56.',
    );
  });

  it('warns someone 65 or older joining a Regular event', () => {
    expect(categoryAgeWarning('Regular', '1950-01-02', today)).toBe(
      'This event is for regular players (under 65). You are 76.',
    );
  });

  it.each([
    ['a senior joining a Senior event', 'Senior', '1950-01-02'],
    ['someone under 65 joining a Regular event', 'Regular', '1990-01-31'],
    ['anyone joining an event with no category', null, '1950-01-02'],
    ['someone with no birthdate on file', 'Senior', null],
  ] as const)('does not warn %s', (_label, category, birthdate) => {
    expect(categoryAgeWarning(category, birthdate, today)).toBeNull();
  });

  it('counts someone as 65 from their 65th birthday', () => {
    expect(categoryAgeWarning('Senior', '1961-11-13', today)).toBeNull();
    expect(categoryAgeWarning('Senior', '1961-11-14', today)).toBe(
      'This event is for seniors (65 and older). You are 64.',
    );
  });
});
