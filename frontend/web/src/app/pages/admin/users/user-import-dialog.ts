import { Component, inject, signal } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService } from '../../../core/api.service';
import { UserImportResult, UserImportRow } from '../../../core/models';

// The import file's header row is fixed to this list: same columns, same order (it is also the
// downloadable template). Cells are read by position. "(optional)" columns may be left blank.
// alt: an older header still accepted, so files made from the previous template keep working.
const COLUMNS: { header: string; field: keyof UserImportRow; alt?: string }[] = [
  { header: 'First Name', field: 'firstName' },
  { header: 'Middle Initial (optional)', field: 'middleInitial' },
  { header: 'Last Name', field: 'lastName' },
  { header: 'Email', field: 'email' },
  { header: 'Username (optional)', field: 'username' },
  { header: 'ID No (optional)', field: 'senateNo' },
  { header: 'Designation (optional)', field: 'level' },
  { header: 'Organization (optional)', field: 'organization' },
  { header: 'Phone (optional; with country code e.g. +639171234567)', field: 'phone', alt: 'Phone (optional)' },
  { header: 'Role (optional)', field: 'role' },
  { header: 'Notification Channel (optional)', field: 'notificationChannel' },
  { header: 'Birthdate', field: 'birthdate' },
];

const TEMPLATE_HEADERS = COLUMNS.map((c) => c.header).join(',');
const TEMPLATE_SAMPLE =
  'Juan,D,Dela Cruz,juan.delacruz@example.com,,12345,Senator,Acme Corp,\'+639171234567,Participant,Email,1990-01-31';

// Case and surrounding spaces don't count as a difference; trim() also drops an Excel BOM (U+FEFF).
function normalizeHeader(h: string): string {
  return h.trim().replace(/\s+/g, ' ').toLowerCase();
}

/** Returns an error message when the header row is not exactly the template header, else null. */
export function headerError(headerRow: string[]): string | null {
  const got = headerRow.map(normalizeHeader);
  // Trailing empty cells (e.g. a stray comma Excel kept) are not columns.
  while (got.length && !got[got.length - 1]) got.pop();
  for (let i = 0; i < Math.max(got.length, COLUMNS.length); i++) {
    const want = COLUMNS[i]?.header;
    const cell = (headerRow[i] ?? '').trim();
    if (want === undefined) return `Unexpected extra column "${cell}" (column ${i + 1}).`;
    if (i >= got.length) return `Missing column "${want}" (column ${i + 1}).`;
    const alt = COLUMNS[i].alt;
    if (got[i] !== normalizeHeader(want) && !(alt && got[i] === normalizeHeader(alt)))
      return `Column ${i + 1} must be "${want}" but is "${cell || '(blank)'}".`;
  }
  return null;
}

// Parse CSV text into rows of string cells (handles quoted fields, embedded commas/newlines).
function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let inQuotes = false;
  const s = text.replace(/\r\n?/g, '\n');
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (inQuotes) {
      if (c === '"') {
        if (s[i + 1] === '"') {
          cell += '"';
          i++;
        } else {
          inQuotes = false;
        }
      } else {
        cell += c;
      }
    } else if (c === '"') {
      inQuotes = true;
    } else if (c === ',') {
      row.push(cell);
      cell = '';
    } else if (c === '\n') {
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
    } else {
      cell += c;
    }
  }
  // flush trailing cell/row
  if (cell.length > 0 || row.length > 0) {
    row.push(cell);
    rows.push(row);
  }
  return rows;
}

/** Modal to bulk-import user accounts from a CSV file. Each new account gets the default password. */
@Component({
  selector: 'app-user-import-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './user-import-dialog.html',
  styles: [
    `
      .import-body { min-width: min(520px, 82vw); }
      .drop-hint {
        border: 1px dashed var(--mat-sys-outline);
        border-radius: 10px;
        padding: 18px;
        text-align: center;
      }
      .file-row {
        display: flex;
        align-items: center;
        gap: 8px;
        flex-wrap: wrap;
        margin-top: 10px;
      }
      .file-name { font-weight: 600; }
      .cols { font-size: 12.5px; color: var(--mat-sys-on-surface-variant); margin: 8px 0 0; }
      .preview-table { width: 100%; border-collapse: collapse; font-size: 12.5px; margin-top: 12px; }
      .preview-table th, .preview-table td {
        text-align: left; padding: 4px 8px; border-bottom: 1px solid var(--mat-sys-outline-variant);
        white-space: nowrap;
      }
      .preview-wrap { overflow-x: auto; max-height: 220px; overflow-y: auto; }
      .summary { display: flex; gap: 16px; flex-wrap: wrap; margin: 4px 0 8px; }
      .summary .stat { font-weight: 700; font-size: 18px; }
      .summary .stat small { display: block; font-weight: 500; font-size: 12px; color: var(--mat-sys-on-surface-variant); }
      .stat.created { color: #1e8e3e; }
      .stat.skipped { color: #b8860b; }
      .stat.failed { color: var(--mat-sys-error); }
      .err-list { margin: 6px 0 0; padding: 0; list-style: none; max-height: 200px; overflow-y: auto; }
      .err-list li { font-size: 12.5px; padding: 3px 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }
      .err-list .err-row { color: var(--mat-sys-on-surface-variant); }
      .note { font-size: 12.5px; color: var(--mat-sys-on-surface-variant); }
    `,
  ],
})
export class UserImportDialogComponent {
  private api = inject(ApiService);
  private ref = inject(MatDialogRef<UserImportDialogComponent>);

  fileName = signal<string | null>(null);
  rows = signal<UserImportRow[]>([]);
  parseError = signal<string | null>(null);
  importing = signal(false);
  result = signal<UserImportResult | null>(null);

  // First few parsed rows, for a preview.
  preview = () => this.rows().slice(0, 5);

  async onFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.parseError.set(null);
    this.result.set(null);
    this.rows.set([]);
    this.fileName.set(file.name);

    try {
      const text = await file.text();
      const table = parseCsv(text).filter((r) => r.some((c) => c.trim().length > 0));
      if (table.length < 2) {
        this.parseError.set('The file needs a header row and at least one data row.');
        return;
      }
      const mismatch = headerError(table[0]);
      if (mismatch) {
        this.parseError.set(
          `${mismatch} The header row must match the template exactly: ${TEMPLATE_HEADERS}. ` +
            'Download the template and copy your data into it.',
        );
        return;
      }
      const parsed: UserImportRow[] = table.slice(1).map((cells) => {
        const r: UserImportRow = {};
        COLUMNS.forEach(({ field }, i) => {
          const v = (cells[i] ?? '').trim();
          if (v) r[field] = v;
        });
        return r;
      });
      this.rows.set(parsed);
    } catch {
      this.parseError.set('Could not read that file. Please upload a valid CSV.');
    }
  }

  downloadTemplate(): void {
    const blob = new Blob([`${TEMPLATE_HEADERS}\n${TEMPLATE_SAMPLE}\n`], {
      type: 'text/csv;charset=utf-8',
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'user-import-template.csv';
    a.click();
    URL.revokeObjectURL(url);
  }

  runImport(): void {
    if (this.rows().length === 0 || this.importing()) return;
    this.importing.set(true);
    this.api.importUsers(this.rows()).subscribe({
      next: (res) => {
        this.importing.set(false);
        this.result.set(res);
      },
      error: (err) => {
        this.importing.set(false);
        this.parseError.set(err?.error?.message ?? 'Import failed. Please try again.');
      },
    });
  }

  // Close, telling the parent to reload when anything was created.
  done(): void {
    this.ref.close(this.result());
  }

  cancel(): void {
    this.ref.close(null);
  }
}
