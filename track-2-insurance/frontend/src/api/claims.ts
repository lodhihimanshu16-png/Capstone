import { http } from './http';
import type { ChangeStatusBody, Claim, ClaimListItem, ClaimNote, NextActions, Paged, QueueItem, ReopenBody } from './types';

export const claimsApi = {
  list: (status?: string, page = 1) => {
    const params = new URLSearchParams({ page: String(page), pageSize: '25' });
    if (status) params.set('status', status);
    return http.get<Paged<ClaimListItem>>(`/api/claims?${params}`);
  },
  get: (id: number) => http.get<Claim>(`/api/claims/${id}`),
  notes: (id: number) => http.get<ClaimNote[]>(`/api/claims/${id}/notes`),
  queue: (filters: { handlerId?: number; regionId?: number; page?: number; pageSize?: number }) => {
    const params = new URLSearchParams({ page: String(filters.page ?? 1), pageSize: String(filters.pageSize ?? 25) });
    if (filters.handlerId) params.set('handlerId', String(filters.handlerId));
    if (filters.regionId) params.set('regionId', String(filters.regionId));
    return http.get<Paged<QueueItem>>(`/api/claims/queue?${params}`);
  },
  nextActions: (id: number, actingUserId?: number) =>
    http.get<NextActions>(`/api/claims/${id}/next-actions${actingUserId ? `?actingUserId=${actingUserId}` : ''}`),
  changeStatus: (id: number, body: ChangeStatusBody) => http.put<Claim>(`/api/claims/${id}/status`, body),
  reopen: (id: number, body: ReopenBody) => http.post<Claim>(`/api/claims/${id}/reopen`, body),
};
