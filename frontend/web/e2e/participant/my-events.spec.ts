import { test, expect } from '../fixtures';
import { createPublishedEvent } from '../helpers/data';

test('a participant registers from Browse Events, sees it pending, then cancels it', async ({
  page,
  adminApi,
}) => {
  const event = await createPublishedEvent(adminApi);

  // Browse Events → Register opens the event page with the account's details filled in.
  await page.goto('/events');
  await page.getByLabel('Search events').fill(event.title);
  const card = page.locator('article').filter({ hasText: event.title });
  await card.getByRole('link', { name: 'Register' }).click();
  await expect(page).toHaveURL(new RegExp(`/e/${event.publicSlug}$`));
  await page.getByRole('button', { name: 'Register for this event' }).click();
  await expect(page.getByText('Your registration is pending confirmation.')).toBeVisible();

  // My Events lists it as pending, with no pass yet.
  await page.goto('/my-events');
  const reg = page.locator('article').filter({ hasText: event.title });
  await expect(reg.getByText('Pending confirmation')).toBeVisible();
  await expect(reg.getByRole('button', { name: 'Show pass' })).toBeDisabled();

  // Cancelling asks first, with the safe choice focused.
  await reg.getByRole('button', { name: 'More actions' }).click();
  await page.getByRole('menuitem', { name: 'Cancel registration' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Cancel registration?' })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Keep it' })).toBeFocused();

  // Keep it leaves the registration alone.
  await dialog.getByRole('button', { name: 'Keep it' }).click();
  await expect(dialog).toBeHidden();
  await expect(reg).toBeVisible();

  await reg.getByRole('button', { name: 'More actions' }).click();
  await page.getByRole('menuitem', { name: 'Cancel registration' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel registration' }).click();
  await expect(page.getByText('Registration cancelled.')).toBeVisible();
  await expect(reg).toHaveCount(0);

  // The event can be registered for again.
  await page.goto('/events');
  await page.getByLabel('Search events').fill(event.title);
  await expect(card.getByRole('link', { name: 'Register' })).toBeVisible();
});
