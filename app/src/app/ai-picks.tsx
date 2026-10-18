import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, RefreshControl, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { Ai, AiBudget, AiItem, AiKind, AiPick } from '@/api/types';
import { Button, Card, Centered, Chip, ErrorText, Field, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';
import { openPaywall } from '@/upsells';

const KINDS: AiKind[] = ['date', 'home', 'film', 'music'];
const BUDGETS: AiBudget[] = ['any', 'free', 'low'];
const NOTE_LENGTH = 140;

// AI selections (Premium): pick a kind, maybe a budget and a wish, and get five ideas both partners see.
// Either partner can save an idea for both; saved ideas and past selections stay after the plan ends.
export default function AiPicksScreen() {
  const s = useStrings();
  const params = useLocalSearchParams<{ kind?: AiKind }>();
  const queryClient = useQueryClient();
  const ai = useQuery({ queryKey: ['ai'], queryFn: api.ai });
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });
  const [tab, setTab] = useState<'picks' | 'saved'>('picks');
  const [kind, setKind] = useState<AiKind>(params.kind && KINDS.includes(params.kind) ? params.kind : 'date');
  const [budget, setBudget] = useState<AiBudget>('any');
  const [note, setNote] = useState('');

  const ask = useMutation({
    mutationFn: () => api.aiPick(kind, kind === 'date' || kind === 'home' ? budget : undefined, note.trim() || undefined),
    onSuccess: (pick) => {
      queryClient.setQueryData<Ai>(['ai'], (old) =>
        old ? { ...old, used: old.used + 1, picks: [pick, ...old.picks] } : old,
      );
      setNote('');
      void queryClient.invalidateQueries({ queryKey: ['ai'] });
    },
  });

  if (ai.isPending) return <Centered />;
  const data = ai.data;
  const partnerName = couple.data?.partner?.name ?? s.partnerFallback;
  const left = data ? Math.max(0, data.limit - data.used) : 0;

  return (
    <Screen
      refreshControl={
        <RefreshControl refreshing={ai.isRefetching} onRefresh={() => void ai.refetch()} tintColor={Colors.accent} />
      }>
      <TopBar title={s.aiPicks} onBack={() => router.back()} />
      <ErrorText error={ai.error} />

      <View style={styles.tabs}>
        <Chip label={s.aiTabPicks} selected={tab === 'picks'} onPress={() => setTab('picks')} />
        <Chip label={s.aiTabSaved} selected={tab === 'saved'} onPress={() => setTab('saved')} />
      </View>

      {tab === 'picks' && data && (
        <>
          {data.available ? (
            <Card>
              <T variant="secondary">{s.aiPicksHint}</T>
              <View style={styles.chips}>
                {KINDS.map((k) => (
                  <Chip key={k} label={s.aiKinds[k]} selected={kind === k} onPress={() => setKind(k)} />
                ))}
              </View>
              {(kind === 'date' || kind === 'home') && (
                <View style={styles.chips}>
                  {BUDGETS.map((b) => (
                    <Chip key={b} label={s.aiBudgets[b]} selected={budget === b} onPress={() => setBudget(b)} />
                  ))}
                </View>
              )}
              <Field
                label={s.aiNoteLabel}
                placeholder={s.aiNotePlaceholder}
                value={note}
                onChangeText={setNote}
                maxLength={NOTE_LENGTH}
              />
              <T variant="small">{s.aiPrivacy}</T>
              <ErrorText error={ask.error} />
              {left > 0 ? (
                <>
                  <Button title={s.aiAsk} onPress={() => ask.mutate()} loading={ask.isPending} />
                  <T variant="small">{s.aiLeft(left, data.limit)}</T>
                </>
              ) : (
                <T variant="small">{s.aiQuotaUsed}</T>
              )}
            </Card>
          ) : (
            <Card>
              <T variant="secondary">{s.aiLocked}</T>
              <Button title={s.aiUnlock} onPress={() => openPaywall('ai')} />
            </Card>
          )}

          {data.picks.length === 0 && data.available && <T variant="small">{s.aiEmpty}</T>}
          {data.picks.map((pick) => (
            <PickCard key={pick.id} pick={pick} partnerName={partnerName} />
          ))}
        </>
      )}

      {tab === 'saved' && data && (
        <>
          {data.saved.length === 0 && <T variant="small">{s.aiSavedEmpty}</T>}
          {data.saved.length > 0 && (
            <Card>
              {data.saved.map((item, i) => (
                <Idea key={item.id} item={item} showKind first={i === 0} />
              ))}
            </Card>
          )}
        </>
      )}
    </Screen>
  );
}

function PickCard({ pick, partnerName }: { pick: AiPick; partnerName: string }) {
  const s = useStrings();
  return (
    <Card>
      <T variant="label">{s.aiKinds[pick.kind]}</T>
      {pick.wish && <T variant="small">“{pick.wish}”</T>}
      {!pick.byMe && <T variant="small">{s.aiAskedBy(partnerName)}</T>}
      {pick.items.map((item) => (
        <Idea key={item.id} item={item} />
      ))}
    </Card>
  );
}

// One idea with the heart that saves it for both partners.
function Idea({ item, showKind, first }: { item: AiItem; showKind?: boolean; first?: boolean }) {
  const s = useStrings();
  const queryClient = useQueryClient();
  const save = useMutation({
    mutationFn: (on: boolean) => api.saveIdea(item.id, on),
    onSuccess: (changed) => {
      queryClient.setQueryData<Ai>(['ai'], (old) =>
        old
          ? {
              ...old,
              picks: old.picks.map((p) => ({ ...p, items: p.items.map((i) => (i.id === changed.id ? changed : i)) })),
              saved: changed.saved ? [changed, ...old.saved.filter((i) => i.id !== changed.id)] : old.saved.filter((i) => i.id !== changed.id),
            }
          : old,
      );
    },
  });

  return (
    <View style={[styles.idea, first && styles.ideaFirst]}>
      <View style={styles.ideaText}>
        {showKind && <T variant="label">{s.aiKinds[item.kind]}</T>}
        <T variant="heading" style={styles.ideaTitle}>
          {item.title}
        </T>
        <T variant="secondary">{item.body}</T>
        <T variant="small">{s.aiMeta(item.kind, item.durationMin, item.costKzt)}</T>
        <ErrorText error={save.error} />
      </View>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={item.saved ? s.aiUnsave : s.aiSave}
        accessibilityState={{ selected: item.saved }}
        hitSlop={8}
        disabled={save.isPending}
        onPress={() => save.mutate(!item.saved)}
        style={[styles.heart, item.saved && styles.heartOn]}>
        <T variant="heading" style={{ color: item.saved ? Colors.onAccent : Colors.accent }}>
          {item.saved ? '♥' : '♡'}
        </T>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  tabs: { flexDirection: 'row', gap: Spacing.two },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
  idea: {
    flexDirection: 'row',
    gap: Spacing.three,
    paddingTop: Spacing.three,
    borderTopWidth: 1,
    borderTopColor: Colors.border,
  },
  ideaFirst: { paddingTop: 0, borderTopWidth: 0 },
  ideaText: { flex: 1, gap: Spacing.one },
  ideaTitle: { fontSize: 17, lineHeight: 23 },
  heart: {
    width: 40,
    height: 40,
    borderRadius: Radius.md,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
    borderColor: Colors.border,
  },
  heartOn: { backgroundColor: Colors.accent, borderColor: Colors.accent },
});
