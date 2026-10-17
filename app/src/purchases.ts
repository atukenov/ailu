import { ApiError } from '@/api/client';

// Placeholder until in-app purchases land: that work replaces this file with App Store / Google
// Play billing and server receipt checks, keeping these exports. The paywall relies only on them.
export type Tier = 'plus' | 'premium';

// Resolves true when the couple's plan changed, false when the person backed out.
export async function purchase(_tier: Tier): Promise<boolean> {
  throw new ApiError({ status: 0, code: 'billing_unavailable' });
}

export async function restorePurchases(): Promise<boolean> {
  throw new ApiError({ status: 0, code: 'billing_unavailable' });
}
