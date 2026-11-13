import fs from 'node:fs';
import path from 'node:path';
import { OUTBOX_DIR, RUN_ID } from '../env';

// File names start with DateTime.UtcNow.Ticks; skip anything written before this run started so
// old dev and e2e mail is never read. .NET ticks = 100 ns since 0001-01-01.
const RUN_START_TICKS = BigInt(RUN_ID) * 10_000n + 621_355_968_000_000_000n;
function writtenThisRun(file: string): boolean {
  const ticks = /^(\d+)_/.exec(file)?.[1];
  return ticks !== undefined && BigInt(ticks) >= RUN_START_TICKS;
}

export interface OutboxMail {
  file: string;
  subject: string;
  html: string;
  /** True when the message carries the QR entry pass. */
  hasPass: boolean;
}

/**
 * Messages the API wrote to its outbox for one address. With email off, EmailService saves each
 * message as `<ticks>_<subject>.html` whose first line is `<!-- To: … | Subject: … -->`, and the
 * QR pass as a sibling `.qrcode.png` (or `.qr.png`) that the HTML references.
 */
export function outboxFor(email: string): OutboxMail[] {
  if (!fs.existsSync(OUTBOX_DIR)) return [];
  const needle = email.toLowerCase();
  return fs
    .readdirSync(OUTBOX_DIR)
    .filter((f) => f.endsWith('.html') && writtenThisRun(f))
    .map((file) => ({ file, html: fs.readFileSync(path.join(OUTBOX_DIR, file), 'utf8') }))
    .filter(({ html }) => (html.split('\n', 1)[0] ?? '').toLowerCase().includes(needle))
    .map(({ file, html }) => ({
      file,
      html,
      subject: /Subject: (.*?) -->/.exec(html)?.[1] ?? '',
      hasPass: /\.qr(code)?\.png/.test(html),
    }));
}
