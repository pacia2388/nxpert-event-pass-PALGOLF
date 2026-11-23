import { test, expect } from '../fixtures';

// Serve this installation's menu.json the way the App site would, before the app boots.
async function useMenus(page: import('@playwright/test').Page, menus: Record<string, boolean>) {
  await page.route('**/menu.json', (route) => route.fulfill({ json: { menus } }));
}

const card = (page: import('@playwright/test').Page, title: string) =>
  page.locator('.module-card').filter({ hasText: title });

test('the shipped menu.json shows the usual Home cards', async ({ page }) => {
  await page.goto('/home');
  await expect(card(page, 'Dashboard')).toBeVisible();
  await expect(card(page, 'Users')).toBeVisible();
  await expect(card(page, 'My Events')).toBeVisible();
});

test('menus switched off lose their Home card and their page', async ({ page }) => {
  await useMenus(page, { dashboard: false, users: false });

  await page.goto('/home');
  await expect(card(page, 'Events').first()).toBeVisible();
  await expect(card(page, 'Dashboard')).toHaveCount(0);
  await expect(card(page, 'Users')).toHaveCount(0);

  await page.goto('/admin/dashboard');
  await expect(page).toHaveURL(/\/home$/);
  await page.goto('/admin/users');
  await expect(page).toHaveURL(/\/home$/);
});

test('event menus stay on even when menu.json lists them as off', async ({ page }) => {
  await useMenus(page, { browseEvents: false, events: false, myEvents: false });

  await page.goto('/events');
  await expect(page).toHaveURL(/\/events$/);
  await page.goto('/admin/events');
  await expect(page).toHaveURL(/\/admin\/events$/);
  await page.goto('/my-events');
  await expect(page).toHaveURL(/\/my-events$/);
});
