import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { MailLimits } from './models';
import { DEFAULT_IMAGE_LIMITS, IMAGE_LIMITS, applyImageLimits } from '../shared/rich-text/rich-text';

// Loads the message editors' inline-image limits once (GET api/settings/mail-limits) and applies
// them to the shared rich-text helpers. The package defaults apply until the answer arrives (or
// when the request fails).
@Injectable({ providedIn: 'root' })
export class MailLimitsService {
  private api = inject(ApiService);

  readonly limits = signal<MailLimits>({ ...DEFAULT_IMAGE_LIMITS });

  constructor() {
    this.api.getMailLimits().subscribe({
      next: (limits) => {
        applyImageLimits(limits);
        this.limits.set({ ...IMAGE_LIMITS });
      },
      error: () => {},
    });
  }

  /** "Images: up to N, X MB each and Y MB total after resizing." built from the loaded numbers. */
  help(): string {
    const l = this.limits();
    const mb = (bytes: number) => Math.round((bytes / 1024 / 1024) * 10) / 10;
    return `Images: up to ${l.maxCount}, ${mb(l.maxImageBytes)} MB each and ${mb(l.maxTotalBytes)} MB total after resizing.`;
  }
}
