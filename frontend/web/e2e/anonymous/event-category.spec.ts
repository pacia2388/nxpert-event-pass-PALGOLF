import { test, expect } from '../fixtures';
import { createParticipant, createPublishedEvent, createTeamMember } from '../helpers/data';
import { signIn } from '../helpers/ui';

// An event's category (Senior / Regular) is compared with the player's team category.
// Registering for the other category is allowed, after a warning.

// Serial: concurrent admin team-member adds can fail with a 500 (serializable transaction).
test.describe.configure({ mode: 'serial' });

test('a Regular-team player is warned before registering for a Senior event', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Senior' });
  const player = await createTeamMember(adminApi, 'Regular');
  await signIn(page, player);
  await expect(page).toHaveURL(/\/home$/);

  await page.goto(`/e/${event.publicSlug}`);
  await page.getByRole('button', { name: 'Register for this event' }).click();

  // The warning names both categories, with the safe choice focused.
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Different category' })).toBeVisible();
  await expect(
    dialog.getByText('This is a Senior event, but your team is in the Regular category.'),
  ).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Cancel' })).toBeFocused();

  // Cancel leaves them unregistered.
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText('Your registration is pending confirmation.')).toBeHidden();

  // Register anyway goes ahead.
  await page.getByRole('button', { name: 'Register for this event' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Register anyway' }).click();
  await expect(page.getByText('Your registration is pending confirmation.')).toBeVisible();
});

test('a Senior-team player is warned before registering for a Regular event', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Regular' });
  const player = await createTeamMember(adminApi, 'Senior');
  await signIn(page, player);
  await expect(page).toHaveURL(/\/home$/);

  await page.goto(`/e/${event.publicSlug}`);
  await page.getByRole('button', { name: 'Register for this event' }).click();

  await expect(
    page.getByRole('dialog').getByText('This is a Regular event, but your team is in the Senior category.'),
  ).toBeVisible();
});

test('a player whose team category matches registers without a warning', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Senior' });
  const player = await createTeamMember(adminApi, 'Senior');
  await signIn(page, player);
  await expect(page).toHaveURL(/\/home$/);

  await page.goto(`/e/${event.publicSlug}`);
  await page.getByRole('button', { name: 'Register for this event' }).click();

  await expect(page.getByText('Your registration is pending confirmation.')).toBeVisible();
});

test('a player who is not on a team registers without a warning', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Senior' });
  const player = await createParticipant(adminApi);
  await signIn(page, player);
  await expect(page).toHaveURL(/\/home$/);

  await page.goto(`/e/${event.publicSlug}`);
  await page.getByRole('button', { name: 'Register for this event' }).click();

  await expect(page.getByText('Your registration is pending confirmation.')).toBeVisible();
});
