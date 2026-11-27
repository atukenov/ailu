import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { LinearGradient } from 'expo-linear-gradient';
import { Platform, StyleSheet, Text, View } from 'react-native';

import { api } from '@/api/client';
import type { Storage } from '@/api/types';
import { LegalLinks } from '@/components/legal-links';
import { Moon } from '@/components/moon';
import { Tap, tapFeedback } from '@/components/motion';
import { Button, Card, ErrorText, Screen, T } from '@/components/ui';
import { Colors, Radius, Spacing, Tones, type Tone } from '@/constants/theme';
import { useStrings, type Strings } from '@/i18n';
import { purchase, restorePurchases, useBilling, type Period, type Tier } from '@/purchases';
import { biggerPlan, type PaywallReason } from '@/upsells';

const NAMES: Record<Tier, string> = { plus: 'Plus', premium: 'Premium' };
// Plus is the couple's pink; Premium is the moon's gold.
const TONES: Record<Tier, Tone> = { plus: 'pink', premium: 'gold' };

// Placeholder prices from the monetization doc, used until billing answers. A year is twelve months
// less 20% (Lev, 2026-10-17); the server's products carry the same numbers.
const YEARLY_DISCOUNT = 20;
const MONTHLY_KZT: Record<Tier, number> = { plus: 1490, premium: 2490 };

const tenge = (n: number) => `${String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ')} ₸`;

// Each soft moment offers Plus; only day 7 of the streak offers Premium right away (monetization
// doc). A Plus couple out of photo room is offered Premium, and AI selections are Premium only. Opened
// without a reason, the paywall shows both.
function tiersFor(reason: PaywallReason | undefined, plan: Storage['plan'] | undefined): Tier[] {
  if (reason === 'streak') return ['premium', 'plus'];
  if (reason === 'ai') return ['premium'];
  if (reason === 'photos') return [biggerPlan(plan) ?? 'plus'];
  if (reason) return ['plus'];
  return ['plus', 'premium'];
}

function heading(s: Strings, reason: PaywallReason | undefined, storage: Storage | undefined) {
  switch (reason) {
    // At the 201st photo this is the limit screen: it says plainly that nothing saved goes anywhere.
    case 'photos':
      if (storage?.photoLimit == null) break;
      return {
        title: s.photoLimitTitle(storage.photoLimit),
        // The next plan's limits, as the server sets them: Plus 500, Premium 1 200.
        body: storage.plan === 'plus' ? s.photoLimitBody('Premium', 1200) : s.photoLimitBody('Plus', 500),
      };
    case 'task':
      return { title: s.paywallTaskTitle, body: s.paywallTaskBody };
    case 'streak':
      return { title: s.paywallStreakTitle, body: s.streakUpsellBody };
    case 'goals':
      return { title: s.paywallGoalsTitle, body: s.paywallGoalsBody };
    case 'quiz':
      return { title: s.paywallQuizTitle, body: s.paywallQuizBody };
    case 'history':
      return { title: s.paywallHistoryTitle, body: s.paywallHistoryBody };
    case 'achievements':
      return { title: s.paywallAchievementsTitle, body: s.paywallAchievementsBody };
    case 'ai':
      return { title: s.paywallAiTitle, body: s.paywallAiBody };
  }
  return { title: s.paywallTitle, body: s.paywallBody };
}

export default function Paywall() {
  const s = useStrings();
  const queryClient = useQueryClient();
  const { reason } = useLocalSearchParams<{ reason?: PaywallReason }>();
  const storage = useQuery({ queryKey: ['storage'], queryFn: api.storage });
  const billing = useBilling();
  const [period, setPeriod] = useState<Period>('year');

  const priceOf = (tier: Tier, p: Period) =>
    billing.data?.products.find((x) => x.tier === tier && x.period === p)?.priceKzt ??
    (p === 'year' ? (MONTHLY_KZT[tier] * 12 * (100 - YEARLY_DISCOUNT)) / 100 : MONTHLY_KZT[tier]);

  const buy = useMutation({
    mutationFn: purchase,
    onSuccess: (changed) => {
      if (!changed) return;
      // The plan lives on the couple, so both partners' limits change; refetch what depends on it.
      void queryClient.invalidateQueries({ queryKey: ['storage'] });
      void queryClient.invalidateQueries({ queryKey: ['couple'] });
      void queryClient.invalidateQueries({ queryKey: ['today'] });
      void queryClient.invalidateQueries({ queryKey: ['goals'] });
      void queryClient.invalidateQueries({ queryKey: ['love-quiz'] });
      void queryClient.invalidateQueries({ queryKey: ['achievements'] });
      router.back();
    },
  });

  // Restoring is offered right here too: App Review expects it next to the subscriptions.
  const restore = useMutation({
    mutationFn: restorePurchases,
    onSuccess: (restored) => {
      if (restored) void queryClient.invalidateQueries();
    },
  });
  const native = Platform.OS === 'ios' || Platform.OS === 'android';

  const { title, body } = heading(s, reason, storage.data);
  const tiers = tiersFor(reason, storage.data?.plan);
  const current = storage.data?.plan;

  return (
    <Screen>
      <View style={styles.hero}>
        <Moon fill={reason === 'streak' ? 1 : 0.75} glow size={110} />
        <T variant="title" style={styles.center}>
          {title}
        </T>
        <T variant="secondary" style={styles.center}>
          {body}
        </T>
      </View>

      <View style={styles.periods} accessibilityRole="radiogroup">
        {(['month', 'year'] as const).map((p) => (
          <Tap
            key={p}
            accessibilityRole="radio"
            accessibilityState={{ checked: period === p }}
            onPress={() => {
              tapFeedback();
              setPeriod(p);
            }}
            style={[styles.period, period === p && styles.periodOn]}>
            {period === p && (
              <LinearGradient colors={Tones.lavender.wash} start={{ x: 0, y: 0 }} end={{ x: 1, y: 1 }} style={StyleSheet.absoluteFill} />
            )}
            <T variant="body" style={period === p ? styles.periodTextOn : undefined}>
              {p === 'month' ? s.periodMonth : s.periodYear}
            </T>
            {p === 'year' && (
              <LinearGradient colors={Tones.mint.gradient} start={{ x: 0, y: 0 }} end={{ x: 1, y: 1 }} style={styles.save}>
                <Text style={styles.saveText}>{s.yearlySave(YEARLY_DISCOUNT)}</Text>
              </LinearGradient>
            )}
          </Tap>
        ))}
      </View>

      {tiers.map((tier) => (
        <Card key={tier} tone={TONES[tier]}>
          <View style={styles.tierHead}>
            <LinearGradient colors={Tones[TONES[tier]].gradient} start={{ x: 0, y: 0 }} end={{ x: 1, y: 1 }} style={styles.tierPill}>
              <Text style={[styles.tierName, { color: Tones[TONES[tier]].onColor }]}>
                {tier === 'premium' ? '★ ' : '♥ '}
                {NAMES[tier].toUpperCase()}
              </Text>
            </LinearGradient>
            <T variant="heading">
              {tenge(priceOf(tier, period))}
              <T variant="small"> {period === 'year' ? s.perYear : s.perMonth}</T>
            </T>
          </View>
          {period === 'year' && (
            <T variant="small" style={styles.right}>
              {s.yearlyPerMonth(tenge(priceOf(tier, 'year') / 12))}
            </T>
          )}
          {tier === 'premium' && (
            <T variant="small" style={[styles.includes, { color: Tones.gold.color }]}>
              {s.premiumIncludes}
            </T>
          )}
          <View style={styles.features}>
            {(tier === 'plus' ? s.plusFeatures : s.premiumFeatures).map((f) => (
              <View key={f} style={styles.feature}>
                <Text style={[styles.tick, { color: Tones[TONES[tier]].color }]}>✓</Text>
                <T variant="body" style={styles.flex}>
                  {f}
                </T>
              </View>
            ))}
          </View>
          {current === tier ? (
            <T variant="small" style={styles.active}>
              {s.planActive(NAMES[tier])}
            </T>
          ) : (
            <Button
              title={s.choosePlan(NAMES[tier])}
              variant={tier === tiers[0] ? 'primary' : 'ghost'}
              onPress={() => buy.mutate({ tier, period })}
              loading={buy.isPending && buy.variables?.tier === tier}
              disabled={buy.isPending}
            />
          )}
        </Card>
      ))}

      <ErrorText error={buy.error} />
      <Button title={s.continueFree} variant="ghost" onPress={() => router.back()} />
      <Button title={s.paywallHaveCode} variant="ghost" onPress={() => router.replace('/redeem')} />
      {native && (
        <Button title={s.planRestore} variant="ghost" onPress={() => restore.mutate()} loading={restore.isPending} />
      )}
      {restore.isSuccess && (
        <T variant="small" style={styles.center}>
          {restore.data ? s.planRestored : s.planNothingToRestore}
        </T>
      )}
      <ErrorText error={restore.error} />
      <T variant="small" style={styles.terms}>
        {s.subscriptionTerms(Platform.OS === 'android' ? 'google' : 'apple')}
      </T>
      <LegalLinks />
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { alignItems: 'center', gap: Spacing.three, paddingTop: Spacing.four },
  center: { textAlign: 'center' },
  terms: { textAlign: 'center', color: Colors.textSecondary },
  tierHead: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: Spacing.two },
  tierPill: { paddingHorizontal: 14, paddingVertical: 6, borderRadius: Radius.pill },
  tierName: { fontSize: 15, fontWeight: '800', letterSpacing: 2 },
  feature: { flexDirection: 'row', gap: Spacing.two, alignItems: 'flex-start' },
  tick: { fontSize: 16, lineHeight: 24, fontWeight: '800' },
  flex: { flex: 1 },
  includes: { color: Colors.accent, fontWeight: '600' },
  features: { gap: Spacing.two },
  active: { color: Colors.success, textAlign: 'center' },
  right: { textAlign: 'right' },
  periods: { flexDirection: 'row', gap: Spacing.two },
  period: {
    flex: 1,
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 6,
    justifyContent: 'center',
    overflow: 'hidden',
    alignItems: 'center',
    paddingVertical: Spacing.three,
    borderRadius: Radius.pill,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  periodOn: { borderColor: Colors.lavender },
  periodTextOn: { fontWeight: '700' },
  save: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: Radius.pill },
  saveText: { color: Tones.mint.onColor, fontSize: 12, fontWeight: '800' },
});
