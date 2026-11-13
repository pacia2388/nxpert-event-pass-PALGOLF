// Settings shared by playwright.config.ts, the fixtures and the global teardown.
//
// The suite starts its own API (port 5188) and web app (port 4300) against a throwaway database,
// so it never touches the dev servers (5088 / 4200) or the dev database.

import path from 'node:path';

export const API_PORT = 5188;
export const WEB_PORT = 4300;
export const API_URL = `http://localhost:${API_PORT}`;
export const WEB_URL = `http://localhost:${WEB_PORT}`;

// One id per run. The config is loaded first in the runner process, which sets this before any
// worker starts, so every worker sees the same value.
process.env['E2E_RUN_ID'] ??= Date.now().toString();
export const RUN_ID = process.env['E2E_RUN_ID'];

// LocalDB by default; set EVENTPASS_E2E_SQLSERVER to use another SQL Server (Windows auth).
export const SQL_SERVER = process.env['EVENTPASS_E2E_SQLSERVER'] ?? '(localdb)\\MSSQLLocalDB';
export const DATABASE = `EventPassE2E_${RUN_ID}`;
export const CONNECTION_STRING =
  `Server=${SQL_SERVER};Database=${DATABASE};Integrated Security=True;TrustServerCertificate=True;`;

export const REPO_ROOT = path.resolve(__dirname, '..', '..', '..');
export const API_PROJECT = path.join(REPO_ROOT, 'backend', 'EventRegistration.Api');
// With email off the API writes every message here (EmailService), including QR passes.
export const OUTBOX_DIR = path.join(API_PROJECT, 'outbox');

// Seeded by DbSeeder on first start.
export const ADMIN = { email: 'admin@demo.com', password: 'Admin123!' };
export const PARTICIPANT = { email: 'alice@example.com', password: 'Passw0rd!' };

export const AUTH_DIR = path.join(__dirname, '.auth');
export const ADMIN_STATE = path.join(AUTH_DIR, 'admin.json');
export const PARTICIPANT_STATE = path.join(AUTH_DIR, 'participant.json');
