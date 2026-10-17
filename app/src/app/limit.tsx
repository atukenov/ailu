import { useQuery } from '@tanstack/react-query';
import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import { Moon } from '@/components/moon';
import { Button, Screen, T } from '@/components/ui';
import { Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { openPlans } from '@/upsells';

// The free plan's photo limit, met at the 201st photo. It is the most honest moment to sell: the
// couple ran out of room in their own archive. It offers Plus, not Premium, and says plainly that
// nothing they saved goes anywhere (monetization doc).
export default function PhotoLimit() {
  const s = useStrings();
  const storage = useQuery({ queryKey: ['storage'], queryFn: api.storage });
  const limit = storage.data?.photoLimit ?? storage.data?.photos;

  return (
    <Screen>
      <View style={styles.hero}>
        <Moon fill={1} />
        {limit != null && (
          <T variant="title" style={styles.center}>
            {s.photoLimitTitle(limit)}
          </T>
        )}
        <T variant="secondary" style={styles.center}>
          {s.photoLimitBody}
        </T>
      </View>
      <Button
        title={s.storageFullCta}
        onPress={() => {
          router.back();
          openPlans('plus');
        }}
      />
      <Button title={s.notNow} variant="ghost" onPress={() => router.back()} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { alignItems: 'center', gap: Spacing.three, paddingTop: Spacing.six },
  center: { textAlign: 'center' },
});
