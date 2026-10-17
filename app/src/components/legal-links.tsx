import { Pressable, StyleSheet, View } from 'react-native';

import { T } from '@/components/ui';
import { Colors, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { openLegal } from '@/legal';
import { useSession } from '@/session';

export function LegalLinks({ notice = false }: { notice?: boolean }) {
  const s = useStrings();
  const locale = useSession().user?.locale;
  return (
    <View style={styles.wrap}>
      {notice && <T variant="small">{s.legalAgree}</T>}
      <Pressable accessibilityRole="link" onPress={() => openLegal('terms', locale)}>
        <T variant="small" style={styles.link}>{s.terms}</T>
      </Pressable>
      <Pressable accessibilityRole="link" onPress={() => openLegal('privacy', locale)}>
        <T variant="small" style={styles.link}>{s.privacyPolicy}</T>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: Spacing.one, alignItems: 'center' },
  link: { color: Colors.accent, textDecorationLine: 'underline' },
});
