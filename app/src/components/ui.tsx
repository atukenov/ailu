import type { ReactNode } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
  type TextInputProps,
  type TextProps,
  type ViewStyle,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { Colors, MaxContentWidth, Radius, Spacing } from '@/constants/theme';

export function Screen({ children, refreshControl }: { children: ReactNode; refreshControl?: React.ReactElement }) {
  return (
    <SafeAreaView style={styles.screen} edges={['top', 'left', 'right']}>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView
          contentContainerStyle={styles.scroll}
          keyboardShouldPersistTaps="handled"
          refreshControl={refreshControl as never}>
          <View style={styles.column}>{children}</View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

export function Card({ children, style }: { children: ReactNode; style?: ViewStyle }) {
  return <View style={[styles.card, style]}>{children}</View>;
}

type Variant = 'title' | 'heading' | 'body' | 'secondary' | 'small' | 'label';

export function T({ variant = 'body', style, ...rest }: TextProps & { variant?: Variant }) {
  return <Text style={[styles.text, textStyles[variant], style]} {...rest} />;
}

export function Button({
  title,
  onPress,
  variant = 'primary',
  loading,
  disabled,
}: {
  title: string;
  onPress: () => void;
  variant?: 'primary' | 'ghost';
  loading?: boolean;
  disabled?: boolean;
}) {
  const inactive = disabled || loading;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: !!inactive, busy: !!loading }}
      onPress={onPress}
      disabled={inactive}
      style={({ pressed }) => [
        styles.button,
        variant === 'primary' ? styles.primary : styles.ghost,
        pressed && (variant === 'primary' ? styles.primaryPressed : styles.ghostPressed),
        inactive && styles.inactive,
      ]}>
      {loading ? (
        <ActivityIndicator color={variant === 'primary' ? Colors.onAccent : Colors.text} />
      ) : (
        <Text style={[styles.buttonText, variant === 'primary' ? styles.primaryText : styles.ghostText]}>{title}</Text>
      )}
    </Pressable>
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
  if (!error) return null;
  const message = error instanceof Error ? error.message : String(error);
  return (
    <T variant="small" style={{ color: Colors.danger }} accessibilityRole="alert">
      {message}
    </T>
  );
}

// A screen's top row: back, title, and an optional action on the right.
export function TopBar({ title, onBack, right }: { title: string; onBack: () => void; right?: ReactNode }) {
  return (
    <View style={styles.topBar}>
      <Pressable accessibilityRole="button" accessibilityLabel="Назад" onPress={onBack} style={styles.round}>
        <Text style={styles.roundText}>‹</Text>
      </Pressable>
      <T variant="heading" style={styles.topTitle} numberOfLines={1}>
        {title}
      </T>
      {right ?? <View style={styles.roundSpacer} />}
    </View>
  );
}

export function RoundButton({ label, glyph, onPress, active }: { label: string; glyph: string; onPress: () => void; active?: boolean }) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ selected: !!active }}
      onPress={onPress}
      style={[styles.round, active && styles.roundActive]}>
      <Text style={[styles.roundText, active && styles.roundTextActive]}>{glyph}</Text>
    </Pressable>
  );
}

export function Centered({ children }: { children?: ReactNode }) {
  return <View style={styles.centered}>{children ?? <ActivityIndicator color={Colors.accent} />}</View>;
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
    backgroundColor: Colors.surface,
    borderColor: Colors.border,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: Radius.lg,
    padding: Spacing.four,
    gap: Spacing.three,
  },
  text: { color: Colors.text },
  button: {
    minHeight: 52,
    borderRadius: Radius.pill,
    paddingHorizontal: Spacing.four,
    alignItems: 'center',
    justifyContent: 'center',
  },
  primary: { backgroundColor: Colors.accent },
  primaryPressed: { backgroundColor: Colors.accentPressed },
  ghost: { borderColor: Colors.border, borderWidth: 1 },
  ghostPressed: { backgroundColor: Colors.surface },
  inactive: { opacity: 0.5 },
  buttonText: { fontSize: 16, fontWeight: '600' },
  primaryText: { color: Colors.onAccent },
  ghostText: { color: Colors.text },
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
  roundActive: { backgroundColor: Colors.accent, borderColor: Colors.accent },
  roundSpacer: { width: 44 },
  roundText: { color: Colors.text, fontSize: 22, lineHeight: 26 },
  roundTextActive: { color: Colors.onAccent },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: Colors.background },
});

const textStyles = StyleSheet.create({
  title: { fontSize: 32, lineHeight: 40, fontWeight: '600' },
  heading: { fontSize: 22, lineHeight: 30, fontWeight: '600' },
  body: { fontSize: 16, lineHeight: 24 },
  secondary: { fontSize: 16, lineHeight: 24, color: Colors.textSecondary },
  small: { fontSize: 14, lineHeight: 20, color: Colors.textSecondary },
  label: { fontSize: 13, lineHeight: 18, color: Colors.textSecondary, textTransform: 'uppercase', letterSpacing: 1 },
});
