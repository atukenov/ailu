import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, router, useFocusEffect, useLocalSearchParams } from 'expo-router';
import * as WebBrowser from 'expo-web-browser';
import { useCallback, useState } from 'react';
import { Alert, Platform, Pressable, RefreshControl, StyleSheet, View } from 'react-native';

import { api, apiLink } from '@/api/client';
import type { BookOrder, PhotoBook } from '@/api/types';
import { CoverView, openPayment, SpreadView, tenge } from '@/components/book';
import { Button, Card, Centered, Chip, ErrorText, Screen, T, TopBar } from '@/components/ui';
import { Colors, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

function confirm(title: string, body: string, ok: string, cancel: string, action: () => void) {
  if (Platform.OS === 'web') {
    if (globalThis.confirm?.(body)) action();
    return;
  }
  Alert.alert(title, body, [
    { text: cancel, style: 'cancel' },
    { text: ok, style: 'destructive', onPress: action },
  ]);
}

export default function PhotoBookScreen() {
  const s = useStrings();
  const queryClient = useQueryClient();
  const params = useLocalSearchParams<{ year?: string }>();
  const years = useQuery({ queryKey: ['photo-book-years'], queryFn: api.photoBookYears });
  const [picked, setPicked] = useState<number | null>(params.year ? Number(params.year) : null);
  // The newest year that has a book, else the newest year with photos.
  const year = picked ?? years.data?.years.find((y) => y.hasBook)?.year ?? years.data?.years[0]?.year ?? null;
  const book = useQuery({
    queryKey: ['photo-book', year],
    queryFn: () => api.photoBook(year!),
    enabled: year !== null,
  });

  // Back from the editor, the order form or Kaspi's page: show what changed.
  useFocusEffect(
    useCallback(() => {
      void queryClient.invalidateQueries({ queryKey: ['photo-book'] });
    }, [queryClient]),
  );

  const set = (b: PhotoBook) => {
    queryClient.setQueryData(['photo-book', b.year], b);
    void queryClient.invalidateQueries({ queryKey: ['photo-book-years'] });
  };
  const build = useMutation({ mutationFn: () => api.buildPhotoBook(year!), onSuccess: set });

  if (years.isPending) return <Centered />;
  const info = years.data?.years.find((y) => y.year === year);
  const data = book.data;

  return (
    <Screen
      refreshControl={
        <RefreshControl
          refreshing={book.isRefetching}
          onRefresh={() => {
            void years.refetch();
            void book.refetch();
          }}
          tintColor={Colors.accent}
        />
      }>
      <TopBar title={s.photoBook} onBack={() => router.back()} />
      <ErrorText error={years.error ?? book.error ?? build.error} />

      {years.data && years.data.years.length === 0 && (
        <Card>
          <T variant="heading">{s.bookNoPhotosTitle}</T>
          <T variant="secondary">{s.bookNoPhotosBody}</T>
          <Button title={s.addMemory} onPress={() => router.push('/memories/new')} />
        </Card>
      )}

      {years.data && years.data.years.length > 1 && (
        <View style={styles.chips}>
          {years.data.years.map((y) => (
            <Chip key={y.year} label={String(y.year)} selected={y.year === year} onPress={() => setPicked(y.year)} />
          ))}
        </View>
      )}

      {year !== null && book.isPending && <Centered />}

      {year !== null && !book.isPending && !data && years.data && (
        <Card>
          <T variant="label">{s.bookKicker}</T>
          <T variant="title">{s.bookTitle(year)}</T>
          <T variant="secondary">{s.bookIntro(years.data.spreads)}</T>
          {info && <T variant="small">{s.bookFrom(info.photos, info.memories)}</T>}
          <T variant="heading">{tenge(years.data.priceKzt)}</T>
          <T variant="small">{s.bookPriceNote}</T>
          <Button title={s.bookBuild} onPress={() => build.mutate()} loading={build.isPending} />
        </Card>
      )}

      {data && (
        <>
          {data.orders.map((o) => (
            <OrderCard key={o.id} order={o} />
          ))}

          <Card>
            <T variant="title">{s.bookTitle(data.year)}</T>
            <T variant="secondary">{data.empty > 0 ? s.bookEmptyLeft(data.empty) : s.bookReady}</T>
            {data.locked && <T variant="small">{s.bookLocked}</T>}
          </Card>

          <Pressable
            accessibilityRole="button"
            accessibilityLabel={s.bookCover}
            disabled={data.locked}
            onPress={() => router.push({ pathname: '/photo-book/[position]', params: { position: 'cover', year: data.year } })}
            style={styles.coverWrap}>
            <CoverView book={data} />
            <T variant="small" style={styles.caption}>
              {s.bookCover}
            </T>
          </Pressable>

          {data.spreads.map((spread) => (
            <Link
              key={spread.position}
              href={{ pathname: '/photo-book/[position]', params: { position: spread.position, year: data.year } }}
              disabled={data.locked}
              asChild>
              <Pressable accessibilityRole="button" accessibilityLabel={s.bookSpread(spread.position + 1)}>
                <SpreadView spread={spread} />
                <T variant="small" style={[styles.caption, spread.empty > 0 && { color: Colors.accent }]}>
                  {spread.empty > 0 ? s.bookSpreadEmpty(spread.position + 1, spread.empty) : s.bookSpread(spread.position + 1)}
                </T>
              </Pressable>
            </Link>
          ))}

          <Button
            title={s.bookOrder(tenge(data.priceKzt))}
            disabled={data.empty > 0 || data.locked}
            onPress={() => router.push({ pathname: '/photo-book/order', params: { year: data.year } })}
          />
          {data.empty > 0 && <T variant="small" style={styles.caption}>{s.bookFillFirst}</T>}
          <Button title={s.bookPreview} variant="ghost" onPress={() => void WebBrowser.openBrowserAsync(apiLink(data.previewUrl))} />
          {!data.locked && (
            <Button
              title={s.bookRebuild}
              variant="ghost"
              loading={build.isPending}
              onPress={() => confirm(s.bookRebuild, s.bookRebuildConfirm, s.bookRebuildOk, s.cancel, () => build.mutate())}
            />
          )}
        </>
      )}
    </Screen>
  );
}

function OrderCard({ order }: { order: BookOrder }) {
  const s = useStrings();
  const queryClient = useQueryClient();
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['photo-book'] });
  const pay = useMutation({
    mutationFn: () => api.payOrder(order.id),
    onSuccess: async (o) => {
      if (o.payUrl) await openPayment(o.payUrl);
      refresh();
    },
  });
  const cancel = useMutation({ mutationFn: () => api.cancelOrder(order.id), onSuccess: refresh });
  const waiting = order.status === 'awaiting_payment';

  return (
    <Card style={waiting ? styles.waiting : undefined}>
      <T variant="label">{s.bookOrderKicker}</T>
      <T variant="heading">{s.bookOrderStatus[order.status]}</T>
      <T variant="small">
        {[order.recipientName, order.city, order.address].filter(Boolean).join(', ')}
      </T>
      {order.trackingNumber && <T variant="small">{s.bookTracking(order.trackingNumber)}</T>}
      {waiting && !order.payUrl && <T variant="secondary">{s.bookKaspiSoon}</T>}
      <ErrorText error={pay.error ?? cancel.error} />
      {waiting && (
        <>
          <Button title={s.bookPay(tenge(order.priceKzt))} onPress={() => pay.mutate()} loading={pay.isPending} />
          <Button
            title={s.bookCancelOrder}
            variant="ghost"
            loading={cancel.isPending}
            onPress={() => confirm(s.bookCancelOrder, s.bookCancelConfirm, s.bookCancelOk, s.cancel, () => cancel.mutate())}
          />
        </>
      )}
    </Card>
  );
}

const styles = StyleSheet.create({
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
  coverWrap: { width: '62%', alignSelf: 'center' },
  caption: { textAlign: 'center', marginTop: Spacing.two },
  waiting: { borderColor: Colors.accent },
});
