import { http } from './http';
import type { Handler, Region, User } from './types';

export const handlersApi = {
  handlers: () => http.get<Handler[]>('/api/handlers?active=true'),
  regions: () => http.get<Region[]>('/api/regions'),
  users: () => http.get<User[]>('/api/users'),
};
