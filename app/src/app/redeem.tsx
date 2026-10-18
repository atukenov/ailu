import { useMutation, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import { Moon } from '@/components/moon';
import { Button, ErrorText, Field, Screen, T, TopBar } from '@/components/ui';
import { Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

const NAMES = { plus: 'Plus', premium: 'Premium' } as const;

// Entering a gift code or a promo code. The server decides what the code is worth; the plan is the
// couple's, so the partner gets it too.
export default function Redeem() {
  const s = useStrings();
  const queryClient = useQueryClient();
  const [code, setCode] = useState('');

  const redeem = useMutation({
    mutationFn: () => api.redeem(code),
    onSuccess: () => {
      for (const key of ['billing', 'storage', 'couple', 'today', 'goals', 'love-quiz', 'achievements'])
        void queryClient.invalidateQueries({ queryKey: [key] });
    },
  });

  if (redeem.data) {
    const done = redeem.data;
    return (
      <Screen>
        <View style={styles.hero}>
          <Moon fill={1} glow />
          <T variant="title" style={styles.center}>
            {done.kind === 'gift' ? s.redeemGiftDone : s.redeemPromoDone}
          </T>
          <T variant="secondary" style={styles.center}>
            {s.redeemDoneBody(NAMES[done.tier], s.date(done.periodEnd.slice(0, 10)))}
          </T>
        </View>
        <Button title={s.redeemDone} onPress={() => router.back()} />
      </Screen>
    );
  }

  return (
    <Screen>
      <TopBar title={s.planEnterCode} onBack={() => router.back()} />
      <T variant="secondary">{s.redeemHint}</T>
      <Field
        label={s.redeemCodeLabel}
        value={code}
        onChangeText={setCode}
        autoCapitalize="characters"
        autoCorrect={false}
        autoComplete="off"
        placeholder="XXXX-XXXX-XXXX"
        maxLength={40}
        returnKeyType="done"
        onSubmitEditing={() => code.trim() && redeem.mutate()}
        style={styles.code}
      />
      <ErrorText error={redeem.error} />
      <Button title={s.redeemButton} onPress={() => redeem.mutate()} loading={redeem.isPending} disabled={!code.trim()} />
      <T variant="small">{s.redeemStoreNote}</T>
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { alignItems: 'center', gap: Spacing.three, paddingTop: Spacing.six },
  center: { textAlign: 'center' },
  code: { fontSize: 20, letterSpacing: 2 },
});
