import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { api } from '@/api/client';
import type { Mood, MoodLevel } from '@/api/types';
import { Button, Card, ErrorText, Field, T } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

export const MOODS: MoodLevel[] = ['hard', 'tired', 'even', 'good', 'great'];

// The mockup's faces: a ring, two eyes and a mouth that goes from a frown to a wide smile.
export function Face({ mood, size = 28, color = Colors.text }: { mood: MoodLevel; size?: number; color?: string }) {
  const line = Math.max(1.5, size / 16);
  const eye = Math.max(2, size / 9);
  const mouth = { hard: 0.42, tired: 0.3, even: 0.38, good: 0.42, great: 0.5 }[mood] * size;
  const curve = mood === 'hard' ? 'frown' : mood === 'good' || mood === 'great' ? 'smile' : 'flat';
  const depth = mood === 'great' ? mouth / 2 : mouth / 3;
  return (
    <View
      style={[styles.face, { width: size, height: size, borderRadius: size / 2, borderWidth: line, borderColor: color }]}>
      <View style={[styles.eyes, { top: size * 0.3, gap: size * 0.22 }]}>
        <View style={{ width: eye, height: eye, borderRadius: eye / 2, backgroundColor: color }} />
        <View style={{ width: eye, height: eye, borderRadius: eye / 2, backgroundColor: color }} />
      </View>
      <View
        style={[
          { position: 'absolute', top: size * (curve === 'frown' ? 0.58 : 0.56), width: mouth, borderColor: color },
          curve === 'flat' && { height: 0, borderTopWidth: line },
          curve === 'smile' && {
            height: depth,
            borderWidth: line,
            borderTopWidth: 0,
            borderBottomLeftRadius: mouth / 2,
            borderBottomRightRadius: mouth / 2,
          },
          curve === 'frown' && {
            height: depth,
            borderWidth: line,
            borderBottomWidth: 0,
            borderTopLeftRadius: mouth / 2,
            borderTopRightRadius: mouth / 2,
          },
        ]}
      />
    </View>
  );
}

function time(iso: string) {
  const d = new Date(iso);
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

function weekday(iso: string, names: string[]) {
  const [y, m, d] = iso.split('-').map(Number);
  return names[new Date(Date.UTC(y, m - 1, d)).getUTCDay()];
}

// Mood is polled with the rest of Today so a partner's mark shows up without a manual refresh.
export function MoodCard({ partnerName, pollMs }: { partnerName: string; pollMs: number }) {
  const s = useStrings();
  const queryClient = useQueryClient();
  const mood = useQuery({ queryKey: ['mood'], queryFn: api.mood, refetchInterval: pollMs });
  const today = mood.data?.days.at(-1);
  const mine = today?.mine ?? null;
  // What the person is typing; until they type, the field shows the saved note.
  const [draft, setDraft] = useState<string | null>(null);
  const note = draft ?? mine?.note ?? '';

  const apply = (next: Mood) => {
    queryClient.setQueryData(['mood'], next);
    // Нұр and the streak are decided by the server; fetch them rather than guess.
    queryClient.invalidateQueries({ queryKey: ['today'] });
  };
  const consent = useMutation({
    mutationFn: api.giveMoodConsent,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['mood'] }),
  });
  const set = useMutation({ mutationFn: (level: MoodLevel) => api.setMood(level), onSuccess: apply });
  const saveNote = useMutation({
    mutationFn: () => api.setMood(mine!.mood, note.trim()),
    onSuccess: (next) => {
      apply(next);
      setDraft(null);
    },
  });
  const share = useMutation({ mutationFn: (on: boolean) => api.shareMoodNote(on), onSuccess: apply });

  if (!mood.data || !today) return null;
  const partner = today.partner;
  const noteChanged = note.trim() !== (mine?.note ?? '');

  return (
    <Card>
      <T variant="label">{s.mood}</T>
      <T variant="heading">{s.moodQuestion}</T>
      <T variant="small">
        {partner
          ? s.moodPartner(partnerName, s.moodLevels[partner.mood], time(partner.markedAt))
          : s.moodPartnerNone(partnerName)}
      </T>
      {partner?.note && (
        <View style={styles.note}>
          <T variant="body">{partner.note}</T>
        </View>
      )}

      {!mood.data.consented ? (
        <>
          <T variant="small">{s.moodConsentText}</T>
          <ErrorText error={consent.error} />
          <Button title={s.moodConsent} onPress={() => consent.mutate()} loading={consent.isPending} />
        </>
      ) : (
        <>
          <View style={styles.levels} accessibilityRole="radiogroup">
            {MOODS.map((level) => {
              const selected = mine?.mood === level;
              return (
                <Pressable
                  key={level}
                  accessibilityRole="radio"
                  accessibilityLabel={s.moodLevels[level]}
                  accessibilityState={{ checked: selected }}
                  disabled={set.isPending}
                  onPress={() => set.mutate(level)}
                  style={[styles.level, selected && styles.levelSelected]}>
                  <Face mood={level} color={selected ? Colors.accent : Colors.text} />
                  <T variant="small" numberOfLines={1} style={[styles.levelLabel, selected && { color: Colors.accent }]}>
                    {s.moodLevels[level]}
                  </T>
                </Pressable>
              );
            })}
          </View>
          <ErrorText error={set.error} />

          {mine && (
            <>
              {partner && <T variant="small" style={{ color: Colors.success }}>{s.moodBoth}</T>}
              <Field
                label={s.moodNote}
                placeholder={s.moodNotePlaceholder}
                value={note}
                onChangeText={setDraft}
                maxLength={280}
              />
              {noteChanged ? (
                <Button title={s.save} onPress={() => saveNote.mutate()} loading={saveNote.isPending} />
              ) : (
                mine.note && (
                  <>
                    <T variant="small">{mine.noteShared ? s.moodNoteShared : s.moodNotePrivate}</T>
                    <Button
                      title={mine.noteShared ? s.moodHideNote : s.moodShowNote}
                      variant="ghost"
                      onPress={() => share.mutate(!mine.noteShared)}
                      loading={share.isPending}
                    />
                  </>
                )
              )}
              <ErrorText error={saveNote.error ?? share.error} />
            </>
          )}
        </>
      )}

      <View style={styles.week} accessibilityLabel={s.moodWeek}>
        {mood.data.days.map((day) => (
          <View key={day.date} style={styles.day}>
            <T variant="small">{weekday(day.date, s.weekdays)}</T>
            <Mark level={day.mine?.mood} />
            <Mark level={day.partner?.mood} />
          </View>
        ))}
      </View>
    </Card>
  );
}

function Mark({ level }: { level: MoodLevel | undefined }) {
  return level ? (
    <Face mood={level} size={18} color={Colors.textSecondary} />
  ) : (
    <View style={styles.empty} />
  );
}

const styles = StyleSheet.create({
  face: { alignItems: 'center' },
  eyes: { position: 'absolute', flexDirection: 'row' },
  levels: { flexDirection: 'row', gap: Spacing.one },
  level: {
    flex: 1,
    alignItems: 'center',
    gap: Spacing.two,
    paddingVertical: Spacing.three,
    borderRadius: Radius.md,
    borderWidth: 1,
    borderColor: Colors.border,
    backgroundColor: Colors.surface,
  },
  levelLabel: { fontSize: 11, lineHeight: 14 },
  levelSelected: { borderColor: Colors.accent, backgroundColor: Colors.surfaceStrong },
  note: { padding: Spacing.three, borderRadius: Radius.md, backgroundColor: Colors.surfaceStrong },
  week: { flexDirection: 'row', justifyContent: 'space-between' },
  day: { alignItems: 'center', gap: Spacing.one },
  empty: { width: 18, height: 18, borderRadius: 9, borderWidth: 1, borderColor: Colors.border, borderStyle: 'dashed' },
});
