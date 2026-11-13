import { test, expect } from '../fixtures';

test.describe('participant role routing', () => {
  for (const path of ['/admin/dashboard', '/admin/events', '/admin/users', '/merchant/scan']) {
    test(`${path} sends a participant back to Home`, async ({ page }) => {
      await page.goto(path);
      await expect(page).toHaveURL(/\/home$/);
    });
  }

  test('the login page sends a signed-in participant to Home', async ({ page }) => {
    await page.goto('/login');
    await expect(page).toHaveURL(/\/home$/);
  });
});
