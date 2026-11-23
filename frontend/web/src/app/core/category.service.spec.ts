import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CategoryService } from './category.service';

describe('CategoryService', () => {
  let http: HttpTestingController;

  function open(): CategoryService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(CategoryService);
  }

  it('loads the installation list once', () => {
    const categories = open();
    http
      .expectOne((r) => r.url.endsWith('/api/settings/categories'))
      .flush([
        { name: 'Senior', icon: 'elderly', teamMaxMembers: 8 },
        { name: 'Junior', icon: 'label', teamMaxMembers: 12 },
      ]);

    expect(categories.enabled()).toBe(true);
    expect(categories.names()).toEqual(['Senior', 'Junior']);
    expect(categories.icon('Senior')).toBe('elderly');
    http.verify();
  });

  it('keeps a record category taken off the list and gives it the default icon', () => {
    const categories = open();
    http.expectOne(() => true).flush([{ name: 'Senior', icon: 'elderly', teamMaxMembers: 8 }]);

    expect(categories.names('Regular')).toEqual(['Senior', 'Regular']);
    expect(categories.names('Senior')).toEqual(['Senior']);
    expect(categories.icon('Regular')).toBe('label');
  });

  it('is off when the installation has no categories', () => {
    const categories = open();
    http.expectOne(() => true).flush([]);

    expect(categories.enabled()).toBe(false);
    expect(categories.names()).toEqual([]);
  });

  it('is off when the list cannot be loaded', () => {
    const categories = open();
    http.expectOne(() => true).flush('down', { status: 500, statusText: 'Server Error' });

    expect(categories.enabled()).toBe(false);
  });
});
