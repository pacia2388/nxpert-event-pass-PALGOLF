import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
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

  async function setup(opts: { tab?: string; registrations?: Observable<unknown> } = {}) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ...(opts.tab
          ? [{ provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ tab: opts.tab }) } } }]
          : []),
        {
          provide: ApiService,
          useValue: {
            browseEvents: () =>
              of([event(1, 'Open Cup', future), event(2, 'My Classic', future), event(3, 'Old Open', past)]),
            myRegistrations: () => opts.registrations ?? of([{ eventId: 2 }]),
          },
        },
      ],
    });
    fixture = TestBed.createComponent(EventsBrowseComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const root = () => fixture.nativeElement as HTMLElement;
  const text = () => root().textContent ?? '';
  const titles = () => Array.from(root().querySelectorAll('.ev-name')).map((h) => h.textContent?.trim());
  const filterButton = (label: string) =>
    Array.from(root().querySelectorAll<HTMLButtonElement>('.pill-tab')).find((b) => b.textContent?.trim() === label)!;
  const clickTab = (label: string) => {
    filterButton(label).click();
    fixture.detectChanges();
  };
  const searchFor = (q: string) => {
    const input = root().querySelector<HTMLInputElement>('.search-field input')!;
    input.value = q;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  it('shows every event under All by default', async () => {
    await setup();
    expect(titles()).toEqual(['Open Cup', 'My Classic', 'Old Open']);
    expect(filterButton('All').getAttribute('aria-pressed')).toBe('true');
  });

  it('filters events by the chosen tab', async () => {
    await setup();
    clickTab('Open');
    expect(titles()).toEqual(['Open Cup']);
    expect(filterButton('Open').getAttribute('aria-pressed')).toBe('true');
    clickTab('Registered');
    expect(titles()).toEqual(['My Classic']);
    clickTab('Ended');
    expect(titles()).toEqual(['Old Open']);
  });

  it('searches only within the chosen tab', async () => {
    await setup();
    clickTab('Open');
    searchFor('open');
    expect(titles()).toEqual(['Open Cup']);
  });

  it('says when the chosen tab has no events', async () => {
    await setup({ registrations: of([]) });
    clickTab('Registered');
    expect(titles()).toEqual([]);
    expect(text()).toContain('No registered events.');
  });

  it('opens on the tab named in the address', async () => {
    await setup({ tab: 'Registered' });
    expect(titles()).toEqual(['My Classic']);
  });

  it('warns when your registrations could not be loaded', async () => {
    await setup({ registrations: throwError(() => new Error('500')) });
    expect(text()).toContain('Couldn’t load your registrations');
  });
});
