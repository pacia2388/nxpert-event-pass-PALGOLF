import { Component, OnInit, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Voucher } from '../../core/models';
import { downloadVoucherPng, voucherPngDataUrl } from '../../core/qr.util';

/** Shows the exact PNG the Download action saves. */
@Component({
  selector: 'app-voucher-preview-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  templateUrl: './voucher-preview-dialog.html',
  styleUrl: './voucher-preview-dialog.scss',
})
export class VoucherPreviewDialogComponent implements OnInit {
  readonly data = inject<Voucher>(MAT_DIALOG_DATA);
  readonly image = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    this.image.set(await voucherPngDataUrl(this.data));
  }

  download(): void {
    void downloadVoucherPng(this.data);
  }
}
