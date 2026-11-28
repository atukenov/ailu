import { useQuery } from '@tanstack/react-query';
import { BlurTargetView, BlurView } from 'expo-blur';
import { Link, router, useFocusEffect } from 'expo-router';
import { type ComponentRef, useCallback, useRef, useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { Achievement, AchievementCategory } from '@/api/types';
import { nextToCelebrate } from '@/celebrations';
import { ACHIEVEMENT_TONES, Badge } from '@/components/badge';
import { Tap } from '@/components/motion';
import { Button, Card, Centered, Chip, ErrorText, ProgressBar, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing, Tones } from '@/constants/theme';
import { isoToText } from '@/dates';
import { useStrings } from '@/i18n';
import { openPaywall } from '@/upsells';
import { usePullRefresh } from '@/refresh';

// A partner's mark or confirmation shows up without a manual refresh.
const POLL_MS = 30_000;

const TABS: (AchievementCategory | 'all')[] = ['all', 'firsts', 'events', 'travel'];

export default function Achievements() {
  const s = useStrings();
  const [tab, setTab] = useState<(typeof TABS)[number]>('all');
  const [refreshing, refresh] = usePullRefresh();
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
        <RefreshControl refreshing={refreshing} onRefresh={() => refresh(list.refetch())} tintColor={Colors.accent} />
      }>
      <TopBar title={s.achievements} onBack={() => router.back()} />
      <ErrorText error={list.error} />

      {data && (
        <Card tone="gold">
          <T variant="heading">🏆 {s.achievementsProgress(data.earned, data.total)}</T>
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
            tone={t === 'all' ? 'lavender' : ACHIEVEMENT_TONES[t]}
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
        <Card tone="lavender">
          <T variant="heading">{s.achievementsLockedTitle(lockedCount)}</T>
          <T variant="secondary">{s.achievementsLockedHint}</T>
          <Button title={s.goalLimitCta} onPress={() => openPaywall('achievements')} />
        </Card>
      )}
    </Screen>
  );
}

function Tile({ achievement: a }: { achievement: Achievement }) {
  const s = useStrings();
  if (a.state === 'locked') return <LockedTile achievement={a} />;
  const earned = a.state === 'earned';
  const tone = ACHIEVEMENT_TONES[a.category];
  const meta =
    a.state === 'earned' && a.happenedOn ? s.achievementDoneOn(isoToText(a.happenedOn)) :
    a.state === 'confirm_needed' ? s.achievementConfirmShort :
    a.state === 'waiting_for_partner' ? s.stepWaiting :
    `+${a.nur} ${s.nur}`;
  return (
    <Link href={`/achievements/${a.slug}`} asChild>
      <Tap accessibilityRole="button" accessibilityLabel={`${a.title}. ${meta}`} style={styles.tileWrap}>
        <Card tone={earned ? tone : undefined} style={styles.tile}>
          <Badge icon={a.icon} tone={tone} earned={earned} />
          <T variant="body" numberOfLines={2} style={styles.center}>
            {a.title}
          </T>
          <T
            variant="small"
            style={[styles.center, a.state === 'confirm_needed' && { color: Colors.accent }, earned && { color: Tones[tone].color, fontWeight: '600' }]}>
            {meta}
          </T>
        </Card>
      </Tap>
    </Link>
  );
}

// Free couples see what a bigger plan holds, but blurred: a tap goes straight to the paywall.
function LockedTile({ achievement: a }: { achievement: Achievement }) {
  const s = useStrings();
  // Android blurs only what sits inside a BlurTargetView; iOS and web blur whatever is behind.
  const target = useRef<ComponentRef<typeof View>>(null);
  return (
    <Tap
      accessibilityRole="button"
      accessibilityLabel={`${s.achievementLockedShort}. ${s.goalLimitCta}`}
      style={styles.tileWrap}
      onPress={() => openPaywall('achievements')}>
      <Card style={{ ...styles.tile, ...styles.lockedTile }}>
        <BlurTargetView ref={target} style={styles.lockedContent}>
          <Badge icon={a.icon} tone={ACHIEVEMENT_TONES[a.category]} earned={false} />
          <T variant="body" numberOfLines={2} style={styles.center}>
            {a.title}
          </T>
          <T variant="small" style={styles.center}>
            +{a.nur} {s.nur}
          </T>
        </BlurTargetView>
        <BlurView
          intensity={30}
          tint="dark"
          blurTarget={target}
          blurMethod="dimezisBlurViewSdk31Plus"
          style={styles.blur}
        />
        <View style={styles.lockOverlay} pointerEvents="none">
          <View style={styles.lockPill}>
            <T variant="small" style={styles.lockText}>
              🔒 {s.achievementLockedShort}
            </T>
          </View>
        </View>
      </Card>
    </Tap>
  );
}

const styles = StyleSheet.create({
  tabs: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.three },
  tileWrap: { width: '47%', flexGrow: 1 },
  tile: { alignItems: 'center', gap: Spacing.two, minHeight: 170 },
  lockedTile: { overflow: 'hidden' },
  lockedContent: { alignItems: 'center', gap: Spacing.two },
  blur: { ...StyleSheet.absoluteFill, borderRadius: Radius.lg, overflow: 'hidden' },
  lockOverlay: { ...StyleSheet.absoluteFill, alignItems: 'center', justifyContent: 'center' },
  lockPill: {
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.one,
    borderRadius: Radius.pill,
    backgroundColor: Colors.lavender,
  },
  lockText: { color: Tones.lavender.onColor, fontWeight: '700' },
  center: { textAlign: 'center' },
});
