import { isValidPassword } from './password';

describe('choosing a password', () => {
  it.each([
    ['letters, a digit and a symbol', 'Passw0rd!'],
    ['a passphrase with spaces', 'green fairway 18!'],
    ['exactly 8 characters', 'abc123#x'],
    ['exactly 128 characters', 'a1!' + 'x'.repeat(125)],
    ['accented letters as the only letters', 'ñññññññ1!'],
  ])('accepts %s', (_label, password) => {
    expect(isValidPassword(password)).toBe(true);
  });

  it.each([
    ['no symbol', 'Passw0rd1'],
    ['a space as the only non-letter, non-digit', 'Pass w0rd1'],
    ['an accented letter as the only non-letter, non-digit', 'Passw0rdñ'],
    ['an ñ typed as n plus a combining tilde, with no symbol', 'Passw0rdñ'],
    ['no digit', 'Password!'],
    ['no letter', '1234567!'],
    ['under 8 characters', 'Pa1!xyz'],
    ['over 128 characters', 'a1!' + 'x'.repeat(126)],
    ['nothing', ''],
  ])('refuses %s', (_label, password) => {
    expect(isValidPassword(password)).toBe(false);
  });

  it('counts the length of ñ the same whichever way the tilde is typed', () => {
    // 7 characters once the tilde joins the n, as the API counts them.
    expect(isValidPassword('ab1!ñcd')).toBe(false);
    expect(isValidPassword('ab1!ñcde')).toBe(true);
  });

  it('refuses the email, whatever its case, once the email is known', () => {
    expect(isValidPassword('Bob1@example.com', 'bob1@example.com ')).toBe(false);
    expect(isValidPassword('Bob1@example.com')).toBe(true);
  });
});
