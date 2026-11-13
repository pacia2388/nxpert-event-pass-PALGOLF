import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService, ViberBroadcastResult } from '../core/api.service';
import { RealtimeService } from '../core/realtime.service';
import { ViberBroadcastProgressNotice } from '../core/models';

export interface ViberBroadcastData {
  eventId: number;
  eventTitle: string;
}

// null = cancelled/sent internally; the dialog performs the send itself and closes with true on success.
/**
 * Lets an event organizer type a message and blast it over Viber to every registered Viber
 * number for the event. Shows the recipient count up front and sends via the API directly.
 */
@Component({
  selector: 'app-viber-broadcast-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatProgressBarModule,
  ],
  template: `
    <h2 mat-dialog-title>Send Viber message</h2>
    <mat-dialog-content>
      <p class="muted vb-intro">
        Type an announcement to blast over Viber to everyone registered for
        <strong>{{ data.eventTitle }}</strong> who chose Viber (or Both) and has a phone on file.
      </p>

      @if (loadingCount()) {
        <p class="muted vb-count"><mat-spinner diameter="16"></mat-spinner> Counting recipients…</p>
      } @else if (recipientCount() === 0) {
        <p class="vb-none">
          <mat-icon>info</mat-icon>
          No registrations with a Viber number were found for this event yet.
        </p>
      } @else {
        <p class="vb-count">
          <mat-icon>groups</mat-icon>
          This will send to <strong>{{ recipientCount() }}</strong>
          Viber number{{ recipientCount() === 1 ? '' : 's' }}.
        </p>
      }

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Message</mat-label>
        <textarea matInput [(ngModel)]="message" rows="5" maxlength="1000"
                  placeholder="e.g. The keynote in Hall A starts in 15 minutes. Please take your seats."
                  [disabled]="sending()"></textarea>
        <mat-hint align="end">{{ message.length }}/1000</mat-hint>
      </mat-form-field>

      <mat-slide-toggle [(ngModel)]="fallbackToEmail" [disabled]="sending()">Fallback to email</mat-slide-toggle>
      <p class="muted vb-hint">
        When on, registrants Viber can't reach (send failed, or their country isn't enabled for Viber)
        get the message by email instead.
      </p>

      @if (sending()) {
        <div class="vb-progress" role="status" aria-live="polite">
          @if (progress(); as p) {
            <mat-progress-bar mode="determinate" [value]="p.total ? (p.done * 100) / p.total : 0"></mat-progress-bar>
            <p class="muted">
              Sending {{ p.done }} of {{ p.total }} — {{ p.sent }} sent by Viber{{ p.emailed ? ', ' + p.emailed + ' emailed' : '' }}{{ p.failed ? ', ' + p.failed + ' failed' : '' }}
            </p>
          } @else {
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
            <p class="muted">Starting…</p>
          }
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close()" [disabled]="sending()">Cancel</button>
      <button mat-flat-button color="primary" (click)="send()"
              [disabled]="!canSend()">
        @if (sending()) {
          <mat-spinner diameter="18"></mat-spinner>
        } @else {
          <mat-icon>send</mat-icon>
        }
        Send now
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      :host { display: block; min-width: min(460px, 86vw); }
      .vb-intro { margin: 0 0 12px; }
      .vb-count, .vb-none {
        display: flex; align-items: center; gap: 8px;
        margin: 0 0 14px; font-size: 13px;
      }
      .vb-none { color: var(--mat-sys-error, #b00020); }
      .full-width { width: 100%; }
      button mat-spinner { display: inline-block; margin-right: 6px; }
      .vb-hint { margin: 4px 0 0; font-size: 12px; }
      .vb-progress { margin-top: 14px; }
      .vb-progress p { margin: 6px 0 0; font-size: 12px; }
    `,
  ],
})
export class ViberBroadcastDialogComponent {
  readonly ref = inject(MatDialogRef<ViberBroadcastDialogComponent, ViberBroadcastResult>);
  readonly data = inject<ViberBroadcastData>(MAT_DIALOG_DATA);
  private api = inject(ApiService);

  message = '';
  fallbackToEmail = false;
  sending = signal(false);
  loadingCount = signal(true);
  recipientCount = signal(0);
  /** Live progress pushed by the API while the broadcast runs (null until the first update). */
  progress = signal<ViberBroadcastProgressNotice | null>(null);
  /** Id of the send in flight; progress pushes for other sends (other tabs/events) are ignored. */
  private broadcastId = '';

  constructor() {
    inject(RealtimeService)
      .viberBroadcastProgress$.pipe(
        filter((n) => this.sending() && n.broadcastId === this.broadcastId),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((n) => this.progress.set(n));

    this.api.viberRecipientCount(this.data.eventId).subscribe({
      next: (r) => {
        this.recipientCount.set(r.count);
        this.loadingCount.set(false);
      },
      error: () => this.loadingCount.set(false),
    });
  }

  canSend(): boolean {
    return !this.sending() && !this.loadingCount() && this.recipientCount() > 0 && this.message.trim().length > 0;
  }

  send(): void {
    if (!this.canSend()) return;
    this.progress.set(null);
    // randomUUID needs HTTPS/localhost; getRandomValues builds the same v4 GUID anywhere.
    this.broadcastId =
      crypto.randomUUID?.() ??
      '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, (c) =>
        (+c ^ (crypto.getRandomValues(new Uint8Array(1))[0] & (15 >> (+c / 4)))).toString(16),
      );
    this.sending.set(true);
    this.api.viberBroadcast(this.data.eventId, this.message.trim(), this.fallbackToEmail, this.broadcastId).subscribe({
      next: (res) => this.ref.close(res),
      error: () => this.sending.set(false),
    });
  }
}
