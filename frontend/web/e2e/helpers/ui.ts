import type { Page } from '@playwright/test';

/** Signs in through the login page. */
export async function signIn(page: Page, creds: { email: string; password: string }): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Username or email').fill(creds.email);
  await page.getByLabel('Password', { exact: true }).fill(creds.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}
