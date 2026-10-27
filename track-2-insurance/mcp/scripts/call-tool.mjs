// Call one tool on the built MCP server over stdio, the way Claude Code does.
// Usage: node scripts/call-tool.mjs <tool_name> '<json arguments>'
// Needs `npm run build` first and HARBOURLINE_MCP_DSN in the environment.
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const [name, argText] = process.argv.slice(2);
if (!name) {
  console.error("Usage: node scripts/call-tool.mjs <tool_name> '<json arguments>'");
  process.exit(1);
}

const transport = new StdioClientTransport({
  command: process.execPath,
  args: [new URL('../dist/server.js', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')],
  env: { ...process.env },
});
const client = new Client({ name: 'call-tool', version: '0.1.0' });
await client.connect(transport);
try {
  if (name === '--list') {
    console.log((await client.listTools()).tools.map((t) => t.name).join('\n'));
  } else {
    const result = await client.callTool({ name, arguments: argText ? JSON.parse(argText) : {} });
    if (result.isError) console.log('ERROR:', result.content.map((c) => c.text).join('\n'));
    else console.log(result.content.map((c) => c.text).join('\n'));
  }
} finally {
  await client.close();
}
