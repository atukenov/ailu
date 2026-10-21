import { router } from 'expo-router';

import { ApiError } from '@/api/client';
import type { Storage } from '@/api/types';
import { deviceStore } from '@/device-store';

// Remembers on this phone which soft upsells were already shown, so each one stays rare: the
// second-task paywall once a day, the streak-day-7 offer once per couple (monetization doc).
const read = (key: string) => deviceStore.read(`upsell.${key}`);
const write = (key: string, value: string) => deviceStore.write(`upsell.${key}`, value);

export const upsells = {
  // The day the second-task paywall was last shown (the couple's date from /today).
  taskShownOn: () => read('task'),
  markTaskShown: (date: string) => write('task', date),
  // Keyed by couple, so a new couple gets their own day-7 moment.
  streakShown: async (coupleId: string) => (await read(`streak7.${coupleId}`)) !== null,
  markStreakShown: (coupleId: string) => write(`streak7.${coupleId}`, '1'),
};

// Why the paywall opened: the 201st photo, a second task in a day, day 7 of the streak, a third goal,
// history older than the free plan shows, an achievement outside the free set, the love-languages quiz,
// AI selections (Premium only). Opened without a reason (from the profile), it shows both plans.
export type PaywallReason = 'photos' | 'task' | 'streak' | 'goals' | 'history' | 'achievements' | 'quiz' | 'ai';

export function openPaywall(reason?: PaywallReason) {
  router.push({ pathname: '/paywall', params: reason ? { reason } : {} });
}

// A full photo limit leads to the paywall while a bigger plan exists: Free (200) to Plus (500), Plus to
// Premium (1 200). A full Premium couple only sees the error.
export function photoLimitReached(storage: Storage | undefined) {
  return storage?.plan !== 'premium' && storage?.photoLimit != null && storage.photos >= storage.photoLimit;
}

export function isPhotoLimit(error: unknown, storage: Storage | undefined) {
  return error instanceof ApiError && error.code === 'storage_full' && storage !== undefined && storage.plan !== 'premium';
}

// The plan with more room for photos, if there is one.
export function biggerPlan(plan: Storage['plan'] | undefined): 'plus' | 'premium' | null {
  return plan === 'plus' ? 'premium' : plan === 'premium' ? null : 'plus';
}
