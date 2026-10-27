-- 02-mcp-views.sql
-- Read-only views for the MCP server. Run BEFORE 03-create-mcp-user.sql, as a database administrator.
-- Safe to run more than once.
--
-- Design:
--   * The MCP user will get SELECT on these views only, never on base tables.
--   * Personal data is masked or left out here, in the view, not in the server.
--   * Every view is scoped to ONE region: Midlands (regions.id = 2, code MID). The scope is
--     applied inside each view, so a query cannot reach another region.
--   * The views run with the definer's rights (SQL SECURITY DEFINER), so the MCP user needs no
--     rights on the tables underneath. `users` (emails, password hashes) is not used by any view.
--   * mcp_audit_log is a TABLE, not a view. The MCP user can only INSERT into it.

USE harbourline;

CREATE TABLE IF NOT EXISTS mcp_audit_log (
  id          BIGINT       NOT NULL AUTO_INCREMENT,
  called_at   DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  db_user     VARCHAR(100) NOT NULL DEFAULT (CURRENT_USER()),
  tool_name   VARCHAR(60)  NOT NULL,
  arguments   TEXT         NULL,
  outcome     VARCHAR(10)  NOT NULL,   -- ok | refused | error
  reason      VARCHAR(300) NULL,       -- why a call was refused or failed
  row_count   INT          NULL,
  duration_ms INT          NULL,
  PRIMARY KEY (id),
  KEY ix_mcp_audit_called_at (called_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Claims. Left out: description and external_ref (free text that may contain personal data).
CREATE OR REPLACE ALGORITHM=UNDEFINED SQL SECURITY DEFINER VIEW mcp_claims AS
SELECT c.id               AS claim_id,
       c.claim_number,
       c.policy_id,
       pr.code            AS product_code,
       c.handler_id,
       c.claim_type,
       c.status,
       c.loss_date,
       c.reported_date,
       c.estimated_amount,
       c.approved_amount,
       c.paid_amount,
       c.excess,
       r.code             AS region_code,
       c.reopened_count,
       c.closed_date,
       c.created_at,
       c.updated_at
FROM claims c
JOIN policies p  ON p.id  = c.policy_id
JOIN products pr ON pr.id = p.product_id
JOIN regions r   ON r.id  = c.region_id
WHERE c.region_id = 2;

-- Policies. The policyholder is a reference (PH-00012), never a name.
CREATE OR REPLACE ALGORITHM=UNDEFINED SQL SECURITY DEFINER VIEW mcp_policies AS
SELECT p.id               AS policy_id,
       p.policy_number,
       CONCAT('PH-', LPAD(p.policyholder_id, 5, '0')) AS holder_ref,
       pr.code            AS product_code,
       pr.name            AS product_name,
       pr.line            AS product_line,
       p.start_date,
       p.end_date,
       p.annual_premium,
       p.sum_insured,
       p.status,
       r.code             AS region_code,
       p.no_claims_years
FROM policies p
JOIN products pr ON pr.id = p.product_id
JOIN regions r   ON r.id  = p.region_id
WHERE p.region_id = 2;

-- Policyholders, masked. Left out: address, date of birth (an age band is shown instead).
CREATE OR REPLACE ALGORITHM=UNDEFINED SQL SECURITY DEFINER VIEW mcp_policyholders AS
SELECT CONCAT('PH-', LPAD(h.id, 5, '0')) AS holder_ref,
       CONCAT(LEFT(h.full_name, 1), '*** ', LEFT(SUBSTRING_INDEX(h.full_name, ' ', -1), 1), '***') AS full_name_masked,
       CONCAT(LEFT(h.email, 1), '***@', SUBSTRING_INDEX(h.email, '@', -1))                          AS email_masked,
       CASE WHEN h.phone IS NULL THEN NULL
            ELSE CONCAT(REPEAT('*', GREATEST(CHAR_LENGTH(h.phone) - 3, 0)), RIGHT(h.phone, 3)) END  AS phone_masked,
       SUBSTRING_INDEX(h.postcode, ' ', 1)                                                           AS postcode_area,
       CONCAT(FLOOR(TIMESTAMPDIFF(YEAR, h.date_of_birth, CURDATE()) / 10) * 10, 's')                AS age_band,
       r.code AS region_code
FROM policyholders h
JOIN regions r ON r.id = h.region_id
WHERE h.region_id = 2;

-- Handlers. Left out: email and user id.
CREATE OR REPLACE ALGORITHM=UNDEFINED SQL SECURITY DEFINER VIEW mcp_handlers AS
SELECT hd.id              AS handler_id,
       hd.name,
       g.code             AS grade_code,
       g.name             AS grade_name,
       g.approval_limit,
       r.code             AS region_code,
       hd.max_open_claims,
       hd.active
FROM handlers hd
JOIN handler_grades g ON g.id = hd.grade_id
JOIN regions r        ON r.id = hd.region_id
WHERE hd.region_id = 2;

-- Status history of the in-scope claims. The user who made a change is shown as the handler,
-- with that handler's grade and limit, so approvals can be checked without access to `users`.
CREATE OR REPLACE ALGORITHM=UNDEFINED SQL SECURITY DEFINER VIEW mcp_claim_history AS
SELECT h.id               AS history_id,
       h.claim_id,
       h.from_status,
       h.to_status,
       h.changed_at,
       h.reason,
       hd.id              AS changed_by_handler_id,
       g.code             AS changed_by_grade,
       g.approval_limit   AS changed_by_limit
FROM claim_status_history h
JOIN claims c              ON c.id = h.claim_id
LEFT JOIN handlers hd      ON hd.user_id = h.changed_by_user_id
LEFT JOIN handler_grades g ON g.id = hd.grade_id
WHERE c.region_id = 2;
