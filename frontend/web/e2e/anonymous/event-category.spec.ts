import { test, expect } from '../fixtures';
import { createParticipant, createPublishedEvent } from '../helpers/data';
import { signIn } from '../helpers/ui';

// Senior events are for players 65 and older; Regular events are for players under 65.
// Registering for the other kind is allowed, after a warning.

test('a player under 65 is warned before registering for a Senior event', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Senior' });
  const player = await createParticipant(adminApi, '1990-01-31');
  await signIn(page, player);
  await expect(page).toHaveURL(/\/home$/);

  await page.goto(`/e/${event.publicSlug}`);
  await page.getByRole('button', { name: 'Register for this event' }).click();

  // The warning names the mismatch, with the safe choice focused.
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'This event is for seniors' })).toBeVisible();
  await expect(dialog.getByText('This event is for seniors (65 and older).')).toBeVisible();
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

test('a player 65 or older is warned before registering for a Regular event', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Regular' });
  const player = await createParticipant(adminApi, '1950-01-02');
  await signIn(page, player);
  await expect(page).toHaveURL(/\/home$/);

  await page.goto(`/e/${event.publicSlug}`);
  await page.getByRole('button', { name: 'Register for this event' }).click();

  await expect(page.getByRole('dialog').getByText('This event is for regular players (under 65).')).toBeVisible();
});

test('a player whose age matches registers without a warning', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Senior' });
  const player = await createParticipant(adminApi, '1950-01-02');
  await signIn(page, player);
  await expect(page).toHaveURL(/\/home$/);

  await page.goto(`/e/${event.publicSlug}`);
  await page.getByRole('button', { name: 'Register for this event' }).click();

  await expect(page.getByText('Your registration is pending confirmation.')).toBeVisible();
});
