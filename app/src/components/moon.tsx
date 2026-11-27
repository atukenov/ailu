import { LinearGradient } from 'expo-linear-gradient';
import { useEffect } from 'react';
import { StyleSheet, View } from 'react-native';
import Animated, { Easing, useAnimatedStyle, useSharedValue, withTiming } from 'react-native-reanimated';

import { Halo } from '@/components/motion';
import { Colors } from '@/constants/theme';

// The couple's phase as a moon lit from the left by `fill` (0..1) — the same reading as the
// phase ladder in the design, where each phase reveals more of the disc. The light grows in when
// the moon appears, and a warm halo breathes behind it; the supermoon's halo is brighter.
export function Moon({ fill, size = 96, glow = false }: { fill: number; size?: number; glow?: boolean }) {
  const lit = Math.max(0, Math.min(1, fill));
  const width = useSharedValue(0);
  useEffect(() => {
    width.set(withTiming(lit, { duration: 1200, easing: Easing.out(Easing.cubic) }));
  }, [lit, width]);
  const animated = useAnimatedStyle(() => ({ width: size * width.get() }));

  return (
    <View style={{ width: size, height: size }} accessibilityRole="image" accessibilityLabel={`${Math.round(lit * 100)}%`}>
      <Halo size={size} color={Colors.gold} strength={glow ? 1.6 : 0.8} />
      <View style={[styles.disc, { width: size, height: size, borderRadius: size / 2 }]}>
        <Animated.View style={[styles.clip, { height: size }, animated]}>
          <LinearGradient
            colors={[Colors.moonLit, Colors.gold]}
            start={{ x: 0, y: 0 }}
            end={{ x: 1, y: 1 }}
            style={{ width: size, height: size, borderRadius: size / 2 }}
          />
        </Animated.View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  disc: { backgroundColor: Colors.moonDark, overflow: 'hidden', borderWidth: 1, borderColor: 'rgba(255, 231, 176, 0.25)' },
  clip: { overflow: 'hidden', position: 'absolute', left: 0, top: 0 },
});
