import { test, expect } from '../fixtures';
import { createPublishedEvent } from '../helpers/data';

test('the events list shows a Senior event\'s category next to its status', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Senior' });

  await page.goto('/admin/events');
  await page.getByLabel('Search events').fill(event.title);
  const card = page.getByRole('link').filter({ hasText: event.title });

  await expect(card).toContainText(/Open.*Senior \(65\+\)/);
});

test('an admin changes an event\'s category on the event form', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Senior' });

  await page.goto(`/admin/events/${event.id}/edit`);
  const category = page.getByRole('radiogroup', { name: 'Event category' });
  await expect(category.getByRole('radio', { name: 'Senior (65+)' })).toBeChecked();

  await category.getByRole('radio', { name: 'Regular' }).click();
  await page.getByRole('button', { name: 'Save changes' }).click();

  await expect
    .poll(async () => (await (await adminApi.get(`/api/events/${event.id}`)).json()).category)
    .toBe('Regular');
});
