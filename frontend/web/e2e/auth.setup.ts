import fs from 'node:fs';
import { test as setup, expect } from './fixtures';
import { ADMIN, ADMIN_STATE, AUTH_DIR, PARTICIPANT, PARTICIPANT_STATE } from './env';
import { signIn } from './helpers/ui';

// Signs in once per role through the real login page and saves the session (kept in
// localStorage) for the role projects.
for (const [role, creds, file] of [
  ['admin', ADMIN, ADMIN_STATE],
  ['participant', PARTICIPANT, PARTICIPANT_STATE],
] as const) {
  setup(`sign in as ${role}`, async ({ page }) => {
    fs.mkdirSync(AUTH_DIR, { recursive: true });
    await signIn(page, creds);
    await expect(page).toHaveURL(/\/home$/);
    await page.context().storageState({ path: file });
  });
}
