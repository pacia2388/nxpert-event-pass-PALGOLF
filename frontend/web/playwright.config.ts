import { defineConfig, devices } from '@playwright/test';
import {
  ADMIN_STATE,
  API_PROJECT,
  API_URL,
  CONNECTION_STRING,
  PARTICIPANT_STATE,
  WEB_PORT,
  WEB_URL,
} from './e2e/env';

const CI = !!process.env['CI'];

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: CI,
  retries: CI ? 2 : 0,
  workers: CI ? 2 : undefined,
  reporter: CI ? [['github'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],
  globalTeardown: './e2e/global-teardown.ts',
  use: {
    baseURL: WEB_URL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    ...devices['Desktop Chrome'],
  },
  projects: [
    { name: 'setup', testMatch: /auth\.setup\.ts/ },
    {
      name: 'anonymous',
      testMatch: /anonymous\/.*\.spec\.ts/,
      dependencies: ['setup'],
    },
    {
      name: 'admin',
      testMatch: /admin\/.*\.spec\.ts/,
      dependencies: ['setup'],
      use: { storageState: ADMIN_STATE },
    },
    {
      name: 'participant',
      testMatch: /participant\/.*\.spec\.ts/,
      dependencies: ['setup'],
      use: { storageState: PARTICIPANT_STATE },
    },
  ],
  webServer: [
    {
      // Not Development: Program.cs loads appsettings.{env}.Local.json after env vars, so a local
      // Development override would win over the settings below.
      command: `dotnet run --project "${API_PROJECT}" --no-launch-profile --urls ${API_URL}`,
      // Any answer below 404 counts as ready; /api/auth/me answers 401 when signed out.
      url: `${API_URL}/api/auth/me`,
      timeout: 180_000,
      reuseExistingServer: false,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        ASPNETCORE_ENVIRONMENT: 'E2E',
        ConnectionStrings__Default: CONNECTION_STRING,
        Email__Enabled: 'false',
        Viber__Enabled: 'false',
        Sms__Enabled: 'false',
        App__FrontendBaseUrl: WEB_URL,
        App__RequireEmailVerification: 'false',
      },
    },
    {
      command: `npx ng serve --port ${WEB_PORT}`,
      url: WEB_URL,
      timeout: 180_000,
      // Never reuse: something on 4300 may be another worktree's e2e run.
      reuseExistingServer: false,
      stdout: 'ignore',
      stderr: 'pipe',
    },
  ],
});
