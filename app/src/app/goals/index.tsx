import { useQuery } from '@tanstack/react-query';
import { Link, router } from 'expo-router';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { Goal } from '@/api/types';
import { Button, Card, Centered, ErrorText, ProgressBar, RoundButton, Screen, T, TopBar } from '@/components/ui';
import { Colors, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

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
        <Card>
          <T variant="heading">{s.goalsEmpty}</T>
          <T variant="secondary">{s.goalsEmptyHint}</T>
          <Button title={s.addGoal} onPress={() => router.push('/goals/new')} />
        </Card>
      ) : (
        goals.map((g) => <GoalCard key={g.id} goal={g} />)
      )}

      {atLimit && data?.limit != null && (
        <Card>
          <T variant="heading">{s.goalLimitTitle(data.limit)}</T>
          <T variant="secondary">{s.goalLimitHint}</T>
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
  return (
    <Link href={`/goals/${goal.id}`} asChild>
      <Pressable accessibilityRole="button" accessibilityLabel={goal.title}>
        <Card>
          <View style={styles.row}>
            <T variant="heading" style={styles.flex} numberOfLines={2}>
              {goal.title}
            </T>
            {needsMe && <View style={styles.dot} accessibilityLabel={s.needsYou} />}
          </View>
          <T variant="body" style={goal.state === 'done' && { color: Colors.success }}>
            {goal.state === 'done' ? `${s.goalDone} · 100%` : `${goal.progress}%`}
          </T>
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
      </Pressable>
    </Link>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  row: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.two },
  dot: { width: 10, height: 10, borderRadius: 5, marginTop: 10, backgroundColor: Colors.accent },
});
