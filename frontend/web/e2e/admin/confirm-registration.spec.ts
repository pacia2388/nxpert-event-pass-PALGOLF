import { test, expect } from '../fixtures';
import { createPublishedEvent, guestRegister } from '../helpers/data';
import { outboxFor } from '../helpers/outbox';

// Attendance toolbar buttons read "Confirm" / "Cancel", plus "(n)" while rows are selected.
const CONFIRM = /^Confirm( \(\d+\))?$/;
const CANCEL = /^Cancel( \(\d+\))?$/;

test('an admin confirms a pending guest registration and the pass is sent', async ({
  page,
  adminApi,
  publicApi,
}) => {
  const event = await createPublishedEvent(adminApi);
  const guest = await guestRegister(publicApi, event.publicSlug);

  await page.goto(`/admin/events/${event.id}`);
  await page.getByRole('tab', { name: 'Attendance (1)' }).click();

  const row = page.getByRole('row').filter({ hasText: guest.fullName });
  await expect(row.getByText('Pending')).toBeVisible();
  const confirm = page.getByRole('button', { name: CONFIRM });
  await expect(confirm).toBeDisabled();

  await row.getByRole('checkbox', { name: `Select ${guest.fullName}` }).check();
  await expect(confirm).toHaveAccessibleName('Confirm (1)');
  await confirm.click();

  await expect(row.getByText('Confirmed')).toBeVisible();
  await expect(page.getByText(/Confirmed 1/)).toBeVisible();
  // Email is off in the e2e API, so the pass lands in the outbox with its QR code.
  await expect.poll(() => outboxFor(guest.email).some((m) => m.hasPass)).toBe(true);
});

test('cancel is offered for pending registrations only', async ({ page, adminApi, publicApi }) => {
  const event = await createPublishedEvent(adminApi);
  const guest = await guestRegister(publicApi, event.publicSlug);

  await page.goto(`/admin/events/${event.id}`);
  await page.getByRole('tab', { name: 'Attendance (1)' }).click();
  const row = page.getByRole('row').filter({ hasText: guest.fullName });
  const checkbox = row.getByRole('checkbox', { name: `Select ${guest.fullName}` });
  const cancel = page.getByRole('button', { name: CANCEL });

  await checkbox.check();
  await expect(cancel).toBeEnabled();
  await page.getByRole('button', { name: CONFIRM }).click();
  await expect(row.getByText('Confirmed')).toBeVisible();

  // A confirmed registration can't be cancelled.
  await checkbox.check();
  await expect(cancel).toBeDisabled();
});
