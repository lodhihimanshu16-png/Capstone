import { describe, expect, it } from 'vitest';
import { GuardError, guardReadonly } from '../src/guard.js';

const refused = (sql: unknown) => expect(() => guardReadonly(sql)).toThrow(GuardError);

describe('guard: what is allowed', () => {
  it('allows a plain SELECT', () => {
    expect(guardReadonly('SELECT claim_number FROM mcp_claims')).toBe('SELECT claim_number FROM mcp_claims');
  });
  it('allows one trailing semicolon and removes it', () => {
    expect(guardReadonly('SELECT 1;  ')).toBe('SELECT 1');
  });
  it('allows WITH ... SELECT', () => {
    guardReadonly('WITH x AS (SELECT status FROM mcp_claims) SELECT COUNT(*) FROM x');
  });
  it('allows column names that contain a forbidden word, such as updated_at', () => {
    guardReadonly('SELECT updated_at, reopened_count FROM mcp_claims ORDER BY updated_at DESC');
  });
  it('allows a forbidden word inside a string literal', () => {
    guardReadonly("SELECT claim_number FROM mcp_claims WHERE status = 'DROP; -- INTO OUTFILE'");
  });
});

describe('guard: writes are refused', () => {
  it.each([
    "INSERT INTO claims (id) VALUES (1)",
    "UPDATE mcp_claims SET status = 'Closed'",
    'DELETE FROM mcp_claims',
    "REPLACE INTO claims VALUES (1)",
    'SELECT * FROM mcp_claims FOR UPDATE',
    'SELECT * FROM mcp_claims LOCK IN SHARE MODE',
    "SELECT 1 INTO @x",
    'CALL some_procedure()',
    'SET GLOBAL max_execution_time = 0',
  ])('refuses %s', (sql) => refused(sql));
});

describe('guard: DDL is refused', () => {
  it.each([
    'CREATE TABLE t (id INT)',
    'DROP TABLE claims',
    'ALTER TABLE claims ADD x INT',
    'TRUNCATE TABLE claims',
    'RENAME TABLE claims TO c2',
    "GRANT ALL ON *.* TO 'x'@'%'",
    'SELECT 1 WHERE 1 = 1 UNION SELECT 1 FROM (DROP TABLE x) y',
  ])('refuses %s', (sql) => refused(sql));
});

describe('guard: more than one statement is refused', () => {
  it.each([
    'SELECT 1; SELECT 2',
    'SELECT 1; DROP TABLE claims',
    'SELECT 1;;',
    "SELECT 1; -- second statement follows\nSELECT 2",
  ])('refuses %s', (sql) => refused(sql));
});

describe('guard: INTO OUTFILE and friends are refused', () => {
  it.each([
    "SELECT * FROM mcp_claims INTO OUTFILE '/tmp/claims.txt'",
    "SELECT * FROM mcp_claims INTO DUMPFILE '/tmp/claims.bin'",
    "SELECT LOAD_FILE('/etc/passwd')",
    "select * from mcp_claims into/**/outfile '/tmp/x'",
  ])('refuses %s', (sql) => refused(sql));
});

describe('guard: other ways round a keyword list are refused', () => {
  it.each([
    'SELECT 1 /* hidden */ ',
    'SELECT 1 # comment',
    'SELECT 1 -- comment',
    'SELECT @@version',
    'SELECT SLEEP(10)',
    'SELECT BENCHMARK(100000000, MD5(1))',
    'SELECT * FROM information_schema.tables',
    'SELECT * FROM `information_schema`.`tables`',
    'SELECT * FROM mysql.user',
    'SHOW TABLES',
    'DESCRIBE claims',
    "EXPLAIN SELECT 1",
    "SELECT 'unterminated",
    '',
    '   ',
    'sElEcT 1 fRoM x WhErE 1; dRoP tAbLe x',
  ])('refuses %j', (sql) => refused(sql));

  it('refuses a query that is not a string', () => refused(undefined));
  it('refuses a very long query', () => refused('SELECT ' + '1,'.repeat(3000) + '1'));
});
