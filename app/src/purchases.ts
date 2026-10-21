import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  deepLinkToSubscriptions,
  fetchProducts,
  finishTransaction,
  getAvailablePurchases,
  initConnection,
  isUserCancelledError,
  purchaseErrorListener,
  purchaseUpdatedListener,
  requestPurchase,
  type Purchase,
} from 'expo-iap';
import { useEffect } from 'react';
import { Platform } from 'react-native';

import { api, ApiError } from '@/api/client';
import type { Billing, BillingProduct, YearStory } from '@/api/types';

// Subscriptions go through Apple and Google in-app purchase. The app never decides what the couple
// has: it hands the store's transaction to the server, which checks it with the store and sets the
// plan for both partners. Store purchases need a development or store build; Expo Go and the web
// can't buy, and say so with `purchases_unavailable`.

export type Tier = 'plus' | 'premium';
export type Period = 'month' | 'year';

const native = Platform.OS === 'ios' || Platform.OS === 'android';

export function useBilling() {
  return useQuery({ queryKey: ['billing'], queryFn: api.billing });
}

// Everything a plan unlocks is read from the server, so a change refreshes all of it.
let planChanged: () => void = () => {};

// Mount once in the paired part of the app: it keeps the cache fresh after a purchase and, on launch,
// hands the server any purchase that was paid for but not yet claimed (the app closed mid-purchase).
export function usePurchaseSync() {
  const queryClient = useQueryClient();
  useEffect(() => {
    planChanged = () => {
      for (const key of ['billing', 'storage', 'goals', 'today']) queryClient.invalidateQueries({ queryKey: [key] });
    };
    if (native) {
      restorePurchases().catch(() => {});
      restoreStoryPurchases().catch(() => {});
    }
    return () => {
      planChanged = () => {};
    };
  }, [queryClient]);
}

let connected: Promise<unknown> | null = null;

async function connect() {
  if (!native) throw new ApiError({ status: 0, code: 'purchases_unavailable' });
  connected ??= initConnection().catch((e) => {
    connected = null;
    throw new ApiError({ status: 0, code: 'purchases_unavailable', detail: e instanceof Error ? e.message : undefined });
  });
  await connected;
}

function storeProductId(p: BillingProduct) {
  return Platform.OS === 'ios' ? p.appleProductId : p.googleProductId;
}

// Hand one store purchase to the server, then finish it with the store. A purchase is finished only
// once the server has it, so nothing paid for is lost if the app closes in between.
async function claim(purchase: Purchase): Promise<Billing> {
  const billing =
    Platform.OS === 'ios'
      ? await api.claimApple(purchase.id)
      : await api.claimGoogle(purchase.purchaseToken ?? '');
  await finishTransaction({ purchase, isConsumable: false });
  planChanged();
  return billing;
}

// Buy a tier for a month or a year. Resolves true when the couple's plan changed, false when the
// person backed out.
export async function purchase({ tier, period }: { tier: Tier; period: Period }): Promise<boolean> {
  const [billing, me] = await Promise.all([api.billing(), api.me(), connect()]);
  const product = billing.products.find((p) => p.tier === tier && p.period === period);
  if (!product) throw new ApiError({ status: 0, code: 'purchase_not_found' });
  const sku = storeProductId(product);

  // Google needs the offer token of the base plan being bought.
  let subscriptionOffers: { sku: string; offerToken: string }[] | undefined;
  if (Platform.OS === 'android') {
    const found = await fetchProducts({ skus: [sku], type: 'subs' });
    const offers = (found ?? []).flatMap((p) => ('subscriptionOffers' in p ? (p.subscriptionOffers ?? []) : []));
    const offer = offers.find((o) => o.basePlanIdAndroid === product.googleBasePlanId) ?? offers[0];
    if (!offer?.offerTokenAndroid) throw new ApiError({ status: 0, code: 'purchase_not_found' });
    subscriptionOffers = [{ sku, offerToken: offer.offerTokenAndroid }];
  }

  const before = billing.plan;
  return new Promise<boolean>((resolve, reject) => {
    const done = (fn: () => void) => {
      updated.remove();
      failed.remove();
      fn();
    };
    const updated = purchaseUpdatedListener((p) => {
      if (p.productId !== sku) return;
      claim(p).then(
        (after) => done(() => resolve(after.plan !== before)),
        (e) => done(() => reject(e)),
      );
    });
    const failed = purchaseErrorListener((e) => {
      done(() => (isUserCancelledError(e) ? resolve(false) : reject(new Error(e.message))));
    });

    // The user id travels with the purchase, so the server can tell whose it is even when two Ailu
    // accounts share one Apple ID or Google account.
    requestPurchase({
      type: 'subs',
      request: {
        apple: { sku, appAccountToken: me.id },
        google: { skus: [sku], obfuscatedAccountId: me.id, subscriptionOffers },
      },
    }).catch((e) => done(() => (isUserCancelledError(e) ? resolve(false) : reject(e))));
  });
}

// "Restore purchases": hand the server every Ailu subscription the store still has for this person.
// Resolves true when the couple's plan changed.
export async function restorePurchases(): Promise<boolean> {
  await connect();
  const [billing, purchases] = await Promise.all([api.billing(), getAvailablePurchases()]);
  const ours = new Set(billing.products.map(storeProductId));
  let plan = billing.plan;
  let lastError: unknown;
  for (const p of purchases.filter((p) => ours.has(p.productId))) {
    try {
      plan = (await claim(p)).plan;
    } catch (e) {
      // One subscription from another Ailu account on the same store account shouldn't stop the rest.
      lastError = e;
    }
  }
  if (plan === billing.plan && lastError) throw lastError;
  return plan !== billing.plan;
}

// The 7-day Premium trial, once per couple, with no card and nothing to cancel.
export async function startTrial(): Promise<Billing> {
  const billing = await api.startTrial();
  planChanged();
  return billing;
}

// Cancelling happens in the store's own subscription settings; this opens them.
export async function manageSubscription(billing: Billing) {
  await connect();
  const product = billing.products.find((p) => p.tier === billing.subscription?.tier);
  await deepLinkToSubscriptions({
    skuAndroid: product?.googleProductId,
    packageNameAndroid: 'kz.ailu.app',
  });
}

// The store's own prices, in the person's currency, for the paywall. Falls back to the server's
// placeholder prices where the store can't be asked.
export async function storePrices(billing: Billing): Promise<Partial<Record<Tier, string>>> {
  try {
    await connect();
    const monthly = billing.products.filter((p) => p.period === 'month');
    const found = await fetchProducts({ skus: monthly.map(storeProductId), type: 'subs' });
    const out: Partial<Record<Tier, string>> = {};
    for (const p of monthly) {
      const item = (found ?? []).find((f) => f.id === storeProductId(p));
      if (item) out[p.tier] = item.displayPrice;
    }
    return out;
  } catch {
    return {};
  }
}

// The video story of the year is bought once per year, by either partner, as a consumable (the same
// product again next year). Like a subscription, the server checks the purchase with the store.

function storySku(story: YearStory) {
  return Platform.OS === 'ios' ? story.appleProductId : story.googleProductId;
}

// Hand a story purchase to the server, then consume it. A refusal because the year was already bought
// (both partners paid at once) still consumes it: the server has logged it for a refund.
async function claimStory(year: number, purchase: Purchase): Promise<YearStory> {
  try {
    const story =
      Platform.OS === 'ios'
        ? await api.orderStoryApple(year, purchase.id)
        : await api.orderStoryGoogle(year, purchase.purchaseToken ?? '');
    await finishTransaction({ purchase, isConsumable: true });
    return story;
  } catch (e) {
    if (e instanceof ApiError && e.code === 'story_ordered') await finishTransaction({ purchase, isConsumable: true });
    throw e;
  }
}

// Buy this year's film. Resolves with the ordered story, or null when the person backed out.
export async function buyYearStory(story: YearStory): Promise<YearStory | null> {
  const [me] = await Promise.all([api.me(), connect()]);
  const sku = storySku(story);
  return new Promise<YearStory | null>((resolve, reject) => {
    const done = (fn: () => void) => {
      updated.remove();
      failed.remove();
      fn();
    };
    const updated = purchaseUpdatedListener((p) => {
      if (p.productId !== sku) return;
      claimStory(story.year, p).then(
        (after) => done(() => resolve(after)),
        (e) => done(() => reject(e)),
      );
    });
    const failed = purchaseErrorListener((e) => {
      done(() => (isUserCancelledError(e) ? resolve(null) : reject(new Error(e.message))));
    });
    requestPurchase({
      type: 'in-app',
      request: {
        apple: { sku, appAccountToken: me.id },
        google: { skus: [sku], obfuscatedAccountId: me.id },
      },
    }).catch((e) => done(() => (isUserCancelledError(e) ? resolve(null) : reject(e))));
  });
}

// A story paid for while the app closed mid-purchase is handed over on the next launch, for the year
// the app offers now.
async function restoreStoryPurchases() {
  await connect();
  const [story, purchases] = await Promise.all([api.yearStory(), getAvailablePurchases()]);
  for (const p of purchases.filter((p) => p.productId === storySku(story))) {
    await claimStory(story.year, p).catch(() => {});
  }
}

// The store's own price for the film, or null where the store can't be asked.
export async function storyPrice(story: YearStory): Promise<string | null> {
  try {
    await connect();
    const found = await fetchProducts({ skus: [storySku(story)], type: 'in-app' });
    return (found ?? []).find((f) => f.id === storySku(story))?.displayPrice ?? null;
  } catch {
    return null;
  }
}
