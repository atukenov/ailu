import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { Storage } from '@/api/types';
import { Moon } from '@/components/moon';
import { Button, Card, ErrorText, Screen, T } from '@/components/ui';
import { Colors, Spacing } from '@/constants/theme';
import { useStrings, type Strings } from '@/i18n';
import { purchase, type Tier } from '@/purchases';
import type { PaywallReason } from '@/upsells';

const NAMES: Record<Tier, string> = { plus: 'Plus', premium: 'Premium' };

// Monthly prices from the monetization doc, until the stores' localized prices come from billing.
const PRICES: Record<Tier, string> = { plus: '1 490 ₸', premium: '2 490 ₸' };

// Each soft moment offers Plus; only day 7 of the streak offers Premium right away (monetization
// doc). Opened without a reason, the paywall shows both.
function tiersFor(reason: PaywallReason | undefined): Tier[] {
  if (reason === 'streak') return ['premium', 'plus'];
  if (reason) return ['plus'];
  return ['plus', 'premium'];
}

function heading(s: Strings, reason: PaywallReason | undefined, storage: Storage | undefined) {
  switch (reason) {
    // At the 201st photo this is the limit screen: it says plainly that nothing saved goes anywhere.
    case 'photos':
      if (storage?.photoLimit == null) break;
      return { title: s.photoLimitTitle(storage.photoLimit), body: s.photoLimitBody };
    case 'task':
      return { title: s.paywallTaskTitle, body: s.paywallTaskBody };
    case 'streak':
      return { title: s.paywallStreakTitle, body: s.streakUpsellBody };
    case 'goals':
      return { title: s.paywallGoalsTitle, body: s.paywallGoalsBody };
  }
  return { title: s.paywallTitle, body: s.paywallBody };
}

export default function Paywall() {
  const s = useStrings();
  const queryClient = useQueryClient();
  const { reason } = useLocalSearchParams<{ reason?: PaywallReason }>();
  const storage = useQuery({ queryKey: ['storage'], queryFn: api.storage });

  const buy = useMutation({
    mutationFn: purchase,
    onSuccess: (changed) => {
      if (!changed) return;
      // The plan lives on the couple, so both partners' limits change; refetch what depends on it.
      void queryClient.invalidateQueries({ queryKey: ['storage'] });
      void queryClient.invalidateQueries({ queryKey: ['couple'] });
      void queryClient.invalidateQueries({ queryKey: ['today'] });
      void queryClient.invalidateQueries({ queryKey: ['goals'] });
      router.back();
    },
  });

  const { title, body } = heading(s, reason, storage.data);
  const tiers = tiersFor(reason);
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

      {tiers.map((tier) => (
        <Card key={tier}>
          <View style={styles.tierHead}>
            <T style={styles.tierName}>{NAMES[tier].toUpperCase()}</T>
            <T variant="heading">
              {PRICES[tier]}
              <T variant="small"> {s.perMonth}</T>
            </T>
          </View>
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
              onPress={() => buy.mutate(tier)}
              loading={buy.isPending && buy.variables === tier}
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
});
