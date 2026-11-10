import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { voucherCodeFor } from '../../core/voucher';

// PROTOTYPE: a Participant or Team Lead enters their Philippine Airlines booking reference and
// gets a voucher code for free golf equipment. See core/voucher.ts for what is not real yet.
@Component({
  selector: 'app-voucher',
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule],
  templateUrl: './voucher.html',
  styleUrl: './voucher.scss',
})
export class VoucherComponent {
  bookingReference = '';
  readonly code = signal<string | null>(null);
  readonly invalid = signal(false);

  submit(): void {
    const code = voucherCodeFor(this.bookingReference);
    this.code.set(code);
    this.invalid.set(code === null);
  }
}
