import { useQuery } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect } from 'react';
import { StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import { addAchievementMemory, markCelebrated } from '@/celebrations';
import { ACHIEVEMENT_TONES, Badge } from '@/components/badge';
import { Burst } from '@/components/motion';
import { Button, Centered, Screen, T } from '@/components/ui';
import { Colors, Radius, Spacing, Tones } from '@/constants/theme';
import { useStrings } from '@/i18n';

// "Новое достижение" from the mockups: shown once per achievement on each partner's phone.
export default function Celebrate() {
  const s = useStrings();
  const { slug } = useLocalSearchParams<{ slug: string }>();
  const achievement = useQuery({ queryKey: ['achievements', slug], queryFn: () => api.achievement(slug) });
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });

  const coupleId = couple.data?.id;
  useEffect(() => {
    if (coupleId) void markCelebrated(coupleId, slug);
  }, [coupleId, slug]);

  const a = achievement.data;
  if (!a) return <Centered />;

  return (
    <Screen>
      <View style={styles.body}>
        <T variant="label" style={styles.kicker}>
          {s.achievementNew}
        </T>
        <View style={styles.badgeWrap}>
          <Burst size={300} />
          <Badge icon={a.icon} tone={ACHIEVEMENT_TONES[a.category]} earned size={168} glow />
        </View>
        <T variant="title" style={styles.center}>
          {a.title}
        </T>
        <T variant="secondary" style={styles.center}>
          {a.body}
        </T>
        <View style={styles.pill}>
          <T variant="body" style={styles.nur}>{`+${a.nur} ${s.nur}`}</T>
        </View>
      </View>
      <Button title={s.achievementsSeeAll} onPress={() => router.dismissTo('/achievements')} />
      {!a.memoryId && <Button title={s.achievementAddMemory} variant="ghost" onPress={() => addAchievementMemory(a)} />}
    </Screen>
  );
}

const styles = StyleSheet.create({
  body: { alignItems: 'center', gap: Spacing.three, paddingVertical: Spacing.five },
  kicker: { color: Colors.accent, letterSpacing: 2 },
  center: { textAlign: 'center' },
  badgeWrap: { width: 220, height: 220, alignItems: 'center', justifyContent: 'center' },
  nur: { color: Colors.gold, fontWeight: '800' },
  pill: {
    paddingHorizontal: Spacing.four,
    paddingVertical: Spacing.two,
    borderRadius: Radius.pill,
    backgroundColor: Tones.gold.wash[0],
    borderWidth: 1,
    borderColor: Tones.gold.border,
  },
});
