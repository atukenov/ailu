import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { Storage } from '@/api/types';
import { Moon } from '@/components/moon';
import { Button, Card, ErrorText, Screen, T } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings, type Strings } from '@/i18n';
import { purchase, useBilling, type Period, type Tier } from '@/purchases';
import { biggerPlan, type PaywallReason } from '@/upsells';

const NAMES: Record<Tier, string> = { plus: 'Plus', premium: 'Premium' };

// Placeholder prices from the monetization doc, used until billing answers. A year is twelve months
// less 20% (Lev, 2026-10-17); the server's products carry the same numbers.
const YEARLY_DISCOUNT = 20;
const MONTHLY_KZT: Record<Tier, number> = { plus: 1490, premium: 2490 };

const tenge = (n: number) => `${String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ')} ₸`;

// Each soft moment offers Plus; only day 7 of the streak offers Premium right away (monetization
// doc). A Plus couple out of photo room is offered Premium. Opened without a reason, the paywall
// shows both.
function tiersFor(reason: PaywallReason | undefined, plan: Storage['plan'] | undefined): Tier[] {
  if (reason === 'streak') return ['premium', 'plus'];
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
      router.back();
    },
  });

  const { title, body } = heading(s, reason, storage.data);
  const tiers = tiersFor(reason, storage.data?.plan);
  const current = storage.data?.plan;

  return (
    <Screen>
      <View style={styles.hero}>
        <Moon fill={reason === 'streak' ? 1 : 0.75} glow={reason === 'streak'} />
        <T variant="title" style={styles.center}>
          {title}
        </T>
        <T variant="secondary" style={styles.center}>
          {body}
        </T>
      </View>

      <View style={styles.periods} accessibilityRole="radiogroup">
        {(['month', 'year'] as const).map((p) => (
          <Pressable
            key={p}
            accessibilityRole="radio"
            accessibilityState={{ checked: period === p }}
            onPress={() => setPeriod(p)}
            style={[styles.period, period === p && styles.periodOn]}>
            <T variant="body" style={period === p ? styles.periodTextOn : undefined}>
              {p === 'month' ? s.periodMonth : s.periodYear}
              {p === 'year' && <T style={styles.save}> {s.yearlySave(YEARLY_DISCOUNT)}</T>}
            </T>
          </Pressable>
        ))}
      </View>

      {tiers.map((tier) => (
        <Card key={tier}>
          <View style={styles.tierHead}>
            <T style={styles.tierName}>{NAMES[tier].toUpperCase()}</T>
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
            <T variant="small" style={styles.includes}>
              {s.premiumIncludes}
            </T>
          )}
          <View style={styles.features}>
            {(tier === 'plus' ? s.plusFeatures : s.premiumFeatures).map((f) => (
              <T key={f} variant="body">
                {f}
              </T>
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
      <T variant="small" style={styles.center}>
        {s.cancelAnytime}
      </T>
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { alignItems: 'center', gap: Spacing.three, paddingTop: Spacing.four },
  center: { textAlign: 'center' },
  tierHead: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'baseline' },
  tierName: { fontSize: 22, fontWeight: '500', letterSpacing: 3, color: Colors.accent },
  includes: { color: Colors.accent, fontWeight: '600' },
  features: { gap: Spacing.two },
  active: { color: Colors.success, textAlign: 'center' },
  right: { textAlign: 'right' },
  periods: { flexDirection: 'row', gap: Spacing.two },
  period: {
    flex: 1,
    alignItems: 'center',
    paddingVertical: Spacing.three,
    borderRadius: Radius.pill,
    borderWidth: 1,
    borderColor: Colors.surface,
  },
  periodOn: { backgroundColor: Colors.surface, borderColor: Colors.accent },
  periodTextOn: { fontWeight: '600' },
  save: { color: Colors.accent, fontWeight: '600' },
});
