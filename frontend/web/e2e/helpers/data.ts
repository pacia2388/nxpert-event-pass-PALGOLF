import { expect, type APIRequestContext } from '@playwright/test';

export interface CreatedEvent {
  id: number;
  title: string;
  publicSlug: string;
}

let counter = 0;
/** A short value unique within the run, for titles, emails and ID numbers. */
export function unique(prefix: string): string {
  counter += 1;
  return `${prefix}-${Date.now().toString(36)}-${process.pid}-${counter}`;
}

export function uniqueEmail(): string {
  return `${unique('e2e')}@example.com`.toLowerCase();
}

/** Creates a published, public in-person event with no sub-events or custom fields. */
export async function createPublishedEvent(
  adminApi: APIRequestContext,
  overrides: Partial<{ title: string; capacity: number | null; category: 'Senior' | 'Regular' | null }> = {},
): Promise<CreatedEvent> {
  const startsAt = new Date(Date.now() + 7 * 24 * 3600_000);
  startsAt.setUTCHours(1, 0, 0, 0);
  const endsAt = new Date(startsAt.getTime() + 8 * 3600_000);
  const title = overrides.title ?? unique('E2E Event');
  const res = await adminApi.post('/api/events', {
    data: {
      title,
      description: 'Created by the e2e suite.',
      startsAt: startsAt.toISOString(),
      endsAt: endsAt.toISOString(),
      venue: 'E2E Hall',
      isPublicLinkEnabled: true,
      status: 'Published',
      capacity: overrides.capacity ?? null,
      formFields: [],
      bannerImage: null,
      welcomeMessage: null,
      sessions: [],
      registrationFees: null,
      schedule: [],
      category: overrides.category ?? null,
    },
  });
  expect(res.ok(), `create event: ${res.status()} ${await res.text()}`).toBeTruthy();
  const ev = (await res.json()) as CreatedEvent;
  return { id: ev.id, title: ev.title, publicSlug: ev.publicSlug };
}

export interface Guest {
  fullName: string;
  email: string;
  senateNo: string;
  phone: string;
}

export function newGuest(): Guest {
  const id = unique('G');
  return {
    fullName: `Guest ${id}`,
    email: uniqueEmail(),
    senateNo: id,
    phone: '+63 917 000 0000',
  };
}

/** Registers a guest through the public endpoint the /e/:slug page uses. */
export async function guestRegister(
  publicApi: APIRequestContext,
  slug: string,
  guest: Guest = newGuest(),
): Promise<Guest> {
  const res = await publicApi.post(`/api/registrations/guest/${slug}`, {
    data: { ...guest, notificationChannel: 'Email', sessionIds: [], responses: {}, face: null },
  });
  expect(res.ok(), `guest register: ${res.status()} ${await res.text()}`).toBeTruthy();
  return guest;
}

export interface CreatedUser {
  email: string;
  password: string;
}

/** Creates a participant account with the given birthdate (yyyy-MM-dd) through the admin Users API. */
export async function createParticipant(adminApi: APIRequestContext, birthdate: string): Promise<CreatedUser> {
  const email = uniqueEmail();
  const password = 'Passw0rd!';
  const res = await adminApi.post('/api/users', {
    data: { firstName: 'Pat', lastName: unique('Cruz'), email, password, role: 'Participant', birthdate },
  });
  expect(res.ok(), `create user: ${res.status()} ${await res.text()}`).toBeTruthy();
  return { email, password };
}
