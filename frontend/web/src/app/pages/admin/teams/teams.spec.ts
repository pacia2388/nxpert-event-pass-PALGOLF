import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import { CategoryDto, CategoryService } from '../../../core/category.service';
import { TeamsComponent } from './teams';

const SENIOR: CategoryDto = { name: 'Senior', icon: 'elderly', teamMaxMembers: 8 };
const JUNIOR: CategoryDto = { name: 'Junior', icon: 'label', teamMaxMembers: 12 };

describe('Teams page form', () => {
  let fixture: ComponentFixture<TeamsComponent>;
  let list: ReturnType<typeof signal<CategoryDto[]>>;

  async function open(categories: CategoryDto[]): Promise<void> {
    TestBed.resetTestingModule();
    list = signal(categories);
    const service = Object.assign(Object.create(CategoryService.prototype), {
      list,
      enabled: () => list().length > 0,
    });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ApiService, useValue: { listTeams: () => of([]), listUsers: () => of([]) } },
        { provide: CategoryService, useValue: service },
      ],
    });
    fixture = TestBed.createComponent(TeamsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => open([SENIOR, JUNIOR]));

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

  it('says a one-letter team name is too short, not missing', () => {
    const name = root().querySelector<HTMLInputElement>('input[name="description"]')!;
    name.dispatchEvent(new Event('focus'));
    name.value = ' A ';
    name.dispatchEvent(new Event('input'));
    name.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    const icon = fixture.debugElement.query(
      By.css('mat-form-field:has(input[name="description"]) mat-icon.req'),
    );
    expect(icon?.injector.get(MatTooltip).message).toBe('At least 2 characters');
  });

  it('offers the listed categories with their caps and starts in the first', () => {
    expect(fixture.componentInstance.category).toBe('Senior');
    expect(fixture.componentInstance.categories).toEqual([
      { name: 'Senior', maxMembers: 8 },
      { name: 'Junior', maxMembers: 12 },
    ]);
  });

  it('picks the first category once the list arrives after the form opened', async () => {
    await open([]);
    list.set([JUNIOR]);
    fixture.detectChanges();

    expect(fixture.componentInstance.category).toBe('Junior');
  });

  it('hides the Category field and column when the installation has no categories', async () => {
    await open([]);

    expect(root().querySelector('mat-select[name="category"]')).toBeNull();
    expect(
      Array.from(root().querySelectorAll('th')).map((th) => th.textContent?.trim()),
    ).not.toContain('Category');
    expect(fixture.componentInstance.category).toBe('');
  });
});
