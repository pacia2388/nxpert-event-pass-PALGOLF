// Who an event is for. null = no category (everyone).
export type EventCategory = 'Senior' | 'Regular';

export const SENIOR_AGE = 65;

export const EVENT_CATEGORY_LABELS: Record<EventCategory, string> = {
  Senior: 'Senior',
  Regular: 'Regular',
};

/** Whole years from a yyyy-MM-dd birthdate to `today` (local time). */
function ageOn(birthdate: string, today: Date): number {
  const [y, m, d] = birthdate.split('-').map(Number);
  let age = today.getFullYear() - y;
  const month = today.getMonth() + 1;
  if (month < m || (month === m && today.getDate() < d)) age--;
  return age;
}

/**
 * The warning to show before someone registers for an event whose category doesn't match their
 * age, or null when it matches, the event has no category, or the birthdate is unknown.
 */
export function categoryAgeWarning(
  category: EventCategory | null | undefined,
  birthdate: string | null | undefined,
  today: Date = new Date(),
): string | null {
  if (!category || !birthdate) return null;
  const age = ageOn(birthdate, today);
  if (category === 'Senior' && age < SENIOR_AGE)
    return `This event is for seniors (${SENIOR_AGE} and older). You are ${age}.`;
  if (category === 'Regular' && age >= SENIOR_AGE)
    return `This event is for regular players (under ${SENIOR_AGE}). You are ${age}.`;
  return null;
}
