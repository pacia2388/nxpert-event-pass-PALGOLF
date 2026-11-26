import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SwUpdate } from '@angular/service-worker';

const CHECK_EVERY_MS = 30 * 60 * 1000;

// Tells the shell when a new version has been downloaded and is ready to use. Checks on start
// and every 30 minutes while the page is visible; does nothing when the service worker is off
// (development, a test host, or a browser without support).
@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  // Optional: an environment without service workers has no SwUpdate at all.
  private readonly updates = inject(SwUpdate, { optional: true });
  private readonly destroyRef = inject(DestroyRef);

  readonly ready = signal(false);
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    const updates = this.updates;
    if (!updates?.isEnabled) return;
    updates.versionUpdates
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => {
        if (event.type === 'VERSION_READY') this.ready.set(true);
      });
    void updates.checkForUpdate().catch(() => {});
    this.timer = setInterval(() => {
      if (document.visibilityState === 'visible') void updates.checkForUpdate().catch(() => {});
    }, CHECK_EVERY_MS);
    this.destroyRef.onDestroy(() => {
      if (this.timer) clearInterval(this.timer);
    });
  }

  /** Switches to the downloaded version now and reloads the page. */
  async reload(): Promise<void> {
    const updates = this.updates;
    if (!updates?.isEnabled) return;
    try {
      await updates.activateUpdate();
    } catch {
      /* the reload below still picks the new version up */
    }
    globalThis.location.reload();
  }
}
