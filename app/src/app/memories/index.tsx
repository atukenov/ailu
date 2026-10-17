import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { Image } from 'expo-image';
import { Link, router } from 'expo-router';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';

import { api, photoUrl } from '@/api/client';
import type { Memory, Storage } from '@/api/types';
import { Button, Card, Centered, ErrorText, RoundButton, Screen, T, TopBar } from '@/components/ui';
import { Colors, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { biggerPlan, openPaywall, photoLimitReached } from '@/upsells';

export default function Memories() {
  const s = useStrings();
  const list = useInfiniteQuery({
    queryKey: ['memories'],
    queryFn: ({ pageParam }) => api.memories(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  });
  const storage = useQuery({ queryKey: ['storage'], queryFn: api.storage });

  if (list.isPending) return <Centered />;
  const items = list.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <Screen
      refreshControl={
        <RefreshControl
          refreshing={list.isRefetching}
          onRefresh={() => {
            void list.refetch();
            void storage.refetch();
          }}
          tintColor={Colors.accent}
        />
      }>
      <TopBar
        title={s.memories}
        onBack={() => router.back()}
        right={<RoundButton label={s.addMemory} glyph="+" onPress={() => router.push('/memories/new')} />}
      />
      <ErrorText error={list.error} />

      {items.length === 0 && !list.isError ? (
        <Card>
          <T variant="heading">{s.memoriesEmpty}</T>
          <T variant="secondary">{s.memoriesEmptyHint}</T>
          <Button title={s.addMemory} onPress={() => router.push('/memories/new')} />
        </Card>
      ) : (
        items.map((m) => <MemoryCard key={m.id} memory={m} />)
      )}

      {list.hasNextPage && (
        <Button
          title={s.showMore}
          variant="ghost"
          onPress={() => void list.fetchNextPage()}
          loading={list.isFetchingNextPage}
        />
      )}
      {storage.data && <StorageLine storage={storage.data} />}
      {storage.data && photoLimitReached(storage.data) && (
        <Button
          title={s.storageFullCta(s.planName(biggerPlan(storage.data.plan) ?? 'plus'))}
          variant="ghost"
          onPress={() => openPaywall('photos')}
        />
      )}
    </Screen>
  );
}

// Free couples keep up to 200 photos, Plus 500, Premium 1 200.
function StorageLine({ storage }: { storage: Storage }) {
  const s = useStrings();
  const { photos, photoLimit: limit } = storage;
  const full = limit !== null && photos >= limit;
  const next = biggerPlan(storage.plan);
  return (
    <T variant="small" style={[styles.storage, full && { color: Colors.danger }]}>
      {limit === null
        ? s.photosStored(photos)
        : full
          ? s.storageFull(limit, next && s.planName(next))
          : s.photosOfLimit(photos, limit)}
    </T>
  );
}

function MemoryCard({ memory }: { memory: Memory }) {
  const s = useStrings();
  const cover = memory.photos[0];
  return (
    <Link href={`/memories/${memory.id}`} asChild>
      <Pressable accessibilityRole="button" accessibilityLabel={memory.title}>
        {cover ? (
          <View style={styles.photoCard}>
            <Image
              source={{ uri: photoUrl(memory.photos[0].screen) }}
              style={StyleSheet.absoluteFill}
              contentFit="cover"
              transition={150}
            />
            <View style={styles.shade} />
            {memory.favorite && <T style={styles.star}>★</T>}
            <View style={styles.caption}>
              <T variant="heading" numberOfLines={1}>
                {memory.title}
              </T>
              <T variant="small" style={styles.captionMeta}>
                {[s.date(memory.happenedOn), memory.place].filter(Boolean).join(' • ')}
                {memory.photos.length > 1 ? ` · ${s.photosStored(memory.photos.length)}` : ''}
              </T>
            </View>
          </View>
        ) : (
          <Card>
            <View style={styles.row}>
              <T variant="heading" style={styles.flex} numberOfLines={2}>
                {memory.title}
              </T>
              {memory.favorite && <T style={styles.starInline}>★</T>}
            </View>
            <T variant="small">{[s.date(memory.happenedOn), memory.place].filter(Boolean).join(' • ')}</T>
            {memory.text && (
              <T variant="secondary" numberOfLines={3}>
                {memory.text}
              </T>
            )}
          </Card>
        )}
      </Pressable>
    </Link>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  row: { flexDirection: 'row', alignItems: 'flex-start', gap: Spacing.two },
  photoCard: { height: 268, borderRadius: 26, overflow: 'hidden', backgroundColor: Colors.surface },
  shade: { ...StyleSheet.absoluteFill, backgroundColor: 'rgba(0,0,0,0.18)' },
  caption: {
    position: 'absolute',
    left: 0,
    right: 0,
    bottom: 0,
    padding: Spacing.four,
    gap: Spacing.one,
    backgroundColor: 'rgba(16,14,18,0.55)',
  },
  captionMeta: { color: Colors.text },
  star: { position: 'absolute', top: Spacing.three, right: Spacing.three, fontSize: 26, color: Colors.moonLit },
  starInline: { fontSize: 22, color: Colors.moonLit },
  storage: { textAlign: 'center' },
});
