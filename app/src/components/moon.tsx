import { StyleSheet, View } from 'react-native';

import { Colors } from '@/constants/theme';

// The couple's phase as a moon lit from the left by `fill` (0..1) — the same reading as the
// phase ladder in the design, where each phase reveals more of the disc.
export function Moon({ fill, size = 96, glow = false }: { fill: number; size?: number; glow?: boolean }) {
  const lit = Math.max(0, Math.min(1, fill));
  return (
    <View
      accessibilityRole="image"
      accessibilityLabel={`${Math.round(lit * 100)}%`}
      style={[
        styles.disc,
        { width: size, height: size, borderRadius: size / 2 },
        glow && { boxShadow: `0 0 ${size / 4}px ${Colors.moonLit}` },
      ]}>
      <View style={[styles.clip, { width: size * lit, height: size }]}>
        <View style={{ width: size, height: size, borderRadius: size / 2, backgroundColor: Colors.moonLit }} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  disc: { backgroundColor: Colors.moonDark, overflow: 'hidden' },
  clip: { overflow: 'hidden', position: 'absolute', left: 0, top: 0 },
});
