import { Component, OnInit, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RegistrationDto } from '../../core/models';
import { downloadPass, fileSlug, passQrDataUrl } from '../../core/qr.util';
import { ScheduleComponent } from '../../shared/schedule';

@Component({
  selector: 'app-pass-dialog',
  imports: [ScheduleComponent, MatDialogModule, MatButtonModule, MatIconModule],
  templateUrl: './pass-dialog.html',
})
export class PassDialogComponent implements OnInit {
  qrDataUrl = signal<string | null>(null);

  reg = inject<RegistrationDto>(MAT_DIALOG_DATA);

  async ngOnInit(): Promise<void> {
    this.qrDataUrl.set(await passQrDataUrl(this.reg.token));
  }

  download(): void {
    downloadPass(this.reg.token, fileSlug(this.reg.eventTitle));
  }
}
