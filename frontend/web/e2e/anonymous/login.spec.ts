import { test, expect } from '../fixtures';
import { ADMIN, PARTICIPANT } from '../env';
import { signIn } from '../helpers/ui';

test.describe('sign-in', () => {
  test('an admin lands on Home with the admin menu', async ({ page }) => {
    await signIn(page, ADMIN);
    await expect(page).toHaveURL(/\/home$/);
    await page.goto('/admin/events');
    await expect(page).toHaveURL(/\/admin\/events$/);
  });

  test('a participant lands on Home', async ({ page }) => {
    await signIn(page, PARTICIPANT);
    await expect(page).toHaveURL(/\/home$/);
  });

  test('a wrong password shows a generic error and stays on the login page', async ({ page }) => {
    await signIn(page, { email: ADMIN.email, password: 'not-the-password1' });
    await expect(page.getByText('Invalid username/email or password.')).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);
  });

  test('an unknown user gets the same generic error', async ({ page }) => {
    await signIn(page, { email: 'nobody@example.com', password: 'Whatever123' });
    await expect(page.getByText('Invalid username/email or password.')).toBeVisible();
  });
});

test.describe('guards when signed out', () => {
  for (const path of ['/admin/events', '/my-events', '/events']) {
    test(`${path} redirects to the login page`, async ({ page }) => {
      await page.goto(path);
      await expect(page).toHaveURL(new RegExp(`/login\\?returnUrl=${encodeURIComponent(path)}$`));
    });
  }

  test('signing in after a redirect returns to the page asked for', async ({ page }) => {
    await page.goto('/my-events');
    await page.getByLabel('Username or email').fill(PARTICIPANT.email);
    await page.getByLabel('Password', { exact: true }).fill(PARTICIPANT.password);
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/my-events$/);
  });
});
