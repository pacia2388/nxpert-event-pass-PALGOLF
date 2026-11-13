// Phone control for user forms: a country code (from the Phone country table) + the number.
// Stored as "+<code><digits>". PH (+63) keeps the mobile mask "9XX XXX XXXX".

const digitsOf = (v: string | null | undefined) => (v ?? '').replace(/\D/g, '');

/** Splits a stored number into a listed code and the formatted rest. An unlisted number keeps
 *  its digits with no code, so the form asks for a code before it can be saved. */
export function splitPhone(stored: string | null | undefined, codes: string[]): { code: string; number: string } {
  const d = digitsOf(stored);
  if (!d) return { code: codes[0] ?? '', number: '' };
  const code = codes.find((c) => d.startsWith(c));
  return code ? { code, number: formatPhoneNumber(code, d.slice(code.length)) } : { code: '', number: '+' + d };
}

/** Live-format the number part. +63: "9XX XXX XXXX" (a leading 0 or 63 is dropped). Others: digits. */
export function formatPhoneNumber(code: string, raw: string): string {
  let d = digitsOf(raw);
  if (code !== '63') return d.slice(0, Math.max(0, 15 - code.length));
  if (d.startsWith('63')) d = d.slice(2);
  else if (d.startsWith('0')) d = d.slice(1);
  d = d.slice(0, 10);
  return [d.slice(0, 3), d.slice(3, 6), d.slice(6, 10)].filter(Boolean).join(' ');
}

/** True when code + number make a full mobile number (PH: 9 + 9 digits; others: 8–15 digits in all). */
export function isCompletePhone(code: string, number: string): boolean {
  const d = digitsOf(number);
  if (!code) return false;
  if (code === '63') return /^9\d{9}$/.test(d);
  const total = code.length + d.length;
  return d.length > 0 && total >= 8 && total <= 15;
}

/** "+<code><digits>", or null when the number is blank. */
export function joinPhone(code: string, number: string): string | null {
  const d = digitsOf(number);
  return d ? `+${code}${d}` : null;
}

/** False when the stored number's code is not one of the codes (blank counts as fine). */
export function phoneSupported(phone: string | null | undefined, codes: string[]): boolean {
  const d = digitsOf(phone);
  return !d || codes.some((c) => d.startsWith(c));
}

/** True when a stored "+<code><number>" value is a full mobile number (PH: +639 + 9 digits). */
export function isCompleteStoredPhone(value: string | null | undefined): boolean {
  const v = value ?? '';
  return v.startsWith('+63') ? /^\+639\d{9}$/.test(v) : /^\+[1-9]\d{7,14}$/.test(v);
}
