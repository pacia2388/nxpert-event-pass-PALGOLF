import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { EventDto } from '../../core/models';
import { EventDetailsDialogComponent } from './event-details-dialog';

describe('Event details dialog', () => {
  const future = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString();

  function actionText(status: string): string {
    const e = {
      id: 1, title: 'Cup', description: '', venue: 'Course', startsAt: future, endsAt: future, status,
      publicSlug: 'cup', sessions: [], meals: [], schedule: [], formFields: [], format: 'Onsite',
      bannerImage: null, registrationFees: null,
    } as unknown as EventDto;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: MAT_DIALOG_DATA, useValue: { event: e, registered: false, blocked: false } },
        { provide: MatDialogRef, useValue: { close: () => {} } },
      ],
    });
    const fixture = TestBed.createComponent(EventDetailsDialogComponent);
    fixture.detectChanges();
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('mat-dialog-actions button');
    return buttons[buttons.length - 1].textContent ?? '';
  }

  it('offers Register for an open upcoming event', () => {
    expect(actionText('Published')).toContain('Register');
  });

  it('offers View details, not Register, for a closed event', () => {
    const text = actionText('Closed');
    expect(text).toContain('View details');
    expect(text).not.toContain('Register');
  });
});
