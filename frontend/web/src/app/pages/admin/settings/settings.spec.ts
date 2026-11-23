import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../../core/api.service';
import { PhoneCountryService } from '../../../core/phone-country.service';
import { ACTIVE_THEME, PALGOLF_THEME } from '../../../core/theme';
import { SettingsComponent } from './settings';

describe('Settings page theme section', () => {
  let fixture: ComponentFixture<SettingsComponent>;
  const template = new Blob(['{ "base": "PALGOLF" }'], { type: 'application/json' });
  const downloadThemeTemplate = vi.fn(() => of(template));

  async function open(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ApiService,
          useValue: {
            getSettings: () => of({ ccEmails: [], mobileProvider: 'Viber' }),
            downloadThemeTemplate,
          },
        },
        {
          provide: PhoneCountryService,
          useValue: { rows: () => [], catalog: () => [], countryName: () => '' },
        },
      ],
    });
    fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const root = () => fixture.nativeElement as HTMLElement;

  afterEach(() => {
    ACTIVE_THEME.current = PALGOLF_THEME;
    vi.restoreAllMocks();
    downloadThemeTemplate.mockClear();
  });

  it('names the theme in use and says it is the default when the server has no theme.json', async () => {
    await open();

    const section = root().querySelector('[data-testid="theme-section"]')!;
    expect(section.textContent).toContain('PALGOLF');
    expect(section.textContent).toContain('default');
  });

  it('says the theme comes from theme.json on the server', async () => {
    ACTIVE_THEME.current = { ...PALGOLF_THEME, name: 'NXPass', source: 'file' };
    await open();

    const section = root().querySelector('[data-testid="theme-section"]')!;
    expect(section.textContent).toContain('NXPass');
    expect(section.textContent).toContain('theme.json on the server');
  });

  it('downloads the template as theme-template.json', async () => {
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:theme');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    let saved = '';
    click.mockImplementation(function (this: HTMLAnchorElement) {
      saved = this.download;
    });
    await open();

    root().querySelector<HTMLButtonElement>('[data-testid="theme-template"]')!.click();

    expect(downloadThemeTemplate).toHaveBeenCalledOnce();
    expect(createUrl).toHaveBeenCalledWith(template);
    expect(saved).toBe('theme-template.json');
  });
});
