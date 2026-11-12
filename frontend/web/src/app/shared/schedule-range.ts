import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const FULL = 'EEE, MMM d y, h:mm a';

type DateInput = string | number | Date | null | undefined;

/** "Starts: Thu, Nov 12 2026, 9:00 AM" and, on the next line, "Ends: Sat, Nov 14 2026, 5:30 PM". */
export function scheduleLines(start: DateInput, end: DateInput, locale = 'en-US'): string[] {
  const lines: string[] = [];
  if (start != null && start !== '') lines.push(`Starts: ${formatDate(start, FULL, locale)}`);
  if (end != null && end !== '') lines.push(`Ends: ${formatDate(end, FULL, locale)}`);
  return lines;
}

/** An event's schedule: the start on one line and the end on the next. */
@Component({
  selector: 'app-schedule',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@for (line of lines(); track $index) { <span class="schedule-line">{{ line }}</span> }`,
  styles: `
    :host { display: inline-flex; flex-direction: column; vertical-align: top; }
    .schedule-line { display: block; }
  `,
})
export class ScheduleComponent {
  readonly start = input<DateInput>();
  readonly end = input<DateInput>();
  protected readonly lines = computed(() => scheduleLines(this.start(), this.end()));
}
