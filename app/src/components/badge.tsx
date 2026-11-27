import { LinearGradient } from 'expo-linear-gradient';
import { StyleSheet, Text, View } from 'react-native';

import type { AchievementCategory } from '@/api/types';
import { Halo, Pop } from '@/components/motion';
import { Colors, Tones, type Tone } from '@/constants/theme';

// Each kind of achievement keeps a colour: firsts pink, family events gold, travel sky.
export const ACHIEVEMENT_TONES: Record<AchievementCategory, Tone> = { firsts: 'pink', events: 'gold', travel: 'sky' };

// An achievement's round badge. Earned ones glow in their colour and pop in; the rest stay frosted.
export function Badge({ icon, tone, earned, size = 64, glow }: { icon: string; tone: Tone; earned: boolean; size?: number; glow?: boolean }) {
  const spec = Tones[tone];
  const round = { width: size, height: size, borderRadius: size / 2 };
  const face = earned ? (
    <LinearGradient
      colors={spec.gradient}
      start={{ x: 0, y: 0 }}
      end={{ x: 1, y: 1 }}
      style={[styles.badge, round, { boxShadow: `0 0 ${Math.round(size / 3)}px ${spec.border}` }]}>
      <Text style={{ fontSize: size * 0.47, lineHeight: size * 0.6 }}>{icon}</Text>
    </LinearGradient>
  ) : (
    <View style={[styles.badge, round, styles.frosted, { borderColor: spec.border }]}>
      <Text style={{ fontSize: size * 0.47, lineHeight: size * 0.6 }}>{icon}</Text>
    </View>
  );
  if (!glow) return earned ? <Pop>{face}</Pop> : face;
  return (
    <View style={[round, styles.center]}>
      <Halo size={size} color={spec.color} strength={1.3} />
      <Pop>{face}</Pop>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: { alignItems: 'center', justifyContent: 'center' },
  frosted: { backgroundColor: Colors.surfaceStrong, borderWidth: 1 },
  center: { alignItems: 'center', justifyContent: 'center' },
});
