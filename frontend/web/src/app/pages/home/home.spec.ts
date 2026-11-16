import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { HomeComponent } from './home';

describe('Home category filters', () => {
  it('are toggle buttons that say which one is pressed', () => {
    const auth = new Proxy({ isAdmin: () => true }, { get: (t, k) => (t as Record<PropertyKey, unknown>)[k] ?? (() => false) });
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: auth }] });
    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>('.pill-tab'));

    buttons.find((b) => b.textContent?.trim() === 'Events')!.click();
    fixture.detectChanges();

    expect(root.querySelector('.pill-tabs')?.getAttribute('role')).toBe('group');
    expect(root.querySelector('[role="tab"]')).toBeNull();
    expect(buttons.map((b) => [b.textContent?.trim(), b.getAttribute('aria-pressed')])).toContainEqual(['Events', 'true']);
    expect(buttons.find((b) => b.textContent?.trim() === 'All')?.getAttribute('aria-pressed')).toBe('false');
  });
});
