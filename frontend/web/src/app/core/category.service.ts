import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';
import { API_BASE } from './config';

/** One category from the server's categories.json. */
export interface CategoryDto {
  name: string;
  icon: string;
  teamMaxMembers: number;
}

const DEFAULT_ICON = 'label';

/**
 * The installation's event / team categories (categories.json on the server), loaded once. Empty
 * when the installation has none, or until it loads (forms then keep a record's own category).
 */
@Injectable({ providedIn: 'root' })
export class CategoryService {
  private http = inject(HttpClient);

  readonly list = toSignal(
    this.http
      .get<CategoryDto[]>(`${API_BASE}/api/settings/categories`)
      .pipe(catchError(() => of([] as CategoryDto[]))),
    { initialValue: [] as CategoryDto[] },
  );

  readonly enabled = computed(() => this.list().length > 0);

  /** Pill icon; a category no longer listed gets the default. */
  icon(name: string): string {
    return this.list().find((c) => c.name === name)?.icon ?? DEFAULT_ICON;
  }

  /** Listed names, plus `current` when a record keeps one taken off the list. */
  names(current?: string | null): string[] {
    const names = this.list().map((c) => c.name);
    return current && !names.includes(current) ? [...names, current] : names;
  }
}
