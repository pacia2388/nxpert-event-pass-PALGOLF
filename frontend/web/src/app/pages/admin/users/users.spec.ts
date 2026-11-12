import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { of } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import { ConfirmService } from '../../../core/confirm.service';
import { UsersComponent } from './users';

describe('Users page', () => {
  let fixture: ComponentFixture<UsersComponent>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideNativeDateAdapter(),
        { provide: ApiService, useValue: { listUsers: () => of([]) } },
        // "Discard" is chosen whenever the page asks.
        { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(true) } },
      ],
    });
    fixture = TestBed.createComponent(UsersComponent);
    await settle();
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const root = () => fixture.nativeElement as HTMLElement;
  const requiredMarkers = () => root().querySelectorAll('mat-icon.req').length;
  const input = (name: string) => root().querySelector<HTMLInputElement>(`input[name="${name}"]`)!;

  async function failSave(): Promise<void> {
    fixture.componentInstance.save();
    await settle();
    expect(requiredMarkers()).toBeGreaterThan(0);
  }

  it('clears the markers when New is pressed on a blank record after a failed save', async () => {
    await failSave();
    await fixture.componentInstance.newUser();
    await settle();

    expect(requiredMarkers()).toBe(0);
  });

  it('discards typed values when New is pressed on a blank record', async () => {
    const first = input('firstName');
    first.value = 'Bob';
    first.dispatchEvent(new Event('input'));
    await settle();

    await fixture.componentInstance.newUser();
    await settle();

    expect(input('firstName').value).toBe('');
  });

  it('clears the markers when the list is refreshed on a blank record after a failed save', async () => {
    await failSave();
    await fixture.componentInstance.refresh();
    await settle();

    expect(requiredMarkers()).toBe(0);
  });
});
