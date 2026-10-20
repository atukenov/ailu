import { tokens } from './tokens';
import type {
  Achievement,
  AchievementList,
  Billing,
  BookOrder,
  BookOrderInput,
  BookYears,
  Couple,
  DailyQuestion,
  DailyTask,
  GiftOrder,
  Gifts,
  Goal,
  GoalInput,
  GoalList,
  Invitation,
  LoveLanguage,
  LoveQuiz,
  Memory,
  MemoryInput,
  MemoryPage,
  Mood,
  PastCouple,
  MoodLevel,
  Phase,
  PhotoBook,
  Problem,
  Redeemed,
  Storage,
  Streak,
  TimelinePage,
  Today,
  TokenPair,
  User,
  YearPhotos,
} from './types';

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
  // A form (a photo upload) sets its own multipart content type, boundary included.
  const form = body instanceof FormData;
  const res = await fetch(`${API_URL}${path}`, {
    method,
    headers: {
      Accept: 'application/json',
      ...(body !== undefined && !form && { 'Content-Type': 'application/json' }),
      ...(access && { Authorization: `Bearer ${access}` }),
    },
    body: body === undefined ? undefined : form ? body : JSON.stringify(body),
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
    throw new ApiError({ status: 0, code: 'offline' });
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
  deleteAccount: () => request<void>('DELETE', '/v1/me'),
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
  moreTask: () => request<DailyTask>('POST', '/v1/couple/tasks/more'),
  answer: (id: string, text: string) => request<DailyQuestion>('POST', `/v1/couple/questions/${id}/answer`, { text }),
  restoreStreak: () => request<Streak>('POST', '/v1/couple/streak/restore'),

  memories: (cursor?: string | null) =>
    request<MemoryPage>('GET', `/v1/couple/memories${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`),
  memory: (id: string) => request<Memory>('GET', `/v1/couple/memories/${id}`),
  createMemory: (input: MemoryInput) => request<Memory>('POST', '/v1/couple/memories', input),
  updateMemory: (id: string, input: Partial<MemoryInput>) => request<Memory>('PATCH', `/v1/couple/memories/${id}`, input),
  deleteMemory: (id: string) => request<void>('DELETE', `/v1/couple/memories/${id}`),
  addPhoto: (id: string, form: FormData) => request<Memory>('POST', `/v1/couple/memories/${id}/photos`, form),
  react: (id: string, on: boolean) => request<Memory>(on ? 'PUT' : 'DELETE', `/v1/couple/memories/${id}/reaction`),
  favorite: (id: string, on: boolean) => request<Memory>(on ? 'PUT' : 'DELETE', `/v1/couple/memories/${id}/favorite`),
  storage: () => request<Storage>('GET', '/v1/couple/storage'),
  archive: () => request<PastCouple[]>('GET', '/v1/archive'),
  archiveMemories: (coupleId: string, cursor?: string | null) =>
    request<MemoryPage>('GET', `/v1/archive/${coupleId}/memories${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`),
  archiveMemory: (coupleId: string, id: string) => request<Memory>('GET', `/v1/archive/${coupleId}/memories/${id}`),
  deleteArchiveMemory: (coupleId: string, id: string) => request<void>('DELETE', `/v1/archive/${coupleId}/memories/${id}`),
  timeline: (cursor?: string | null) =>
    request<TimelinePage>('GET', `/v1/couple/timeline${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`),

  mood: () => request<Mood>('GET', '/v1/couple/mood'),
  // A note left out keeps the one already there; an empty string removes it.
  setMood: (mood: MoodLevel, note?: string) => request<Mood>('PUT', '/v1/couple/mood', { mood, note }),
  shareMoodNote: (on: boolean) => request<Mood>(on ? 'PUT' : 'DELETE', '/v1/couple/mood/note-shared'),
  giveMoodConsent: () => request<void>('PUT', '/v1/me/mood-consent'),
  withdrawMoodConsent: () => request<void>('DELETE', '/v1/me/mood-consent'),

  goals: () => request<GoalList>('GET', '/v1/couple/goals'),
  goal: (id: string) => request<Goal>('GET', `/v1/couple/goals/${id}`),
  createGoal: (input: GoalInput) => request<Goal>('POST', '/v1/couple/goals', input),
  updateGoal: (id: string, input: Partial<Omit<GoalInput, 'steps'>>) => request<Goal>('PATCH', `/v1/couple/goals/${id}`, input),
  joinGoal: (id: string) => request<Goal>('POST', `/v1/couple/goals/${id}/join`),
  // Returns the goal while it waits for the partner's word, nothing once it is gone.
  removeGoal: (id: string) => request<Goal | undefined>('DELETE', `/v1/couple/goals/${id}`),
  keepGoal: (id: string) => request<Goal>('POST', `/v1/couple/goals/${id}/keep`),
  addStep: (id: string, title: string) => request<Goal>('POST', `/v1/couple/goals/${id}/steps`, { title }),
  deleteStep: (id: string, stepId: string) => request<Goal>('DELETE', `/v1/couple/goals/${id}/steps/${stepId}`),
  markStep: (id: string, stepId: string, on: boolean) =>
    request<Goal>(on ? 'POST' : 'DELETE', `/v1/couple/goals/${id}/steps/${stepId}/done`),

  loveQuiz: () => request<LoveQuiz>('GET', '/v1/couple/love-quiz'),
  // One language per question, in question order.
  answerLoveQuiz: (answers: LoveLanguage[]) => request<LoveQuiz>('POST', '/v1/couple/love-quiz/answers', { answers }),
  achievements: () => request<AchievementList>('GET', '/v1/couple/achievements'),
  achievement: (slug: string) => request<Achievement>('GET', `/v1/couple/achievements/${slug}`),
  // The first partner's mark asks the other to confirm; the other's mark earns it. HappenedOn is yyyy-MM-dd.
  markAchievement: (slug: string, input: { happenedOn?: string; memoryId?: string } = {}) =>
    request<Achievement>('POST', `/v1/couple/achievements/${slug}/done`, input),
  unmarkAchievement: (slug: string) => request<Achievement>('DELETE', `/v1/couple/achievements/${slug}/done`),

  photoBookYears: () => request<BookYears>('GET', '/v1/couple/photo-books'),
  photoBook: async (year: number): Promise<PhotoBook | null> => {
    try {
      return await request<PhotoBook>('GET', `/v1/couple/photo-books/${year}`);
    } catch (e) {
      if (e instanceof ApiError && e.code === 'photo_book_not_found') return null;
      throw e;
    }
  },
  // Lays the book out from the year's memories; on an existing book this replaces every spread.
  buildPhotoBook: (year: number) => request<PhotoBook>('POST', `/v1/couple/photo-books/${year}`),
  updateBookCover: (year: number, input: { title?: string; subtitle?: string; coverPhotoId?: string }) =>
    request<PhotoBook>('PATCH', `/v1/couple/photo-books/${year}`, input),
  bookPhotos: (year: number) => request<YearPhotos>('GET', `/v1/couple/photo-books/${year}/photos`),
  // One photo id (or null for an empty box) per box of the layout.
  updateSpread: (year: number, position: number, input: { layout: string; photoIds: (string | null)[]; title: string; text: string }) =>
    request<PhotoBook>('PUT', `/v1/couple/photo-books/${year}/spreads/${position}`, input),
  moveSpread: (year: number, position: number, to: number) =>
    request<PhotoBook>('POST', `/v1/couple/photo-books/${year}/spreads/${position}/move`, { to }),
  orderBook: (year: number, input: BookOrderInput) => request<BookOrder>('POST', `/v1/couple/photo-books/${year}/orders`, input),
  payOrder: (id: string) => request<BookOrder>('POST', `/v1/couple/photo-books/orders/${id}/pay`),
  cancelOrder: (id: string) => request<BookOrder>('DELETE', `/v1/couple/photo-books/orders/${id}`),

  billing: () => request<Billing>('GET', '/v1/billing'),
  claimApple: (transactionId: string) => request<Billing>('POST', '/v1/billing/apple', { transactionId }),
  claimGoogle: (purchaseToken: string) => request<Billing>('POST', '/v1/billing/google', { purchaseToken }),
  startTrial: () => request<Billing>('POST', '/v1/billing/trial'),
  // A gift code or a promo code; the plan is the couple's, like a subscription.
  redeem: (code: string) => request<Redeemed>('POST', '/v1/billing/redeem', { code }),
  gifts: () => request<Gifts>('GET', '/v1/gifts'),
  orderGift: (productId: string) => request<GiftOrder>('POST', '/v1/gifts', { productId }),
  gift: (id: string) => request<GiftOrder>('GET', `/v1/gifts/${id}`),
};

// Photo URLs from the API are signed paths; they work without the token, so any image view can load them.
export const photoUrl = (path: string) => `${API_URL}${path}`;

// Links the API hands out (the book's PDF preview, a payment page) are either full URLs or paths on the API.
export const apiLink = (link: string) => (link.startsWith('/') ? `${API_URL}${link}` : link);
