import type { Page } from '@playwright/test';
import { test, expect } from '../fixtures';

/** Whichever theme file appSettings.json names (a developer's local choice). */
const THEME_FILES = '**/theme.*.json';

/** Serves the shipped theme.<name>.json in place of the file appSettings.json names. */
async function useTheme(page: Page, name: 'palgolf' | 'nxpass') {
  await page.route(THEME_FILES, (route) =>
    route.continue({
      url: route
        .request()
        .url()
        .replace(/theme\.[a-z]+\.json/, `theme.${name}.json`),
    }),
  );
}

// The look comes from the theme file the web app's appSettings.json names, read from the web app
// itself, never from the API. The tests pick the file, so a local appSettings.json can't sway them.
test.describe('theme', () => {
  test('the PALGOLF theme file gives the login page its poster', async ({ page }) => {
    await useTheme(page, 'palgolf');

    await page.goto('/login');

    await expect(page.locator('.login-poster img')).toHaveAttribute(
      'src',
      'themes/palgolf/login-poster.jpg',
    );
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'palgolf');
  });

  test('an NXPass theme brings back the teal look, its logo and its name', async ({ page }) => {
    await useTheme(page, 'nxpass');

    await page.goto('/login');

    await expect(page.locator('.login-poster img')).toHaveAttribute(
      'src',
      'themes/nxpass/logo.png',
    );
    const html = page.locator('html');
    await expect(html).toHaveAttribute('data-theme', 'nxpass');
    await expect(html).toHaveAttribute('data-footer', 'glass');
    expect(
      await html.evaluate((el) => getComputedStyle(el).getPropertyValue('--pg-blue').trim()),
    ).toBe('#0d9488');
    await expect(page).toHaveTitle('NXPass');
    await expect(page.locator('link[rel="icon"][type="image/x-icon"]')).toHaveAttribute(
      'href',
      'themes/nxpass/favicon.ico',
    );
  });

  test('when the theme file cannot be loaded the app still starts in PALGOLF', async ({ page }) => {
    await page.route(THEME_FILES, (route) => route.fulfill({ status: 500 }));

    await page.goto('/login');

    await expect(page.getByLabel('Username or email')).toBeVisible();
    await expect(page.locator('.login-poster img')).toHaveAttribute(
      'src',
      'themes/palgolf/login-poster.jpg',
    );
  });

  test('a slow API never changes the look', async ({ page }) => {
    await useTheme(page, 'nxpass');
    await page.route('**/api/**', () => {});

    await page.goto('/login');

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'nxpass');
  });

  test('a returning visitor sees the last theme name in the tab before the theme loads', async ({
    page,
  }) => {
    await useTheme(page, 'nxpass');
    await page.goto('/login');
    await expect(page).toHaveTitle('NXPass');

    // The next load: the theme file is held back, so only the page itself can name the tab.
    await page.unroute(THEME_FILES);
    await page.route(THEME_FILES, () => {});
    await page.reload({ waitUntil: 'domcontentloaded' });

    await expect(page).toHaveTitle('NXPass', { timeout: 1000 });
    await expect(page.locator('link[rel="icon"][type="image/x-icon"]')).toHaveAttribute(
      'href',
      'themes/nxpass/favicon.ico',
    );
  });
});
