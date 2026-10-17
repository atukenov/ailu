import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { Achievement } from '@/api/types';
import { addAchievementMemory, markCelebrated } from '@/celebrations';
import { Button, Card, Centered, ErrorText, Field, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { isoToText, todayText, toIso } from '@/dates';
import { useStrings } from '@/i18n';
import { openPaywall } from '@/upsells';

const POLL_MS = 30_000;

export default function AchievementScreen() {
  const s = useStrings();
  const { slug } = useLocalSearchParams<{ slug: string }>();
  const queryClient = useQueryClient();
  const achievement = useQuery({
    queryKey: ['achievements', slug],
    queryFn: () => api.achievement(slug),
    refetchInterval: POLL_MS,
  });
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });
  const [date, setDate] = useState(todayText);

  const update = (a: Achievement) => {
    queryClient.setQueryData(['achievements', slug], a);
    void queryClient.invalidateQueries({ queryKey: ['achievements'], exact: true });
  };

  const mark = useMutation({
    mutationFn: () => {
      const happenedOn = toIso(date);
      if (!happenedOn) throw new Error(s.memoryDateInvalid);
      return api.markAchievement(slug, { happenedOn });
    },
    onSuccess: update,
  });
  const confirm = useMutation({
    mutationFn: () => api.markAchievement(slug),
    onSuccess: async (a) => {
      update(a);
      // Earning it adds нұр and can move the moon.
      void queryClient.invalidateQueries({ queryKey: ['today'] });
      if (a.state === 'earned' && couple.data) {
        await markCelebrated(couple.data.id, a.slug);
        router.push({ pathname: '/achievements/celebrate', params: { slug: a.slug } });
      }
    },
  });
  const unmark = useMutation({ mutationFn: () => api.unmarkAchievement(slug), onSuccess: update });

  if (achievement.isPending) return <Centered />;
  if (achievement.isError)
    return (
      <Screen>
        <TopBar title={s.achievements} onBack={() => router.back()} />
        <ErrorText error={achievement.error} />
      </Screen>
    );

  const a = achievement.data;
  const partner = couple.data?.partner?.name ?? s.partnerFallback;

  return (
    <Screen>
      <TopBar title="" onBack={() => router.back()} />

      <Card style={styles.hero}>
        <View style={[styles.badge, a.state === 'earned' && styles.badgeEarned]}>
          <T style={styles.icon}>{a.icon}</T>
        </View>
        <T variant="title" style={styles.center}>
          {a.title}
        </T>
        <T variant="secondary" style={styles.center}>
          {a.body}
        </T>
        <T variant="small" style={styles.center}>
          {[s.achievementCategories[a.category], `+${a.nur} ${s.nur}`].join(' · ')}
        </T>
      </Card>

      {a.state === 'locked' && (
        <Card>
          <T variant="secondary">{s.achievementLocked}</T>
          <Button title={s.goalLimitCta} onPress={() => openPaywall('achievements')} />
        </Card>
      )}

      {a.state === 'open' && (
        <Card>
          <T variant="secondary">{s.achievementHowTo(partner)}</T>
          <Field
            label={s.memoryDate}
            placeholder={s.memoryDatePlaceholder}
            value={date}
            onChangeText={setDate}
            keyboardType="numbers-and-punctuation"
            maxLength={10}
          />
          <Button title={s.achievementMark} onPress={() => mark.mutate()} loading={mark.isPending} />
          <ErrorText error={mark.error} />
        </Card>
      )}

      {a.state === 'waiting_for_partner' && (
        <Card>
          <T variant="secondary">{s.achievementWaiting(partner, isoToText(a.happenedOn ?? ''))}</T>
          <Button title={s.stepUndo} variant="ghost" onPress={() => unmark.mutate()} loading={unmark.isPending} />
          <ErrorText error={unmark.error} />
        </Card>
      )}

      {a.state === 'confirm_needed' && (
        <Card>
          <T variant="secondary">{s.achievementConfirm(partner, isoToText(a.happenedOn ?? ''))}</T>
          <Button title={s.stepConfirm} onPress={() => confirm.mutate()} loading={confirm.isPending} />
          <Button title={s.achievementDecline} variant="ghost" onPress={() => unmark.mutate()} loading={unmark.isPending} />
          <ErrorText error={confirm.error ?? unmark.error} />
        </Card>
      )}

      {a.state === 'earned' && (
        <Card>
          <T variant="heading" style={{ color: Colors.success }}>
            {s.achievementDoneOn(isoToText(a.happenedOn ?? ''))}
          </T>
          {a.memoryId ? (
            <Button title={s.achievementOpenMemory} variant="ghost" onPress={() => router.push(`/memories/${a.memoryId}`)} />
          ) : (
            <Button title={s.achievementAddMemory} variant="ghost" onPress={() => addAchievementMemory(a)} />
          )}
        </Card>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { alignItems: 'center', gap: Spacing.two },
  center: { textAlign: 'center' },
  badge: {
    width: 120,
    height: 120,
    borderRadius: Radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: Colors.surfaceStrong,
  },
  badgeEarned: { backgroundColor: Colors.accent },
  icon: { fontSize: 56, lineHeight: 68 },
});
