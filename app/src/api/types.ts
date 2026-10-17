// Mirrors the DTOs in backend/src/Ailu.Api. Keep the two in step when either side changes.

export type User = {
  id: string;
  phone: string;
  name: string | null;
  locale: 'kk' | 'ru' | 'en';
  timeZone: string;
};

export type TokenPair = {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  user: User;
};

export type Partner = { id: string; name: string | null };

export type Couple = {
  id: string;
  startedAt: string;
  status: 'Active' | 'Paused' | 'Ended';
  daysTogether: number;
  partner: Partner | null;
};

export type Invitation = { id: string; phone: string; expiresAt: string };

export type Phase = {
  cycle: number;
  index: number;
  slug: string;
  name: string;
  moon: string;
  earned: number;
  balance: number;
  phaseStart: number;
  nextAt: number | null;
  nextName: string | null;
  fill: number;
};

export type TaskState =
  | 'offered'
  | 'waiting_for_partner'
  | 'accepted'
  | 'waiting_for_confirmation'
  | 'confirm_needed'
  | 'done';

export type DailyTask = {
  id: string;
  date: string;
  slug: string;
  category: string;
  title: string;
  body: string;
  costKzt: number;
  durationMin: number;
  state: TaskState;
  acceptedByMe: boolean;
  acceptedByPartner: boolean;
  completedAt: string | null;
  confirmedAt: string | null;
};

export type DailyQuestion = {
  id: string;
  date: string;
  text: string;
  myAnswer: string | null;
  partnerAnswered: boolean;
  partnerAnswer: string | null;
  revealedAt: string | null;
};

// Offered when a missed day broke the streak and the couple can pay нұр to bring it back.
export type StreakRestore = {
  days: number;
  missedDays: number;
  cost: number;
  restoresLeft: number;
  restoresPerMonth: number;
  canAfford: boolean;
};

export type Streak = { days: number; lastDay: string | null; restore: StreakRestore | null };

export type Today = {
  date: string;
  task: DailyTask;
  question: DailyQuestion;
  streak: Streak;
  phase: Phase;
};

export type Photo = { id: string; width: number; height: number; thumb: string; screen: string; full: string };

export type Memory = {
  id: string;
  title: string;
  text: string | null;
  place: string | null;
  happenedOn: string;
  author: { id: string; name: string | null };
  mine: boolean;
  favorite: boolean;
  reactedByMe: boolean;
  reactedByPartner: boolean;
  coupleTaskId: string | null;
  createdAt: string;
  photos: Photo[];
};

export type MemoryInput = {
  title: string;
  text?: string | null;
  place?: string | null;
  happenedOn?: string;
  coupleTaskId?: string | null;
};

export type MemoryPage = { items: Memory[]; nextCursor: string | null };

export type Storage = {
  plan: 'free' | 'plus' | 'premium';
  photos: number;
  photoLimit: number | null;
  usedBytes: number;
  quotaBytes: number;
};

export type MoodLevel = 'hard' | 'tired' | 'even' | 'good' | 'great';

export type MyMood = { mood: MoodLevel; note: string | null; noteShared: boolean; markedAt: string };

// The partner's note is only sent once they have shared it.
export type PartnerMood = { mood: MoodLevel; markedAt: string; note: string | null };

export type MoodDay = { date: string; mine: MyMood | null; partner: PartnerMood | null };

// `days` runs oldest first and ends with today.
export type Mood = { date: string; consented: boolean; days: MoodDay[] };

// RFC 9457 problem details, with the API's stable `code`.
export type Problem = {
  status: number;
  code?: string;
  detail?: string;
  errors?: Record<string, string[]>;
};
