import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, router } from 'expo-router';
import { useEffect, useState } from 'react';
import { LinearGradient } from 'expo-linear-gradient';
import { Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';

import { api } from '@/api/client';
import type { AiKind, Couple, DailyQuestion, DailyTask, Phase, StreakRestore, Today } from '@/api/types';
import { MoodCard } from '@/components/mood';
import { Moon } from '@/components/moon';
import { Pop, Pulse, Tap, tapFeedback } from '@/components/motion';
import { Button, Card, Centered, Chip, ErrorText, Field, ProgressBar, Screen, T } from '@/components/ui';
import { Colors, Radius, Spacing, Tones, type Tone } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { registerForPush } from '@/notifications';
import { useSession } from '@/session';
import { openPaywall, upsells } from '@/upsells';

// Until the realtime hub exists, poll so a partner's accept or answer shows up without a manual refresh.
const POLL_MS = 30_000;

export default function TodayScreen() {
  const s = useStrings();
  const { user } = useSession();
  const queryClient = useQueryClient();
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });
  const today = useQuery({ queryKey: ['today'], queryFn: api.today, refetchInterval: POLL_MS });
  const storage = useQuery({ queryKey: ['storage'], queryFn: api.storage });

  // Only reachable in a couple, which is when there is something to notify about.
  useEffect(() => {
    registerForPush();
  }, []);

  if (today.isPending) return <Centered />;
  if (today.isError)
    return (
      <Screen>
        <T variant="heading">{s.somethingWrong}</T>
        <ErrorText error={today.error} />
        <Button title={s.retry} onPress={() => today.refetch()} />
      </Screen>
    );

  const partnerName = couple.data?.partner?.name ?? s.partnerFallback;
  // Soft upsells only reach free couples; until the plan is known, show none.
  const free = storage.data?.plan === 'free';
  return (
    <Screen
      refreshControl={
        <RefreshControl
          refreshing={today.isRefetching}
          onRefresh={() => {
            void today.refetch();
            void queryClient.invalidateQueries({ queryKey: ['mood'] });
          }}
          tintColor={Colors.accent}
        />
      }>
      <Header me={user?.name ?? s.someone} partner={partnerName} couple={couple.data ?? null} />
      <QuickNav />
      <PhaseCard phase={today.data.phase} streak={today.data.streak.days} />
      {free && couple.data && today.data.streak.days >= 7 && (
        <StreakUpsell coupleId={couple.data.id} days={today.data.streak.days} />
      )}
      {today.data.streak.restore && <RestoreCard restore={today.data.streak.restore} streak={today.data.streak.days} />}
      {[today.data.task, ...today.data.moreTasks].map((task, i, tasks) => (
        <TaskCard
          key={task.id}
          task={task}
          index={i}
          last={i === tasks.length - 1}
          tasksPerDay={today.data.tasksPerDay}
          free={free}
        />
      ))}
      <QuestionCard question={today.data.question} partnerName={partnerName} />
      {storage.data && <PicksCard premium={storage.data.plan === 'premium'} />}
      <MoodCard partnerName={partnerName} pollMs={POLL_MS} />
      <LoveQuizNudge partnerName={partnerName} />
      <YearStoryNudge />
    </Screen>
  );
}

// "Привет," sits small above the two names, so long names wrap to two lines at most instead of
// pushing the greeting down to four; the screens the couple visits most sit as bright tiles below.
function Header({ me, partner, couple }: { me: string; partner: string; couple: Couple | null }) {
  const s = useStrings();
  return (
    <View style={styles.header}>
      <View style={styles.headerText}>
        <T variant="label" style={{ color: Colors.accent }}>
          {s.hello}
        </T>
        <T variant="title" numberOfLines={2} adjustsFontSizeToFit minimumFontScale={0.7} style={styles.names}>
          {s.greeting(me, partner)}
        </T>
        {couple && (
          <Link href="/timeline" asChild>
            <Pressable accessibilityRole="link" accessibilityLabel={s.timeline} hitSlop={8} style={styles.together}>
              <Text style={styles.togetherText}>
                ♥ {s.daysTogether(couple.daysTogether)} ›
              </Text>
            </Pressable>
          </Link>
        )}
      </View>
      <Tap
        accessibilityRole="button"
        accessibilityLabel={s.profile}
        onPress={() => {
          tapFeedback();
          router.push('/profile');
        }}
        scaleTo={0.88}
        style={styles.profileButton}>
        <Text style={styles.profileGlyph}>≡</Text>
      </Tap>
    </View>
  );
}

const NAV: { href: '/memories' | '/goals' | '/achievements'; glyph: string; tone: Tone; label: (s: ReturnType<typeof useStrings>) => string }[] = [
  { href: '/memories', glyph: '✦', tone: 'pink', label: (s) => s.memories },
  { href: '/goals', glyph: '◎', tone: 'mint', label: (s) => s.navGoals },
  { href: '/achievements', glyph: '★', tone: 'gold', label: (s) => s.achievements },
];

function QuickNav() {
  const s = useStrings();
  return (
    <View style={styles.nav}>
      {NAV.map((item, i) => {
        const tone = Tones[item.tone];
        return (
          <Tap
            key={item.href}
            accessibilityRole="button"
            accessibilityLabel={item.label(s)}
            onPress={() => {
              tapFeedback();
              router.push(item.href);
            }}
            style={styles.navItem}>
            <LinearGradient colors={tone.wash} start={{ x: 0, y: 0 }} end={{ x: 1, y: 1 }} style={[styles.navTile, { borderColor: tone.border }]}>
              <Pop delay={200 + i * 90}>
                <LinearGradient colors={tone.gradient} style={styles.navIcon}>
                  <Text style={[styles.navGlyph, { color: tone.onColor }]}>{item.glyph}</Text>
                </LinearGradient>
              </Pop>
              <Text style={styles.navLabel} numberOfLines={1} adjustsFontSizeToFit minimumFontScale={0.75}>
                {item.label(s)}
              </Text>
            </LinearGradient>
          </Tap>
        );
      })}
    </View>
  );
}

// Each phase lights another quarter of the disc, filling smoothly within the phase; the
// supermoon is a full disc with a glow.
function moonFill(p: Phase) {
  return p.index >= 4 ? 1 : (p.index + p.fill) / 4;
}

function PhaseCard({ phase, streak }: { phase: Phase; streak: number }) {
  const s = useStrings();
  return (
    <Card tone="gold" style={styles.phaseCard}>
      <View style={styles.phaseRow}>
        <Moon fill={moonFill(phase)} glow={phase.index === 5} size={88} />
        <View style={styles.phaseText}>
          {phase.cycle > 1 && <T variant="label">{s.cycle(phase.cycle)}</T>}
          <T variant="heading">{s.phases[phase.index]?.name ?? phase.name}</T>
          <T variant="small">{s.phases[phase.index]?.moon ?? phase.moon}</T>
          <Pop delay={250}>
            <Text style={styles.nur}>
              {s.number(phase.earned)} <Text style={styles.nurUnit}>{s.nur}</Text>
            </Text>
          </Pop>
        </View>
      </View>
      {phase.nextAt !== null && phase.nextName && (
        <View style={styles.next}>
          <ProgressBar percent={phase.fill * 100} />
          <T variant="small">
            {s.toNextPhase(phase.nextAt - phase.earned, s.phases[(phase.index + 1) % s.phases.length]?.name ?? phase.nextName)}
          </T>
        </View>
      )}
      <View style={[styles.streak, streak > 0 ? styles.streakOn : styles.streakOff]}>
        {streak > 0 ? (
          <Pulse from={0.9} to={1.12} duration={900}>
            <Text style={styles.flame}>🔥</Text>
          </Pulse>
        ) : (
          <Text style={styles.flame}>🌙</Text>
        )}
        <T variant="small" style={[styles.streakText, { color: streak > 0 ? Colors.coral : Colors.textSecondary }]}>
          {streak > 0 ? s.streak(streak) : s.streakZero}
        </T>
      </View>
    </Card>
  );
}

// The reset is worded without blame (gamification design): the days stay in the couple's history.
function RestoreCard({ restore, streak }: { restore: StreakRestore; streak: number }) {
  const s = useStrings();
  const mutation = useTodayUpdate(api.restoreStreak, (t, streak) => ({ ...t, streak }));
  const noneLeft = restore.restoresLeft < restore.missedDays;
  return (
    <Card tone="coral">
      <T variant="heading">{streak > 0 ? s.streakRestarted(restore.days) : s.streakLapsed}</T>
      <T variant="secondary">{s.restoreOffer(restore.days, restore.cost)}</T>
      <T variant="small">{s.restoresLeft(restore.restoresLeft, restore.restoresPerMonth)}</T>
      <ErrorText error={mutation.error} />
      {noneLeft ? (
        <Status text={s.restoreNoneLeft} />
      ) : !restore.canAfford ? (
        <Status text={s.restoreNotEnoughNur(restore.cost)} />
      ) : (
        <Button title={s.restoreStreak} onPress={() => mutation.mutate()} loading={mutation.isPending} />
      )}
    </Card>
  );
}

// Day 7 of the streak is a moment of pride, not need: the one place Premium is offered right away,
// once per couple (monetization doc).
function StreakUpsell({ coupleId, days }: { coupleId: string; days: number }) {
  const s = useStrings();
  const [show, setShow] = useState(false);
  useEffect(() => {
    let live = true;
    void upsells.streakShown(coupleId).then((seen) => live && setShow(!seen));
    return () => {
      live = false;
    };
  }, [coupleId]);
  if (!show) return null;

  const close = () => {
    setShow(false);
    void upsells.markStreakShown(coupleId);
  };
  return (
    <Card tone="gold">
      <T variant="heading">🔥 {s.streak(days)}</T>
      <T variant="secondary">{s.streakUpsellBody}</T>
      <Button
        title={s.streakUpsellCta}
        onPress={() => {
          close();
          openPaywall('streak');
        }}
      />
      <Button title={s.notNow} variant="ghost" onPress={close} />
    </Card>
  );
}

const AI_KINDS: AiKind[] = ['date', 'home', 'film', 'music'];
const AI_TONES: Record<AiKind, Tone> = { date: 'pink', home: 'gold', film: 'lavender', music: 'mint' };

// The way into AI selections, as in the mockup's Today ("Музыка на вечер", "Фильм для вас", "Идеи для
// свидания"). Selections are Premium; other plans see the same row and it opens the Premium paywall.
function PicksCard({ premium }: { premium: boolean }) {
  const s = useStrings();
  const open = (kind: AiKind) =>
    premium ? router.push({ pathname: '/ai-picks', params: { kind } }) : openPaywall('ai');
  return (
    <Card tone="sky">
      <View style={styles.picksHead}>
        <T variant="label">{s.aiPicks}</T>
        {!premium && (
          <LinearGradient colors={Tones.gold.gradient} start={{ x: 0, y: 0 }} end={{ x: 1, y: 1 }} style={styles.premiumTag}>
            <Text style={styles.premiumTagText}>Premium</Text>
          </LinearGradient>
        )}
      </View>
      <View style={styles.picks}>
        {AI_KINDS.map((kind) => (
          <Chip key={kind} label={s.aiKinds[kind]} tone={AI_TONES[kind]} onPress={() => open(kind)} />
        ))}
      </View>
    </Card>
  );
}

// Once a partner has taken the love-languages quiz, the other is invited from the Today screen, since the
// result opens only when both have answered.
function LoveQuizNudge({ partnerName }: { partnerName: string }) {
  const s = useStrings();
  const quiz = useQuery({ queryKey: ['love-quiz'], queryFn: api.loveQuiz, refetchInterval: POLL_MS });
  const round = quiz.data?.round;
  if (!round?.partnerAnswered || round.myAnswered) return null;
  return (
    <Card tone="lavender">
      <T variant="label">{s.loveQuiz}</T>
      <T variant="secondary">{s.loveQuizPartnerDone(partnerName)}</T>
      <Button title={s.loveQuizStart} onPress={() => router.push('/love-quiz')} />
    </Card>
  );
}

// In December, when the year's film can be ordered and has enough memories, Today invites the couple to
// watch its preview. It goes away once the film is ordered.
function YearStoryNudge() {
  const s = useStrings();
  const december = new Date().getMonth() === 11;
  const story = useQuery({ queryKey: ['year-story', 'default'], queryFn: () => api.yearStory(), enabled: december });
  const data = story.data;
  if (!december || !data?.open || data.order || data.memories < data.minMemories) return null;
  return (
    <Card tone="gold">
      <T variant="label">{s.yearStoryKicker}</T>
      <T variant="secondary">{s.yearStoryNudge(data.year)}</T>
      <Button title={s.yearStoryOpen} onPress={() => router.push('/year-story')} />
    </Card>
  );
}

function useTodayUpdate<T>(fn: () => Promise<T>, apply: (today: Today, result: T) => Today) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: (result) => {
      queryClient.setQueryData<Today>(['today'], (old) => (old ? apply(old, result) : old));
      // Нұр, phase and streak are decided by the server; fetch them rather than guess.
      queryClient.invalidateQueries({ queryKey: ['today'] });
    },
  });
}

// Puts a changed task back where it came from: the task of the day or one of the extra tasks.
function withTask(today: Today, task: DailyTask): Today {
  if (today.task.id === task.id) return { ...today, task };
  const known = today.moreTasks.some((t) => t.id === task.id);
  return {
    ...today,
    moreTasks: known ? today.moreTasks.map((t) => (t.id === task.id ? task : t)) : [...today.moreTasks, task],
  };
}

type TaskCardProps = { task: DailyTask; index: number; last: boolean; tasksPerDay: number; free: boolean };

function TaskCard({ task, index, last, tasksPerDay, free }: TaskCardProps) {
  const s = useStrings();
  const [tomorrow, setTomorrow] = useState(false);
  const paid = tasksPerDay > 1;
  const next = useTodayUpdate(api.moreTask, withTask);
  // Paid couples get the next task right away, up to their plan's number a day. A free couple
  // asking for another task is the warmest moment for Plus, but the paywall opens at most once a
  // day; after that the button only says when the next task comes.
  const moreTask = async () => {
    if (paid) {
      next.mutate();
      return;
    }
    if ((await upsells.taskShownOn()) === task.date) {
      setTomorrow(true);
      return;
    }
    await upsells.markTaskShown(task.date);
    openPaywall('task');
  };
  const accept = useTodayUpdate(() => api.acceptTask(task.id), withTask);
  const complete = useTodayUpdate(() => api.completeTask(task.id), withTask);
  const showMore = last && (paid ? index + 1 < tasksPerDay : free);

  return (
    <Card tone={index === 0 ? 'pink' : 'coral'}>
      <T variant="label">
        {index === 0 ? s.taskOfDay : s.extraTask(index + 1, tasksPerDay)}
        {task.personal && ` · ${s.personalTask}`}
      </T>
      <T variant="heading">{task.title}</T>
      <T variant="secondary">{task.body}</T>
      <View style={styles.metaPill}>
        <T variant="small" style={styles.metaText}>
          ⏱ {s.taskMeta(task.durationMin, task.costKzt)}
        </T>
      </View>
      <ErrorText error={accept.error ?? complete.error ?? next.error} />
      {task.state === 'offered' && (
        <>
          {task.acceptedByPartner && <T variant="small">{s.partnerAccepted}</T>}
          <Button title={s.acceptTask} onPress={() => accept.mutate()} loading={accept.isPending} />
        </>
      )}
      {task.state === 'waiting_for_partner' && <Status text={s.waitingPartnerAccept} />}
      {task.state === 'accepted' && (
        <Button title={s.markDone} onPress={() => complete.mutate()} loading={complete.isPending} />
      )}
      {task.state === 'waiting_for_confirmation' && <Status text={s.waitingConfirm} />}
      {task.state === 'confirm_needed' && (
        <Button title={s.confirmDone} onPress={() => complete.mutate()} loading={complete.isPending} />
      )}
      {task.state === 'done' && (
        <>
          {/* Нұр for tasks is one a day, so only the task of the day promises it. */}
          <Status text={index === 0 ? s.taskDone : s.extraTaskDone} done />
          <Button
            title={s.saveToMemories}
            variant="ghost"
            onPress={() => router.push({ pathname: '/memories/new', params: { taskId: task.id, title: task.title } })}
          />
          {showMore && (
            <Button title={s.moreTask} variant="ghost" onPress={() => void moreTask()} loading={next.isPending} />
          )}
          {last && paid && index + 1 >= tasksPerDay && <T variant="small">{s.allTasksDone}</T>}
          {tomorrow && <T variant="small">{s.moreTaskTomorrow}</T>}
        </>
      )}
    </Card>
  );
}

function QuestionCard({ question, partnerName }: { question: DailyQuestion; partnerName: string }) {
  const s = useStrings();
  const [text, setText] = useState('');
  const answer = useTodayUpdate(
    () => api.answer(question.id, text.trim()),
    (t, question) => ({ ...t, question }),
  );

  const revealed = !!question.revealedAt;
  return (
    <Card tone="lavender">
      <T variant="label">{s.questionOfDay}</T>
      <T variant="heading">“{question.text}”</T>

      {revealed ? (
        <>
          <Answer label={s.yourAnswer} text={question.myAnswer ?? ''} />
          <Answer label={s.partnerAnswerLabel(partnerName)} text={question.partnerAnswer ?? ''} />
        </>
      ) : question.myAnswer ? (
        <>
          <Answer label={s.yourAnswer} text={question.myAnswer} />
          <Status text={s.waitingAnswer(partnerName)} />
        </>
      ) : (
        <>
          <T variant="small">{question.partnerAnswered ? s.partnerAnsweredFirst : s.answerHidden}</T>
          <Field
            label={s.answerPlaceholder}
            value={text}
            onChangeText={setText}
            multiline
            maxLength={1000}
            style={styles.answerInput}
          />
          <ErrorText error={answer.error} />
          <Button
            title={s.sendAnswer}
            onPress={() => answer.mutate(undefined, { onSuccess: () => setText('') })}
            loading={answer.isPending}
            disabled={!text.trim()}
          />
        </>
      )}
    </Card>
  );
}

function Answer({ label, text }: { label: string; text: string }) {
  return (
    <View style={styles.answer}>
      <View style={styles.answerBar} />
      <T variant="label">{label}</T>
      <T variant="body">{text}</T>
    </View>
  );
}

// Waiting on the partner breathes softly; a finished step shows in mint.
function Status({ text, done }: { text: string; done?: boolean }) {
  return (
    <View style={[styles.status, done && styles.statusDone]}>
      {done ? (
        <Pop>
          <Text style={styles.statusIcon}>✓</Text>
        </Pop>
      ) : (
        <Pulse from={0.8} to={1.1} duration={1100}>
          <View style={styles.waitingDot} />
        </Pulse>
      )}
      <T variant="small" style={[styles.statusText, { color: done ? Colors.success : Colors.textSecondary }]}>
        {text}
      </T>
    </View>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.three },
  headerText: { flex: 1, gap: Spacing.one },
  names: { fontSize: 30, lineHeight: 38 },
  together: {
    alignSelf: 'flex-start',
    marginTop: Spacing.two,
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: Radius.pill,
    backgroundColor: Tones.pink.wash[0],
    borderWidth: 1,
    borderColor: Tones.pink.border,
  },
  togetherText: { color: Colors.accent, fontSize: 14, fontWeight: '600' },
  profileButton: {
    width: 44,
    height: 44,
    borderRadius: Radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: Colors.surfaceStrong,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  profileGlyph: { color: Colors.text, fontSize: 22, lineHeight: 26 },
  nav: { flexDirection: 'row', gap: Spacing.two },
  navItem: { flex: 1 },
  navTile: {
    alignItems: 'center',
    gap: Spacing.two,
    paddingVertical: Spacing.three,
    paddingHorizontal: Spacing.one,
    borderRadius: Radius.md,
    borderWidth: 1,
  },
  navIcon: { width: 40, height: 40, borderRadius: 20, alignItems: 'center', justifyContent: 'center' },
  navGlyph: { fontSize: 20, lineHeight: 24, fontWeight: '700' },
  navLabel: { color: Colors.text, fontSize: 11, fontWeight: '700', textAlign: 'center' },
  phaseCard: { gap: Spacing.three },
  phaseRow: { flexDirection: 'row', alignItems: 'center', gap: Spacing.four },
  phaseText: { flex: 1, gap: 2 },
  nur: { color: Colors.gold, fontSize: 26, lineHeight: 32, fontWeight: '800' },
  nurUnit: { fontSize: 16, fontWeight: '600' },
  next: { gap: Spacing.two },
  streak: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.two,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
    borderRadius: Radius.pill,
  },
  streakOn: { backgroundColor: Tones.coral.wash[0], borderWidth: 1, borderColor: Tones.coral.border },
  streakOff: { backgroundColor: Colors.surface },
  streakText: { flex: 1, fontWeight: '600' },
  flame: { fontSize: 16, lineHeight: 20 },
  metaPill: {
    alignSelf: 'flex-start',
    paddingHorizontal: 12,
    paddingVertical: 4,
    borderRadius: Radius.pill,
    backgroundColor: Colors.surfaceStrong,
  },
  metaText: { color: Colors.text },
  answer: {
    gap: Spacing.one,
    padding: Spacing.three,
    paddingLeft: Spacing.three + 6,
    borderRadius: Radius.md,
    backgroundColor: Colors.surfaceStrong,
    overflow: 'hidden',
  },
  answerBar: { position: 'absolute', left: 0, top: 0, bottom: 0, width: 4, backgroundColor: Colors.lavender },
  answerInput: { minHeight: 96, paddingTop: Spacing.three, textAlignVertical: 'top', fontSize: 16 },
  status: { flexDirection: 'row', alignItems: 'center', gap: Spacing.two, paddingVertical: Spacing.two },
  statusDone: {
    paddingHorizontal: Spacing.three,
    borderRadius: Radius.md,
    backgroundColor: Tones.mint.wash[0],
  },
  statusIcon: { color: Colors.mint, fontSize: 18, fontWeight: '800' },
  statusText: { flex: 1 },
  waitingDot: { width: 10, height: 10, borderRadius: 5, backgroundColor: Colors.lavender },
  picksHead: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  premiumTag: { paddingHorizontal: 10, paddingVertical: 3, borderRadius: Radius.pill },
  premiumTagText: { color: Tones.gold.onColor, fontSize: 12, fontWeight: '800' },
  picks: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
});
