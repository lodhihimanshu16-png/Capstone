import { http } from './http';
import type { LimitBreach, LossRatioRow } from './types';

export const reportsApi = {
  lossRatio: (from: string, to: string) => http.get<LossRatioRow[]>(`/api/reports/loss-ratio?from=${from}&to=${to}`),
  limitsBreaches: (from: string, to: string, regionId?: number) => {
    const params = new URLSearchParams();
    if (from) params.set('from', from);
    if (to) params.set('to', to);
    if (regionId) params.set('regionId', String(regionId));
    return http.get<LimitBreach[]>(`/api/reports/limits-breaches?${params}`);
  },
};
