import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { Image } from 'expo-image';
import { Link, router, useLocalSearchParams } from 'expo-router';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';

import { api, photoUrl } from '@/api/client';
import type { Memory } from '@/api/types';
import { archiveTitle, localDay } from '@/components/past-couples';
import { Button, Card, Centered, ErrorText, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

// The memories of a couple that ended: readable by both, nothing to add or heart.
export default function Archive() {
  const s = useStrings();
  const { coupleId } = useLocalSearchParams<{ coupleId: string }>();
  const archive = useQuery({ queryKey: ['archive'], queryFn: api.archive });
  const list = useInfiniteQuery({
    queryKey: ['archive', coupleId, 'memories'],
    queryFn: ({ pageParam }) => api.archiveMemories(coupleId, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  });

  if (list.isPending) return <Centered />;
  const past = archive.data?.find((c) => c.id === coupleId);
  const items = list.data?.pages.flatMap((p) => p.items) ?? [];
  const photosOpen = past && new Date(past.partnerPhotosUntil) > new Date();

  return (
    <Screen
      refreshControl={
        <RefreshControl refreshing={list.isRefetching} onRefresh={() => void list.refetch()} tintColor={Colors.accent} />
      }>
      <TopBar title={past ? archiveTitle(s, past) : s.archiveTitlePlain} onBack={() => router.back()} />
      <ErrorText error={list.error} />
      {past && (
        <T variant="small" style={styles.note}>
          {s.archiveReadOnly}{' '}
          {photosOpen ? s.archivePartnerPhotosUntil(s.date(localDay(past.partnerPhotosUntil))) : s.archivePartnerPhotosGone}
        </T>
      )}

      {items.map((m) => (
        <ArchiveCard key={m.id} coupleId={coupleId} memory={m} />
      ))}

      {list.hasNextPage && (
        <Button
          title={s.showMore}
          variant="ghost"
          onPress={() => void list.fetchNextPage()}
          loading={list.isFetchingNextPage}
        />
      )}
    </Screen>
  );
}

function ArchiveCard({ coupleId, memory }: { coupleId: string; memory: Memory }) {
  const s = useStrings();
  const cover = memory.photos[0];
  return (
    <Link href={`/archive/${coupleId}/${memory.id}`} asChild>
      <Pressable accessibilityRole="button" accessibilityLabel={memory.title}>
        <Card>
          <View style={styles.row}>
            {cover && <Image source={{ uri: photoUrl(cover.thumb) }} style={styles.thumb} contentFit="cover" />}
            <View style={styles.flex}>
              <T variant="heading" numberOfLines={2}>
                {memory.title}
              </T>
              <T variant="small">{[s.date(memory.happenedOn), memory.place].filter(Boolean).join(' • ')}</T>
            </View>
          </View>
        </Card>
      </Pressable>
    </Link>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, gap: Spacing.one },
  row: { flexDirection: 'row', alignItems: 'center', gap: Spacing.three },
  thumb: { width: 64, height: 64, borderRadius: Radius.sm },
  note: { textAlign: 'center' },
});
