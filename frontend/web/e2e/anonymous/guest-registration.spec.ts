import { test, expect } from '../fixtures';
import { createPublishedEvent, newGuest, type CreatedEvent } from '../helpers/data';
import { outboxFor } from '../helpers/outbox';

test.describe('guest registration on the public event page', () => {
  let event: CreatedEvent;

  test.beforeEach(async ({ adminApi, page }) => {
    event = await createPublishedEvent(adminApi);
    await page.goto(`/e/${event.publicSlug}`);
    await expect(page.getByRole('heading', { name: 'Register to attend' })).toBeVisible();
  });

  test('errors show only after a field is touched', async ({ page }) => {
    const submit = page.getByRole('button', { name: 'Register to attend' });
    await expect(submit).toBeDisabled();
    await expect(page.getByText('Please enter your name.')).toBeHidden();
    await expect(page.getByText('Please enter your ID No.')).toBeHidden();

    // Touch Full name and leave it empty: only that field complains.
    await page.getByLabel('Full name').focus();
    await page.getByLabel('ID No.').focus();
    await expect(page.getByText('Please enter your name.')).toBeVisible();
    await expect(page.getByText('Please enter your ID No.')).toBeHidden();
  });

  test('a malformed email and mobile number are flagged after leaving the field', async ({ page }) => {
    await page.getByLabel('Email').fill('not-an-email');
    await page.getByLabel('Mobile number').fill('12345');
    await page.getByLabel('Full name').focus();
    await expect(page.getByText('Enter a valid email address.')).toBeVisible();
    await expect(page.getByText('Enter a valid PH mobile number: +63 9XX XXX XXXX.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Register to attend' })).toBeDisabled();
  });

  test('a valid registration is received as pending and no pass is sent', async ({ page }) => {
    const guest = newGuest();
    await page.getByLabel('Full name').fill(guest.fullName);
    await page.getByLabel('ID No.').fill(guest.senateNo);
    await page.getByLabel('Email').fill(guest.email);
    await page.getByLabel('Mobile number').fill(guest.phone);
    await page.getByRole('button', { name: 'Register to attend' }).click();

    await expect(page.getByRole('heading', { name: 'Registration received!' })).toBeVisible();
    await expect(page.getByText('Your registration is pending confirmation.')).toBeVisible();
    // A pending registration gets no QR pass until an organizer confirms it.
    expect(outboxFor(guest.email).filter((m) => m.hasPass)).toEqual([]);
  });
});
