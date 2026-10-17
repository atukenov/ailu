import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';

import { api } from '@/api/client';
import { Button, Card, ErrorText, Field, Screen, T } from '@/components/ui';
import { useStrings } from '@/i18n';
import { useSession } from '@/session';

// Phone first, then the SMS code, on one screen: the code step only makes sense right after the phone step.
export default function SignIn() {
  const s = useStrings();
  const { signIn } = useSession();
  const [phone, setPhone] = useState('+7 ');
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [code, setCode] = useState('');

  const request = useMutation({ mutationFn: () => api.requestCode(phone), onSuccess: (res) => setSentTo(res.phone) });
  const resend = useMutation({ mutationFn: () => api.requestCode(sentTo ?? phone) });
  const verify = useMutation({ mutationFn: () => api.verifyCode(sentTo ?? phone, code), onSuccess: signIn });

  return (
    <Screen>
      <T variant="title">{s.welcomeTitle} ♡</T>
      <T variant="secondary">{s.welcomeSubtitle}</T>

      {sentTo === null ? (
        <Card>
          <T variant="heading">{s.phoneTitle}</T>
          <T variant="secondary">{s.phoneHint}</T>
          <Field
            label={s.phoneLabel}
            value={phone}
            onChangeText={setPhone}
            keyboardType="phone-pad"
            autoComplete="tel"
            textContentType="telephoneNumber"
            onSubmitEditing={() => request.mutate()}
          />
          <ErrorText error={request.error} />
          <Button title={s.sendCode} onPress={() => request.mutate()} loading={request.isPending} />
        </Card>
      ) : (
        <Card>
          <T variant="heading">{s.codeTitle}</T>
          <T variant="secondary">{s.codeHint(sentTo)}</T>
          <Field
            label={s.codeLabel}
            value={code}
            onChangeText={(v) => setCode(v.replace(/\D/g, '').slice(0, 6))}
            keyboardType="number-pad"
            autoComplete="sms-otp"
            textContentType="oneTimeCode"
            autoFocus
            onSubmitEditing={() => verify.mutate()}
          />
          <ErrorText error={verify.error ?? resend.error} />
          <Button title={s.signIn} onPress={() => verify.mutate()} loading={verify.isPending} disabled={code.length !== 6} />
          <Button title={s.resendCode} variant="ghost" onPress={() => resend.mutate()} loading={resend.isPending} />
          <Button
            title={s.changeNumber}
            variant="ghost"
            onPress={() => {
              setSentTo(null);
              setCode('');
            }}
          />
        </Card>
      )}
    </Screen>
  );
}
