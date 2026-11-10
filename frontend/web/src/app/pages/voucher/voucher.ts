import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { Voucher } from '../../core/models';
import { downloadVoucherPng } from '../../core/qr.util';
import { VoucherPreviewDialogComponent } from './voucher-preview-dialog';

// Mirrors VouchersController.NormalizeTicket (server is authoritative).
const TICKET_PATTERN = /^[A-Z0-9-]{4,30}$/;

/** Airline baggage vouchers (golf equipment) for Participants and Team Leads. */
@Component({
  selector: 'app-voucher',
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatTableModule,
    MatTooltipModule,
  ],
  templateUrl: './voucher.html',
  styleUrl: './voucher.scss',
})
export class VoucherComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private dialog = inject(MatDialog);
  readonly auth = inject(AuthService);

  readonly columns = ['voucherNo', 'ticketNumber', 'createdAt', 'actions'];
  readonly email = signal('');
  readonly vouchers = signal<Voucher[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly ticketError = signal('');
  ticketNumber = '';

  ngOnInit(): void {
    this.api.getMyProfile().subscribe({ next: (p) => this.email.set(p.email) });
    this.api.getMyVouchers().subscribe({
      next: (list) => {
        this.vouchers.set(list);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  normalized(): string {
    return this.ticketNumber.trim().toUpperCase();
  }

  isValid(): boolean {
    const t = this.normalized();
    return TICKET_PATTERN.test(t) && /[A-Z0-9]/.test(t);
  }

  generate(form: NgForm): void {
    if (!this.isValid() || this.saving()) return;
    this.saving.set(true);
    this.ticketError.set('');
    this.api.createVoucher(this.normalized()).subscribe({
      next: (v) => {
        this.vouchers.update((list) => [v, ...list]);
        // resetForm clears touched/submitted too, so the emptied required field shows no error.
        form.resetForm({ ticketNumber: '' });
        this.saving.set(false);
        this.toast.success(`Voucher ${v.voucherNo} generated.`);
      },
      error: (err: HttpErrorResponse) => {
        const message = err.error?.message ?? 'Could not generate the voucher. Please try again.';
        if (err.status === 409 || err.status === 400) this.ticketError.set(message);
        this.toast.error(message);
        this.saving.set(false);
      },
    });
  }

  preview(v: Voucher): void {
    this.dialog.open(VoucherPreviewDialogComponent, { data: v, autoFocus: false });
  }

  async download(v: Voucher): Promise<void> {
    try {
      await downloadVoucherPng(v);
    } catch {
      this.toast.error('Could not download the voucher. Please try again.');
    }
  }
}
