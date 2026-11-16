import { fileSlug, passPayload, passQrDataUrl, voucherDate, voucherQrDataUrl } from './qr.util';

describe('QR passes and vouchers', () => {
  describe('the pass QR payload', () => {
    it('marks the token as a registration pass for the scanner', () => {
      expect(passPayload('3f2b1c9e-0d4a-4e8b-9c7f-1a2b3c4d5e6f')).toBe(
        'REG:3f2b1c9e-0d4a-4e8b-9c7f-1a2b3c4d5e6f',
      );
    });
  });

  describe('QR images', () => {
    it('renders a pass as a PNG image', async () => {
      expect(await passQrDataUrl('3f2b1c9e-0d4a-4e8b-9c7f-1a2b3c4d5e6f')).toMatch(
        /^data:image\/png;base64,/,
      );
    });

    it('renders a voucher as a PNG image', async () => {
      expect(await voucherQrDataUrl('PALGOLF-AB12-CD34')).toMatch(/^data:image\/png;base64,/);
    });

    it('renders a voucher differently from a pass with the same text', async () => {
      expect(await voucherQrDataUrl('ABC123')).not.toBe(await passQrDataUrl('ABC123'));
    });
  });

  describe('download file names', () => {
    it.each([
      ['an event title', 'PAL Golf Open 2026!', 'pal-golf-open-2026'],
      ['a ticket number with symbols', '079-2345678901', '079-2345678901'],
      ['a title with stray separators', '  --Hello__World--  ', 'hello-world'],
      ['a title with accented letters', 'Ñandú Cup', 'and-cup'],
    ])('turns %s into a lower-case dashed name', (_label, value, slug) => {
      expect(fileSlug(value)).toBe(slug);
    });

    it('falls back to "event" when nothing usable is left', () => {
      expect(fileSlug('')).toBe('event');
      expect(fileSlug('!!!')).toBe('event');
    });
  });

  describe('the voucher generated date', () => {
    it('shows the local date and time as yyyy-MM-dd HH:mm', () => {
      expect(voucherDate('2026-11-14T17:05:00')).toBe('2026-11-14 17:05');
    });

    it('pads single-digit months, days, hours and minutes', () => {
      expect(voucherDate('2026-01-02T03:04:59')).toBe('2026-01-02 03:04');
    });
  });
});
