import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, LOCALE_ID, computed, inject, input } from '@angular/core';

const FULL = 'EEE, MMM d y, h:mm a';

type DateInput = string | number | Date | null | undefined;

const present = (value: DateInput): value is string | number | Date => value != null && value !== '';

/** One schedule date and time: "Sat, Nov 14 2026, 5:30 PM" (empty when there is no value). */
export function scheduleWhen(value: DateInput, locale = 'en-US'): string {
  return present(value) ? formatDate(value, FULL, locale) : '';
}

/** "Starts: Thu, Nov 12 2026, 9:00 AM" and, on the next line, "Ends: Sat, Nov 14 2026, 5:30 PM". */
export function scheduleLines(start: DateInput, end: DateInput, locale = 'en-US'): string[] {
  const lines: string[] = [];
  if (present(start)) lines.push(`Starts: ${scheduleWhen(start, locale)}`);
  if (present(end)) lines.push(`Ends: ${scheduleWhen(end, locale)}`);
  return lines;
}

/** An event's schedule: the start on one line and the end on the next. */
@Component({
  selector: 'app-schedule',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@for (line of lines(); track $index) { <span class="schedule-line">{{ line }}</span> }`,
  styles: `
    /* min-width: 0 lets the lines shrink inside flex rows (e.g. .ev-line) so long ones end in "…". */
    :host { display: inline-flex; flex-direction: column; row-gap: 2px; vertical-align: top; min-width: 0; max-width: 100%; line-height: 1.45; }
    .schedule-line { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  `,
})
export class ScheduleComponent {
  private readonly locale = inject(LOCALE_ID);
  readonly start = input<DateInput>();
  readonly end = input<DateInput>();
  protected readonly lines = computed(() => scheduleLines(this.start(), this.end(), this.locale));
}
