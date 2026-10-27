import { z } from 'zod';
import { GuardError, guardReadonly } from './guard.js';
import { runSelect, writeAudit, MAX_ROWS, type QueryResult } from './db.js';

const date = z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Use a date like 2026-09-30.');

export interface ToolDef<S extends z.ZodRawShape = z.ZodRawShape> {
  name: string;
  description: string;
  input: S;
  run: (args: z.infer<z.ZodObject<S>>) => Promise<QueryResult>;
}

// The default period is the one the loss-ratio report uses.
const DEFAULT_FROM = '2025-10-01';
const DEFAULT_TO = '2026-09-30';

function nextDay(d: string): string {
  const next = new Date(`${d}T00:00:00Z`);
  next.setUTCDate(next.getUTCDate() + 1);
  return next.toISOString().slice(0, 10);
}

export const claimsOverAuthority: ToolDef<{ from: z.ZodOptional<z.ZodString>; to: z.ZodOptional<z.ZodString> }> = {
  name: 'claims_over_authority',
  description:
    'Approvals where the claim\'s approved amount is above the approval limit of the grade of the handler who approved it. ' +
    'Optional from/to (YYYY-MM-DD) filter on the approval date. Amount is the claim\'s current approved amount. ' +
    'Approvers who are not handlers (for example the Head of Claims) have no limit and are not listed. Scope: Midlands only.',
  input: { from: date.optional(), to: date.optional() },
  run: ({ from, to }) =>
    runSelect(
      `SELECT cl.claim_number, cl.product_code, cl.approved_amount, h.changed_at AS approved_at,
              h.changed_by_handler_id AS approver_handler_id, h.changed_by_grade AS approver_grade,
              h.changed_by_limit AS approver_limit, cl.approved_amount - h.changed_by_limit AS over_by
       FROM mcp_claim_history h
       JOIN mcp_claims cl ON cl.claim_id = h.claim_id
       WHERE h.to_status = 'Approved' AND cl.approved_amount > h.changed_by_limit
         AND h.changed_at >= ? AND h.changed_at < ?
       ORDER BY over_by DESC, cl.claim_number`,
      [from ?? '1970-01-01', nextDay(to ?? '2999-12-31')],
    ),
};

export const ageingByStage: ToolDef<{ as_of: z.ZodOptional<z.ZodString> }> = {
  name: 'ageing_by_stage',
  description:
    'For open claims (New, Assessing, Approved, Reopened): how many are in each stage and how many days they have been there ' +
    '(average and longest), counted from the latest entry into the current status. Optional as_of date (default today). Scope: Midlands only.',
  input: { as_of: date.optional() },
  run: ({ as_of }) => {
    const asOf = as_of ?? new Date().toISOString().slice(0, 10);
    return runSelect(
      `SELECT cl.status, COUNT(*) AS claims,
              ROUND(AVG(DATEDIFF(?, COALESCE(e.entered_at, cl.updated_at))), 1) AS avg_days,
              MAX(DATEDIFF(?, COALESCE(e.entered_at, cl.updated_at))) AS max_days
       FROM mcp_claims cl
       LEFT JOIN (SELECT claim_id, to_status, MAX(changed_at) AS entered_at
                  FROM mcp_claim_history GROUP BY claim_id, to_status) e
              ON e.claim_id = cl.claim_id AND e.to_status = cl.status
       WHERE cl.status IN ('New', 'Assessing', 'Approved', 'Reopened')
       GROUP BY cl.status
       ORDER BY FIELD(cl.status, 'New', 'Assessing', 'Approved', 'Reopened')`,
      [asOf, asOf],
    );
  },
};

export const lossRatioByProduct: ToolDef<{ from: z.ZodOptional<z.ZodString>; to: z.ZodOptional<z.ZodString> }> = {
  name: 'loss_ratio_by_product',
  description:
    'Loss ratio per product for a period (default 2025-10-01 to 2026-09-30). Written premium is policies starting in the period ' +
    '(not cancelled). Paid claims counts each claim once if it was settled in the period, so a claim that was settled, reopened ' +
    'and settled again is not counted twice. Scope: Midlands only.',
  input: { from: date.optional(), to: date.optional() },
  run: ({ from, to }) => {
    const start = from ?? DEFAULT_FROM;
    const endExclusive = nextDay(to ?? DEFAULT_TO);
    return runSelect(
      `SELECT pr.product_code, pr.written_premium,
              COALESCE(pd.paid, 0) AS paid_claims, COALESCE(pd.claims_paid, 0) AS claims_paid,
              ROUND(COALESCE(pd.paid, 0) / pr.written_premium * 100, 1) AS loss_ratio_pct
       FROM (SELECT product_code, SUM(annual_premium) AS written_premium
             FROM mcp_policies
             WHERE start_date >= ? AND start_date < ? AND status <> 'Cancelled'
             GROUP BY product_code) pr
       LEFT JOIN (SELECT cl.product_code, COUNT(*) AS claims_paid, SUM(COALESCE(cl.paid_amount, 0)) AS paid
                  FROM mcp_claims cl
                  WHERE cl.claim_id IN (SELECT claim_id FROM mcp_claim_history
                                        WHERE to_status = 'Settled' AND changed_at >= ? AND changed_at < ?)
                  GROUP BY cl.product_code) pd
              ON pd.product_code = pr.product_code
       ORDER BY pr.product_code`,
      [start, endExclusive, start, endExclusive],
    );
  },
};

export const reopenedClaims: ToolDef<{ min_times: z.ZodOptional<z.ZodNumber> }> = {
  name: 'reopened_claims',
  description:
    'Claims that have been reopened at least min_times (default 1), with the date and reason of the latest reopening. Scope: Midlands only.',
  input: { min_times: z.number().int().min(1).max(20).optional() },
  run: ({ min_times }) =>
    runSelect(
      `SELECT cl.claim_number, cl.product_code, cl.status, cl.reopened_count,
              (SELECT MAX(h.changed_at) FROM mcp_claim_history h
                WHERE h.claim_id = cl.claim_id AND h.to_status = 'Reopened') AS last_reopened_at,
              (SELECT h.reason FROM mcp_claim_history h
                WHERE h.claim_id = cl.claim_id AND h.to_status = 'Reopened'
                ORDER BY h.changed_at DESC, h.history_id DESC LIMIT 1) AS last_reason
       FROM mcp_claims cl
       WHERE cl.reopened_count >= ?
       ORDER BY cl.reopened_count DESC, last_reopened_at DESC, cl.claim_number`,
      [min_times ?? 1],
    ),
};

export const runReadonlyQuery: ToolDef<{ sql: z.ZodString }> = {
  name: 'run_readonly_query',
  description:
    `Run ONE read-only SELECT (or WITH ... SELECT) against the mcp_* views: mcp_claims, mcp_policies, mcp_policyholders, ` +
    `mcp_handlers, mcp_claim_history. No comments, no semicolons inside, no INTO, no variables. Personal data is masked. ` +
    `At most ${MAX_ROWS} rows are returned. Scope: Midlands only.`,
  input: { sql: z.string().min(1).max(4000) },
  run: ({ sql }) => runSelect(guardReadonly(sql)),
};

export const TOOLS = [claimsOverAuthority, ageingByStage, lossRatioByProduct, reopenedClaims, runReadonlyQuery] as const;

/** Run a tool call, record it in the audit log, and return the result or an error message. */
export async function callTool(
  tool: { name: string; run: (args: never) => Promise<QueryResult> },
  args: unknown,
): Promise<{ ok: true; result: QueryResult } | { ok: false; message: string }> {
  const started = performance.now();
  let outcome: 'ok' | 'refused' | 'error' = 'ok';
  let reason: string | undefined;
  let result: QueryResult | undefined;
  try {
    result = await tool.run(args as never);
  } catch (e) {
    outcome = e instanceof GuardError ? 'refused' : 'error';
    reason = e instanceof Error ? e.message : String(e);
  }
  try {
    await writeAudit({
      tool: tool.name,
      args,
      outcome,
      reason,
      rowCount: result?.rows.length,
      durationMs: performance.now() - started,
    });
  } catch (e) {
    // Fail closed: if the call cannot be recorded, it is not answered.
    return { ok: false, message: `Call not completed: the audit log could not be written (${e instanceof Error ? e.message : e}).` };
  }
  return result ? { ok: true, result } : { ok: false, message: reason ?? 'Unknown error.' };
}
