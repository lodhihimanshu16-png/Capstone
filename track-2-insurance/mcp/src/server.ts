import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { TOOLS, callTool } from './tools.js';

const server = new McpServer({ name: 'harbourline-db', version: '0.1.0' });

// The tools share one handler shape, so register them through a loosely typed wrapper.
const register = server.registerTool.bind(server) as unknown as (
  name: string,
  config: { description: string; inputSchema: unknown },
  handler: (args: unknown) => Promise<unknown>,
) => void;

for (const tool of TOOLS) {
  register(tool.name, { description: tool.description, inputSchema: tool.input }, async (args) => {
    const outcome = await callTool(tool as never, args);
    if (!outcome.ok) return { isError: true, content: [{ type: 'text', text: outcome.message }] };
    const { rows, truncated } = outcome.result;
    return { content: [{ type: 'text', text: JSON.stringify({ rowCount: rows.length, truncated, rows }, null, 2) }] };
  });
}

await server.connect(new StdioServerTransport());
