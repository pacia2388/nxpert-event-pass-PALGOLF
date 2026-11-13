import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { EventDto } from '../../core/models';
import { EventsBrowseComponent } from './events-browse';

function event(id: number, title: string, endsAt: string, status = 'Published'): EventDto {
  return {
    id, title, endsAt, status, startsAt: endsAt, description: '', venue: 'Clubhouse', publicSlug: `ev-${id}`,
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
          ? [{ provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({ tab: opts.tab })) } }]
          : []),
        {
          provide: ApiService,
          useValue: {
            browseEvents: () =>
              of([
                event(1, 'Open Cup', future),
                event(2, 'My Classic', future),
                event(3, 'Old Open', past),
                event(4, 'Shut Cup', future, 'Closed'),
              ]),
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
    expect(titles()).toEqual(['Open Cup', 'My Classic', 'Old Open', 'Shut Cup']);
    expect(filterButton('All').getAttribute('aria-pressed')).toBe('true');
  });

  it('puts the search field above the filter tabs', async () => {
    await setup();
    const search = root().querySelector('.search-field')!;
    const tabs = root().querySelector('.pill-tabs')!;
    expect(search.compareDocumentPosition(tabs) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
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
    clickTab('Closed');
    expect(titles()).toEqual(['Shut Cup']);
  });

  it('marks a closed event as Closed and offers View instead of Register', async () => {
    await setup();
    clickTab('Closed');
    const card = root().querySelector('.ev-card')!;
    expect(card.querySelector('.ev-tags')?.textContent).toContain('Closed');
    expect(card.querySelector('.ev-tags')?.textContent).not.toContain('Open');
    expect(card.querySelector('.ev-btn.solid')?.textContent).toContain('View');
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

  it('follows a new ?tab= while the page is already open', async () => {
    await setup();
    await TestBed.inject(Router).navigateByUrl('/?tab=Closed');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(titles()).toEqual(['Shut Cup']);
  });

  it('warns when your registrations could not be loaded', async () => {
    await setup({ registrations: throwError(() => new Error('500')) });
    expect(text()).toContain('Couldn’t load your registrations');
  });
});
