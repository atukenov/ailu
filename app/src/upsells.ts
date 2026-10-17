import { router, type Href } from 'expo-router';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';

import { ApiError } from '@/api/client';
import type { Storage } from '@/api/types';

// Remembers on this phone which soft upsells were already shown, so each one stays rare: the
// second-task plans screen once a day, the streak-day-7 offer once per couple (monetization doc).
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
  // The day the second-task plans screen was last opened (the couple's date from /today).
  taskShownOn: () => read('task'),
  markTaskShown: (date: string) => write('task', date),
  // Keyed by couple, so a new couple gets their own day-7 moment.
  streakShown: async (coupleId: string) => (await read(`streak7.${coupleId}`)) !== null,
  markStreakShown: (coupleId: string) => write(`streak7.${coupleId}`, '1'),
};

// Where each soft upsell leads. The plans screen (tiers, purchase, restore, trial) belongs to the
// in-app purchases work; these moments only open it on the right tier. Each offers Plus, except
// day 7 of the streak, the one place Premium is offered right away (monetization doc).
export type PaidPlan = 'plus' | 'premium';

export function openPlans(tier: PaidPlan) {
  // Cast: /plans lands with the purchases work, so typed routes may not know it yet.
  router.push(`/plans?tier=${tier}` as Href);
}

// The 201st photo goes to the limit screen first, which explains that nothing saved is lost.
export function openPhotoLimit() {
  router.push('/limit');
}

// The free plan's photo limit is the one that leads to the limit screen (the 201st photo). A paid
// couple that runs out of room only sees the error, since there is no bigger plan to offer yet.
export function photoLimitReached(storage: Storage | undefined) {
  return storage?.plan === 'free' && storage.photoLimit !== null && storage.photos >= storage.photoLimit;
}

export function isFreePhotoLimit(error: unknown, storage: Storage | undefined) {
  return error instanceof ApiError && error.code === 'storage_full' && storage?.plan === 'free';
}
