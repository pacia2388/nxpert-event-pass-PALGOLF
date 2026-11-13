import { execFileSync } from 'node:child_process';
import { DATABASE, SQL_SERVER } from './env';

// Drops the run's throwaway database. ROLLBACK IMMEDIATE closes the API's open connections first.
export default function globalTeardown(): void {
  const sql =
    `IF DB_ID(N'${DATABASE}') IS NOT NULL BEGIN ` +
    `ALTER DATABASE [${DATABASE}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ` +
    `DROP DATABASE [${DATABASE}]; END`;
  try {
    execFileSync('sqlcmd', ['-S', SQL_SERVER, '-E', '-b', '-Q', sql], { stdio: 'pipe' });
  } catch (err) {
    console.warn(`Could not drop e2e database ${DATABASE} (is sqlcmd installed?): ${String(err)}`);
  }
}
