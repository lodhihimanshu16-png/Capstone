// Logic for block-write-sql.sh (PreToolUse hook on the Bash tool).
// Reads the tool call as JSON on stdin. Exit code 2 blocks the call and prints the reason to stderr.
//
// It looks for a MySQL client (mysql, mysqldump, mysqlsh, mariadb) only where a command actually runs:
// at the start of a command, after `docker exec <container>`, `sudo`, `env VAR=x`, or inside `bash -c "..."`.
// Prose in a heredoc, a commit message or an echo that merely mentions "mysql" is not a command and is ignored.
// If a client is run, then write or DDL SQL anywhere in its arguments, or a password on its command line, is blocked.

const CLIENTS = new Set(['mysql', 'mysqldump', 'mysqlsh', 'mariadb']);
const WRITE_SQL =
  /\b(insert|update|delete|drop|alter|create|truncate|grant|revoke|replace|rename|load\s+data|into\s+outfile|into\s+dumpfile)\b/i;
const PASSWORD_ARG = /^(--password(=|$)|-p.)/;
const PREFIXES = new Set(['sudo', 'command', 'time', 'nohup', 'env', 'exec', 'nice']);
const DOCKER_FLAGS_WITH_VALUE = new Set(['-e', '-u', '-w', '-v', '-p', '--env', '--user', '--workdir', '--volume', '--name', '--network', '--entrypoint']);

function readStdin() {
  return new Promise((resolve) => {
    let s = '';
    process.stdin.on('data', (d) => (s += d)).on('end', () => resolve(s));
  });
}

function stripHeredocs(cmd) {
  return cmd.replace(/<<-?\s*(['"]?)([A-Za-z_][A-Za-z0-9_]*)\1[^\n]*\n[\s\S]*?\n[ \t]*\2[ \t]*(?=\n|$)/g, '');
}

// Split into commands, honouring single and double quotes. Operators outside quotes end a command.
function parse(cmd) {
  const commands = [];
  let words = [];
  let cur = '';
  let has = false;
  let quote = null;
  const endWord = () => {
    if (has) words.push(cur);
    cur = '';
    has = false;
  };
  const endCommand = () => {
    endWord();
    if (words.length) commands.push(words);
    words = [];
  };
  for (let i = 0; i < cmd.length; i++) {
    const ch = cmd[i];
    if (quote) {
      if (ch === quote) quote = null;
      else if (ch === '\\' && quote === '"' && i + 1 < cmd.length) cur += cmd[++i];
      else cur += ch;
      continue;
    }
    if (ch === "'" || ch === '"') {
      quote = ch;
      has = true;
    } else if (ch === '\\' && i + 1 < cmd.length) {
      cur += cmd[++i];
      has = true;
    } else if (ch === '\n' || ch === ';' || ch === '&' || ch === '|' || ch === '(' || ch === ')' || ch === '`') {
      endCommand();
    } else if (/\s/.test(ch)) {
      endWord();
    } else {
      cur += ch;
      has = true;
    }
  }
  endCommand();
  return commands;
}

const base = (w) => w.split(/[\\/]/).pop().replace(/\.exe$/i, '').toLowerCase();

function skipPrefixes(words) {
  let w = [...words];
  for (;;) {
    if (w.length === 0) return w;
    if (/^[A-Za-z_][A-Za-z0-9_]*=/.test(w[0])) {
      if (/^MYSQL_PWD=/i.test(w[0])) throw new Block('do not put a database password in the command line. Use an environment variable that is not committed.');
      w = w.slice(1);
    } else if (PREFIXES.has(base(w[0]))) {
      w = w.slice(1);
    } else if (base(w[0]) === 'docker' && ['exec', 'run'].includes(w[1])) {
      w = w.slice(2);
      while (w.length && w[0].startsWith('-')) w = w.slice(DOCKER_FLAGS_WITH_VALUE.has(w[0]) ? 2 : 1);
      w = w.slice(1); // container or image name
    } else {
      return w;
    }
  }
}

class Block extends Error {}

function check(cmd, depth = 0) {
  if (depth > 3) return;
  for (const raw of parse(stripHeredocs(cmd))) {
    const w = skipPrefixes(raw);
    if (w.length === 0) continue;
    const first = base(w[0]);
    if (['bash', 'sh', 'zsh'].includes(first)) {
      const i = w.indexOf('-c');
      if (i >= 0 && w[i + 1]) check(w[i + 1], depth + 1);
    }
    if (CLIENTS.has(first)) {
      const args = w.slice(1);
      if (args.some((a) => PASSWORD_ARG.test(a)))
        throw new Block('do not put a database password on the command line. Use an environment variable that is not committed.');
      if (WRITE_SQL.test(args.join(' ')))
        throw new Block('write or DDL SQL through the Bash tool. Use the db-analyst agent and the read-only MCP tools instead.');
    }
  }
}

const input = await readStdin();
let cmd = '';
try {
  cmd = String(JSON.parse(input).tool_input?.command ?? '');
} catch {
  cmd = input;
}
try {
  check(cmd);
} catch (e) {
  if (e instanceof Block) {
    console.error(`Blocked: ${e.message}`);
    process.exit(2);
  }
  throw e;
}
