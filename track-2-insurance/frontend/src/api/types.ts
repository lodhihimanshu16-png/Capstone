export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ClaimListItem {
  id: number;
  claimNumber: string;
  policyNumber: string;
  policyholderName: string;
  productName: string;
  handlerName: string | null;
  status: string;
  claimType: string;
  lossDate: string;
  estimatedAmount: number;
  noteCount: number;
  reopenedCount: number;
}

export interface Claim {
  id: number;
  claimNumber: string;
  policyId: number;
  handlerId: number | null;
  claimType: string;
  status: string;
  lossDate: string;
  reportedDate: string;
  description: string;
  estimatedAmount: number;
  approvedAmount: number | null;
  paidAmount: number | null;
  excess: number;
  reopenedCount: number;
  reserveReviewDue: boolean;
  assessedByUserId: number | null;
  approvedByUserId: number | null;
  settlementAmount: number | null;
}

export interface Region {
  id: number;
  code: string;
  name: string;
}

export interface Handler {
  id: number;
  userId: number;
  name: string;
  email: string;
  gradeId: number;
  gradeCode: string;
  approvalLimit: number;
  regionId: number;
  regionCode: string;
  maxOpenClaims: number;
  active: boolean;
}

// No password hash: the API no longer returns one (BR-24).
export interface User {
  id: number;
  email: string;
  role: string;
  displayName: string;
  active: boolean;
  createdAt: string;
}

export type RequiredField = 'estimatedAmount' | 'approvedAmount' | 'settlementAmount' | 'reason';

export interface NextAction {
  action: string;
  toStatus: string;
  allowed: boolean;
  blockedReason: string | null;
  requires: RequiredField[];
}

export interface NextActions {
  claimId: number;
  claimNumber: string;
  status: string;
  isFinal: boolean;
  daysInStage: number;
  targetDays: number | null;
  dueBy: string | null;
  overdue: boolean;
  actingUserApprovalLimit: number | null;
  actions: NextAction[];
  nextStep: string;
}

export interface QueueItem {
  claimId: number;
  claimNumber: string;
  status: string;
  productName: string;
  policyholderName: string;
  handlerName: string | null;
  estimatedAmount: number;
  reportedDate: string;
  daysInStage: number;
  targetDays: number;
  overdue: boolean;
  daysOverTarget: number;
}

export interface LimitBreach {
  claimId: number;
  claimNumber: string;
  kind: 'BlockedAttempt' | 'ApprovedAboveLimit';
  amount: number;
  limit: number | null;
  overBy: number;
  userId: number;
  userName: string;
  gradeCode: string | null;
  regionCode: string;
  occurredAt: string;
  reason: string | null;
}

export interface ChangeStatusBody {
  status: string;
  changedByUserId: number;
  reason?: string | null;
  estimatedAmount?: number | null;
  approvedAmount?: number | null;
  settlementAmount?: number | null;
}

export interface ReopenBody {
  changedByUserId: number;
  reason: string;
}

export interface ClaimNote {
  id: number;
  claimId: number;
  authorUserId: number;
  noteType: string;
  body: string;
  createdAt: string;
}

export interface Policy {
  id: number;
  policyNumber: string;
  policyholderName: string;
  productCode: string;
  productName: string;
  startDate: string;
  endDate: string;
  annualPremium: number;
  sumInsured: number;
  status: string;
}

export interface Policyholder {
  id: number;
  fullName: string;
  email: string;
  phone: string | null;
  city: string;
  postcode: string;
}

export interface Product {
  id: number;
  code: string;
  name: string;
  line: string;
  basePremium: number;
  defaultExcess: number;
  active: boolean;
}

export interface LossRatioRow {
  productCode: string;
  productName: string;
  writtenPremium: number;
  paidClaims: number;
  lossRatioPct: number;
  claimCount: number;
}
