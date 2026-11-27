import { useInfiniteQuery } from '@tanstack/react-query';
import { Image } from 'expo-image';
import { LinearGradient } from 'expo-linear-gradient';
import { router, type Href } from 'expo-router';
import { RefreshControl, StyleSheet, View } from 'react-native';

import { api, photoUrl } from '@/api/client';
import type { TimelineEvent } from '@/api/types';
import { Tap } from '@/components/motion';
import { Button, Card, Centered, ErrorText, Screen, T, TopBar } from '@/components/ui';
import { BackgroundGradient, Colors, Spacing, Tones, type Tone } from '@/constants/theme';
import { useStrings, type Strings } from '@/i18n';
import { openPaywall } from '@/upsells';

// The relationship history: everything that happened to the couple, newest first, on one line.
export default function Timeline() {
  const s = useStrings();
  const list = useInfiniteQuery({
    queryKey: ['timeline'],
    queryFn: ({ pageParam }) => api.timeline(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  });

  if (list.isPending) return <Centered />;
  const pages = list.data?.pages ?? [];
  const first = pages[0];
  const items = pages.flatMap((p) => p.items);
  const last = pages[pages.length - 1];

  // On the free plan the older moments sit between the last 90 days and the day the couple began.
  const locked = !list.hasNextPage && last?.hidden ? { count: last.hidden, days: last.freeDays ?? 0 } : null;
  const startAt = locked ? items.findIndex((e) => e.kind === 'start') : -1;
  const recent = startAt >= 0 ? items.slice(0, startAt) : items;
  const tail = startAt >= 0 ? items.slice(startAt) : [];

  return (
    <Screen
      refreshControl={
        <RefreshControl refreshing={list.isRefetching} onRefresh={() => void list.refetch()} tintColor={Colors.accent} />
      }>
      <TopBar title={s.timeline} onBack={() => router.back()} />
      <ErrorText error={list.error} />
      {first && (
        <T variant="secondary">
          {s.timelineSummary(first.summary.daysTogether, first.summary.memories, s.number(first.summary.nurEarned))}
        </T>
      )}

      <View style={styles.line}>
        <LinearGradient
          colors={[Colors.accent, Colors.lavender, Colors.sky, 'rgba(127, 200, 255, 0.1)']}
          style={styles.rail}
        />
        {recent.map((e, i) => (
          <Moment key={e.id} event={e} fade={i} />
        ))}
        {locked && (
          <Row fade={recent.length} tone="gold">
            <Card tone="gold">
              <T variant="heading">{s.timelineLocked(locked.count)}</T>
              <T variant="secondary">{s.timelineLockedBody(locked.days)}</T>
              <Button title={s.timelineLockedCta} variant="ghost" onPress={() => openPaywall('history')} />
            </Card>
          </Row>
        )}
        {tail.map((e, i) => (
          <Moment key={e.id} event={e} fade={recent.length + 1 + i} />
        ))}
      </View>

      {list.hasNextPage && (
        <Button
          title={s.showMore}
          variant="ghost"
          onPress={() => void list.fetchNextPage()}
          loading={list.isFetchingNextPage}
        />
      )}
    </Screen>
  );
}

// The dot on the line dims as the history goes back, like in the mockup.
function Row({ fade, children, tone = 'pink' }: { fade: number; children: React.ReactNode; tone?: Tone }) {
  return (
    <View style={styles.row}>
      <View
        style={[
          styles.dot,
          { backgroundColor: Tones[tone].color, boxShadow: `0 0 10px ${Tones[tone].color}`, opacity: Math.max(0.45, 1 - fade * 0.12) },
        ]}
      />
      {children}
    </View>
  );
}

function Moment({ event, fade }: { event: TimelineEvent; fade: number }) {
  const s = useStrings();
  const { title, body, href } = describe(s, event);
  const tone = TONES[event.kind];
  const content = (
    <Card style={styles.card} tone={tone}>
      <View style={styles.cardText}>
        <T variant="label">
          {s.date(event.date)}
        </T>
        <T variant="heading">{title}</T>
        {body && <T variant="small">{body}</T>}
      </View>
      {event.thumb && (
        <Image source={{ uri: photoUrl(event.thumb) }} style={styles.thumb} contentFit="cover" transition={150} />
      )}
    </Card>
  );
  return (
    <Row fade={fade} tone={tone}>
      {href ? (
        <Tap accessibilityRole="button" accessibilityLabel={title} onPress={() => router.push(href)}>
          {content}
        </Tap>
      ) : (
        content
      )}
    </Row>
  );
}

// Each kind of moment keeps its colour: memories pink, phases gold, goals mint, achievements coral.
const TONES: Record<TimelineEvent['kind'], Tone> = {
  start: 'lavender',
  days_together: 'pink',
  anniversary: 'pink',
  phase: 'gold',
  first_task: 'sky',
  first_answers: 'lavender',
  memory: 'pink',
  goal_done: 'mint',
  achievement: 'coral',
};

function describe(s: Strings, e: TimelineEvent): { title: string; body: string | null; href?: Href } {
  switch (e.kind) {
    case 'start':
      return { title: s.timelineStart, body: s.timelineStartBody };
    case 'days_together':
      return { title: s.timelineDays(e.days ?? 0), body: s.timelineDaysBody };
    case 'anniversary':
      return { title: s.timelineYears(e.years ?? 0), body: s.timelineYearsBody };
    case 'phase': {
      const phase = s.phases[e.phase ?? 0];
      const name = (e.cycle ?? 1) > 1 ? `${phase.name} · ${s.cycle(e.cycle ?? 1)}` : phase.name;
      return { title: s.timelinePhase(name), body: s.timelinePhaseBody(phase.moon) };
    }
    case 'first_task':
      return { title: s.timelineFirstTask, body: s.timelineFirstTaskBody };
    case 'first_answers':
      return { title: s.timelineFirstAnswers, body: s.timelineFirstAnswersBody };
    case 'memory':
      return {
        title: e.title ?? s.timelineMemory,
        body: [e.place, e.photos ? s.photosStored(e.photos) : null].filter(Boolean).join(' · ') || s.timelineMemory,
        href: `/memories/${e.memoryId}`,
      };
    case 'goal_done':
      return { title: e.title ?? s.goalDone, body: s.goalDone, href: `/goals/${e.goalId}` };
    case 'achievement':
      return {
        title: [e.icon, e.title ?? s.achievements].filter(Boolean).join(' '),
        body: s.timelineAchievementBody,
        href: `/achievements/${e.slug}`,
      };
  }
}

const styles = StyleSheet.create({
  line: { position: 'relative', paddingLeft: 26, gap: Spacing.three },
  rail: {
    position: 'absolute',
    left: 5,
    top: 8,
    bottom: 8,
    width: 2,
    borderRadius: 1,
    opacity: 0.6,
  },
  row: { position: 'relative' },
  dot: {
    position: 'absolute',
    left: -26,
    top: 22,
    width: 12,
    height: 12,
    borderRadius: 6,
    borderWidth: 2,
    borderColor: BackgroundGradient[1],
  },
  card: { flexDirection: 'row', alignItems: 'center', gap: Spacing.three },
  cardText: { flex: 1, gap: Spacing.one },
  thumb: { width: 64, height: 64, borderRadius: 14, backgroundColor: Colors.surface },
});
