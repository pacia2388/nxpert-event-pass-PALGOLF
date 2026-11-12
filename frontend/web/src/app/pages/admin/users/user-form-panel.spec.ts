import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { UserFormPanelComponent } from './user-form-panel';

describe('User form panel', () => {
  let fixture: ComponentFixture<UserFormPanelComponent>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNativeDateAdapter()],
    });
    fixture = TestBed.createComponent(UserFormPanelComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  const root = () => fixture.nativeElement as HTMLElement;
  const requiredMarkers = () => root().querySelectorAll('mat-icon.req').length;

  it('opens a blank new user without required-field markers', () => {
    expect(requiredMarkers()).toBe(0);
  });

  it('marks the empty required fields after a save attempt', async () => {
    fixture.componentInstance.save();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    // First name, last name, email and password are empty on a new record.
    expect(requiredMarkers()).toBe(4);
  });

  it('marks an empty required field once the user leaves it', () => {
    const first = root().querySelector<HTMLInputElement>('input[name="firstName"]')!;
    first.dispatchEvent(new Event('focus'));
    first.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(requiredMarkers()).toBe(1);
  });
});
