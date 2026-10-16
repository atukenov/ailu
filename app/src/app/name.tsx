import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';

import { api } from '@/api/client';
import { Button, Card, ErrorText, Field, Screen, T } from '@/components/ui';
import { useStrings } from '@/i18n';
import { useSession } from '@/session';

export default function Name() {
  const s = useStrings();
  const { setUser } = useSession();
  const [name, setName] = useState('');
  const save = useMutation({ mutationFn: () => api.updateMe({ name }), onSuccess: setUser });

  return (
    <Screen>
      <Card>
        <T variant="heading">{s.nameTitle}</T>
        <T variant="secondary">{s.nameHint}</T>
        <Field
          label={s.nameLabel}
          value={name}
          onChangeText={setName}
          autoComplete="given-name"
          textContentType="givenName"
          maxLength={60}
          autoFocus
          onSubmitEditing={() => save.mutate()}
        />
        <ErrorText error={save.error} />
        <Button title={s.save} onPress={() => save.mutate()} loading={save.isPending} disabled={!name.trim()} />
      </Card>
    </Screen>
  );
}
