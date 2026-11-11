import * as QRCode from 'qrcode';
import { BRANDING } from './config';

/** The QR payload matches the emailed pass and what the scanner expects. */
export function passPayload(token: string): string {
  return `REG:${token}`;
}

/** Renders the pass token to a PNG data URL. */
export function passQrDataUrl(token: string, width = 260): Promise<string> {
  return QRCode.toDataURL(passPayload(token), {
    errorCorrectionLevel: 'Q',
    margin: 2,
    width,
  });
}

/** Triggers a browser download of the QR pass as a PNG image. */
export async function downloadPass(token: string, fileBase: string): Promise<void> {
  const dataUrl = await passQrDataUrl(token, 600);
  const a = document.createElement('a');
  a.href = dataUrl;
  a.download = `${fileBase}-pass.png`;
  document.body.appendChild(a);
  a.click();
  a.remove();
}

/** Slugifies a string for use in a download filename. */
export function fileSlug(value: string): string {
  return (
    value
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '') || 'event'
  );
}

// shortcut: dummy payload, not validated anywhere; replace with a server token when vouchers are scanned.
// Must not start with "REG:" so the check-in scanner rejects it instead of reading it as a pass.
export function voucherQrDataUrl(voucherNo: string, width = 260): Promise<string> {
  return QRCode.toDataURL(`VOUCHER:${voucherNo}`, { errorCorrectionLevel: 'Q', margin: 2, width });
}

export interface VoucherImageData {
  voucherNo: string;
  fullName: string;
  ticketNumber: string;
  createdAt: string;
}

/** "yyyy-MM-dd HH:mm" in the viewer's local time. */
export function voucherDate(iso: string): string {
  const d = new Date(iso);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

/** Draws the voucher (layout: documentation/samples/sample-voucher.png) and returns a PNG data URL. */
export async function voucherPngDataUrl(v: VoucherImageData): Promise<string> {
  const W = 800, H = 1000, QR = 520;
  const canvas = document.createElement('canvas');
  canvas.width = W;
  canvas.height = H;
  const ctx = canvas.getContext('2d')!;
  const font = (size: number, bold = false) => `${bold ? 'bold ' : ''}${size}px Arial, Helvetica, sans-serif`;
  const text = (y: number, value: string, size: number, color: string, bold = false) => {
    ctx.font = font(size, bold);
    ctx.fillStyle = color;
    ctx.fillText(value, W / 2, y, W - 80); // maxWidth shrinks long names instead of clipping
  };

  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, W, H);
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  ctx.fillStyle = '#051f63';
  ctx.fillRect(0, 0, W, 110);
  text(22, BRANDING.appName, 40, '#ffffff', true);
  text(70, 'Airline Baggage Voucher — Golf Equipment', 24, '#ffffff');
  text(150, 'FULL NAME', 20, '#666666');
  text(178, v.fullName, 40, '#000000', true);
  text(250, 'AIRLINE TICKET NUMBER', 20, '#666666');
  text(278, v.ticketNumber, 40, '#000000', true);

  const img = new Image();
  img.src = await voucherQrDataUrl(v.voucherNo, QR);
  await img.decode();
  ctx.drawImage(img, (W - QR) / 2, 350, QR, QR);

  ctx.strokeStyle = '#cccccc';
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(60, 900);
  ctx.lineTo(W - 60, 900);
  ctx.stroke();
  text(920, `Voucher No. ${v.voucherNo}  ·  Generated ${voucherDate(v.createdAt)}`, 20, '#666666');
  ctx.strokeStyle = '#051f63';
  ctx.lineWidth = 4;
  ctx.strokeRect(2, 2, W - 4, H - 4);
  return canvas.toDataURL('image/png');
}

/** Triggers a browser download of the voucher as a PNG image. */
export async function downloadVoucherPng(v: VoucherImageData): Promise<void> {
  const a = document.createElement('a');
  a.href = await voucherPngDataUrl(v);
  a.download = `voucher-${fileSlug(v.ticketNumber)}.png`;
  document.body.appendChild(a);
  a.click();
  a.remove();
}
