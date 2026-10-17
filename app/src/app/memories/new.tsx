import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import { Button, ErrorText, Field, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { MAX_PHOTOS, pickPhotos, toUpload, type Picked } from '@/photos';

// Dates are typed as ДД.ММ.ГГГГ: a picker needs another native module, and most memories are "today".
function toIso(text: string): string | null {
  const m = /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/.exec(text.trim());
  if (!m) return null;
  const [d, mo, y] = [Number(m[1]), Number(m[2]), Number(m[3])];
  const date = new Date(Date.UTC(y, mo - 1, d));
  if (date.getUTCDate() !== d || date.getUTCMonth() !== mo - 1 || date > new Date()) return null;
  return `${y}-${String(mo).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
}

function todayText() {
  const now = new Date();
  return `${String(now.getDate()).padStart(2, '0')}.${String(now.getMonth() + 1).padStart(2, '0')}.${now.getFullYear()}`;
}

export default function NewMemory() {
  const s = useStrings();
  const queryClient = useQueryClient();
  // Opened from a finished task, the memory is tied to it and starts with its title.
  const params = useLocalSearchParams<{ taskId?: string; title?: string }>();
  const [title, setTitle] = useState(params.title ?? '');
  const [date, setDate] = useState(todayText);
  const [place, setPlace] = useState('');
  const [text, setText] = useState('');
  const [photos, setPhotos] = useState<Picked[]>([]);
  const [progress, setProgress] = useState<string | null>(null);

  const pick = useMutation({
    mutationFn: () => pickPhotos(MAX_PHOTOS - photos.length),
    onSuccess: (more) => setPhotos((p) => [...p, ...more].slice(0, MAX_PHOTOS)),
  });

  const save = useMutation({
    mutationFn: async () => {
      const happenedOn = toIso(date);
      if (!happenedOn) throw new Error(s.memoryDateInvalid);
      const memory = await api.createMemory({
        title: title.trim(),
        text: text.trim() || null,
        place: place.trim() || null,
        happenedOn,
        coupleTaskId: params.taskId ?? null,
      });
      // The memory exists from here on; a photo that fails can be added again from its page.
      let failed = 0;
      for (const [i, photo] of photos.entries()) {
        setProgress(s.uploading(i + 1, photos.length));
        try {
          await api.addPhoto(memory.id, await toUpload(photo));
        } catch {
          failed++;
        }
      }
      return { id: memory.id, failed };
    },
    onSettled: () => setProgress(null),
    onSuccess: ({ id, failed }) => {
      void queryClient.invalidateQueries({ queryKey: ['memories'] });
      void queryClient.invalidateQueries({ queryKey: ['storage'] });
      void queryClient.invalidateQueries({ queryKey: ['today'] });
      if (failed > 0) globalThis.alert?.(s.uploadFailed(failed));
      router.replace(`/memories/${id}`);
    },
  });

  return (
    <Screen>
      <TopBar title={s.newMemory} onBack={() => router.back()} />
      <Field
        label={s.memoryTitle}
        placeholder={s.memoryTitlePlaceholder}
        value={title}
        onChangeText={setTitle}
        maxLength={120}
      />
      <Field
        label={s.memoryDate}
        placeholder={s.memoryDatePlaceholder}
        value={date}
        onChangeText={setDate}
        keyboardType="numbers-and-punctuation"
        maxLength={10}
      />
      <Field label={s.memoryPlace} value={place} onChangeText={setPlace} maxLength={120} />
      <Field
        label={s.memoryText}
        value={text}
        onChangeText={setText}
        multiline
        maxLength={2000}
        style={styles.textInput}
      />

      {photos.length > 0 && (
        <View style={styles.grid}>
          {photos.map((p, i) => (
            <View key={`${p.uri}-${i}`} style={styles.tile}>
              <Image source={{ uri: p.uri }} style={StyleSheet.absoluteFill} contentFit="cover" />
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={s.removePhoto}
                onPress={() => setPhotos((all) => all.filter((_, j) => j !== i))}
                style={styles.remove}>
                <T variant="small" style={styles.removeText}>
                  ✕
                </T>
              </Pressable>
            </View>
          ))}
        </View>
      )}
      {photos.length < MAX_PHOTOS && (
        <Button
          title={photos.length ? `${s.addPhotos} · ${s.photosCount(photos.length, MAX_PHOTOS)}` : s.addPhotos}
          variant="ghost"
          onPress={() => pick.mutate()}
          disabled={save.isPending}
        />
      )}

      <ErrorText error={pick.error ?? save.error} />
      {progress && <T variant="small">{progress}</T>}
      <Button title={s.save} onPress={() => save.mutate()} loading={save.isPending} disabled={!title.trim()} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  textInput: { minHeight: 120, paddingTop: Spacing.three, textAlignVertical: 'top', fontSize: 16 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
  tile: { width: 96, height: 96, borderRadius: Radius.sm, overflow: 'hidden', backgroundColor: Colors.surface },
  remove: {
    position: 'absolute',
    top: 4,
    right: 4,
    width: 26,
    height: 26,
    borderRadius: Radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: 'rgba(0,0,0,0.6)',
  },
  removeText: { color: Colors.text },
});
