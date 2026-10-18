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
  // A task AI wrote for this couple: Premium's extra tasks.
  personal: boolean;
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

// `task` is the task of the day; `moreTasks` are the ones a paid couple asked for after it, and
// `tasksPerDay` is how many the plan allows, the task of the day included.
export type Today = {
  date: string;
  task: DailyTask;
  moreTasks: DailyTask[];
  tasksPerDay: number;
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

export type GoalCategory = 'travel' | 'home' | 'health' | 'money' | 'together';
export type GoalState = 'proposed' | 'join_needed' | 'active' | 'done';
export type StepState = 'open' | 'waiting_for_partner' | 'confirm_needed' | 'done';

export type GoalStep = { id: string; title: string; state: StepState; closedAt: string | null };

export type Goal = {
  id: string;
  title: string;
  category: GoalCategory;
  targetMonth: string | null; // yyyy-MM
  weeklyReminder: boolean;
  state: GoalState;
  progress: number;
  stepsDone: number;
  stepsTotal: number;
  createdByMe: boolean;
  removalRequestedByMe: boolean;
  removalRequestedByPartner: boolean;
  createdAt: string;
  completedAt: string | null;
  steps: GoalStep[];
};

export type GoalList = { items: Goal[]; limit: number | null; unfinished: number; canCreate: boolean };

// On update a missing field stays as is; targetMonth '' removes the deadline.
export type GoalInput = {
  title: string;
  category: GoalCategory;
  targetMonth: string;
  weeklyReminder: boolean;
  steps: string[];
};

export type LoveLanguage = 'words' | 'time' | 'gifts' | 'service' | 'touch';

// Options are languages in the order to show them; the texts are in the app's strings.
export type LoveQuizQuestion = { id: number; options: LoveLanguage[] };
// 0–100 per language; top holds every language on a tie.
export type LoveQuizScores = { scores: Record<LoveLanguage, number>; top: LoveLanguage[] };

export type LoveQuiz = {
  // Whether the plan lets the couple take it (Plus and up). A result taken earlier stays visible.
  available: boolean;
  nur: number;
  questions: LoveQuizQuestion[];
  // The round waiting for answers; the partner's picks stay on the server until both have answered.
  round: { myAnswered: boolean; partnerAnswered: boolean } | null;
  // The latest round both finished. partner is null if their side is gone (a deleted account).
  result: { completedAt: string; me: LoveQuizScores | null; partner: LoveQuizScores | null; nurAwarded: number } | null;
};

export type TimelineKind =
  | 'start'
  | 'days_together'
  | 'anniversary'
  | 'phase'
  | 'first_task'
  | 'first_answers'
  | 'memory'
  | 'goal_done'
  | 'achievement';

// One moment in the couple's history. The app writes the words from `kind`; titles are the couple's own,
// except an achievement's, which comes from the library in the person's language.
export type TimelineEvent = {
  id: string;
  kind: TimelineKind;
  date: string;
  at: string;
  days: number | null;
  years: number | null;
  cycle: number | null;
  phase: number | null;
  memoryId: string | null;
  goalId: string | null;
  title: string | null;
  place: string | null;
  thumb: string | null;
  photos: number | null;
  slug: string | null;
  icon: string | null;
};

// Newest first. On the free plan `hidden` counts the older moments and `freeDays` is how far back a free
// couple sees; both are null on a paid plan. The couple's start is always the last item.
export type TimelinePage = {
  summary: { daysTogether: number; memories: number; goalsDone: number; nurEarned: number };
  items: TimelineEvent[];
  nextCursor: string | null;
  hidden: number | null;
  freeDays: number | null;
};

export type Plan = 'free' | 'plus' | 'premium';

export type AchievementCategory = 'firsts' | 'events' | 'travel';
export type AchievementState = 'locked' | 'open' | 'waiting_for_partner' | 'confirm_needed' | 'earned';

// Title and body come from the server in the caller's language.
export type Achievement = {
  slug: string;
  category: AchievementCategory;
  tier: Plan;
  icon: string;
  title: string;
  body: string;
  state: AchievementState;
  happenedOn: string | null;
  memoryId: string | null;
  earnedAt: string | null;
  nur: number;
};

export type AchievementList = { items: Achievement[]; earned: number; total: number; plan: Plan };

export type BillingProduct = {
  id: string;
  tier: Exclude<Plan, 'free'>;
  period: 'month' | 'year';
  // A placeholder until the store answers with its own localized price.
  priceKzt: number;
  appleProductId: string;
  googleProductId: string;
  googleBasePlanId: string;
};

export type Billing = {
  plan: Plan;
  subscription: {
    tier: Exclude<Plan, 'free'>;
    provider: 'apple' | 'google' | 'trial';
    status: 'active' | 'grace';
    periodEnd: string;
    autoRenew: boolean;
    paidByMe: boolean;
  } | null;
  trialAvailable: boolean;
  trialDays: number;
  products: BillingProduct[];
};

// AI selections (Premium). Ideas come in the reader's language; `saved` is shared by both partners.
export type AiKind = 'date' | 'home' | 'film' | 'music';
export type AiBudget = 'any' | 'free' | 'low';

export type AiItem = {
  id: string;
  pickId: string;
  kind: AiKind;
  title: string;
  body: string;
  costKzt: number;
  durationMin: number;
  saved: boolean;
  savedAt: string | null;
};

export type AiPick = {
  id: string;
  kind: AiKind;
  budget: AiBudget;
  wish: string | null;
  byMe: boolean;
  createdAt: string;
  items: AiItem[];
};

// `available`: the plan lets the couple ask for new selections. `used` of `limit` this month; `resetsOn`
// is the first day of the next month.
export type Ai = { available: boolean; used: number; limit: number; resetsOn: string; picks: AiPick[]; saved: AiItem[] };
