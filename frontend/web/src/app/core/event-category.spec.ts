import { categoryTeamWarning } from './event-category';

describe('event category team warning', () => {
  it('warns a Regular-team player joining a Senior event', () => {
    expect(categoryTeamWarning('Senior', 'Regular')).toBe(
      'This is a Senior event, but your team is in the Regular category.',
    );
  });

  it('warns a Senior-team player joining a Regular event', () => {
    expect(categoryTeamWarning('Regular', 'Senior')).toBe(
      'This is a Regular event, but your team is in the Senior category.',
    );
  });

  it.each([
    ['a Senior-team player joining a Senior event', 'Senior', 'Senior'],
    ['a Regular-team player joining a Regular event', 'Regular', 'Regular'],
    ['anyone joining an event with no category', null, 'Senior'],
    ['a player who is not on a team', 'Senior', null],
  ] as const)('does not warn %s', (_label, eventCategory, teamCategory) => {
    expect(categoryTeamWarning(eventCategory, teamCategory)).toBeNull();
  });
});
