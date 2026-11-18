import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { PhoneFieldComponent } from './phone-field';

describe('Phone field', () => {
  function create(value: string | null) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiService, useValue: { getPhoneCountries: () => of([]) } },
      ],
    });
    const fixture = TestBed.createComponent(PhoneFieldComponent);
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('hands back blank for a stored value with no digits, as it shows', () => {
    expect(create('sdf').value()).toBeNull();
  });

  it('keeps a real stored number', () => {
    expect(create('+639171234567').value()).toBe('+639171234567');
  });
});
