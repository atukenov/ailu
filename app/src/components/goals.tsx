import { Pressable, StyleSheet, Text, View } from 'react-native';

import { T } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

export const MAX_STEPS = 20;

function currentMonth() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

function shiftMonth(ym: string, by: number) {
  const [y, m] = ym.split('-').map(Number);
  const d = new Date(y, m - 1 + by, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

// The deadline is a month, not a day. Stepping back past this month means "no deadline".
export function MonthPicker({ value, onChange }: { value: string | null; onChange: (v: string | null) => void }) {
  const s = useStrings();
  const now = currentMonth();
  return (
    <View style={styles.group}>
      <T variant="label">{s.goalDeadline}</T>
      <View style={styles.month}>
        <Arrow glyph="‹" disabled={value === null} onPress={() => onChange(value === null || value <= now ? null : shiftMonth(value, -1))} />
        <T variant="body" style={styles.monthText}>
          {value ? s.monthYear(value) : s.noDeadline}
        </T>
        <Arrow glyph="›" onPress={() => onChange(value === null ? now : shiftMonth(value, 1))} />
      </View>
    </View>
  );
}

function Arrow({ glyph, onPress, disabled }: { glyph: string; onPress: () => void; disabled?: boolean }) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={glyph === '‹' ? 'Раньше' : 'Позже'}
      onPress={onPress}
      disabled={disabled}
      style={[styles.arrow, disabled && styles.disabled]}>
      <Text style={styles.arrowText}>{glyph}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  group: { gap: Spacing.two },
  month: {
    flexDirection: 'row',
    alignItems: 'center',
    minHeight: 52,
    borderRadius: Radius.pill,
    borderWidth: 1,
    borderColor: Colors.border,
    backgroundColor: Colors.surface,
    paddingHorizontal: Spacing.one,
  },
  monthText: { flex: 1, textAlign: 'center' },
  arrow: { width: 44, height: 44, alignItems: 'center', justifyContent: 'center' },
  arrowText: { color: Colors.text, fontSize: 26, lineHeight: 30 },
  disabled: { opacity: 0.3 },
});
