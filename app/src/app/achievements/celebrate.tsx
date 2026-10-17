import { useQuery } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect } from 'react';
import { StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import { addAchievementMemory, markCelebrated } from '@/celebrations';
import { Button, Centered, Screen, T } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
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
        <View style={styles.badge}>
          <T style={styles.icon}>{a.icon}</T>
        </View>
        <T variant="title" style={styles.center}>
          {a.title}
        </T>
        <T variant="secondary" style={styles.center}>
          {a.body}
        </T>
        <View style={styles.pill}>
          <T variant="body">{`+${a.nur} ${s.nur}`}</T>
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
  badge: {
    width: 168,
    height: 168,
    borderRadius: Radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: Colors.accent,
    shadowColor: Colors.accent,
    shadowOpacity: 0.6,
    shadowRadius: 40,
  },
  icon: { fontSize: 80, lineHeight: 96 },
  pill: {
    paddingHorizontal: Spacing.four,
    paddingVertical: Spacing.two,
    borderRadius: Radius.pill,
    backgroundColor: Colors.surfaceStrong,
  },
});
