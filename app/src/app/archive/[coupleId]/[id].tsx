import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Alert, Platform, Pressable, ScrollView, StyleSheet, View } from 'react-native';

import { api, photoUrl } from '@/api/client';
import { Button, Card, Centered, ErrorText, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

// One memory of an ended couple. Read-only; its author can still delete it.
export default function ArchiveMemory() {
  const s = useStrings();
  const { coupleId, id } = useLocalSearchParams<{ coupleId: string; id: string }>();
  const queryClient = useQueryClient();
  const memory = useQuery({ queryKey: ['archive', coupleId, 'memory', id], queryFn: () => api.archiveMemory(coupleId, id) });
  const [shown, setShown] = useState(0);
  const remove = useMutation({
    mutationFn: () => api.deleteArchiveMemory(coupleId, id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['archive'] });
      router.back();
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
      if (globalThis.confirm?.(s.deleteArchiveMemoryConfirm)) remove.mutate();
      return;
    }
    Alert.alert(s.deleteMemory, s.deleteArchiveMemoryConfirm, [
      { text: s.cancel, style: 'cancel' },
      { text: s.delete, style: 'destructive', onPress: () => remove.mutate() },
    ]);
  };

  return (
    <Screen>
      <TopBar title="" onBack={() => router.back()} />

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
      </Card>

      {m.mine && (
        <>
          <ErrorText error={remove.error} />
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
});
