import { Component, computed, input, model } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MailAttachment } from '../../core/models';

// Attachments of a composed email / plan. Files are sent as multipart parts; the server saves the
// whole message (with the files) as an .eml. Existing files can be kept or removed.
@Component({
  selector: 'app-mail-attachments',
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './mail-attachments.html',
  styleUrl: './mail-attachments.scss',
})
export class MailAttachmentsComponent {
  /** Attachments already saved on the email being edited. */
  readonly existing = input<MailAttachment[]>([]);
  readonly maxTotalBytes = 25 * 1024 * 1024;

  // Two-way: the owner can keep the picked and removed lists itself, so they survive this panel
  // leaving the screen (the mail planner shows it on the days step and saves from the last step).
  readonly files = model<File[]>([]);
  readonly removed = model<string[]>([]);

  readonly kept = computed(() =>
    this.existing().filter((a) => !this.removed().includes(a.name)),
  );
  readonly keptNames = computed(() => this.kept().map((a) => a.name));
  readonly totalBytes = computed(
    () => this.files().reduce((sum, f) => sum + f.size, 0) +
      this.kept().reduce((sum, a) => sum + a.size, 0),
  );
  readonly overLimit = computed(() => this.totalBytes() > this.maxTotalBytes);
  readonly percent = computed(() =>
    Math.min(100, Math.round((this.totalBytes() / this.maxTotalBytes) * 100)),
  );

  size(bytes: number): string {
    return bytes >= 1024 * 1024
      ? `${(bytes / 1024 / 1024).toFixed(1)} MB`
      : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  pick(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files?.length) this.files.update((list) => [...list, ...Array.from(input.files!)]);
    input.value = '';
  }

  removeNew(index: number): void {
    this.files.update((list) => list.filter((_, i) => i !== index));
  }

  removeExisting(name: string): void {
    this.removed.update((list) => [...list, name]);
  }

  clear(): void {
    this.files.set([]);
    this.removed.set([]);
  }
}
