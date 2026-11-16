// The password rule the API enforces (PasswordRules): 8–128 characters with at least one letter,
// one digit and one symbol (anything but a letter, digit or space). The API also refuses the
// player's own email.
export const PASSWORD_MIN = 8;
export const PASSWORD_MAX = 128;
export const PASSWORD_PATTERN = '^(?=.*[A-Za-z])(?=.*\\d)(?=.*[^A-Za-z\\d\\s]).+$';

const pattern = new RegExp(PASSWORD_PATTERN);

export function isValidPassword(password: string): boolean {
  return (
    password.length >= PASSWORD_MIN && password.length <= PASSWORD_MAX && pattern.test(password)
  );
}
