import { useQuery } from '@tanstack/react-query';
import { LinearGradient } from 'expo-linear-gradient';
import { Link, router } from 'expo-router';
import { RefreshControl, StyleSheet, Text, View } from 'react-native';

import { api } from '@/api/client';
import type { Goal } from '@/api/types';
import { GOAL_LOOK } from '@/components/goals';
import { Pop, Tap } from '@/components/motion';
import { Button, Card, Centered, ErrorText, ProgressBar, RoundButton, Screen, T, TopBar } from '@/components/ui';
import { Colors, Spacing, Tones } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { openPaywall } from '@/upsells';

// A partner's join, step or removal request shows up without a manual refresh.
const POLL_MS = 30_000;

export default function Goals() {
  const s = useStrings();
  const list = useQuery({ queryKey: ['goals'], queryFn: api.goals, refetchInterval: POLL_MS });

  if (list.isPending) return <Centered />;
  const data = list.data;
  const goals = data?.items ?? [];
  // Free couples hit the limit on the third goal; the existing two stay as they are (monetization doc).
  const atLimit = data ? !data.canCreate : false;

  return (
    <Screen
      refreshControl={
        <RefreshControl refreshing={list.isRefetching} onRefresh={() => void list.refetch()} tintColor={Colors.accent} />
      }>
      <TopBar
        title={s.goals}
        onBack={() => router.back()}
        right={atLimit ? undefined : <RoundButton label={s.newGoal} glyph="+" onPress={() => router.push('/goals/new')} />}
      />
      <ErrorText error={list.error} />

      {goals.length === 0 && !list.isError ? (
        <Card tone="mint">
          <T variant="heading">{s.goalsEmpty}</T>
          <T variant="secondary">{s.goalsEmptyHint}</T>
          <Button title={s.addGoal} onPress={() => router.push('/goals/new')} />
        </Card>
      ) : (
        goals.map((g) => <GoalCard key={g.id} goal={g} />)
      )}

      {atLimit && data?.limit != null && (
        <Card tone="gold">
          <T variant="heading">{s.goalLimitTitle(data.limit)}</T>
          <T variant="secondary">{s.goalLimitHint}</T>
          <Button title={s.goalLimitCta} variant="ghost" onPress={() => openPaywall('goals')} />
        </Card>
      )}
    </Screen>
  );
}

function GoalCard({ goal }: { goal: Goal }) {
  const s = useStrings();
  const needsMe =
    goal.state === 'join_needed' ||
    goal.removalRequestedByPartner ||
    goal.steps.some((step) => step.state === 'confirm_needed');
  const done = goal.state === 'done';
  const look = GOAL_LOOK[goal.category];
  const tone = done ? 'mint' : look.tone;
  return (
    <Link href={`/goals/${goal.id}`} asChild>
      <Tap accessibilityRole="button" accessibilityLabel={goal.title} scaleTo={0.97}>
        <Card tone={tone}>
          <View style={styles.row}>
            <Pop>
              <LinearGradient colors={Tones[tone].gradient} style={styles.icon}>
                <Text style={styles.iconText}>{done ? '✓' : look.icon}</Text>
              </LinearGradient>
            </Pop>
            <T variant="heading" style={styles.flex} numberOfLines={2}>
              {goal.title}
            </T>
            {needsMe && <View style={styles.dot} accessibilityLabel={s.needsYou} />}
          </View>
          <Text style={[styles.percent, { color: Tones[tone].color }]}>
            {done ? `${s.goalDone} · 100%` : `${goal.progress}%`}
          </Text>
          <ProgressBar percent={goal.progress} />
          <T variant="small">
            {[
              s.goalCategories[goal.category],
              goal.targetMonth && s.monthYear(goal.targetMonth),
              goal.stepsTotal > 0 && s.stepsProgress(goal.stepsDone, goal.stepsTotal),
            ]
              .filter(Boolean)
              .join(' · ')}
          </T>
        </Card>
      </Tap>
    </Link>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  row: { flexDirection: 'row', alignItems: 'center', gap: Spacing.three },
  icon: { width: 44, height: 44, borderRadius: 22, alignItems: 'center', justifyContent: 'center' },
  iconText: { fontSize: 20, lineHeight: 24, fontWeight: '800', color: Tones.mint.onColor },
  percent: { fontSize: 18, fontWeight: '800' },
  dot: { width: 10, height: 10, borderRadius: 5, backgroundColor: Colors.accent },
});
