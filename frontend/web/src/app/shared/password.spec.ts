import { isValidPassword, PASSWORD_PATTERN } from './password';

describe('choosing a password', () => {
  it.each([
    ['letters, a digit and a symbol', 'Passw0rd!'],
    ['a passphrase with spaces', 'green fairway 18!'],
    ['exactly 8 characters', 'abc123#x'],
    ['exactly 128 characters', 'a1!' + 'x'.repeat(125)],
  ])('accepts %s', (_label, password) => {
    expect(isValidPassword(password)).toBe(true);
  });

  it.each([
    ['no symbol', 'Passw0rd1'],
    ['a space as the only non-letter, non-digit', 'Pass w0rd1'],
    ['no digit', 'Password!'],
    ['no letter', '1234567!'],
    ['under 8 characters', 'Pa1!xyz'],
    ['over 128 characters', 'a1!' + 'x'.repeat(126)],
    ['nothing', ''],
  ])('refuses %s', (_label, password) => {
    expect(isValidPassword(password)).toBe(false);
  });

  it('gives the form a pattern that agrees on letters, digits and symbols', () => {
    const pattern = new RegExp(PASSWORD_PATTERN);
    expect(pattern.test('Passw0rd!')).toBe(true);
    expect(pattern.test('Passw0rd1')).toBe(false);
  });
});
