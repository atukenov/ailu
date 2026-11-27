import { useEffect, useMemo, type ReactNode } from 'react';
import { Platform, Pressable, StyleSheet, View, type PressableProps, type StyleProp, type ViewStyle } from 'react-native';
import * as Haptics from 'expo-haptics';
import Animated, {
  Easing,
  FadeInDown,
  useAnimatedStyle,
  useSharedValue,
  withDelay,
  withRepeat,
  withSequence,
  withSpring,
  withTiming,
} from 'react-native-reanimated';

import { Colors } from '@/constants/theme';

// Small motion pieces every screen shares. Reanimated follows the phone's "reduce motion" setting for
// entrances by default, so these stay calm for people who asked for that.

const AnimatedPressable = Animated.createAnimatedComponent(Pressable);

// A light tap on the phone for real choices; nothing on the web.
export function tapFeedback() {
  if (Platform.OS !== 'web') void Haptics.selectionAsync().catch(() => undefined);
}

// Cards rise into place one after another when a screen opens.
export function Rise({ index = 0, children, style }: { index?: number; children: ReactNode; style?: StyleProp<ViewStyle> }) {
  return (
    <Animated.View
      style={style}
      entering={FadeInDown.duration(420)
        .delay(Math.min(index, 8) * 70)
        .springify()
        .damping(18)}>
      {children}
    </Animated.View>
  );
}

// A Pressable that sinks a little under the finger and springs back.
export function Tap({
  children,
  style,
  scaleTo = 0.96,
  onPressIn,
  onPressOut,
  ...rest
}: Omit<PressableProps, 'style'> & { style?: StyleProp<ViewStyle>; scaleTo?: number }) {
  const scale = useSharedValue(1);
  const animated = useAnimatedStyle(() => ({ transform: [{ scale: scale.get() }] }));
  return (
    <AnimatedPressable
      {...rest}
      onPressIn={(e) => {
        scale.set(withSpring(scaleTo, { damping: 15, stiffness: 400 }));
        onPressIn?.(e);
      }}
      onPressOut={(e) => {
        scale.set(withSpring(1, { damping: 12, stiffness: 300 }));
        onPressOut?.(e);
      }}
      style={[style, animated]}>
      {children}
    </AnimatedPressable>
  );
}

// A gentle, endless breathing: used for the moon's glow and things waiting on the partner.
export function Pulse({ children, style, from = 0.92, to = 1.06, duration = 1800 }: {
  children?: ReactNode;
  style?: StyleProp<ViewStyle>;
  from?: number;
  to?: number;
  duration?: number;
}) {
  const t = useSharedValue(0);
  useEffect(() => {
    t.set(withRepeat(withTiming(1, { duration, easing: Easing.inOut(Easing.sin) }), -1, true));
  }, [t, duration]);
  const animated = useAnimatedStyle(() => ({
    transform: [{ scale: from + (to - from) * t.get() }],
    opacity: 0.75 + 0.25 * t.get(),
  }));
  return <Animated.View style={[style, animated]}>{children}</Animated.View>;
}

// A soft, breathing glow behind a round thing: a few faint rings, drawn without blur so it looks the
// same on every platform.
export function Halo({ size, color, strength = 1 }: { size: number; color: string; strength?: number }) {
  const rings = [1.5, 1.32, 1.16];
  return (
    <Pulse style={[StyleSheet.absoluteFill, styles.center]} from={0.94} to={1.04} duration={2000}>
      {rings.map((r, i) => (
        <View
          key={r}
          style={{
            position: 'absolute',
            width: size * r,
            height: size * r,
            borderRadius: (size * r) / 2,
            backgroundColor: color,
            opacity: (0.07 + i * 0.05) * strength,
          }}
        />
      ))}
    </Pulse>
  );
}

// A badge or number that pops in with a little overshoot.
export function Pop({ children, delay = 0, style }: { children: ReactNode; delay?: number; style?: StyleProp<ViewStyle> }) {
  const scale = useSharedValue(0.4);
  useEffect(() => {
    scale.set(withDelay(delay, withSpring(1, { damping: 9, stiffness: 180 })));
  }, [scale, delay]);
  const animated = useAnimatedStyle(() => ({ transform: [{ scale: scale.get() }], opacity: Math.min(1, scale.get() * 1.4) }));
  return <Animated.View style={[style, animated]}>{children}</Animated.View>;
}

// A few stars twinkling behind the content: the app is about the moon, so its sky is never empty.
const STARS = 14;

export function Sky() {
  const stars = useMemo(
    () =>
      Array.from({ length: STARS }, (_, i) => {
        // Fixed pseudo-random spots so the sky doesn't jump between renders.
        const r = (n: number) => ((Math.sin(i * 12.9898 + n * 78.233) * 43758.5453) % 1 + 1) % 1;
        return {
          left: `${Math.round(r(1) * 96)}%` as const,
          top: `${Math.round(r(2) * 70)}%` as const,
          size: 1.5 + r(3) * 2.5,
          delay: Math.round(r(4) * 3000),
          duration: 1600 + Math.round(r(5) * 2400),
          color: i % 5 === 0 ? Colors.gold : i % 7 === 0 ? Colors.accent : Colors.text,
        };
      }),
    [],
  );
  return (
    <View style={StyleSheet.absoluteFill} pointerEvents="none">
      {stars.map((star, i) => (
        <Star key={i} {...star} />
      ))}
    </View>
  );
}

function Star({ left, top, size, delay, duration, color }: {
  left: `${number}%`;
  top: `${number}%`;
  size: number;
  delay: number;
  duration: number;
  color: string;
}) {
  const o = useSharedValue(0.15);
  useEffect(() => {
    o.set(
      withDelay(delay, withRepeat(withSequence(withTiming(0.9, { duration }), withTiming(0.15, { duration })), -1, false)),
    );
  }, [o, delay, duration]);
  const animated = useAnimatedStyle(() => ({ opacity: o.get() }));
  return (
    <Animated.View
      style={[
        { position: 'absolute', left, top, width: size, height: size, borderRadius: size, backgroundColor: color },
        animated,
      ]}
    />
  );
}

// Confetti-like sparks that fly out from the centre once, for a new achievement or a finished goal.
const SPARKS = ['✦', '❤', '★', '✧', '❤', '✦', '★', '✧', '❤', '✦', '★', '✧'];
const SPARK_COLORS = [Colors.accent, Colors.gold, Colors.lavender, Colors.mint, Colors.coral, Colors.sky];

export function Burst({ size = 220 }: { size?: number }) {
  return (
    <View style={[styles.burst, { width: size, height: size, marginLeft: -size / 2, marginTop: -size / 2 }]} pointerEvents="none">
      {SPARKS.map((glyph, i) => (
        <Spark key={i} glyph={glyph} angle={(i / SPARKS.length) * Math.PI * 2} distance={size / 2} color={SPARK_COLORS[i % SPARK_COLORS.length]} delay={i * 25} />
      ))}
    </View>
  );
}

function Spark({ glyph, angle, distance, color, delay }: { glyph: string; angle: number; distance: number; color: string; delay: number }) {
  const t = useSharedValue(0);
  useEffect(() => {
    t.set(withDelay(delay, withTiming(1, { duration: 1100, easing: Easing.out(Easing.cubic) })));
  }, [t, delay]);
  const animated = useAnimatedStyle(() => ({
    opacity: t.get() < 0.7 ? 1 : 1 - (t.get() - 0.7) / 0.3,
    transform: [
      { translateX: Math.cos(angle) * distance * t.get() },
      { translateY: Math.sin(angle) * distance * t.get() },
      { scale: 0.4 + t.get() * 0.8 },
      { rotate: `${t.get() * 180}deg` },
    ],
  }));
  return <Animated.Text style={[styles.spark, { color }, animated]}>{glyph}</Animated.Text>;
}

const styles = StyleSheet.create({
  center: { alignItems: 'center', justifyContent: 'center' },
  burst: { position: 'absolute', left: '50%', top: '50%', alignItems: 'center', justifyContent: 'center' },
  spark: { position: 'absolute', fontSize: 18 },
});
