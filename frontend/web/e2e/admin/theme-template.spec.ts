import { readFile } from 'node:fs/promises';
import { test, expect } from '../fixtures';

test('an admin sees the theme in use and downloads the theme.json template', async ({ page }) => {
  await page.goto('/admin/settings');

  const section = page.getByTestId('theme-section');
  await expect(section).toContainText('PALGOLF');
  await expect(section).toContainText('default');

  const [download] = await Promise.all([
    page.waitForEvent('download'),
    section.getByRole('button', { name: 'Download theme template' }).click(),
  ]);

  expect(download.suggestedFilename()).toBe('theme-template.json');
  const template = JSON.parse(await readFile((await download.path())!, 'utf-8'));
  expect(template.base).toBe('PALGOLF');
  expect(template.version).toBe(1);
  expect(template.colors.primary).toBe('#0f5fb8');
  expect(template.logos.loginImage).toBe('brand/pal-interclub-2027-poster.jpg');
});
