// PROTOTYPE: the voucher code is derived from the booking reference in the browser. Nothing is
// checked against Philippine Airlines and nothing is stored, so the same reference always gives
// the same code. Replace with a server endpoint before real use.

// A Philippine Airlines booking reference (PNR): 6 letters or digits.
const BOOKING_REF_RE = /^[A-Z0-9]{6}$/;
const CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

/** The golf equipment voucher code for a booking reference, or null when it is not a valid one. */
export function voucherCodeFor(bookingReference: string): string | null {
  const ref = bookingReference.trim().toUpperCase();
  if (!BOOKING_REF_RE.test(ref)) return null;

  // FNV-1a hash of the reference, re-mixed per character of the code.
  let h = 0x811c9dc5;
  for (const ch of ref) h = Math.imul(h ^ ch.charCodeAt(0), 0x01000193);
  let code = '';
  for (let i = 0; i < 8; i++) {
    h = Math.imul(h ^ (h >>> 15), 0x2c1b3c6d) + i;
    code += CODE_ALPHABET[(h >>> 8) % CODE_ALPHABET.length];
  }
  return `PALGOLF-${code.slice(0, 4)}-${code.slice(4)}`;
}
