import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { MailAttachmentsComponent } from '../../shared/mail-attachments/mail-attachments';
import { MailPlanDialogComponent, MailPlanDialogData } from './mail-plan-dialog';

// The attachment panel only exists on the days step; a plan is saved from the last step.
describe('Mail plan dialog attachments', () => {
  let fixture: ComponentFixture<MailPlanDialogComponent>;
  let api: { createMailPlan: ReturnType<typeof vi.fn>; getMailLimits: ReturnType<typeof vi.fn> };

  const data: MailPlanDialogData = {
    plan: null,
    groups: [],
    events: [],
    savedEvents: [],
    onSaveEvent: () => {},
    savedCc: [],
    onSavedCcChange: () => {},
    defaultDay: new Date(Date.now() + 24 * 60 * 60 * 1000),
  };

  beforeEach(() => {
    api = {
      createMailPlan: vi.fn(() => of({ id: 1 })),
      getMailLimits: vi.fn(() =>
        of({ maxImageBytes: 5_000_000, maxImages: 10, maxBodyBytes: 15_000_000 }),
      ),
    };
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        { provide: ApiService, useValue: api },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: ToastService, useValue: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } },
        { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(true) } },
      ],
    });
    fixture = TestBed.createComponent(MailPlanDialogComponent);
  });

  it('keeps a picked attachment when the plan is saved from the last step', () => {
    const dialog = fixture.componentInstance;
    const tomorrow = new Date(Date.now() + 24 * 60 * 60 * 1000);
    dialog.entries.set([{ uid: 1, id: null, date: tomorrow, time: '09:00', subject: 'Hi', body: 'Hello' }]);
    dialog.emails.set(['a@example.com']);

    dialog.step.set(1);
    fixture.detectChanges();
    const panel = fixture.debugElement.query(By.directive(MailAttachmentsComponent))
      ?.componentInstance as MailAttachmentsComponent;
    const file = new File(['agenda'], 'agenda.txt');
    panel.files.set([file]);

    // Leaving the days step destroys the panel; the file must still be in the request.
    dialog.step.set(3);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.directive(MailAttachmentsComponent))).toBeNull();

    dialog.save(false);

    const [request, files] = api.createMailPlan.mock.calls[0];
    expect(request.sends[0].subject).toBe('Hi');
    expect((files as File[]).map((f) => f.name)).toEqual(['agenda.txt']);
  });
});
