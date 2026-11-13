import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { EVENT_CATEGORY_LABELS, EventCategory } from '../core/event-category';

const ICONS: Record<EventCategory, string> = { Senior: 'elderly', Regular: 'person' };

// The "Senior" / "Regular" pill shown beside an event's status pill on event cards.
@Component({
  selector: 'app-category-pill',
  imports: [MatIconModule],
  host: { class: 'ev-tag brand' },
  template: `<mat-icon>{{ icons[category()] }}</mat-icon> {{ labels[category()] }}`,
})
export class CategoryPillComponent {
  readonly category = input.required<EventCategory>();
  protected readonly icons = ICONS;
  protected readonly labels = EVENT_CATEGORY_LABELS;
}
