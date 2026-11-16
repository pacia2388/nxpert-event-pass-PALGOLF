import type { TeamCategory } from './models';

// Who an event is for. null = no category (everyone).
export type EventCategory = 'Senior' | 'Regular';

export const EVENT_CATEGORY_LABELS: Record<EventCategory, string> = {
  Senior: 'Senior',
  Regular: 'Regular',
};

/**
 * The warning to show before someone registers for an event whose category differs from their
 * team's, or null when they match, the event has no category, or the player is not on a team.
 */
export function categoryTeamWarning(
  eventCategory: EventCategory | null | undefined,
  teamCategory: TeamCategory | null | undefined,
): string | null {
  if (!eventCategory || !teamCategory || eventCategory === teamCategory) return null;
  return `This is a ${eventCategory} event, but your team is in the ${teamCategory} category.`;
}
