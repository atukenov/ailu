import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, router } from 'expo-router';
import { useEffect, useState } from 'react';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { Couple, DailyQuestion, DailyTask, Phase, StreakRestore, Today } from '@/api/types';
import { MoodCard } from '@/components/mood';
import { Moon } from '@/components/moon';
import { Button, Card, Centered, ErrorText, Field, Screen, T } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
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
      <PhaseCard phase={today.data.phase} streak={today.data.streak.days} />
      {free && couple.data && today.data.streak.days >= 7 && (
        <StreakUpsell coupleId={couple.data.id} days={today.data.streak.days} />
      )}
      {today.data.streak.restore && <RestoreCard restore={today.data.streak.restore} streak={today.data.streak.days} />}
      <TaskCard task={today.data.task} free={free} />
      <QuestionCard question={today.data.question} partnerName={partnerName} />
      <MoodCard partnerName={partnerName} pollMs={POLL_MS} />
    </Screen>
  );
}

function Header({ me, partner, couple }: { me: string; partner: string; couple: Couple | null }) {
  const s = useStrings();
  return (
    <View style={styles.header}>
      <View style={styles.headerText}>
        <T variant="title">{s.greeting(me, partner)}</T>
        {couple && <T variant="secondary">{s.daysTogether(couple.daysTogether)}</T>}
      </View>
      <Link href="/memories" asChild>
        <Pressable accessibilityRole="button" accessibilityLabel={s.memories} style={styles.profileButton}>
          <T variant="heading">✦</T>
        </Pressable>
      </Link>
      <Link href="/goals" asChild>
        <Pressable accessibilityRole="button" accessibilityLabel={s.goals} style={styles.profileButton}>
          <T variant="heading">◎</T>
        </Pressable>
      </Link>
      <Link href="/profile" asChild>
        <Pressable accessibilityRole="button" accessibilityLabel={s.profile} style={styles.profileButton}>
          <T variant="heading">≡</T>
        </Pressable>
      </Link>
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
    <Card style={styles.phaseCard}>
      <Moon fill={moonFill(phase)} glow={phase.index === 5} />
      <View style={styles.phaseText}>
        {phase.cycle > 1 && <T variant="label">{s.cycle(phase.cycle)}</T>}
        <T variant="heading">{s.phases[phase.index]?.name ?? phase.name}</T>
        <T variant="small">{s.phases[phase.index]?.moon ?? phase.moon}</T>
        <T variant="body">
          {s.number(phase.earned)} {s.nur}
        </T>
        {phase.nextAt !== null && phase.nextName && (
          <T variant="small">
            {s.toNextPhase(phase.nextAt - phase.earned, s.phases[(phase.index + 1) % s.phases.length]?.name ?? phase.nextName)}
          </T>
        )}
        <T variant="small" style={{ color: streak > 0 ? Colors.success : Colors.textMuted }}>
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
    <Card>
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
    <Card>
      <T variant="heading">{s.streak(days)}</T>
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

function TaskCard({ task, free }: { task: DailyTask; free: boolean }) {
  const s = useStrings();
  const [tomorrow, setTomorrow] = useState(false);
  // A free couple asking for another task today is the warmest moment for Plus, but the paywall
  // opens at most once a day; after that the button only says when the next task comes.
  const moreTask = async () => {
    if ((await upsells.taskShownOn()) === task.date) {
      setTomorrow(true);
      return;
    }
    await upsells.markTaskShown(task.date);
    openPaywall('task');
  };
  const accept = useTodayUpdate(() => api.acceptTask(task.id), (t, task) => ({ ...t, task }));
  const complete = useTodayUpdate(() => api.completeTask(task.id), (t, task) => ({ ...t, task }));

  return (
    <Card>
      <T variant="label">{s.taskOfDay}</T>
      <T variant="heading">{task.title}</T>
      <T variant="secondary">{task.body}</T>
      <T variant="small">{s.taskMeta(task.durationMin, task.costKzt)}</T>
      <ErrorText error={accept.error ?? complete.error} />
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
          <Status text={s.taskDone} done />
          <Button
            title={s.saveToMemories}
            variant="ghost"
            onPress={() => router.push({ pathname: '/memories/new', params: { taskId: task.id, title: task.title } })}
          />
          {free && <Button title={s.moreTask} variant="ghost" onPress={() => void moreTask()} />}
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
    <Card>
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
      <T variant="label">{label}</T>
      <T variant="body">{text}</T>
    </View>
  );
}

function Status({ text, done }: { text: string; done?: boolean }) {
  return (
    <View style={styles.status}>
      <T variant="small" style={{ color: done ? Colors.success : Colors.textSecondary }}>
        {text}
      </T>
    </View>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.three },
  headerText: { flex: 1, gap: Spacing.two },
  profileButton: { padding: Spacing.two },
  phaseCard: { flexDirection: 'row', alignItems: 'center', gap: Spacing.four },
  phaseText: { flex: 1, gap: Spacing.one },
  answer: { gap: Spacing.one, padding: Spacing.three, borderRadius: Radius.md, backgroundColor: Colors.surfaceStrong },
  answerInput: { minHeight: 96, paddingTop: Spacing.three, textAlignVertical: 'top', fontSize: 16 },
  status: { paddingVertical: Spacing.two },
});
