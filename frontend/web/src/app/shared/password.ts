// The password rule the API enforces (PasswordRules): 8–128 characters with at least one letter,
// one digit and one symbol, and not the player's email. Letters and digits are Unicode ones, as
// .NET's char.IsLetter / char.IsDigit count them, so "ñ" is a letter here too; a symbol is
// anything that is not a letter, digit or space.
export const PASSWORD_MIN = 8;
export const PASSWORD_MAX = 128;

export interface PasswordCheck {
  text: string;
  met: boolean;
}

const LETTER = /\p{L}/u;
const DIGIT = /\p{Nd}/u;
const SYMBOL = /[^\p{L}\p{Nd}\s]/u;

// Each rule with whether the password meets it. The email rule is listed only when the email is
// known.
export function passwordChecks(password: string, email?: string | null): PasswordCheck[] {
  const p = password ?? '';
  const checks: PasswordCheck[] = [
    {
      text: `${PASSWORD_MIN} to ${PASSWORD_MAX} characters`,
      met: p.length >= PASSWORD_MIN && p.length <= PASSWORD_MAX,
    },
    { text: 'A letter', met: LETTER.test(p) },
    { text: 'A number', met: DIGIT.test(p) },
    { text: 'A symbol, like ! @ # or ?', met: SYMBOL.test(p) },
  ];
  const mail = email?.trim().toLowerCase();
  if (mail) checks.push({ text: 'Not your email', met: !!p && p.toLowerCase() !== mail });
  return checks;
}

export function isValidPassword(password: string, email?: string | null): boolean {
  return passwordChecks(password, email).every((c) => c.met);
}
