import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { AuthService } from '../../core/auth.service';

export interface ForgotPasswordData {
  email: string;
}

// "Forgot password?" dialog: asks for the account email and always gives the same answer.
@Component({
  selector: 'app-forgot-password-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  templateUrl: './forgot-password-dialog.html',
  styleUrl: './forgot-password-dialog.scss',
})
export class ForgotPasswordDialogComponent {
  private auth = inject(AuthService);
  private data = inject<ForgotPasswordData>(MAT_DIALOG_DATA);

  email = this.data.email ?? '';
  sending = signal(false);
  sent = signal(false);

  submit(): void {
    if (this.sending() || !this.email.trim()) return;
    this.sending.set(true);
    this.auth.forgotPassword(this.email.trim()).subscribe({
      next: () => {
        this.sending.set(false);
        this.sent.set(true);
      },
      error: () => this.sending.set(false),
    });
  }
}
