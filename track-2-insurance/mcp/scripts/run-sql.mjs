// Admin helper: run a SQL file against MySQL as an administrator.
// Usage: node scripts/run-sql.mjs <file.sql> [--ping]
// Connection comes from environment variables, never from a file:
//   MYSQL_HOST (default 127.0.0.1), MYSQL_PORT (default 3306),
//   MYSQL_ADMIN_USER (default root), MYSQL_ADMIN_PASSWORD (required)
// ${NAME} placeholders in the SQL are replaced from the environment (for example ${MCP_DB_PASSWORD}).
// A placeholder with no value is an error, so a password is never silently empty.
import { readFileSync } from 'node:fs';
import mysql from 'mysql2/promise';

const args = process.argv.slice(2);
const ping = args.includes('--ping');
const file = args.find((a) => !a.startsWith('--'));

const password = process.env.MYSQL_ADMIN_PASSWORD;
if (password === undefined) {
  console.error('MYSQL_ADMIN_PASSWORD is not set.');
  process.exit(1);
}

const conn = await mysql.createConnection({
  host: process.env.MYSQL_HOST ?? '127.0.0.1',
  port: Number(process.env.MYSQL_PORT ?? 3306),
  user: process.env.MYSQL_ADMIN_USER ?? 'root',
  password,
  multipleStatements: true,
});

try {
  if (ping || !file) {
    const [rows] = await conn.query('SELECT VERSION() AS version, CURRENT_USER() AS who');
    console.log(JSON.stringify(rows[0]));
    if (!file) process.exit(0);
  }
  const sql = readFileSync(file, 'utf8').replace(/\$\{([A-Z0-9_]+)\}/g, (_m, name) => {
    const value = process.env[name];
    if (value === undefined) throw new Error(`Placeholder \${${name}} has no value in the environment.`);
    return value.replace(/\\/g, '\\\\').replace(/'/g, "''");
  });
  await conn.query(sql);
  console.log(`Ran ${file}`);
} finally {
  await conn.end();
}
