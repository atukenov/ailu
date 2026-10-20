import { Image } from 'expo-image';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { Pressable, StyleSheet, Text, View, type LayoutChangeEvent } from 'react-native';

import { apiLink, photoUrl } from '@/api/client';
import type { BookPhoto, Box, PhotoBook, Spread } from '@/api/types';
import { Colors } from '@/constants/theme';
import { isoToText } from '@/dates';

// The printed book's own colors: paper, ink and the grey of an empty box, as the server draws the PDF.
export const Paper = { paper: '#FFFDF9', ink: '#2A1E18', muted: '#8F857B', hole: '#EEE8E1' } as const;

export const tenge = (n: number) => `${String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ')} ₸`;

// Kaspi's payment page opens in the browser; the book screen fetches the order again when the person is back.
export async function openPayment(url: string) {
  await WebBrowser.openBrowserAsync(apiLink(url));
}

// Point sizes of the PDF over the spread's width in points (two 20 cm pages), so text scales with the preview.
const SPREAD_PT = 1133.86;
const scale = (width: number, pt: number) => (width * pt) / SPREAD_PT;

function useWidth() {
  const [width, setWidth] = useState(0);
  return [width, (e: LayoutChangeEvent) => setWidth(e.nativeEvent.layout.width)] as const;
}

function place(b: Box) {
  return { left: `${b.x * 100}%`, top: `${b.y * 100}%`, width: `${b.w * 100}%`, height: `${b.h * 100}%` } as const;
}

function Photo({ photo, large, hint }: { photo: BookPhoto | null; large?: boolean; hint?: string }) {
  if (!photo)
    return (
      <View style={styles.hole}>
        {hint ? <Text style={styles.holeText}>{hint}</Text> : null}
      </View>
    );
  return (
    <Image
      source={{ uri: photoUrl(large ? photo.screen : photo.thumb) }}
      style={StyleSheet.absoluteFill}
      contentFit="cover"
      transition={120}
    />
  );
}

// One spread as it will be printed. With onSlotPress each box can be tapped; `selected` rings one of them.
export function SpreadView({
  spread,
  large,
  selected,
  onSlotPress,
  emptyHint,
}: {
  spread: Pick<Spread, 'slots' | 'story' | 'caption' | 'title' | 'text' | 'date'>;
  large?: boolean;
  selected?: number | null;
  onSlotPress?: (index: number) => void;
  emptyHint?: string;
}) {
  const [width, onLayout] = useWidth();
  const date = spread.date ? isoToText(spread.date) : null;
  return (
    <View style={styles.spread} onLayout={onLayout}>
      {spread.slots.map((slot, i) => (
        <Pressable
          key={i}
          disabled={!onSlotPress}
          onPress={() => onSlotPress?.(i)}
          accessibilityRole={onSlotPress ? 'button' : undefined}
          style={[styles.slot, place(slot.box), selected === i && styles.selected]}>
          <Photo photo={slot.photo} large={large} hint={emptyHint} />
        </Pressable>
      ))}
      {spread.story && width > 0 && (
        <View style={[styles.abs, place(spread.story)]} pointerEvents="none">
          {spread.title ? (
            <Text numberOfLines={3} style={[styles.storyTitle, { fontSize: scale(width, 18), lineHeight: scale(width, 24) }]}>
              {spread.title}
            </Text>
          ) : null}
          {date && <Text style={{ color: Paper.muted, fontSize: scale(width, 10), marginTop: scale(width, 6) }}>{date}</Text>}
          {spread.text ? (
            <Text
              style={{ color: Paper.ink, fontSize: scale(width, 11), lineHeight: scale(width, 16), marginTop: scale(width, 12) }}
              numberOfLines={Math.max(1, Math.floor((spread.story.h * width * 0.5 - scale(width, 70)) / scale(width, 16)))}>
              {spread.text}
            </Text>
          ) : null}
        </View>
      )}
      {spread.caption && width > 0 && (spread.title || date) && (
        <View style={[styles.abs, styles.captionBox, place(spread.caption)]} pointerEvents="none">
          <Text numberOfLines={1} style={{ color: Paper.muted, fontSize: scale(width, 9) }}>
            {[spread.title, date].filter(Boolean).join(' · ')}
          </Text>
        </View>
      )}
      <View style={styles.fold} pointerEvents="none" />
    </View>
  );
}

export function CoverView({ book, onPhotoPress }: { book: PhotoBook; onPhotoPress?: () => void }) {
  const [width, onLayout] = useWidth();
  const { cover } = book;
  // The cover is one page: half a spread's width in points.
  const pt = (n: number) => (width * n) / (SPREAD_PT / 2);
  return (
    <View style={styles.cover} onLayout={onLayout}>
      <Pressable disabled={!onPhotoPress} onPress={onPhotoPress} style={[styles.slot, place(cover.photoBox)]}>
        <Photo photo={cover.photo} large />
      </Pressable>
      {width > 0 && (
        <>
          <View style={[styles.abs, styles.center, place(cover.titleBox)]} pointerEvents="none">
            <Text numberOfLines={1} style={[styles.coverTitle, { fontSize: pt(26) }]}>
              {cover.title}
            </Text>
          </View>
          {cover.subtitle ? (
            <View style={[styles.abs, styles.center, place(cover.subtitleBox)]} pointerEvents="none">
              <Text numberOfLines={1} style={{ color: Paper.muted, fontSize: pt(14) }}>
                {cover.subtitle}
              </Text>
            </View>
          ) : null}
        </>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  spread: { width: '100%', aspectRatio: 2, backgroundColor: Paper.paper, borderRadius: 6, overflow: 'hidden' },
  cover: { width: '100%', aspectRatio: 1, backgroundColor: Paper.paper, borderRadius: 6, overflow: 'hidden' },
  abs: { position: 'absolute' },
  slot: { position: 'absolute', overflow: 'hidden', backgroundColor: Paper.hole },
  selected: { borderWidth: 3, borderColor: Colors.accent },
  hole: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: Paper.hole },
  holeText: { color: Paper.muted, fontSize: 22 },
  storyTitle: { color: Paper.ink, fontWeight: '600' },
  captionBox: { justifyContent: 'center' },
  center: { alignItems: 'center', justifyContent: 'center' },
  coverTitle: { color: Paper.ink, fontWeight: '600' },
  // The gutter between the two pages: a faint shadow, as an open book has.
  fold: { position: 'absolute', left: '49.6%', width: '0.8%', top: 0, bottom: 0, backgroundColor: 'rgba(42,30,24,0.08)' },
});
