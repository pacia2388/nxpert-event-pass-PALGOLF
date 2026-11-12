import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import { TeamsComponent } from './teams';

describe('Teams page form', () => {
  let fixture: ComponentFixture<TeamsComponent>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ApiService, useValue: { listTeams: () => of([]), listUsers: () => of([]) } },
      ],
    });
    fixture = TestBed.createComponent(TeamsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  const root = () => fixture.nativeElement as HTMLElement;
  const requiredMarkers = () => root().querySelectorAll('mat-icon.req').length;

  it('opens a blank new team without required-field markers', () => {
    expect(requiredMarkers()).toBe(0);
  });

  it('marks the team name and Captain after a save attempt', async () => {
    fixture.componentInstance.save();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(requiredMarkers()).toBe(2);
  });
});
