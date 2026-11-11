import { TeamMemberDto } from './models';
import { selectableEmails, selectionChanges, teamCheckState, toggleMember, toggleTeam } from './team-invite.util';

function member(email: string, userId = 1): TeamMemberDto {
  return { userId, fullName: `User ${userId}`, email, addedAt: '2026-11-11T00:00:00Z' };
}

const ana = member('Ana@Example.com', 1);
const ben = member('ben@example.com', 2);
const cy = member('cy@example.com', 3);
const team = [ana, ben, cy];

describe('picking team members to invite', () => {
  describe('who can be picked', () => {
    it('offers each member by lower-cased email', () => {
      expect(selectableEmails(team)).toEqual(['ana@example.com', 'ben@example.com', 'cy@example.com']);
    });

    it('leaves out members without a usable email', () => {
      expect(selectableEmails([ana, member('', 4), member('not-an-email', 5)])).toEqual(['ana@example.com']);
    });
  });

  describe('the team checkbox', () => {
    it('is unchecked when no member is ticked', () => {
      expect(teamCheckState(team, new Set())).toBe('none');
    });

    it('is partial when some members are ticked', () => {
      expect(teamCheckState(team, new Set(['ana@example.com']))).toBe('some');
    });

    it('is checked when every member is ticked', () => {
      expect(teamCheckState(team, new Set(['ana@example.com', 'ben@example.com', 'cy@example.com']))).toBe('all');
    });

    it('is unchecked for a team with nobody to pick', () => {
      expect(teamCheckState([], new Set())).toBe('none');
      expect(teamCheckState([member('', 4)], new Set())).toBe('none');
    });
  });

  describe('ticking the team checkbox', () => {
    it('ticks every member, keeping ticks from other teams', () => {
      const ticked = toggleTeam(team, new Set(['zed@example.com', 'ana@example.com']));
      expect([...ticked].sort()).toEqual(['ana@example.com', 'ben@example.com', 'cy@example.com', 'zed@example.com']);
    });

    it('clears the team when every member is already ticked, keeping other teams', () => {
      const ticked = toggleTeam(team, new Set(['zed@example.com', 'ana@example.com', 'ben@example.com', 'cy@example.com']));
      expect([...ticked]).toEqual(['zed@example.com']);
    });
  });

  describe('ticking one member', () => {
    it('ticks an unticked member and unticks a ticked one', () => {
      expect([...toggleMember('ana@example.com', new Set())]).toEqual(['ana@example.com']);
      expect([...toggleMember('ana@example.com', new Set(['ana@example.com', 'ben@example.com']))]).toEqual(['ben@example.com']);
    });
  });

  describe('what changes in To', () => {
    it('adds newly ticked members and removes unticked ones that were in To', () => {
      const changes = selectionChanges(new Set(['ben@example.com', 'cy@example.com']), ['ana@example.com', 'ben@example.com']);
      expect(changes).toEqual({ add: ['cy@example.com'], remove: ['ana@example.com'] });
    });

    it('changes nothing when the ticks match To', () => {
      expect(selectionChanges(new Set(['ana@example.com']), ['ana@example.com'])).toEqual({ add: [], remove: [] });
    });
  });
});
