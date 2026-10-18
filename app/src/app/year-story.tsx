import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Image } from 'expo-image';
import { router } from 'expo-router';
import { useEffect, useState } from 'react';
import { Linking, Pressable, RefreshControl, StyleSheet, View } from 'react-native';

import { api, photoUrl } from '@/api/client';
import type { StorySlide, YearStory } from '@/api/types';
import { Button, Card, Centered, Chip, ErrorText, Screen, T, TopBar } from '@/components/ui';
import { Moon } from '@/components/moon';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings, type Strings } from '@/i18n';
import { buyYearStory, storyPrice } from '@/purchases';
import { useSession } from '@/session';

// While the film is being made, the screen notices when it is ready.
const POLL_MS = 60_000;

// The video story of the year (roadmap phase 4): a one-minute film from the year's best memories and dates.
// The preview plays the same slides the film will have, so the couple sees what they are paying for.
export default function YearStoryScreen() {
  const s = useStrings();
  const queryClient = useQueryClient();
  const [year, setYear] = useState<number | undefined>(undefined);
  const story = useQuery({
    queryKey: ['year-story', year ?? 'default'],
    queryFn: () => api.yearStory(year),
    refetchInterval: (q) => (q.state.data?.order?.status === 'making' ? POLL_MS : false),
  });
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });
  const { user } = useSession();
  const data = story.data;
  const price = useQuery({
    queryKey: ['year-story-price'],
    queryFn: () => storyPrice(data!),
    enabled: !!data,
    staleTime: Infinity,
  });
  const buy = useMutation({
    mutationFn: () => buyYearStory(data!),
    onSuccess: (ordered) => {
      if (ordered) queryClient.setQueryData(['year-story', year ?? 'default'], ordered);
    },
  });

  if (story.isPending) return <Centered />;
  const partnerName = couple.data?.partner?.name ?? s.partnerFallback;
  const names = { me: user?.name ?? '', partner: partnerName };

  return (
    <Screen
      refreshControl={
        <RefreshControl refreshing={story.isRefetching} onRefresh={() => void story.refetch()} tintColor={Colors.accent} />
      }>
      <TopBar title={s.yearStoryShort} onBack={() => router.back()} />
      <ErrorText error={story.error} />
      {data && (
        <>
          {data.years.length > 1 && (
            <View style={styles.years}>
              {data.years.map((y) => (
                <Chip key={y} label={String(y)} selected={y === data.year} onPress={() => setYear(y)} />
              ))}
            </View>
          )}
          <View style={styles.titleBlock}>
            <T variant="label">{s.yearStoryKicker}</T>
            <T variant="title">{s.yearStoryTitle(data.year)}</T>
          </View>

          <Player key={data.year} story={data} names={names} />
          <T variant="small">
            {s.yearStoryLength(Math.round(data.durationMs / 1000), data.memories)}
            {data.order ? '' : ` · ${s.yearStoryPreview}`}
          </T>

          <OrderCard
            story={data}
            partnerName={partnerName}
            price={price.data ?? `${s.number(data.priceKzt)} ₸`}
            onBuy={() => buy.mutate()}
            buying={buy.isPending}
          />
          <ErrorText error={buy.error} />
        </>
      )}
    </Screen>
  );
}

function OrderCard({
  story,
  partnerName,
  price,
  onBuy,
  buying,
}: {
  story: YearStory;
  partnerName: string;
  price: string;
  onBuy: () => void;
  buying: boolean;
}) {
  const s = useStrings();
  const order = story.order;

  if (order?.status === 'ready' && order.video) {
    return (
      <Card>
        <T variant="heading">{s.yearStoryReady}</T>
        <T variant="secondary">{s.yearStoryReadyBody}</T>
        <Button title={s.yearStoryDownload} onPress={() => void Linking.openURL(photoUrl(order.video!))} />
      </Card>
    );
  }
  if (order?.status === 'failed') {
    return (
      <Card>
        <T variant="secondary">{s.yearStoryFailed}</T>
      </Card>
    );
  }
  if (order) {
    return (
      <Card>
        <T variant="secondary">{order.paidByMe ? s.yearStoryMaking : s.yearStoryMakingPartner(partnerName)}</T>
      </Card>
    );
  }

  const missing = story.minMemories - story.memories;
  return (
    <Card>
      <T variant="secondary">{s.yearStoryIntro}</T>
      {missing > 0 ? (
        <>
          <T variant="body">{s.yearStoryNeedMore(missing)}</T>
          <Button title={s.yearStoryAddMemory} variant="ghost" onPress={() => router.push('/memories/new')} />
        </>
      ) : !story.open ? (
        <T variant="body">{s.yearStoryOpensOn(s.date(story.opensOn))}</T>
      ) : (
        <>
          <Button title={s.yearStoryBuy(price)} onPress={onBuy} loading={buying} />
          <T variant="small">{s.yearStoryBuyNote}</T>
        </>
      )}
    </Card>
  );
}

// Plays the slides like stories: a bar per slide on top, tap the left third to go back, the right
// two thirds to go on, and the button to pause. It starts paused, so nothing moves until asked.
function Player({ story, names }: { story: YearStory; names: { me: string; partner: string } }) {
  const s = useStrings();
  const slides = story.slides;
  const [{ index, elapsed }, setPos] = useState({ index: 0, elapsed: 0 });
  const [playing, setPlaying] = useState(false);

  const slide = slides[Math.min(index, slides.length - 1)];
  const ended = !!slide && index === slides.length - 1 && elapsed >= slide.durationMs;
  const running = playing && !ended;

  useEffect(() => {
    if (!running) return;
    let last = Date.now();
    const timer = setInterval(() => {
      const now = Date.now();
      const step = now - last;
      last = now;
      setPos((p) => {
        const length = slides[p.index]?.durationMs ?? 0;
        if (p.elapsed + step < length) return { index: p.index, elapsed: p.elapsed + step };
        return p.index < slides.length - 1 ? { index: p.index + 1, elapsed: 0 } : { index: p.index, elapsed: length };
      });
    }, 100);
    return () => clearInterval(timer);
  }, [running, slides]);

  // Upcoming photos load before their turn.
  useEffect(() => {
    const next = slides.slice(index + 1, index + 3).flatMap((x) => (x.photo ? [photoUrl(x.photo)] : []));
    if (next.length) void Image.prefetch(next);
  }, [index, slides]);

  if (!slide) return null;

  const go = (to: number) => setPos({ index: Math.max(0, Math.min(slides.length - 1, to)), elapsed: 0 });
  const toggle = () => {
    if (ended) {
      go(0);
      setPlaying(true);
    } else setPlaying(!playing);
  };

  return (
    <View style={styles.player}>
      <SlideView slide={slide} year={story.year} names={names} />
      <View style={styles.bars}>
        {slides.map((x, i) => (
          <View key={i} style={[styles.bar, { flex: x.durationMs }]}>
            <View
              style={[
                styles.barFill,
                { width: `${i < index ? 100 : i > index ? 0 : Math.min(100, (elapsed / x.durationMs) * 100)}%` },
              ]}
            />
          </View>
        ))}
      </View>
      <View style={styles.taps}>
        <Pressable accessibilityRole="button" accessibilityLabel={s.back} style={styles.tapBack} onPress={() => go(index - 1)} />
        <Pressable accessibilityRole="button" accessibilityLabel={s.storyNext} style={styles.tapNext} onPress={() => go(index + 1)} />
      </View>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={running ? s.storyPause : s.storyPlay}
        onPress={toggle}
        style={styles.play}>
        <T variant="body" style={styles.playText}>
          {running ? '❚❚' : '▶'}
        </T>
      </Pressable>
    </View>
  );
}

function SlideView({ slide, year, names }: { slide: StorySlide; year: number; names: { me: string; partner: string } }) {
  const s = useStrings();
  if (slide.kind === 'memory' && slide.photo) {
    return (
      <View style={styles.fill}>
        <Image source={{ uri: photoUrl(slide.photo) }} style={styles.fill} contentFit="cover" transition={400} />
        <View style={styles.caption}>
          {slide.date && (
            <T variant="label" style={styles.captionDate}>
              {s.date(slide.date)}
            </T>
          )}
          <T variant="heading" style={styles.captionTitle}>
            {slide.title}
          </T>
          {slide.place && <T variant="small" style={styles.captionPlace}>{slide.place}</T>}
        </View>
      </View>
    );
  }

  const { kicker, title, lines, moon } = describe(s, slide, year, names);
  return (
    <View style={[styles.fill, styles.card]}>
      <Moon fill={moon} glow={slide.kind === 'intro' || slide.kind === 'outro'} />
      {kicker && (
        <T variant="label" style={styles.cardKicker}>
          {kicker}
        </T>
      )}
      <T variant="title" style={styles.cardTitle}>
        {title}
      </T>
      {lines.map((line) => (
        <T key={line} variant="secondary" style={styles.cardLine}>
          {line}
        </T>
      ))}
    </View>
  );
}

function describe(
  s: Strings,
  slide: StorySlide,
  year: number,
  names: { me: string; partner: string },
): { kicker: string | null; title: string; lines: string[]; moon: number } {
  const date = slide.date ? s.date(slide.date) : null;
  switch (slide.kind) {
    case 'intro':
      return { kicker: s.storyIntroLine, title: s.storyIntroYear(year), lines: [s.coupleNames(names.me, names.partner)], moon: 0.5 };
    case 'start':
      return { kicker: date, title: s.storyStart, lines: [], moon: 0.1 };
    case 'days_together':
      return { kicker: date, title: s.timelineDays(slide.days ?? 0), lines: [s.timelineDaysBody], moon: 0.6 };
    case 'anniversary':
      return { kicker: date, title: s.timelineYears(slide.years ?? 0), lines: [s.timelineYearsBody], moon: 1 };
    case 'goal_done':
      return { kicker: date, title: s.storyGoalDone, lines: slide.title ? [`«${slide.title}»`] : [], moon: 0.75 };
    case 'achievement':
      return {
        kicker: date,
        title: s.storyAchievement,
        lines: [[slide.icon, slide.title].filter(Boolean).join(' ')],
        moon: 0.75,
      };
    case 'outro': {
      const st = slide.stats;
      return {
        kicker: String(year),
        title: s.storyOutro,
        // Only what the couple actually has: "0 tasks" is no way to end a film.
        lines: st ? s.storyStats(st.daysTogether, st.memories, st.tasks, st.nur, s.number(st.nur)) : [],
        moon: 1,
      };
    }
    default:
      return { kicker: date, title: slide.title ?? '', lines: [], moon: 0.5 };
  }
}

const styles = StyleSheet.create({
  years: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
  titleBlock: { gap: Spacing.two },
  player: {
    aspectRatio: 3 / 4,
    borderRadius: Radius.lg,
    overflow: 'hidden',
    backgroundColor: '#120E0C',
    borderWidth: 1,
    borderColor: Colors.border,
  },
  fill: { ...StyleSheet.absoluteFill },
  card: { alignItems: 'center', justifyContent: 'center', gap: Spacing.three, padding: Spacing.four },
  cardKicker: { color: Colors.accent, textAlign: 'center' },
  cardTitle: { textAlign: 'center' },
  cardLine: { textAlign: 'center' },
  caption: {
    position: 'absolute',
    left: 0,
    right: 0,
    bottom: 0,
    padding: Spacing.four,
    paddingRight: Spacing.six + Spacing.three,
    gap: Spacing.one,
    backgroundColor: 'rgba(18, 14, 12, 0.55)',
  },
  captionDate: { color: Colors.accent },
  captionTitle: { color: Colors.text },
  captionPlace: { color: Colors.textSecondary },
  bars: { position: 'absolute', top: Spacing.three, left: Spacing.three, right: Spacing.three, flexDirection: 'row', gap: 3 },
  bar: { height: 3, borderRadius: 2, backgroundColor: 'rgba(255, 255, 255, 0.25)', overflow: 'hidden' },
  barFill: { height: 3, backgroundColor: Colors.text },
  taps: { ...StyleSheet.absoluteFill, flexDirection: 'row' },
  tapBack: { flex: 1 },
  tapNext: { flex: 2 },
  play: {
    position: 'absolute',
    bottom: Spacing.three,
    right: Spacing.three,
    width: 48,
    height: 48,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: Radius.pill,
    backgroundColor: 'rgba(28, 23, 20, 0.75)',
    borderWidth: 1,
    borderColor: Colors.border,
  },
  playText: { color: Colors.text, fontWeight: '600' },
});
