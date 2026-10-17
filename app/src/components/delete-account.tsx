import { useMutation } from '@tanstack/react-query';
import { router } from 'expo-router';
import { Alert, Platform } from 'react-native';

import { api } from '@/api/client';
import { Button, ErrorText } from '@/components/ui';
import { useStrings } from '@/i18n';
import { useSession } from '@/session';

// The stores require deleting the account inside the app, reachable whether or not the person is in a couple.
export function DeleteAccountButton() {
  const s = useStrings();
  const { signOut } = useSession();
  const remove = useMutation({
    mutationFn: api.deleteAccount,
    onSuccess: async () => {
      if (router.canDismiss()) router.dismissAll();
      await signOut();
    },
  });

  const confirm = () => {
    if (Platform.OS === 'web') {
      if (globalThis.confirm?.(s.deleteAccountConfirm)) remove.mutate();
      return;
    }
    Alert.alert(s.deleteAccount, s.deleteAccountConfirm, [
      { text: s.cancel, style: 'cancel' },
      { text: s.delete, style: 'destructive', onPress: () => remove.mutate() },
    ]);
  };

  return (
    <>
      <ErrorText error={remove.error} />
      <Button title={s.deleteAccount} variant="ghost" onPress={confirm} loading={remove.isPending} />
    </>
  );
}
