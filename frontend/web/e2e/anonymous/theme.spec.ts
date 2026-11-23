import { test, expect } from '../fixtures';

// The look comes from GET /api/theme (theme.json beside the API). These tests stand in for the
// server's file by answering that request, so they never change the shared e2e API.
test.describe('theme', () => {
  test('with no theme.json the login page wears PALGOLF and its poster', async ({ page }) => {
    await page.goto('/login');

    await expect(page.locator('.login-poster img')).toHaveAttribute(
      'src',
      'brand/pal-interclub-2027-poster.jpg',
    );
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'palgolf');
  });

  test('an NXPass theme brings back the teal look, its logo and its name', async ({
    page,
    publicApi,
  }) => {
    const palgolf = await (await publicApi.get('/api/theme')).json();
    const nxpass = {
      ...palgolf,
      name: 'NXPass',
      source: 'file',
      appName: 'NXPass',
      colors: { ...palgolf.colors, primary: '#0d9488', browserBar: '#0d9488' },
      layout: { buttons: 'gradient', footer: 'glass', authBackground: 'glow' },
      logos: {
        ...palgolf.logos,
        favicon: 'themes/nxpass/favicon.ico',
        loginImage: 'nxpax-logo.png',
      },
    };
    await page.route('**/api/theme', (route) => route.fulfill({ json: nxpass }));

    await page.goto('/login');

    await expect(page.locator('.login-poster img')).toHaveAttribute('src', 'nxpax-logo.png');
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

  test('when the theme cannot be loaded the app still starts in PALGOLF', async ({ page }) => {
    await page.route('**/api/theme', (route) => route.fulfill({ status: 500 }));

    await page.goto('/login');

    await expect(page.getByLabel('Username or email')).toBeVisible();
    await expect(page.locator('.login-poster img')).toHaveAttribute(
      'src',
      'brand/pal-interclub-2027-poster.jpg',
    );
  });
});
