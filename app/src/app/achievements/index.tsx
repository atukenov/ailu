import { useQuery } from '@tanstack/react-query';
import { Link, router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { Achievement, AchievementCategory } from '@/api/types';
import { nextToCelebrate } from '@/celebrations';
import { Button, Card, Centered, Chip, ErrorText, ProgressBar, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { isoToText } from '@/dates';
import { useStrings } from '@/i18n';
import { openPaywall } from '@/upsells';

// A partner's mark or confirmation shows up without a manual refresh.
const POLL_MS = 30_000;

const TABS: (AchievementCategory | 'all')[] = ['all', 'firsts', 'events', 'travel'];

export default function Achievements() {
  const s = useStrings();
  const [tab, setTab] = useState<(typeof TABS)[number]>('all');
  const list = useQuery({ queryKey: ['achievements'], queryFn: api.achievements, refetchInterval: POLL_MS });
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });

  // One the partner confirmed while this phone wasn't looking gets its moment here.
  const coupleId = couple.data?.id;
  const items = list.data?.items;
  useFocusEffect(
    useCallback(() => {
      if (!coupleId || !items) return;
      let active = true;
      void nextToCelebrate(coupleId, items).then((a) => {
        if (active && a) router.push({ pathname: '/achievements/celebrate', params: { slug: a.slug } });
      });
      return () => {
        active = false;
      };
    }, [coupleId, items]),
  );

  if (list.isPending) return <Centered />;
  const data = list.data;
  const shown = (data?.items ?? []).filter((a) => tab === 'all' || a.category === tab);
  const lockedCount = (data?.items ?? []).filter((a) => a.state === 'locked').length;

  return (
    <Screen
      refreshControl={
        <RefreshControl refreshing={list.isRefetching} onRefresh={() => void list.refetch()} tintColor={Colors.accent} />
      }>
      <TopBar title={s.achievements} onBack={() => router.back()} />
      <ErrorText error={list.error} />

      {data && (
        <Card>
          <T variant="heading">{s.achievementsProgress(data.earned, data.total)}</T>
          <ProgressBar percent={data.total ? (data.earned * 100) / data.total : 0} />
          <T variant="secondary">{s.achievementsHint}</T>
        </Card>
      )}

      <View style={styles.tabs}>
        {TABS.map((t) => (
          <Chip
            key={t}
            label={t === 'all' ? s.achievementsAll : s.achievementCategories[t]}
            selected={tab === t}
            onPress={() => setTab(t)}
          />
        ))}
      </View>

      <View style={styles.grid}>
        {shown.map((a) => (
          <Tile key={a.slug} achievement={a} />
        ))}
      </View>

      {lockedCount > 0 && (
        <Card>
          <T variant="heading">{s.achievementsLockedTitle(lockedCount)}</T>
          <T variant="secondary">{s.achievementsLockedHint}</T>
          <Button title={s.goalLimitCta} variant="ghost" onPress={() => openPaywall('achievements')} />
        </Card>
      )}
    </Screen>
  );
}

function Tile({ achievement: a }: { achievement: Achievement }) {
  const s = useStrings();
  const earned = a.state === 'earned';
  const meta =
    a.state === 'earned' && a.happenedOn ? s.achievementDoneOn(isoToText(a.happenedOn)) :
    a.state === 'confirm_needed' ? s.achievementConfirmShort :
    a.state === 'waiting_for_partner' ? s.stepWaiting :
    a.state === 'locked' ? s.achievementLockedShort :
    `+${a.nur} ${s.nur}`;
  return (
    <Link href={`/achievements/${a.slug}`} asChild>
      <Pressable accessibilityRole="button" accessibilityLabel={`${a.title}. ${meta}`} style={styles.tileWrap}>
        <Card style={a.state === 'locked' ? { ...styles.tile, ...styles.locked } : styles.tile}>
          <View style={[styles.badge, earned && styles.badgeEarned]}>
            <T style={styles.icon}>{a.state === 'locked' ? '🔒' : a.icon}</T>
          </View>
          <T variant="body" numberOfLines={2} style={styles.center}>
            {a.title}
          </T>
          <T
            variant="small"
            style={[styles.center, a.state === 'confirm_needed' && { color: Colors.accent }, earned && { color: Colors.success }]}>
            {meta}
          </T>
        </Card>
      </Pressable>
    </Link>
  );
}

const styles = StyleSheet.create({
  tabs: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.three },
  tileWrap: { width: '47%', flexGrow: 1 },
  tile: { alignItems: 'center', gap: Spacing.two, minHeight: 170 },
  locked: { opacity: 0.55 },
  center: { textAlign: 'center' },
  badge: {
    width: 64,
    height: 64,
    borderRadius: Radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: Colors.surfaceStrong,
  },
  badgeEarned: { backgroundColor: Colors.accent },
  icon: { fontSize: 30, lineHeight: 38 },
});
