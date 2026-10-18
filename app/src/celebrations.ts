import { router } from 'expo-router';

import type { Achievement } from '@/api/types';
import { deviceStore } from '@/device-store';

// Each partner sees the "new achievement" screen once per achievement on this phone: whoever confirms
// it right away, whoever marked it the next time they open achievements.
const key = (coupleId: string) => `achievements.seen.${coupleId}`;

// The first time this phone opens achievements, only the last week's are still news.
const FRESH_MS = 7 * 24 * 60 * 60 * 1000;

async function seen(coupleId: string): Promise<Set<string> | null> {
  const raw = await deviceStore.read(key(coupleId));
  if (raw === null) return null;
  try {
    return new Set(JSON.parse(raw) as string[]);
  } catch {
    return new Set();
  }
}

export async function nextToCelebrate(coupleId: string, items: Achievement[]): Promise<Achievement | null> {
  const earned = items.filter((x) => x.state === 'earned');
  let known = await seen(coupleId);
  if (known === null) {
    known = new Set(earned.filter((x) => Date.now() - Date.parse(x.earnedAt ?? '') > FRESH_MS).map((x) => x.slug));
    await deviceStore.write(key(coupleId), JSON.stringify([...known]));
  }
  return earned.find((x) => !known.has(x.slug)) ?? null;
}

export async function markCelebrated(coupleId: string, slug: string) {
  const known = (await seen(coupleId)) ?? new Set<string>();
  known.add(slug);
  await deviceStore.write(key(coupleId), JSON.stringify([...known]));
}

// A memory of an earned achievement starts with its title and date and is linked to it once saved.
export function addAchievementMemory(a: Achievement) {
  router.push({
    pathname: '/memories/new',
    params: { achievement: a.slug, title: a.title, ...(a.happenedOn && { happenedOn: a.happenedOn }) },
  });
}
