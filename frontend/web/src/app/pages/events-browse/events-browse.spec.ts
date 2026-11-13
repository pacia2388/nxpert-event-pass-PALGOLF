import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { EventDto } from '../../core/models';
import { EventsBrowseComponent } from './events-browse';

function event(id: number, title: string, endsAt: string): EventDto {
  return {
    id, title, endsAt, startsAt: endsAt, description: '', venue: 'Clubhouse', publicSlug: `ev-${id}`,
    isFull: false, sessions: [], format: 'Onsite', bannerImage: null,
  } as unknown as EventDto;
}

describe('Browse Events tabs', () => {
  let fixture: ComponentFixture<EventsBrowseComponent>;
  const day = 24 * 60 * 60 * 1000;
  const future = new Date(Date.now() + 7 * day).toISOString();
  const past = new Date(Date.now() - 7 * day).toISOString();

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: ApiService,
          useValue: {
            browseEvents: () =>
              of([event(1, 'Open Cup', future), event(2, 'My Classic', future), event(3, 'Old Open', past)]),
            myRegistrations: () => of([{ eventId: 2 }]),
          },
        },
      ],
    });
    fixture = TestBed.createComponent(EventsBrowseComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  const root = () => fixture.nativeElement as HTMLElement;
  const titles = () => Array.from(root().querySelectorAll('.ev-name')).map((h) => h.textContent?.trim());
  const clickTab = (label: string) => {
    const tab = Array.from(root().querySelectorAll<HTMLButtonElement>('[role="tab"]')).find(
      (b) => b.textContent?.trim() === label,
    );
    tab!.click();
    fixture.detectChanges();
  };

  it('shows every event under All by default', () => {
    expect(titles()).toEqual(['Open Cup', 'My Classic', 'Old Open']);
  });

  it('filters events by the chosen tab', () => {
    clickTab('Open');
    expect(titles()).toEqual(['Open Cup']);
    clickTab('Registered');
    expect(titles()).toEqual(['My Classic']);
    clickTab('Ended');
    expect(titles()).toEqual(['Old Open']);
  });
});
