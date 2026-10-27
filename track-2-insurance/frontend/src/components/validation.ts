// Client-side checks that mirror the backend rules in docs/specs/claims-workflow.md.
// Each returns an error message, or null when the value is acceptable.

export const REASON_MAX = 300;

export function validateReason(value: string): string | null {
  const reason = value.trim();
  if (!reason) return 'A reason is required.';
  if (reason.length > REASON_MAX) return `The reason must be at most ${REASON_MAX} characters (now ${reason.length}).`;
  return null;
}

/** Amount above zero, a plain number, at most 2 decimals. */
export function validateAmount(value: string, label: string): string | null {
  const text = value.trim();
  if (!text) return `${label} is required.`;
  if (!/^\d+(\.\d+)?$/.test(text)) return `${label} must be a number.`;
  if (!/^\d+(\.\d{1,2})?$/.test(text)) return `${label} can have at most 2 decimal places.`;
  if (Number(text) <= 0) return `${label} must be above zero.`;
  return null;
}

export function validateSettlement(value: string, approvedAmount: number | null): string | null {
  const basic = validateAmount(value, 'Settlement amount');
  if (basic) return basic;
  if (approvedAmount != null && Number(value) > approvedAmount)
    return `Settlement amount cannot be above the approved amount (${approvedAmount.toFixed(2)}).`;
  return null;
}

export function validateDateRange(from: string, to: string): string | null {
  if (from && to && to < from) return 'The "to" date cannot be before the "from" date.';
  return null;
}
