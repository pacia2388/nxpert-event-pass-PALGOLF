import { test, expect } from '../fixtures';
import { createPublishedEvent } from '../helpers/data';

test('a closed event sits under the Closed tab and can only be viewed', async ({ page, adminApi }) => {
  const event = await createPublishedEvent(adminApi);
  expect((await adminApi.post(`/api/events/${event.id}/close`)).ok()).toBeTruthy();

  await page.goto('/events');
  await page.getByLabel('Search events').fill(event.title);
  const card = page.locator('article').filter({ hasText: event.title });

  await page.getByRole('group', { name: 'Filter events' }).getByRole('button', { name: 'Open' }).click();
  await expect(card).toHaveCount(0);

  await page.getByRole('group', { name: 'Filter events' }).getByRole('button', { name: 'Closed' }).click();
  await expect(page).toHaveURL(/[?&]tab=Closed/);
  await expect(card).toContainText('Closed');
  await expect(card.getByRole('link', { name: 'View' })).toBeVisible();
  await expect(card.getByRole('link', { name: 'Register' })).toHaveCount(0);
});

// Layout rule from the user: tabs to the right of the search on wide screens, below it on phones.
for (const { width, sameRow } of [
  { width: 1600, sameRow: true },
  { width: 375, sameRow: false },
]) {
  test(`at ${width}px the filter tabs sit ${sameRow ? 'right of' : 'below'} the search`, async ({
    page,
    adminApi,
  }) => {
    await createPublishedEvent(adminApi);
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/events');

    const search = (await page.locator('.search-field').boundingBox())!;
    const tabs = (await page.getByRole('group', { name: 'Filter events' }).boundingBox())!;

    if (sameRow) {
      expect(tabs.x).toBeGreaterThanOrEqual(search.x + search.width);
      expect(tabs.y).toBeLessThan(search.y + search.height);
    } else {
      expect(tabs.y).toBeGreaterThanOrEqual(search.y + search.height);
    }
  });
}
