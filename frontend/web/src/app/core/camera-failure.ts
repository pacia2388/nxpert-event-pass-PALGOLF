// Why a camera failed to open. Errors are a DOMException from getUserMedia or html5-qrcode's
// "Error getting userMedia, error = <name>: <message>" string.
export type CameraFailure = 'denied' | 'busy' | 'missing' | 'insecure' | 'other';

export function cameraFailure(err: unknown): CameraFailure {
  if (!window.isSecureContext) return 'insecure';
  const text = err instanceof DOMException ? err.name : String(err);
  if (/NotAllowedError|PermissionDenied|SecurityError/.test(text)) return 'denied';
  if (/NotReadableError|TrackStartError|AbortError/.test(text)) return 'busy';
  if (/NotFoundError|DevicesNotFound|OverconstrainedError/.test(text)) return 'missing';
  return 'other';
}

export const CAMERA_FAILURE_MESSAGES: Record<Exclude<CameraFailure, 'other'>, string> = {
  denied:
    'Camera access is blocked. Allow the camera for this site (and in Windows Settings → Privacy → Camera) — it opens by itself once allowed.',
  busy: 'The camera is in use by another app (Teams, Zoom…). It opens by itself once it is free.',
  missing: 'No camera found. Connect a camera, then retry.',
  insecure: 'The camera needs HTTPS (or localhost). Open this page over HTTPS.',
};
