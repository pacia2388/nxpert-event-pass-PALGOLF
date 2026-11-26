import { TestBed } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AppUpdateService } from './app-update.service';

describe('app update service', () => {
  function service(sw: Partial<SwUpdate>): AppUpdateService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: SwUpdate, useValue: sw }] });
    return TestBed.inject(AppUpdateService);
  }

  it('does nothing when the service worker is disabled', () => {
    const checkForUpdate = vi.fn();
    const svc = service({ isEnabled: false, checkForUpdate } as Partial<SwUpdate>);

    expect(svc.ready()).toBe(false);
    expect(checkForUpdate).not.toHaveBeenCalled();
  });

  it('becomes ready when a downloaded version is waiting', async () => {
    const updates = new Subject<{ type: string }>();
    const svc = service({
      isEnabled: true,
      versionUpdates: updates.asObservable(),
      checkForUpdate: vi.fn().mockResolvedValue(false),
    } as unknown as Partial<SwUpdate>);

    updates.next({ type: 'NO_NEW_VERSION_DETECTED' });
    expect(svc.ready()).toBe(false);

    updates.next({ type: 'VERSION_READY' });
    expect(svc.ready()).toBe(true);
  });
});
