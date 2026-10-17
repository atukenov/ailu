import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { LoveLanguage, LoveQuiz, LoveQuizScores } from '@/api/types';
import { Button, Card, Centered, ErrorText, ProgressBar, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { useSession } from '@/session';
import { openPaywall } from '@/upsells';

// The partner's taking shows up without a manual refresh.
const POLL_MS = 30_000;

// The love-languages quiz (gamification doc): each partner answers alone, and the result opens to both
// at once. The result tunes the tasks; it is worded as what is close to each, never as a label.
export default function LoveQuizScreen() {
  const s = useStrings();
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });
  const quiz = useQuery({ queryKey: ['love-quiz'], queryFn: api.loveQuiz, refetchInterval: POLL_MS });
  const [taking, setTaking] = useState(false);

  if (quiz.isPending) return <Centered />;
  const partnerName = couple.data?.partner?.name ?? s.partnerFallback;
  const data = quiz.data;

  if (data && taking) return <Questions quiz={data} onDone={() => setTaking(false)} onCancel={() => setTaking(false)} />;

  const round = data?.round;
  const waiting = round?.myAnswered && !round.partnerAnswered;
  return (
    <Screen
      refreshControl={
        <RefreshControl refreshing={quiz.isRefetching} onRefresh={() => void quiz.refetch()} tintColor={Colors.accent} />
      }>
      <TopBar title={s.loveQuiz} onBack={() => router.back()} />
      <ErrorText error={quiz.error} />

      {data && waiting && (
        <Card>
          <T variant="secondary">{s.loveQuizWaiting(partnerName)}</T>
        </Card>
      )}
      {data && round?.partnerAnswered && !round.myAnswered && (
        <Card>
          <T variant="secondary">{s.loveQuizPartnerDone(partnerName)}</T>
          {data.available && <Button title={s.loveQuizStart} onPress={() => setTaking(true)} />}
        </Card>
      )}

      {data?.result ? (
        <Result quiz={data} partnerName={partnerName} />
      ) : (
        data && <Intro quiz={data} />
      )}

      {data && !waiting && !data.available && (
        <Card>
          <T variant="secondary">{s.loveQuizLocked}</T>
          <Button title={s.loveQuizUnlock} onPress={() => openPaywall('quiz')} />
        </Card>
      )}
      {data && data.available && !round && (
        <Button
          title={data.result ? s.loveQuizRetake : s.loveQuizStart}
          variant={data.result ? 'ghost' : 'primary'}
          onPress={() => setTaking(true)}
        />
      )}
      {data?.result && data.available && !round && <T variant="small">{s.loveQuizRetakeHint}</T>}
    </Screen>
  );
}

function Intro({ quiz }: { quiz: LoveQuiz }) {
  const s = useStrings();
  return (
    <Card>
      <T variant="heading">{s.loveQuiz}</T>
      <T variant="secondary">{s.loveQuizIntro}</T>
      <T variant="small">{s.loveQuizNotLabel}</T>
      <T variant="small" style={styles.reward}>
        {s.loveQuizReward(quiz.nur)}
      </T>
    </Card>
  );
}

// One question at a time; the picks stay on the phone until the last one, then go together.
function Questions({ quiz, onDone, onCancel }: { quiz: LoveQuiz; onDone: () => void; onCancel: () => void }) {
  const s = useStrings();
  const queryClient = useQueryClient();
  const [index, setIndex] = useState(0);
  const [picks, setPicks] = useState<(LoveLanguage | null)[]>(() => quiz.questions.map(() => null));
  const send = useMutation({
    mutationFn: api.answerLoveQuiz,
    onSuccess: (result) => {
      queryClient.setQueryData(['love-quiz'], result);
      // Нұр and phase may have changed.
      void queryClient.invalidateQueries({ queryKey: ['today'] });
      onDone();
    },
  });

  const question = quiz.questions[index];
  const text = s.loveQuizQuestions[index];
  const pick = picks[index];
  const last = index === quiz.questions.length - 1;

  const next = () => {
    if (!last) {
      setIndex(index + 1);
      return;
    }
    if (picks.every((p) => p !== null)) send.mutate(picks as LoveLanguage[]);
  };

  return (
    <Screen>
      <TopBar title={s.loveQuiz} onBack={() => (index > 0 ? setIndex(index - 1) : onCancel())} />
      <View style={styles.progressRow}>
        <T variant="label">{s.loveQuizKicker}</T>
        <T variant="label">{s.loveQuizProgress(index + 1, quiz.questions.length)}</T>
      </View>
      <ProgressBar percent={((index + 1) / quiz.questions.length) * 100} />
      <T variant="title">{text?.prompt}</T>
      <View style={styles.options} accessibilityRole="radiogroup">
        {question.options.map((language) => (
          <Pressable
            key={language}
            accessibilityRole="radio"
            accessibilityState={{ checked: pick === language }}
            onPress={() => setPicks(picks.map((p, i) => (i === index ? language : p)))}
            style={[styles.option, pick === language && styles.optionOn]}>
            <View style={[styles.dot, pick === language && styles.dotOn]} />
            <T variant="body" style={styles.flex}>
              {text?.options[language] ?? s.loveLanguages[language]}
            </T>
          </Pressable>
        ))}
      </View>
      <ErrorText error={send.error} />
      <Button title={last ? s.loveQuizFinish : s.loveQuizNext} onPress={next} disabled={!pick} loading={send.isPending} />
    </Screen>
  );
}

function Result({ quiz, partnerName }: { quiz: LoveQuiz; partnerName: string }) {
  const s = useStrings();
  const { user } = useSession();
  const result = quiz.result!;
  const languages = Object.keys(s.loveLanguages) as LoveLanguage[];
  const total = (l: LoveLanguage) => (result.me?.scores[l] ?? 0) + (result.partner?.scores[l] ?? 0);
  const order = [...languages].sort((a, b) => total(b) - total(a));
  const tops = [...new Set([...(result.me?.top ?? []), ...(result.partner?.top ?? [])])];

  return (
    <>
      <View style={styles.titleBlock}>
        <T variant="title">{s.loveQuizResultTitle}</T>
        <T variant="secondary">{s.loveQuizResultSubtitle(quiz.questions.length)}</T>
      </View>
      <View style={styles.people}>
        <Person name={user?.name ?? s.loveQuizYou} scores={result.me} />
        <Person name={partnerName} scores={result.partner} />
      </View>
      {!result.partner && <T variant="small">{s.loveQuizPartnerGone}</T>}

      <Card>
        <T variant="label">{s.loveQuizScales}</T>
        {order.map((l) => (
          <View key={l} style={styles.scale}>
            <T variant="body">{s.loveLanguages[l]}</T>
            <Bar label={user?.name ?? s.loveQuizYou} value={result.me?.scores[l]} />
            {result.partner && <Bar label={partnerName} value={result.partner.scores[l]} />}
          </View>
        ))}
      </Card>

      <Card>
        <T variant="label">{s.loveQuizTips}</T>
        {tops.map((l) => (
          <T key={l} variant="body">
            {s.loveLanguageTips[l]}
          </T>
        ))}
        {result.nurAwarded > 0 && (
          <T variant="small" style={styles.reward}>
            {s.loveQuizNurEarned(result.nurAwarded)}
          </T>
        )}
      </Card>
    </>
  );
}

function Person({ name, scores }: { name: string; scores: LoveQuizScores | null }) {
  const s = useStrings();
  return (
    <Card style={styles.person}>
      <T variant="label">{name}</T>
      <T variant="small">{s.loveQuizClosest}</T>
      <T variant="heading" style={styles.top}>
        {scores ? scores.top.map((l) => s.loveLanguages[l]).join(' · ') : '—'}
      </T>
    </Card>
  );
}

function Bar({ label, value }: { label: string; value: number | undefined }) {
  return (
    <View style={styles.bar}>
      <View style={styles.barHead}>
        <T variant="small">{label}</T>
        <T variant="small">{value ?? 0}%</T>
      </View>
      <ProgressBar percent={value ?? 0} />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  reward: { color: Colors.accent },
  progressRow: { flexDirection: 'row', justifyContent: 'space-between' },
  options: { gap: Spacing.two },
  option: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.three,
    padding: Spacing.three,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.border,
    backgroundColor: Colors.surface,
  },
  optionOn: { backgroundColor: Colors.surfaceStrong, borderColor: Colors.accent },
  dot: { width: 18, height: 18, borderRadius: 9, borderWidth: 2, borderColor: Colors.textSecondary },
  dotOn: { borderColor: Colors.accent, backgroundColor: Colors.accent },
  titleBlock: { gap: Spacing.two },
  people: { flexDirection: 'row', gap: Spacing.three },
  person: { flex: 1 },
  top: { color: Colors.accent, fontSize: 16, lineHeight: 22 },
  scale: { gap: Spacing.two, paddingTop: Spacing.two },
  bar: { gap: Spacing.one },
  barHead: { flexDirection: 'row', justifyContent: 'space-between' },
});
