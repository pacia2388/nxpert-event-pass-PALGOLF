import { ALL_USERS, recipientAddress } from './invitation.util';
import { TeamMemberDto } from './models';

export type TeamCheckState = 'all' | 'some' | 'none';

/** What confirming the picker does to the To field. */
export interface TeamInviteChanges {
  add: string[];
  remove: string[];
}

/** A member's address as it appears in the To field, or null when they have no usable email. */
export function memberAddress(member: TeamMemberDto): string | null {
  const address = recipientAddress(member.email ?? '');
  return address && address !== ALL_USERS ? address : null;
}

/** Addresses of the members that can be ticked: those with a usable email. */
export function selectableEmails(members: TeamMemberDto[]): string[] {
  return [...new Set(members.map(memberAddress).filter((e): e is string => !!e))];
}

/** Team checkbox: checked, partial or unchecked. `selected` starts as the addresses already in To. */
export function teamCheckState(members: TeamMemberDto[], selected: ReadonlySet<string>): TeamCheckState {
  const emails = selectableEmails(members);
  const ticked = emails.filter((e) => selected.has(e)).length;
  if (!ticked) return 'none';
  return ticked === emails.length ? 'all' : 'some';
}

/** Ticking a team ticks all its members, or clears them when all are already ticked. */
export function toggleTeam(members: TeamMemberDto[], selected: ReadonlySet<string>): Set<string> {
  const next = new Set(selected);
  const clear = teamCheckState(members, selected) === 'all';
  for (const email of selectableEmails(members)) clear ? next.delete(email) : next.add(email);
  return next;
}

export function toggleMember(email: string, selected: ReadonlySet<string>): Set<string> {
  const next = new Set(selected);
  if (!next.delete(email)) next.add(email);
  return next;
}

/** Newly ticked addresses to add to To, and unticked ones to take out of it. */
export function selectionChanges(selected: ReadonlySet<string>, alreadyInTo: string[]): TeamInviteChanges {
  return {
    add: [...selected].filter((e) => !alreadyInTo.includes(e)),
    remove: alreadyInTo.filter((e) => !selected.has(e)),
  };
}
