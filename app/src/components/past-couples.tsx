import { useQuery } from '@tanstack/react-query';
import { router } from 'expo-router';

import { api } from '@/api/client';
import type { PastCouple } from '@/api/types';
import { Button, Card, T } from '@/components/ui';
import { useStrings } from '@/i18n';

// The day an instant falls on here, as YYYY-MM-DD for s.date.
export function localDay(instant: string) {
  const d = new Date(instant);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export function archiveTitle(s: ReturnType<typeof useStrings>, past: PastCouple) {
  return past.partner?.name ? s.archiveTitle(past.partner.name) : s.archiveTitlePlain;
}

// Ended couples that still hold memories, each opening its read-only archive. Shows nothing otherwise.
export function PastCouples() {
  const s = useStrings();
  const archive = useQuery({ queryKey: ['archive'], queryFn: api.archive });
  const withMemories = archive.data?.filter((c) => c.memories > 0) ?? [];

  return withMemories.map((c) => (
    <Card key={c.id}>
      <T variant="label">{archiveTitle(s, c)}</T>
      <T variant="secondary">
        {s.archiveDates(s.date(localDay(c.startedAt)), s.date(localDay(c.endedAt)))} · {s.archiveMemories(c.memories)}
      </T>
      <Button title={s.archiveOpen} variant="ghost" onPress={() => router.push(`/archive/${c.id}`)} />
    </Card>
  ));
}
