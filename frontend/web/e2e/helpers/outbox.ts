import fs from 'node:fs';
import path from 'node:path';
import { SENT_DIR } from '../env';

export interface OutboxMail {
  file: string;
  subject: string;
  /** True when the message carries the QR entry pass. */
  hasPass: boolean;
}

/**
 * Emails the API "sent" to one address. The e2e API writes every email as a whole MIME message
 * `<time>_<to>_<subject>.eml` to the run's own SENT_DIR (Email:TestSendDir); the QR pass is the
 * inline image with Content-Id `qrcode`.
 */
export function outboxFor(email: string): OutboxMail[] {
  if (!fs.existsSync(SENT_DIR)) return [];
  const needle = email.toLowerCase();
  return fs
    .readdirSync(SENT_DIR)
    .filter((f) => f.endsWith('.eml') && f.toLowerCase().includes(needle))
    .map((file) => {
      const eml = fs.readFileSync(path.join(SENT_DIR, file), 'utf8');
      return {
        file,
        subject: /^Subject: (.*)$/m.exec(eml)?.[1] ?? '',
        hasPass: /^Content-Id:\s*<qrcode[>@]/im.test(eml),
      };
    });
}
