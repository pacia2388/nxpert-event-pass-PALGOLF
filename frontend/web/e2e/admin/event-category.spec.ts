import { test, expect } from '../fixtures';
import { createPublishedEvent } from '../helpers/data';

test('the events list shows a Senior event\'s category next to its status', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi, { category: 'Senior' });

  await page.goto('/admin/events');
  await page.getByLabel('Search events').fill(event.title);
  const card = page.getByRole('link').filter({ hasText: event.title });

  await expect(card).toContainText(/Open.*Senior \(65\+\)/);
});
