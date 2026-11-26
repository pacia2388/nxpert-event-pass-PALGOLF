import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';
import { API_BASE } from './config';

/** One optional user field from the server's user-fields.json. */
export interface UserFieldDto {
  name: 'Department' | 'JobRole';
  label: string;
  enabled: boolean;
  maxLength: number;
}

/**
 * The installation's optional user fields (user-fields.json on the server), loaded once. A
 * disabled field is neither shown nor sent; the server ignores it too.
 */
@Injectable({ providedIn: 'root' })
export class UserFieldsService {
  private http = inject(HttpClient);

  readonly list = toSignal(
    this.http
      .get<UserFieldDto[]>(`${API_BASE}/api/settings/user-fields`)
      .pipe(catchError(() => of([] as UserFieldDto[]))),
    { initialValue: [] as UserFieldDto[] },
  );

  readonly departmentEnabled = computed(
    () => this.list().find((f) => f.name === 'Department')?.enabled ?? false,
  );
  readonly jobRoleEnabled = computed(
    () => this.list().find((f) => f.name === 'JobRole')?.enabled ?? false,
  );
  readonly departmentMax = computed(
    () => this.list().find((f) => f.name === 'Department')?.maxLength ?? 100,
  );
  readonly jobRoleMax = computed(
    () => this.list().find((f) => f.name === 'JobRole')?.maxLength ?? 200,
  );
}
