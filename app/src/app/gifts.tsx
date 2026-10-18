import { useMutation, useQuery } from '@tanstack/react-query';
import * as WebBrowser from 'expo-web-browser';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, RefreshControl, Share, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { GiftOrder } from '@/api/types';
import { Button, Card, Centered, Chip, ErrorText, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

type Tier = 'plus' | 'premium';
const NAMES: Record<Tier, string> = { plus: 'Plus', premium: 'Premium' };

const tenge = (n: number) => `${String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ')} ₸`;

// Buying Ailu for another couple. The payment happens on the provider's page (Kaspi on the website,
// monetization doc), never in the app; once the server hears it went through, the order shows its code.
// Until the server has a payment provider, only the codes already bought are shown.
export default function Gifts() {
  const s = useStrings();
  const gifts = useQuery({ queryKey: ['gifts'], queryFn: api.gifts });
  const [tier, setTier] = useState<Tier>('plus');
  const [productId, setProductId] = useState<string | null>(null);

  const pay = async (order: GiftOrder) => {
    if (order.paymentUrl) await WebBrowser.openBrowserAsync(order.paymentUrl);
    // Back from the payment page: the server asks the provider again.
    await gifts.refetch();
  };
  const order = useMutation({
    mutationFn: async (id: string) => pay(await api.orderGift(id)),
  });

  if (gifts.isPending) return <Centered />;
  const products = gifts.data?.products.filter((p) => p.tier === tier) ?? [];
  const chosen = products.find((p) => p.id === productId) ?? products[0];
  const orders = gifts.data?.orders.filter((o) => o.status !== 'cancelled') ?? [];

  return (
    <Screen refreshControl={<RefreshControl refreshing={gifts.isRefetching} onRefresh={() => void gifts.refetch()} />}>
      <TopBar title={s.giftsTitle} onBack={() => router.back()} />
      <ErrorText error={gifts.error} />
      <T variant="secondary">{s.giftsHint}</T>

      {gifts.data?.available ? (
        <Card>
          <View style={styles.row}>
            {(['plus', 'premium'] as const).map((t) => (
              <Chip key={t} label={NAMES[t]} selected={tier === t} onPress={() => setTier(t)} />
            ))}
          </View>
          <T variant="label">{s.giftsLength}</T>
          <View accessibilityRole="radiogroup" style={styles.options}>
            {products.map((p) => (
              <Pressable
                key={p.id}
                accessibilityRole="radio"
                accessibilityState={{ checked: chosen?.id === p.id }}
                onPress={() => setProductId(p.id)}
                style={[styles.option, chosen?.id === p.id && styles.optionOn]}>
                <T variant="body">{s.giftName(NAMES[p.tier], p.months)}</T>
                <T variant="body" style={styles.price}>
                  {tenge(p.priceKzt)}
                </T>
              </Pressable>
            ))}
          </View>
          {chosen && (
            <Button
              title={s.giftsPay(tenge(chosen.priceKzt))}
              onPress={() => order.mutate(chosen.id)}
              loading={order.isPending}
            />
          )}
          <ErrorText error={order.error} />
        </Card>
      ) : (
        <Card>
          <T variant="body">{s.giftsSoon}</T>
        </Card>
      )}

      {orders.length > 0 && <T variant="label">{s.giftsMine}</T>}
      {orders.map((o) => (
        <GiftCard key={o.id} order={o} onPay={() => pay(o)} />
      ))}
    </Screen>
  );
}

function GiftCard({ order, onPay }: { order: GiftOrder; onPay: () => Promise<void> }) {
  const s = useStrings();
  const pay = useMutation({ mutationFn: onPay });
  const name = s.giftName(NAMES[order.tier], order.months);

  return (
    <Card>
      <T variant="heading">{name}</T>
      {order.status === 'pending' && (
        <>
          <T variant="secondary">{s.giftPending}</T>
          {order.paymentUrl && <Button title={s.giftPayNow} variant="ghost" onPress={() => pay.mutate()} loading={pay.isPending} />}
        </>
      )}
      {order.status === 'refunded' && <T variant="secondary">{s.giftRefunded}</T>}
      {order.status === 'paid' && order.redeemed && <T style={styles.redeemed}>{s.giftRedeemed}</T>}
      {order.status === 'paid' && !order.redeemed && order.code && (
        <>
          <T variant="small">{s.giftCodeHint}</T>
          <T selectable style={styles.code}>
            {order.code}
          </T>
          <Button
            title={s.giftSend}
            onPress={() => Share.share({ message: s.giftShare(NAMES[order.tier], order.months, order.code!) })}
          />
        </>
      )}
    </Card>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: Spacing.two },
  options: { gap: Spacing.two },
  option: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingVertical: Spacing.three,
    paddingHorizontal: Spacing.three,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  optionOn: { backgroundColor: Colors.surfaceStrong, borderColor: Colors.accent },
  price: { fontWeight: '600' },
  code: { fontSize: 26, letterSpacing: 3, fontWeight: '600', color: Colors.accent, textAlign: 'center' },
  redeemed: { color: Colors.success },
});
