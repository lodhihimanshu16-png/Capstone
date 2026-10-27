-- V006: claims workflow (spec docs/specs/claims-workflow.md section 4.7).
-- Adds assessor/approver/settlement/version to claims, an amount on the history,
-- a table for refused approvals, two indexes, and backfills legacy data.
-- Every new NOT NULL column has a default, so this is safe on a populated database.

ALTER TABLE claims
  ADD COLUMN assessed_by_user_id INT NULL,
  ADD COLUMN approved_by_user_id INT NULL,
  ADD COLUMN settlement_amount DECIMAL(12,2) NULL,
  ADD COLUMN row_version INT NOT NULL DEFAULT 0;

ALTER TABLE claims
  ADD KEY ix_claims_handler_status (handler_id, status),
  ADD KEY ix_claims_assessed_by (assessed_by_user_id),
  ADD KEY ix_claims_approved_by (approved_by_user_id);

ALTER TABLE claims
  ADD CONSTRAINT fk_claims_assessed_by FOREIGN KEY (assessed_by_user_id) REFERENCES users (id),
  ADD CONSTRAINT fk_claims_approved_by FOREIGN KEY (approved_by_user_id) REFERENCES users (id);

ALTER TABLE claim_status_history
  ADD COLUMN amount DECIMAL(12,2) NULL,
  ADD KEY ix_claim_history_claim_to_changed (claim_id, to_status, changed_at);

CREATE TABLE claim_approval_attempts (
  id INT NOT NULL AUTO_INCREMENT,
  claim_id INT NOT NULL,
  user_id INT NOT NULL,
  grade_code VARCHAR(10) NULL,
  approval_limit DECIMAL(12,2) NULL,
  amount DECIMAL(12,2) NOT NULL,
  reason VARCHAR(300) NOT NULL,
  attempted_at DATETIME NOT NULL,
  PRIMARY KEY (id),
  KEY ix_claim_attempts_claim (claim_id),
  KEY ix_claim_attempts_user (user_id),
  KEY ix_claim_attempts_at (attempted_at),
  CONSTRAINT fk_claim_attempts_claim FOREIGN KEY (claim_id) REFERENCES claims (id),
  CONSTRAINT fk_claim_attempts_user FOREIGN KEY (user_id) REFERENCES users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Backfill 1: assessor of claims now Assessing = who made the latest move to Assessing.
UPDATE claims c
SET c.assessed_by_user_id = (
  SELECT h.changed_by_user_id
  FROM claim_status_history h
  WHERE h.claim_id = c.id AND h.to_status = 'Assessing'
  ORDER BY h.changed_at DESC, h.id DESC
  LIMIT 1)
WHERE c.status = 'Assessing';

-- Backfill 2: approver of claims now Approved, Settled or Closed = who made the latest move to Approved.
UPDATE claims c
SET c.approved_by_user_id = (
  SELECT h.changed_by_user_id
  FROM claim_status_history h
  WHERE h.claim_id = c.id AND h.to_status = 'Approved'
  ORDER BY h.changed_at DESC, h.id DESC
  LIMIT 1)
WHERE c.status IN ('Approved', 'Settled', 'Closed');

-- Backfill 3: settlement amount of claims already Settled or Closed = what was paid.
UPDATE claims
SET settlement_amount = paid_amount
WHERE status IN ('Settled', 'Closed') AND paid_amount IS NOT NULL;
