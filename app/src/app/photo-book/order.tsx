import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import { CoverView, openPayment, tenge } from '@/components/book';
import { Button, Card, Centered, ErrorText, Field, Screen, T, TopBar } from '@/components/ui';
import { Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { useSession } from '@/session';

// Where the printed book goes, then Kaspi's page to pay. The order keeps the book as it is now.
export default function OrderBook() {
  const s = useStrings();
  const { user } = useSession();
  const queryClient = useQueryClient();
  const year = Number(useLocalSearchParams<{ year: string }>().year);
  const book = useQuery({ queryKey: ['photo-book', year], queryFn: () => api.photoBook(year) });
  const [name, setName] = useState(user?.name ?? '');
  const [phone, setPhone] = useState(user?.phone ?? '');
  const [city, setCity] = useState('');
  const [address, setAddress] = useState('');
  const [comment, setComment] = useState('');

  const order = useMutation({
    mutationFn: () => api.orderBook(year, { recipientName: name, phone, city, address, comment }),
    onSuccess: async (o) => {
      if (o.payUrl) await openPayment(o.payUrl);
      void queryClient.invalidateQueries({ queryKey: ['photo-book', year] });
      router.back();
    },
  });

  if (book.isPending) return <Centered />;
  const data = book.data;
  const ready = name.trim() && phone.trim() && city.trim() && address.trim();

  return (
    <Screen>
      <TopBar title={s.bookOrderTitle} onBack={() => router.back()} />
      {data && (
        <Card>
          <View style={styles.summary}>
            <View style={styles.thumb}>
              <CoverView book={data} />
            </View>
            <View style={styles.flex}>
              <T variant="heading">{s.bookTitle(data.year)}</T>
              <T variant="small">{s.bookPrintSpec(data.spreads.length)}</T>
              <T variant="heading">{tenge(data.priceKzt)}</T>
            </View>
          </View>
          <T variant="small">{s.bookPriceNote}</T>
        </Card>
      )}

      <Field label={s.bookRecipient} value={name} onChangeText={setName} maxLength={100} autoComplete="name" />
      <Field label={s.phoneLabel} value={phone} onChangeText={setPhone} keyboardType="phone-pad" autoComplete="tel" />
      <Field label={s.bookCity} value={city} onChangeText={setCity} maxLength={60} placeholder={s.bookCityPlaceholder} />
      <Field label={s.bookAddress} value={address} onChangeText={setAddress} maxLength={250} placeholder={s.bookAddressPlaceholder} />
      <Field label={s.bookComment} value={comment} onChangeText={setComment} maxLength={250} />

      <T variant="small">{s.bookFrozenNote}</T>
      <ErrorText error={order.error ?? book.error} />
      <Button
        title={s.bookPayKaspi(tenge(data?.priceKzt ?? 0))}
        onPress={() => order.mutate()}
        loading={order.isPending}
        disabled={!ready || !data}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  summary: { flexDirection: 'row', gap: Spacing.three, alignItems: 'center' },
  thumb: { width: 96 },
  flex: { flex: 1, gap: Spacing.one },
});
