import { voucherCodeFor } from './voucher';

describe('golf equipment voucher', () => {
  it('gives a voucher code for a Philippine Airlines booking reference', () => {
    expect(voucherCodeFor('ABC123')).toMatch(/^PALGOLF-[A-Z0-9]{4}-[A-Z0-9]{4}$/);
  });

  it('gives the same code however the reference is typed', () => {
    expect(voucherCodeFor('  abc123 ')).toBe(voucherCodeFor('ABC123'));
  });

  it('gives different references different codes', () => {
    expect(voucherCodeFor('ABC123')).not.toBe(voucherCodeFor('ABC124'));
  });

  it.each([
    ['empty', ''],
    ['too short', 'ABC12'],
    ['too long', 'ABC1234'],
    ['with symbols', 'ABC-12'],
  ])('gives no code for a reference that is %s', (_label, ref) => {
    expect(voucherCodeFor(ref)).toBeNull();
  });
});
