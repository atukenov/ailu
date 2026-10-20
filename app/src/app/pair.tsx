import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api } from '@/api/client';
import { DeleteAccountButton } from '@/components/delete-account';
import { PastCouples } from '@/components/past-couples';
import { Button, Card, ErrorText, Field, Screen, T } from '@/components/ui';
import { useStrings } from '@/i18n';
import { useSession } from '@/session';

// Either partner can start: one invites by phone, the other enters the code from the SMS.
export default function Pair() {
  const s = useStrings();
  const queryClient = useQueryClient();
  const { signOut } = useSession();
  const [phone, setPhone] = useState('+7 ');
  const [code, setCode] = useState('');

  const refreshCouple = () => queryClient.invalidateQueries({ queryKey: ['couple'] });
  const invite = useMutation({ mutationFn: () => api.invite(phone) });
  const accept = useMutation({
    mutationFn: () => api.acceptInvitation(code),
    onSuccess: (couple) => queryClient.setQueryData(['couple'], couple),
  });

  return (
    <Screen>
      <Card>
        <T variant="heading">{s.pairTitle}</T>
        <T variant="secondary">{s.pairHint}</T>
        <Field label={s.phoneLabel} value={phone} onChangeText={setPhone} keyboardType="phone-pad" autoComplete="tel" />
        <ErrorText error={invite.error} />
        {invite.data ? (
          <>
            <T variant="small">{s.inviteSent(invite.data.phone)}</T>
            <Button title={s.checkAgain} variant="ghost" onPress={refreshCouple} />
          </>
        ) : (
          <>
            <Button title={s.invite} onPress={() => invite.mutate()} loading={invite.isPending} />
            <T variant="small">{s.partnerGetsCode}</T>
          </>
        )}
      </Card>

      <Card>
        <T variant="heading">{s.haveCode}</T>
        <T variant="secondary">{s.haveCodeHint}</T>
        <Field
          label={s.codeLabel}
          value={code}
          onChangeText={(v) => setCode(v.replace(/\D/g, '').slice(0, 6))}
          keyboardType="number-pad"
        />
        <ErrorText error={accept.error} />
        <Button title={s.joinCouple} onPress={() => accept.mutate()} loading={accept.isPending} disabled={code.length !== 6} />
      </Card>

      <PastCouples />

      <Button title={s.signOut} variant="ghost" onPress={signOut} />
      <DeleteAccountButton />
    </Screen>
  );
}
