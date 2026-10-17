import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { Alert, Platform, Pressable, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { User } from '@/api/types';
import { DeleteAccountButton } from '@/components/delete-account';
import { LegalLinks } from '@/components/legal-links';
import { Button, Card, ErrorText, Screen, T } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { manageSubscription, restorePurchases, startTrial, useBilling } from '@/purchases';
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

  // Tasks and questions come from the server in the reader's language, so they reload with it.
  const language = useMutation({
    mutationFn: (locale: User['locale']) => api.updateMe({ locale }),
    onSuccess: (user) => {
      setUser(user);
      void queryClient.invalidateQueries({ queryKey: ['today'] });
    },
  });
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

  const billing = useBilling();
  const trial = useMutation({ mutationFn: startTrial });
  const restore = useMutation({ mutationFn: restorePurchases });
  const manage = useMutation({ mutationFn: manageSubscription });
  const native = Platform.OS === 'ios' || Platform.OS === 'android';
  const sub = billing.data?.subscription;
  const subLine = !sub
    ? null
    : sub.provider === 'trial'
      ? s.planTrialEnds(s.date(sub.periodEnd.slice(0, 10)))
      : sub.autoRenew && sub.paidByMe
        ? s.planRenews(s.date(sub.periodEnd.slice(0, 10)))
        : s.planEnds(s.date(sub.periodEnd.slice(0, 10)));

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

      {billing.data && (
        <Card>
          <T variant="label">{s.planTitle}</T>
          <T variant="heading">{s.planName(billing.data.plan)}</T>
          {subLine && <T variant="secondary">{subLine}</T>}
          {sub && sub.provider !== 'trial' && (
            <T variant="secondary">
              {sub.paidByMe ? s.planPaidByMe : s.planPaidByPartner(couple.data?.partner?.name ?? s.someone)}
            </T>
          )}
          {billing.data.plan !== 'premium' && <Button title={s.planChoose} onPress={() => router.push('/paywall')} />}
          {billing.data.plan === 'free' && billing.data.trialAvailable && (
            <Button
              title={s.planTrial(billing.data.trialDays)}
              variant="ghost"
              onPress={() => trial.mutate()}
              loading={trial.isPending}
            />
          )}
          {native && sub?.paidByMe && sub.provider !== 'trial' && (
            <Button
              title={s.planManage}
              variant="ghost"
              onPress={() => manage.mutate(billing.data)}
              loading={manage.isPending}
            />
          )}
          {native && (
            <Button title={s.planRestore} variant="ghost" onPress={() => restore.mutate()} loading={restore.isPending} />
          )}
          {restore.isSuccess && (
            <T variant="small">{restore.data ? s.planRestored : s.planNothingToRestore}</T>
          )}
          <T variant="small">{s.planCancelAnytime}</T>
          <ErrorText error={trial.error ?? restore.error ?? manage.error} />
        </Card>
      )}

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
      <DeleteAccountButton />
      <LegalLinks />
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
