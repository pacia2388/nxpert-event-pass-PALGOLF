import { expandRecipients, invitationErrorMessage, recipientAddress } from './invitation.util';

describe('event invitation composer', () => {
  describe('explaining a failed send', () => {
    it('shows the server message and keeps the draft', () => {
      expect(
        invitationErrorMessage({ status: 400, error: { message: 'Subject is required.' } }),
      ).toBe('Subject is required. Your draft has been kept.');
    });

    it('lists the validation errors when there is no message', () => {
      expect(
        invitationErrorMessage({
          status: 400,
          error: {
            message: '  ',
            errors: { Subject: ['Subject is required.'], To: ['Add at least one recipient.'] },
          },
        }),
      ).toBe('Subject is required. Add at least one recipient. Your draft has been kept.');
    });

    it('skips validation entries that are not text', () => {
      expect(
        invitationErrorMessage({ status: 400, error: { errors: { To: [42, 'Bad address.'] } } }),
      ).toBe('Bad address. Your draft has been kept.');
    });

    it.each([
      [
        404,
        'The invitation composer endpoint is unavailable on the API server. Deploy the updated backend, then reload this page. Your draft has been kept.',
      ],
      [
        405,
        'The invitation composer endpoint is unavailable on the API server. Deploy the updated backend, then reload this page. Your draft has been kept.',
      ],
      [
        0,
        'Cannot reach the email API. Check the connection and server availability. Your draft has been kept.',
      ],
      [
        401,
        'Your session has expired. Sign in again before sending invitations. Your draft has been kept.',
      ],
      [403, 'Only an administrator can send event invitations. Your draft has been kept.'],
      [
        413,
        'The server rejected the message size. Use smaller images or update the server upload limit. Your draft has been kept.',
      ],
      [
        400,
        'The server rejected the invitation. Check the recipients, subject and image limits. Your draft has been kept.',
      ],
      [
        422,
        'The server rejected the invitation. Check the recipients, subject and image limits. Your draft has been kept.',
      ],
      [
        500,
        'The API could not process the invitation (HTTP 500). Check the server logs before retrying. Your draft has been kept.',
      ],
    ])('explains HTTP %i without server details', (status, message) => {
      expect(invitationErrorMessage({ status, error: {} })).toBe(message);
    });

    it('explains by status when the body is plain text', () => {
      expect(invitationErrorMessage({ status: 413, error: 'Payload Too Large' })).toBe(
        'The server rejected the message size. Use smaller images or update the server upload limit. Your draft has been kept.',
      );
    });

    it('leaves out the HTTP code when there is none', () => {
      expect(invitationErrorMessage({})).toBe(
        'The API could not process the invitation. Check the server logs before retrying. Your draft has been kept.',
      );
    });
  });

  describe('reading a typed recipient', () => {
    it('trims and lower-cases an email address', () => {
      expect(recipientAddress('  Ana.Santos@Example.COM ')).toBe('ana.santos@example.com');
    });

    it('accepts the all-users shortcut in any case', () => {
      expect(recipientAddress(' @N-PAX ')).toBe('@n-pax');
    });

    it('accepts an address of 256 characters', () => {
      const address = 'a'.repeat(244) + '@example.com';
      expect(recipientAddress(address)).toBe(address);
    });

    it.each([
      ['blank', ''],
      ['with no domain dot', 'ana@example'],
      ['with no @', 'ana.example.com'],
      ['with two @', 'ana@golf@example.com'],
      ['with a space', 'ana santos@example.com'],
      ['with a display name', 'Ana <ana@example.com>'],
      ['two addresses in one', 'ana@example.com,ben@example.com'],
      ['longer than 256 characters', 'a'.repeat(245) + '@example.com'],
    ])('refuses a value %s', (_label, value) => {
      expect(recipientAddress(value)).toBeNull();
    });
  });

  describe('expanding the recipient lists', () => {
    const directory = ['Ana@Example.com', 'ben@example.com', 'not-an-email', '@n-pax', ''];

    it('expands the all-users shortcut to every valid address in the directory', () => {
      expect(expandRecipients(['@n-pax'], [], directory)).toEqual({
        to: ['ana@example.com', 'ben@example.com'],
        cc: [],
      });
    });

    it('lists each address once, in the order first seen', () => {
      expect(expandRecipients(['ben@example.com', '@n-pax'], [], directory)).toEqual({
        to: ['ben@example.com', 'ana@example.com'],
        cc: [],
      });
    });

    it('lists a directory address once even when it appears in two cases', () => {
      expect(expandRecipients(['@n-pax'], [], ['ana@example.com', 'ANA@example.com'])).toEqual({
        to: ['ana@example.com'],
        cc: [],
      });
    });

    it('does not copy anyone who is already a main recipient', () => {
      expect(
        expandRecipients(['ana@example.com'], ['ana@example.com', 'cy@example.com'], directory),
      ).toEqual({ to: ['ana@example.com'], cc: ['cy@example.com'] });
    });

    it('copies everyone else when the all-users shortcut is in copy', () => {
      expect(expandRecipients(['ana@example.com'], ['@n-pax'], directory)).toEqual({
        to: ['ana@example.com'],
        cc: ['ben@example.com'],
      });
    });

    it('expands the shortcut to nobody when the directory is empty', () => {
      expect(expandRecipients(['@n-pax'], ['@n-pax'], [])).toEqual({ to: [], cc: [] });
    });
  });
});
