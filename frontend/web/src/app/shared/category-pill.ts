import { Component, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { CategoryService } from '../core/category.service';
import { EventCategory } from '../core/event-category';

// The category pill (e.g. "Senior") shown beside an event's status pill on event cards. The icon
// comes from categories.json.
@Component({
  selector: 'app-category-pill',
  imports: [MatIconModule],
  host: { class: 'ev-tag brand' },
  template: `<mat-icon>{{ categories.icon(category()) }}</mat-icon> {{ category() }}`,
})
export class CategoryPillComponent {
  readonly category = input.required<EventCategory>();
  protected readonly categories = inject(CategoryService);
}
