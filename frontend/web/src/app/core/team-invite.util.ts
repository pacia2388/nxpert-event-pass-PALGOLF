import { ALL_USERS, recipientAddress } from './invitation.util';
import { TeamMemberDto } from './models';

export type TeamCheckState = 'all' | 'some' | 'none';

/** A member's address as it appears in the To field, or null when they have no usable email. */
export function memberAddress(member: TeamMemberDto): string | null {
  const address = recipientAddress(member.email ?? '');
  return address && address !== ALL_USERS ? address : null;
}

/** Addresses of the members that can still be picked: a usable email that is not already in To. */
export function selectableEmails(members: TeamMemberDto[], alreadyAdded: string[]): string[] {
  const emails = members.map(memberAddress).filter((e): e is string => !!e && !alreadyAdded.includes(e));
  return [...new Set(emails)];
}

/** Team checkbox: checked, partial or unchecked. Members already in To count as done. */
export function teamCheckState(members: TeamMemberDto[], selected: ReadonlySet<string>, alreadyAdded: string[]): TeamCheckState {
  const emails = selectableEmails(members, alreadyAdded);
  const picked = emails.filter((e) => selected.has(e)).length;
  if (!picked) return 'none';
  return picked === emails.length ? 'all' : 'some';
}

/** Ticking a team picks all its members, or clears them when all are already picked. */
export function toggleTeam(members: TeamMemberDto[], selected: ReadonlySet<string>, alreadyAdded: string[]): Set<string> {
  const emails = selectableEmails(members, alreadyAdded);
  const next = new Set(selected);
  const clear = teamCheckState(members, selected, alreadyAdded) === 'all';
  for (const email of emails) clear ? next.delete(email) : next.add(email);
  return next;
}

export function toggleMember(email: string, selected: ReadonlySet<string>): Set<string> {
  const next = new Set(selected);
  if (!next.delete(email)) next.add(email);
  return next;
}
