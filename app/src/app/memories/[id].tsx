import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Alert, Platform, Pressable, ScrollView, StyleSheet, View } from 'react-native';

import { api, photoUrl } from '@/api/client';
import type { Memory } from '@/api/types';
import { Button, Card, Centered, ErrorText, RoundButton, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { MAX_PHOTOS, pickPhotos, toUpload } from '@/photos';
import { isPhotoLimit, openPaywall, photoLimitReached } from '@/upsells';

export default function MemoryScreen() {
  const s = useStrings();
  const { id } = useLocalSearchParams<{ id: string }>();
  const queryClient = useQueryClient();
  const memory = useQuery({ queryKey: ['memory', id], queryFn: () => api.memory(id) });
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });
  const storage = useQuery({ queryKey: ['storage'], queryFn: api.storage });
  const [shown, setShown] = useState(0);
  const [progress, setProgress] = useState<string | null>(null);

  const update = (m: Memory) => {
    queryClient.setQueryData(['memory', id], m);
    void queryClient.invalidateQueries({ queryKey: ['memories'] });
  };
  const react = useMutation({
    mutationFn: (on: boolean) => api.react(id, on),
    onSuccess: (m) => {
      update(m);
      // A partner's heart can earn нұр; let the home screen pick that up.
      void queryClient.invalidateQueries({ queryKey: ['today'] });
    },
  });
  const favorite = useMutation({ mutationFn: (on: boolean) => api.favorite(id, on), onSuccess: update });
  const remove = useMutation({
    mutationFn: () => api.deleteMemory(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['memories'] });
      void queryClient.invalidateQueries({ queryKey: ['storage'] });
      router.back();
    },
  });
  const addPhotos = useMutation({
    mutationFn: async (count: number) => {
      const picked = await pickPhotos(count);
      let latest: Memory | null = null;
      for (const [i, photo] of picked.entries()) {
        setProgress(s.uploading(i + 1, picked.length));
        latest = await api.addPhoto(id, await toUpload(photo));
        update(latest);
      }
      return latest;
    },
    onError: (e) => {
      if (isPhotoLimit(e, storage.data)) openPaywall('photos');
    },
    onSettled: () => {
      setProgress(null);
      void queryClient.invalidateQueries({ queryKey: ['storage'] });
    },
  });

  if (memory.isPending) return <Centered />;
  if (memory.isError)
    return (
      <Screen>
        <TopBar title={s.memories} onBack={() => router.back()} />
        <ErrorText error={memory.error} />
      </Screen>
    );

  const m = memory.data;
  const hero = m.photos[Math.min(shown, m.photos.length - 1)];
  const confirmDelete = () => {
    if (Platform.OS === 'web') {
      if (globalThis.confirm?.(s.deleteMemoryConfirm)) remove.mutate();
      return;
    }
    Alert.alert(s.deleteMemory, s.deleteMemoryConfirm, [
      { text: s.cancel, style: 'cancel' },
      { text: s.delete, style: 'destructive', onPress: () => remove.mutate() },
    ]);
  };

  return (
    <Screen>
      <TopBar
        title=""
        onBack={() => router.back()}
        right={<RoundButton label={s.favorite} glyph="★" active={m.favorite} onPress={() => favorite.mutate(!m.favorite)} />}
      />

      {hero && (
        <View style={[styles.hero, { aspectRatio: Math.max(0.75, Math.min(1.6, hero.width / hero.height)) }]}>
          <Image source={{ uri: photoUrl(hero.screen) }} style={StyleSheet.absoluteFill} contentFit="cover" transition={150} />
        </View>
      )}
      {m.photos.length > 1 && (
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.thumbs}>
          {m.photos.map((p, i) => (
            <Pressable key={p.id} onPress={() => setShown(i)} accessibilityRole="imagebutton">
              <Image
                source={{ uri: photoUrl(p.thumb) }}
                style={[styles.thumb, i === shown && styles.thumbSelected]}
                contentFit="cover"
              />
            </Pressable>
          ))}
        </ScrollView>
      )}

      <Card>
        <T variant="title">{m.title}</T>
        <T variant="small">{[s.date(m.happenedOn), m.place].filter(Boolean).join(' • ')}</T>
        {m.text && <T variant="body">{m.text}</T>}
        <T variant="small">{s.by(m.mine ? s.someone : (m.author.name ?? s.partnerFallback))}</T>

        {m.mine ? (
          m.reactedByPartner && <T variant="small" style={styles.hearted}>♥ {s.partnerHearted(couple.data?.partner?.name ?? s.partnerFallback)}</T>
        ) : (
          <Button
            title={`${m.reactedByMe ? '♥' : '♡'} ${s.heart}`}
            variant={m.reactedByMe ? 'primary' : 'ghost'}
            onPress={() => react.mutate(!m.reactedByMe)}
            loading={react.isPending}
          />
        )}
        <ErrorText error={react.error ?? favorite.error} />
      </Card>

      {m.mine && (
        <>
          {m.photos.length < MAX_PHOTOS && (
            <Button
              title={progress ?? s.addPhotos}
              variant="ghost"
              onPress={() =>
                photoLimitReached(storage.data) ? openPaywall('photos') : addPhotos.mutate(MAX_PHOTOS - m.photos.length)
              }
              loading={addPhotos.isPending && !progress}
              disabled={addPhotos.isPending}
            />
          )}
          <ErrorText error={addPhotos.error ?? remove.error} />
          <Button title={s.deleteMemory} variant="ghost" onPress={confirmDelete} loading={remove.isPending} />
        </>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { width: '100%', borderRadius: Radius.lg, overflow: 'hidden', backgroundColor: Colors.surface },
  thumbs: { gap: Spacing.two },
  thumb: { width: 64, height: 64, borderRadius: Radius.sm, opacity: 0.7 },
  thumbSelected: { opacity: 1, borderWidth: 2, borderColor: Colors.accent },
  hearted: { color: Colors.accent },
});
