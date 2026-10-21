import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Image } from 'expo-image';
import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';
import { Pressable, StyleSheet, TextInput, View } from 'react-native';

import { api, photoUrl } from '@/api/client';
import type { BookPhoto, PhotoBook, SpreadLayout } from '@/api/types';
import { CoverView, SpreadView } from '@/components/book';
import { Button, Centered, Chip, ErrorText, Field, RoundButton, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

const LAYOUTS: SpreadLayout[] = ['full', 'photo_text', 'pair', 'trio', 'quad'];

// Edits one spread (or the cover, at position "cover"): its layout, which photo goes in which box, and
// its words. Nothing is saved until "Save", so trying layouts costs nothing.
export default function SpreadEditor() {
  const s = useStrings();
  const { position, year: yearParam } = useLocalSearchParams<{ position: string; year: string }>();
  const year = Number(yearParam);
  const isCover = position === 'cover';
  const book = useQuery({ queryKey: ['photo-book', year], queryFn: () => api.photoBook(year) });
  const photos = useQuery({ queryKey: ['photo-book-photos', year], queryFn: () => api.bookPhotos(year) });

  if (book.isPending || photos.isPending) return <Centered />;
  if (!book.data)
    return (
      <Screen>
        <TopBar title={s.photoBook} onBack={() => router.back()} />
        <ErrorText error={book.error ?? photos.error} />
      </Screen>
    );
  return isCover ? (
    <CoverEditor book={book.data} photos={photos.data?.memories ?? []} />
  ) : (
    <Editor
      key={position}
      book={book.data}
      position={Number(position)}
      photos={photos.data?.memories ?? []}
      used={photos.data?.used ?? []}
    />
  );
}

type Memories = { memoryId: string; title: string; happenedOn: string; photos: BookPhoto[] }[];

function Editor({ book, position, photos, used }: { book: PhotoBook; position: number; photos: Memories; used: string[] }) {
  const s = useStrings();
  const queryClient = useQueryClient();
  const spread = book.spreads[position];
  const [layout, setLayout] = useState<SpreadLayout>(spread.layout);
  const [ids, setIds] = useState<(string | null)[]>(spread.slots.map((x) => x.photo?.id ?? null));
  const [title, setTitle] = useState(spread.title ?? '');
  const [text, setText] = useState(spread.text ?? '');
  const [slot, setSlot] = useState<number | null>(() => {
    const empty = spread.slots.findIndex((x) => !x.photo);
    return empty >= 0 ? empty : 0;
  });

  const byId = useMemo(() => new Map(photos.flatMap((m) => m.photos.map((p) => [p.id, p] as const))), [photos]);
  const boxes = book.layouts.find((l) => l.layout === layout)!;
  const draft = {
    slots: boxes.slots.map((box, i) => ({ box, photo: (ids[i] && byId.get(ids[i]!)) || null })),
    story: boxes.story,
    caption: boxes.story ? null : spread.caption ?? { x: 0.03, y: 0.9, w: 0.44, h: 0.06 },
    title: title.trim() || null,
    text: boxes.story ? text.trim() || null : null,
    date: spread.date,
  };

  // Changing the layout keeps the photos already placed, in order, as far as the new layout has room.
  const pickLayout = (next: SpreadLayout) => {
    const count = book.layouts.find((l) => l.layout === next)!.slots.length;
    const kept = ids.filter((x): x is string => !!x).slice(0, count);
    setIds([...kept, ...Array<null>(count - kept.length).fill(null)]);
    setLayout(next);
    setSlot(kept.length < count ? kept.length : 0);
  };

  const put = (photoId: string) => {
    const target = slot ?? 0;
    // A photo already on this spread swaps places instead of appearing twice.
    setIds((all) => all.map((x, i) => (i === target ? photoId : x === photoId ? all[target] : x)));
    const nextEmpty = ids.findIndex((x, i) => i !== target && !x);
    setSlot(nextEmpty >= 0 ? nextEmpty : target);
  };

  const done = (b: PhotoBook) => {
    queryClient.setQueryData(['photo-book', b.year], b);
    void queryClient.invalidateQueries({ queryKey: ['photo-book-photos', b.year] });
  };
  const save = useMutation({
    mutationFn: () => api.updateSpread(book.year, position, { layout, photoIds: ids, title, text: boxes.story ? text : '' }),
    onSuccess: (b) => {
      done(b);
      router.back();
    },
  });
  const move = useMutation({
    mutationFn: (to: number) => api.moveSpread(book.year, position, to),
    onSuccess: (b, to) => {
      done(b);
      router.setParams({ position: String(to) });
    },
  });

  const last = book.spreads.length - 1;
  return (
    <Screen>
      <TopBar
        title={s.bookSpread(position + 1)}
        onBack={() => router.back()}
        right={
          <View style={styles.row}>
            <RoundButton label={s.bookMoveEarlier} glyph="←" onPress={() => position > 0 && move.mutate(position - 1)} />
            <RoundButton label={s.bookMoveLater} glyph="→" onPress={() => position < last && move.mutate(position + 1)} />
          </View>
        }
      />
      <SpreadView spread={draft} large selected={slot} onSlotPress={setSlot} emptyHint="+" />
      <T variant="small" style={styles.center}>
        {slot !== null ? s.bookPickHint(slot + 1) : s.bookTapBox}
      </T>

      <View style={styles.chips}>
        {LAYOUTS.map((l) => (
          <Chip key={l} label={s.bookLayouts[l]} selected={l === layout} onPress={() => pickLayout(l)} />
        ))}
      </View>

      {slot !== null && ids[slot] && (
        <Button
          title={s.bookRemovePhoto}
          variant="ghost"
          onPress={() => setIds((all) => all.map((x, i) => (i === slot ? null : x)))}
        />
      )}

      <PhotoGrid memories={photos} used={used} current={ids} onPick={put} />

      <Field label={s.bookCaption} value={title} onChangeText={setTitle} maxLength={250} placeholder={s.bookCaptionPlaceholder} />
      {boxes.story && (
        <View style={styles.group}>
          <T variant="label">{s.bookStory}</T>
          <TextInput
            value={text}
            onChangeText={setText}
            multiline
            maxLength={1000}
            placeholder={s.bookStoryPlaceholder}
            placeholderTextColor={Colors.textMuted}
            style={styles.textArea}
          />
        </View>
      )}

      <ErrorText error={save.error ?? move.error} />
      <Button title={s.save} onPress={() => save.mutate()} loading={save.isPending} />
    </Screen>
  );
}

function CoverEditor({ book, photos }: { book: PhotoBook; photos: Memories }) {
  const s = useStrings();
  const queryClient = useQueryClient();
  const [title, setTitle] = useState(book.cover.title);
  const [subtitle, setSubtitle] = useState(book.cover.subtitle ?? '');
  const [photoId, setPhotoId] = useState(book.cover.photo?.id ?? null);
  const byId = new Map(photos.flatMap((m) => m.photos.map((p) => [p.id, p] as const)));

  const save = useMutation({
    mutationFn: () => api.updateBookCover(book.year, { title, subtitle, ...(photoId && { coverPhotoId: photoId }) }),
    onSuccess: (b) => {
      queryClient.setQueryData(['photo-book', b.year], b);
      router.back();
    },
  });

  const draft: PhotoBook = {
    ...book,
    cover: { ...book.cover, title: title.trim() || book.cover.title, subtitle: subtitle.trim() || null, photo: (photoId && byId.get(photoId)) || null },
  };
  return (
    <Screen>
      <TopBar title={s.bookCover} onBack={() => router.back()} />
      <View style={styles.cover}>
        <CoverView book={draft} />
      </View>
      <Field label={s.bookCoverTitle} value={title} onChangeText={setTitle} maxLength={80} />
      <Field label={s.bookCoverSubtitle} value={subtitle} onChangeText={setSubtitle} maxLength={80} />
      <T variant="label">{s.bookCoverPhoto}</T>
      <PhotoGrid memories={photos} used={[]} current={photoId ? [photoId] : []} onPick={setPhotoId} />
      <ErrorText error={save.error} />
      <Button title={s.save} onPress={() => save.mutate()} loading={save.isPending} disabled={!title.trim()} />
    </Screen>
  );
}

// The year's photos by memory. A dot marks photos already somewhere in the book; a ring those on this page.
function PhotoGrid({
  memories,
  used,
  current,
  onPick,
}: {
  memories: Memories;
  used: string[];
  current: (string | null)[];
  onPick: (id: string) => void;
}) {
  const s = useStrings();
  return (
    <View style={styles.group}>
      {memories.map((m) => (
        <View key={m.memoryId} style={styles.group}>
          <T variant="small">
            {m.title} · {s.date(m.happenedOn)}
          </T>
          <View style={styles.grid}>
            {m.photos.map((p) => {
              const here = current.includes(p.id);
              return (
                <Pressable
                  key={p.id}
                  accessibilityRole="button"
                  accessibilityLabel={s.bookUsePhoto}
                  accessibilityState={{ selected: here }}
                  onPress={() => onPick(p.id)}
                  style={[styles.thumb, here && styles.thumbHere]}>
                  <Image source={{ uri: photoUrl(p.thumb) }} style={StyleSheet.absoluteFill} contentFit="cover" />
                  {!here && used.includes(p.id) && <View style={styles.dot} />}
                </Pressable>
              );
            })}
          </View>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: Spacing.two },
  center: { textAlign: 'center' },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
  group: { gap: Spacing.two },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
  thumb: { width: 72, height: 72, borderRadius: Radius.sm, overflow: 'hidden', backgroundColor: Colors.surface },
  thumbHere: { borderWidth: 3, borderColor: Colors.accent },
  dot: {
    position: 'absolute',
    right: 6,
    top: 6,
    width: 10,
    height: 10,
    borderRadius: 5,
    backgroundColor: Colors.moonLit,
  },
  cover: { width: '70%', alignSelf: 'center' },
  textArea: {
    minHeight: 120,
    borderRadius: Radius.md,
    borderColor: Colors.border,
    borderWidth: 1,
    backgroundColor: Colors.surfaceStrong,
    color: Colors.text,
    fontSize: 16,
    padding: Spacing.three,
    textAlignVertical: 'top',
  },
});
