import { router } from 'expo-router';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';

import { ApiError } from '@/api/client';
import type { Storage } from '@/api/types';

// Remembers on this phone which soft upsells were already shown, so each one stays rare: the
// second-task paywall once a day, the streak-day-7 offer once per couple (monetization doc).
const PREFIX = 'ailu.upsell.';

async function read(key: string): Promise<string | null> {
  try {
    if (Platform.OS === 'web') return globalThis.localStorage?.getItem(PREFIX + key) ?? null;
    return await SecureStore.getItemAsync(PREFIX + key);
  } catch {
    return null;
  }
}

async function write(key: string, value: string): Promise<void> {
  try {
    if (Platform.OS === 'web') globalThis.localStorage?.setItem(PREFIX + key, value);
    else await SecureStore.setItemAsync(PREFIX + key, value);
  } catch {
    // Not remembering only means the offer may show once more.
  }
}

export const upsells = {
  // The day the second-task paywall was last shown (the couple's date from /today).
  taskShownOn: () => read('task'),
  markTaskShown: (date: string) => write('task', date),
  // Keyed by couple, so a new couple gets their own day-7 moment.
  streakShown: async (coupleId: string) => (await read(`streak7.${coupleId}`)) !== null,
  markStreakShown: (coupleId: string) => write(`streak7.${coupleId}`, '1'),
};

// Why the paywall opened: the 201st photo, a second task in a day, day 7 of the streak, a third goal.
// Opened without a reason (from the profile), it shows both plans.
export type PaywallReason = 'photos' | 'task' | 'streak' | 'goals';

export function openPaywall(reason?: PaywallReason) {
  router.push({ pathname: '/paywall', params: reason ? { reason } : {} });
}

// The free plan's photo limit is the one that leads to the paywall (the 201st photo). A paid
// couple that runs out of room only sees the error, since there is no bigger plan to offer yet.
export function photoLimitReached(storage: Storage | undefined) {
  return storage?.plan === 'free' && storage.photoLimit !== null && storage.photos >= storage.photoLimit;
}

export function isFreePhotoLimit(error: unknown, storage: Storage | undefined) {
  return error instanceof ApiError && error.code === 'storage_full' && storage?.plan === 'free';
}
