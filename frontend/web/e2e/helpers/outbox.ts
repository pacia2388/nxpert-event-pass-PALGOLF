import fs from 'node:fs';
import path from 'node:path';
import { OUTBOX_DIR } from '../env';

export interface OutboxMail {
  file: string;
  subject: string;
  html: string;
  /** True when the message carries the QR entry pass. */
  hasPass: boolean;
}

/**
 * Messages the API wrote to its outbox for one address. With email off, EmailService saves each
 * message as `<ticks>_<subject>.html` whose first line is `<!-- To: … | Subject: … -->`, and a
 * QR pass as a sibling `.qr.png` that the HTML references.
 */
export function outboxFor(email: string): OutboxMail[] {
  if (!fs.existsSync(OUTBOX_DIR)) return [];
  const needle = email.toLowerCase();
  return fs
    .readdirSync(OUTBOX_DIR)
    .filter((f) => f.endsWith('.html'))
    .map((file) => ({ file, html: fs.readFileSync(path.join(OUTBOX_DIR, file), 'utf8') }))
    .filter(({ html }) => (html.split('\n', 1)[0] ?? '').toLowerCase().includes(needle))
    .map(({ file, html }) => ({
      file,
      html,
      subject: /Subject: (.*?) -->/.exec(html)?.[1] ?? '',
      hasPass: html.includes('.qr.png'),
    }));
}
