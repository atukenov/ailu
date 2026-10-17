import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { Alert, Platform, Pressable, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { User } from '@/api/types';
import { Button, Card, ErrorText, Screen, T } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { useSession } from '@/session';

// English is left out until it is translated (see i18n/index.ts).
const LANGUAGES: { value: User['locale']; native: string }[] = [
  { value: 'ru', native: 'Русский' },
  { value: 'kk', native: 'Қазақша' },
];

export default function Profile() {
  const s = useStrings();
  const queryClient = useQueryClient();
  const { user, setUser, signOut } = useSession();
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });

  const language = useMutation({ mutationFn: (locale: User['locale']) => api.updateMe({ locale }), onSuccess: setUser });
  const end = useMutation({
    mutationFn: api.endCouple,
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: ['today'] });
      queryClient.removeQueries({ queryKey: ['mood'] });
      queryClient.setQueryData(['couple'], null);
    },
  });

  const mood = useQuery({ queryKey: ['mood'], queryFn: api.mood });
  const withdraw = useMutation({
    mutationFn: api.withdrawMoodConsent,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['mood'] }),
  });
  const confirmWithdraw = () => {
    if (Platform.OS === 'web') {
      if (globalThis.confirm?.(s.moodWithdrawConfirm)) withdraw.mutate();
      return;
    }
    Alert.alert(s.moodWithdraw, s.moodWithdrawConfirm, [
      { text: s.cancel, style: 'cancel' },
      { text: s.delete, style: 'destructive', onPress: () => withdraw.mutate() },
    ]);
  };

  const confirmEnd = () => {
    if (Platform.OS === 'web') {
      if (globalThis.confirm?.(s.endCoupleConfirm)) end.mutate();
      return;
    }
    Alert.alert(s.endCouple, s.endCoupleConfirm, [
      { text: s.cancel, style: 'cancel' },
      { text: s.endCouple, style: 'destructive', onPress: () => end.mutate() },
    ]);
  };

  return (
    <Screen>
      <Card>
        <T variant="heading">
          {couple.data?.partner?.name ? s.coupleNames(user?.name ?? s.someone, couple.data.partner.name) : user?.name}
        </T>
        <T variant="secondary">{user?.phone}</T>
      </Card>

      <Card>
        <T variant="label">{s.language}</T>
        <View style={styles.languages}>
          {LANGUAGES.map((l) => (
            <Pressable
              key={l.value}
              accessibilityRole="radio"
              accessibilityState={{ checked: user?.locale === l.value }}
              onPress={() => language.mutate(l.value)}
              style={[styles.language, user?.locale === l.value && styles.languageSelected]}>
              <T variant="body">{l.native}</T>
            </Pressable>
          ))}
        </View>
        <ErrorText error={language.error} />
      </Card>

      {mood.data?.consented && (
        <>
          <ErrorText error={withdraw.error} />
          <Button title={s.moodWithdraw} variant="ghost" onPress={confirmWithdraw} loading={withdraw.isPending} />
        </>
      )}
      <ErrorText error={end.error} />
      <Button title={s.endCouple} variant="ghost" onPress={confirmEnd} loading={end.isPending} />
      <Button
        title={s.signOut}
        variant="ghost"
        onPress={async () => {
          router.dismissAll();
          await signOut();
        }}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  languages: { flexDirection: 'row', gap: Spacing.two, flexWrap: 'wrap' },
  language: {
    paddingVertical: Spacing.two,
    paddingHorizontal: Spacing.three,
    borderRadius: Radius.pill,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  languageSelected: { backgroundColor: Colors.surfaceStrong, borderColor: Colors.accent },
});
