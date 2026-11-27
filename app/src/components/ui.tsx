import { LinearGradient } from 'expo-linear-gradient';
import { Children, createContext, useContext, useEffect, type ReactNode } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
  type TextInputProps,
  type TextProps,
  type ViewStyle,
} from 'react-native';
import Animated, { Easing, useAnimatedStyle, useSharedValue, withTiming } from 'react-native-reanimated';
import { SafeAreaView } from 'react-native-safe-area-context';

import { ApiError } from '@/api/client';
import { Rise, Sky, Tap, tapFeedback } from '@/components/motion';
import { BackgroundGradient, Colors, MaxContentWidth, PrimaryGradient, Radius, Spacing, Tones, type Tone } from '@/constants/theme';
import { useStrings } from '@/i18n';

// The tone of the card a piece sits in, so a kicker, a progress bar or a button picks up its colour.
const ToneContext = createContext<Tone | undefined>(undefined);
export const useTone = () => useContext(ToneContext);

// The night-sky ground behind every screen.
export function Backdrop() {
  return (
    <View style={StyleSheet.absoluteFill} pointerEvents="none">
      <LinearGradient colors={BackgroundGradient} locations={[0, 0.45, 1]} style={StyleSheet.absoluteFill} />
      {/* Soft washes of pink from the top right and lavender from the left, drawn as gradients so they
          look the same on phones and the web. */}
      <LinearGradient
        colors={['rgba(255, 143, 184, 0.22)', 'rgba(255, 143, 184, 0)']}
        start={{ x: 1, y: 0 }}
        end={{ x: 0.3, y: 0.35 }}
        style={StyleSheet.absoluteFill}
      />
      <LinearGradient
        colors={['rgba(185, 162, 255, 0)', 'rgba(185, 162, 255, 0.10)', 'rgba(185, 162, 255, 0)']}
        locations={[0.25, 0.55, 0.9]}
        start={{ x: 0, y: 0 }}
        end={{ x: 0.6, y: 1 }}
        style={StyleSheet.absoluteFill}
      />
      <Sky />
    </View>
  );
}

export function Screen({ children, refreshControl }: { children: ReactNode; refreshControl?: React.ReactElement }) {
  return (
    <SafeAreaView style={styles.screen} edges={['top', 'left', 'right']}>
      <Backdrop />
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView
          contentContainerStyle={styles.scroll}
          keyboardShouldPersistTaps="handled"
          refreshControl={refreshControl as never}>
          <View style={styles.column}>
            {/* Each block rises in a moment after the one above it. */}
            {Children.toArray(children).map((child, i) => (
              <Rise key={(child as { key?: string }).key ?? i} index={i}>
                {child}
              </Rise>
            ))}
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

// A frosted card. With a tone it takes a soft wash and border of that colour, and the kickers,
// progress and buttons inside it follow.
export function Card({ children, style, tone }: { children: ReactNode; style?: ViewStyle; tone?: Tone }) {
  const spec = tone ? Tones[tone] : undefined;
  return (
    <ToneContext.Provider value={tone}>
      <LinearGradient
        colors={spec ? spec.wash : ['rgba(255, 255, 255, 0.09)', 'rgba(255, 255, 255, 0.04)']}
        start={{ x: 0, y: 0 }}
        end={{ x: 1, y: 1 }}
        style={[styles.card, spec && { borderColor: spec.border }, style]}>
        {children}
      </LinearGradient>
    </ToneContext.Provider>
  );
}

type Variant = 'title' | 'heading' | 'body' | 'secondary' | 'small' | 'label';

export function T({ variant = 'body', style, ...rest }: TextProps & { variant?: Variant }) {
  const tone = useTone();
  const toned = variant === 'label' && tone ? { color: Tones[tone].color } : undefined;
  return <Text style={[styles.text, textStyles[variant], toned, style]} {...rest} />;
}

export function Button({
  title,
  onPress,
  variant = 'primary',
  loading,
  disabled,
  tone,
}: {
  title: string;
  onPress: () => void;
  variant?: 'primary' | 'ghost';
  loading?: boolean;
  disabled?: boolean;
  tone?: Tone;
}) {
  const cardTone = useTone();
  const spec = Tones[tone ?? cardTone ?? 'pink'];
  const inactive = disabled || loading;
  const primary = variant === 'primary';
  // A primary button in a toned card takes that tone; elsewhere it is the pink-to-coral gradient.
  const fill = tone || cardTone ? spec.gradient : PrimaryGradient;
  const textColor = primary ? spec.onColor : Colors.text;
  return (
    <Tap
      accessibilityRole="button"
      accessibilityState={{ disabled: !!inactive, busy: !!loading }}
      onPress={() => {
        tapFeedback();
        onPress();
      }}
      disabled={inactive}
      style={[styles.buttonWrap, inactive && styles.inactive]}>
      {({ pressed }) => (
        <LinearGradient
          colors={primary ? fill : pressed ? [spec.wash[0], spec.wash[0]] : ['transparent', 'transparent']}
          start={{ x: 0, y: 0 }}
          end={{ x: 1, y: 1 }}
          style={[styles.button, primary ? styles.primary : [styles.ghost, { borderColor: spec.border }]]}>
          {loading ? (
            <ActivityIndicator color={textColor} />
          ) : (
            <Text style={[styles.buttonText, { color: textColor }]}>{title}</Text>
          )}
        </LinearGradient>
      )}
    </Tap>
  );
}

export function Field({ label, style, ...rest }: TextInputProps & { label: string }) {
  return (
    <View style={styles.field}>
      <T variant="label">{label}</T>
      <TextInput placeholderTextColor={Colors.textMuted} style={[styles.input, style]} {...rest} />
    </View>
  );
}

export function ErrorText({ error }: { error: unknown }) {
  const s = useStrings();
  if (!error) return null;
  const translated = error instanceof ApiError && error.code ? s.errors[error.code] : undefined;
  const message = translated ?? (error instanceof Error ? error.message : String(error));
  return (
    <T variant="small" style={{ color: Colors.danger }} accessibilityRole="alert">
      {message}
    </T>
  );
}

// A screen's top row: back, title, and an optional action on the right.
export function TopBar({ title, onBack, right }: { title: string; onBack: () => void; right?: ReactNode }) {
  const s = useStrings();
  return (
    <View style={styles.topBar}>
      <Tap accessibilityRole="button" accessibilityLabel={s.back} onPress={onBack} style={styles.round} scaleTo={0.88}>
        <Text style={styles.roundText}>‹</Text>
      </Tap>
      <T variant="heading" style={styles.topTitle} numberOfLines={1}>
        {title}
      </T>
      {right ?? <View style={styles.roundSpacer} />}
    </View>
  );
}

export function RoundButton({ label, glyph, onPress, active }: { label: string; glyph: string; onPress: () => void; active?: boolean }) {
  return (
    <Tap
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ selected: !!active }}
      onPress={() => {
        tapFeedback();
        onPress();
      }}
      scaleTo={0.88}
      style={[styles.round, styles.roundAccent, active && styles.roundActive]}>
      <Text style={[styles.roundText, styles.roundTextAccent, active && styles.roundTextActive]}>{glyph}</Text>
    </Tap>
  );
}

// Progress as a bright gradient bar that fills smoothly when it appears or changes.
export function ProgressBar({ percent, tone }: { percent: number; tone?: Tone }) {
  const cardTone = useTone();
  const spec = Tones[tone ?? cardTone ?? 'pink'];
  const clamped = Math.max(0, Math.min(100, percent));
  const width = useSharedValue(0);
  useEffect(() => {
    width.set(withTiming(clamped, { duration: 900, easing: Easing.out(Easing.cubic) }));
  }, [clamped, width]);
  const animated = useAnimatedStyle(() => ({ width: `${width.get()}%` }));
  return (
    <View style={styles.track} accessibilityRole="progressbar" accessibilityValue={{ min: 0, max: 100, now: clamped }}>
      <Animated.View style={[styles.fill, animated]}>
        <LinearGradient colors={spec.gradient} start={{ x: 0, y: 0 }} end={{ x: 1, y: 0 }} style={StyleSheet.absoluteFill} />
      </Animated.View>
    </View>
  );
}

// A pill to pick one of a few options, like a goal's category.
export function Chip({ label, selected, onPress, tone }: { label: string; selected?: boolean; onPress: () => void; tone?: Tone }) {
  const cardTone = useTone();
  const spec = Tones[tone ?? cardTone ?? 'pink'];
  return (
    <Tap
      accessibilityRole="button"
      accessibilityState={{ selected: !!selected }}
      onPress={() => {
        tapFeedback();
        onPress();
      }}
      style={[styles.chip, tone && !selected && { borderColor: spec.border, backgroundColor: spec.wash[1] }]}>
      {selected && (
        <LinearGradient colors={spec.gradient} start={{ x: 0, y: 0 }} end={{ x: 1, y: 1 }} style={[StyleSheet.absoluteFill, styles.chipFill]} />
      )}
      <Text style={[styles.chipText, tone && !selected && { color: spec.color }, selected && { color: spec.onColor }]}>{label}</Text>
    </Tap>
  );
}

export function Centered({ children }: { children?: ReactNode }) {
  return (
    <View style={styles.centered}>
      <Backdrop />
      {children ?? <ActivityIndicator color={Colors.accent} />}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  screen: { flex: 1, backgroundColor: Colors.background },
  scroll: { flexGrow: 1, alignItems: 'center', paddingBottom: Spacing.five },
  column: {
    width: '100%',
    maxWidth: MaxContentWidth,
    paddingHorizontal: Spacing.four,
    paddingTop: Spacing.four,
    gap: Spacing.four,
  },
  card: {
    overflow: 'hidden',
    borderColor: Colors.border,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: Radius.lg,
    padding: Spacing.four,
    gap: Spacing.three,
  },
  text: { color: Colors.text },
  buttonWrap: { borderRadius: Radius.pill },
  button: {
    minHeight: 52,
    borderRadius: Radius.pill,
    paddingHorizontal: Spacing.four,
    alignItems: 'center',
    justifyContent: 'center',
  },
  primary: { boxShadow: '0 6px 18px rgba(255, 120, 160, 0.28)' },
  ghost: { borderWidth: 1 },
  inactive: { opacity: 0.5 },
  buttonText: { fontSize: 16, fontWeight: '700', textAlign: 'center' },
  field: { gap: Spacing.two },
  input: {
    minHeight: 52,
    borderRadius: Radius.md,
    borderColor: Colors.border,
    borderWidth: 1,
    backgroundColor: Colors.surfaceStrong,
    color: Colors.text,
    fontSize: 18,
    paddingHorizontal: Spacing.three,
  },
  topBar: { flexDirection: 'row', alignItems: 'center', gap: Spacing.three },
  topTitle: { flex: 1 },
  round: {
    width: 44,
    height: 44,
    borderRadius: Radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: Colors.surfaceStrong,
    borderColor: Colors.border,
    borderWidth: StyleSheet.hairlineWidth,
  },
  roundAccent: { borderColor: Tones.pink.border },
  roundTextAccent: { color: Colors.accent },
  roundActive: { backgroundColor: Colors.accent, borderColor: Colors.accent },
  roundSpacer: { width: 44 },
  roundText: { color: Colors.text, fontSize: 22, lineHeight: 26 },
  roundTextActive: { color: Colors.onAccent },
  track: { height: 8, borderRadius: Radius.pill, backgroundColor: Colors.surfaceStrong, overflow: 'hidden' },
  fill: { height: '100%', borderRadius: Radius.pill, overflow: 'hidden' },
  chip: {
    paddingHorizontal: Spacing.three,
    paddingVertical: 10,
    borderRadius: Radius.pill,
    borderWidth: 1,
    borderColor: Colors.border,
    backgroundColor: Colors.surface,
    overflow: 'hidden',
  },
  chipFill: { borderRadius: Radius.pill },
  chipText: { color: Colors.text, fontSize: 14, fontWeight: '600' },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: Colors.background },
});

const textStyles = StyleSheet.create({
  title: { fontSize: 32, lineHeight: 40, fontWeight: '700' },
  heading: { fontSize: 22, lineHeight: 30, fontWeight: '700' },
  body: { fontSize: 16, lineHeight: 24 },
  secondary: { fontSize: 16, lineHeight: 24, color: Colors.textSecondary },
  small: { fontSize: 14, lineHeight: 20, color: Colors.textSecondary },
  label: { fontSize: 13, lineHeight: 18, color: Colors.textSecondary, textTransform: 'uppercase', letterSpacing: 1, fontWeight: '700' },
});
