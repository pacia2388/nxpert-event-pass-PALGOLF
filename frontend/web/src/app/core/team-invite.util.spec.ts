import { TeamMemberDto } from './models';
import { selectableEmails, teamCheckState, toggleMember, toggleTeam } from './team-invite.util';

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
      expect(selectableEmails(team, [])).toEqual(['ana@example.com', 'ben@example.com', 'cy@example.com']);
    });

    it('leaves out members already in To', () => {
      expect(selectableEmails(team, ['ben@example.com'])).toEqual(['ana@example.com', 'cy@example.com']);
    });

    it('leaves out members without a usable email', () => {
      expect(selectableEmails([ana, member('', 4), member('not-an-email', 5)], [])).toEqual(['ana@example.com']);
    });
  });

  describe('the team checkbox', () => {
    it('is unchecked when no member is picked', () => {
      expect(teamCheckState(team, new Set(), [])).toBe('none');
    });

    it('is partial when some members are picked', () => {
      expect(teamCheckState(team, new Set(['ana@example.com']), [])).toBe('some');
    });

    it('is checked when every member is picked', () => {
      expect(teamCheckState(team, new Set(['ana@example.com', 'ben@example.com', 'cy@example.com']), [])).toBe('all');
    });

    it('counts members already in To as done', () => {
      expect(teamCheckState(team, new Set(['ana@example.com', 'cy@example.com']), ['ben@example.com'])).toBe('all');
    });

    it('is unchecked for a team with nobody left to pick', () => {
      expect(teamCheckState([ben], new Set(), ['ben@example.com'])).toBe('none');
      expect(teamCheckState([], new Set(), [])).toBe('none');
    });
  });

  describe('ticking the team checkbox', () => {
    it('picks every member, keeping picks from other teams', () => {
      const picked = toggleTeam(team, new Set(['zed@example.com', 'ana@example.com']), []);
      expect([...picked].sort()).toEqual(['ana@example.com', 'ben@example.com', 'cy@example.com', 'zed@example.com']);
    });

    it('clears the team when every member is already picked, keeping other teams', () => {
      const picked = toggleTeam(team, new Set(['zed@example.com', 'ana@example.com', 'ben@example.com', 'cy@example.com']), []);
      expect([...picked]).toEqual(['zed@example.com']);
    });

    it('does not pick members already in To', () => {
      expect([...toggleTeam(team, new Set(), ['ben@example.com'])].sort()).toEqual(['ana@example.com', 'cy@example.com']);
    });
  });

  describe('ticking one member', () => {
    it('picks an unpicked member and unpicks a picked one', () => {
      expect([...toggleMember('ana@example.com', new Set())]).toEqual(['ana@example.com']);
      expect([...toggleMember('ana@example.com', new Set(['ana@example.com', 'ben@example.com']))]).toEqual(['ben@example.com']);
    });
  });
});
