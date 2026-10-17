import { tokens } from './tokens';
import type { Couple, DailyQuestion, DailyTask, Invitation, Phase, Problem, Today, TokenPair, User } from './types';

export const API_URL = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5210';

export class ApiError extends Error {
  constructor(public readonly problem: Problem) {
    super(problem.detail ?? `HTTP ${problem.status}`);
  }
  get code() {
    return this.problem.code;
  }
}

// Called when the session can no longer be refreshed, so the app can return to sign-in.
let onSignedOut: () => void = () => {};
export function setSignedOutHandler(handler: () => void) {
  onSignedOut = handler;
}

// Both partners' screens poll the same couple, so several requests can hit an expired token at
// once. They share one refresh call; the refresh token only works once.
let refreshing: Promise<boolean> | null = null;

async function refresh(): Promise<boolean> {
  const { refresh: refreshToken } = await tokens.get();
  if (!refreshToken) return false;
  const res = await fetch(`${API_URL}/v1/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  });
  if (!res.ok) return false;
  const pair = (await res.json()) as TokenPair;
  await tokens.set(pair.accessToken, pair.refreshToken);
  return true;
}

async function send(method: string, path: string, body: unknown, retry: boolean): Promise<Response> {
  const { access } = await tokens.get();
  const res = await fetch(`${API_URL}${path}`, {
    method,
    headers: {
      Accept: 'application/json',
      ...(body !== undefined && { 'Content-Type': 'application/json' }),
      ...(access && { Authorization: `Bearer ${access}` }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (res.status === 401 && retry && access) {
    refreshing ??= refresh().finally(() => (refreshing = null));
    if (await refreshing) return send(method, path, body, false);
    await tokens.clear();
    onSignedOut();
  }
  return res;
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let res: Response;
  try {
    res = await send(method, path, body, true);
  } catch {
    throw new ApiError({ status: 0, code: 'offline', detail: 'Нет связи. Проверьте интернет и попробуйте ещё раз.' });
  }
  if (res.status === 204) return undefined as T;
  const json = await res.json().catch(() => ({}));
  if (!res.ok) throw new ApiError({ status: res.status, ...json });
  return json as T;
}

export const api = {
  requestCode: (phone: string) => request<{ phone: string; expiresInSeconds: number }>('POST', '/v1/auth/phone/request', { phone }),
  verifyCode: (phone: string, code: string) => request<TokenPair>('POST', '/v1/auth/phone/verify', { phone, code }),
  me: () => request<User>('GET', '/v1/me'),
  registerDevice: (pushToken: string, platform: 'ios' | 'android') =>
    request<void>('PUT', '/v1/me/devices', { pushToken, platform }),
  unregisterDevice: (pushToken: string, platform: 'ios' | 'android') =>
    request<void>('POST', '/v1/me/devices/unregister', { pushToken, platform }),
  updateMe: (patch: Partial<Pick<User, 'name' | 'locale' | 'timeZone'>>) => request<User>('PATCH', '/v1/me', patch),

  couple: async (): Promise<Couple | null> => {
    try {
      return await request<Couple>('GET', '/v1/couple');
    } catch (e) {
      if (e instanceof ApiError && e.code === 'no_couple') return null;
      throw e;
    }
  },
  invite: (phone: string) => request<Invitation>('POST', '/v1/couple/invitations', { phone }),
  acceptInvitation: (code: string) => request<Couple>('POST', `/v1/couple/invitations/${encodeURIComponent(code)}/accept`),
  endCouple: () => request<void>('DELETE', '/v1/couple'),

  today: () => request<Today>('GET', '/v1/couple/today'),
  phase: () => request<Phase>('GET', '/v1/couple/phase'),
  acceptTask: (id: string) => request<DailyTask>('POST', `/v1/couple/tasks/${id}/accept`),
  completeTask: (id: string) => request<DailyTask>('POST', `/v1/couple/tasks/${id}/complete`),
  answer: (id: string, text: string) => request<DailyQuestion>('POST', `/v1/couple/questions/${id}/answer`, { text }),
};
