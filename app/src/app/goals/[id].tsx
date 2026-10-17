import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Alert, Platform, Pressable, StyleSheet, Switch, TextInput, View } from 'react-native';

import { api } from '@/api/client';
import type { Goal, GoalStep } from '@/api/types';
import { MAX_STEPS, MonthPicker } from '@/components/goals';
import { Button, Card, Centered, Chip, ErrorText, ProgressBar, RoundButton, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

const POLL_MS = 30_000;

export default function GoalScreen() {
  const s = useStrings();
  const { id } = useLocalSearchParams<{ id: string }>();
  const queryClient = useQueryClient();
  const goal = useQuery({ queryKey: ['goal', id], queryFn: () => api.goal(id), refetchInterval: POLL_MS });
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple });
  const [newStep, setNewStep] = useState('');

  const update = (g: Goal) => {
    queryClient.setQueryData(['goal', id], g);
    void queryClient.invalidateQueries({ queryKey: ['goals'] });
  };
  const gone = () => {
    void queryClient.invalidateQueries({ queryKey: ['goals'] });
    router.back();
  };

  const join = useMutation({ mutationFn: () => api.joinGoal(id), onSuccess: update });
  const edit = useMutation({
    mutationFn: (patch: { targetMonth?: string; weeklyReminder?: boolean }) => api.updateGoal(id, patch),
    onSuccess: update,
  });
  const keep = useMutation({ mutationFn: () => api.keepGoal(id), onSuccess: update });
  const remove = useMutation({
    mutationFn: () => api.removeGoal(id),
    onSuccess: (g) => (g ? update(g) : gone()),
  });
  const addStep = useMutation({
    mutationFn: () => api.addStep(id, newStep.trim()),
    onSuccess: (g) => {
      update(g);
      setNewStep('');
    },
  });

  if (goal.isPending) return <Centered />;
  if (goal.isError)
    return (
      <Screen>
        <TopBar title={s.goals} onBack={() => router.back()} />
        <ErrorText error={goal.error} />
      </Screen>
    );

  const g = goal.data;
  const partner = couple.data?.partner?.name ?? s.partnerFallback;
  const shared = g.state === 'active' || g.state === 'done';

  const askRemoval = () => {
    if (Platform.OS === 'web') {
      if (globalThis.confirm?.(s.removeGoalConfirm)) remove.mutate();
      return;
    }
    Alert.alert(s.removeGoal, s.removeGoalConfirm, [
      { text: s.cancel, style: 'cancel' },
      { text: s.removeGoal, style: 'destructive', onPress: () => remove.mutate() },
    ]);
  };

  return (
    <Screen>
      <TopBar title="" onBack={() => router.back()} />

      <Card>
        <T variant="title">{g.title}</T>
        <T variant="small">
          {[s.goalCategories[g.category], g.targetMonth && s.monthYear(g.targetMonth)].filter(Boolean).join(' · ')}
        </T>
        <T variant="heading" style={g.state === 'done' && { color: Colors.success }}>
          {g.state === 'done' ? `${s.goalDone} · 100%` : `${g.progress}%`}
        </T>
        <ProgressBar percent={g.progress} />
        <T variant="small">{g.stepsTotal > 0 ? s.stepsProgress(g.stepsDone, g.stepsTotal) : s.noSteps}</T>

        {g.state === 'proposed' && <T variant="secondary">{s.goalProposed(partner)}</T>}
        {g.state === 'join_needed' && (
          <>
            <T variant="secondary">{s.goalJoinNeeded(partner)}</T>
            <Button title={s.joinGoal} onPress={() => join.mutate()} loading={join.isPending} />
            <Button title={s.declineGoal} variant="ghost" onPress={() => remove.mutate()} loading={remove.isPending} />
          </>
        )}

        {g.removalRequestedByPartner && (
          <>
            <T variant="secondary">{s.removalByPartner(partner)}</T>
            <Button title={s.removeKeep} onPress={() => keep.mutate()} loading={keep.isPending} />
            <Button title={s.removeAgree} variant="ghost" onPress={() => remove.mutate()} loading={remove.isPending} />
          </>
        )}
        {g.removalRequestedByMe && (
          <>
            <T variant="secondary">{s.removalAsked}</T>
            <Button title={s.removeCancel} variant="ghost" onPress={() => keep.mutate()} loading={keep.isPending} />
          </>
        )}
        <ErrorText error={join.error ?? keep.error ?? remove.error} />
      </Card>

      <Card>
        <T variant="label">{s.goalSteps}</T>
        {g.steps.map((step) => (
          <StepRow key={step.id} goalId={g.id} step={step} canMark={shared} onChange={update} />
        ))}
        {g.steps.length < MAX_STEPS && (
          <View style={styles.addRow}>
            <TextInput
              value={newStep}
              onChangeText={setNewStep}
              placeholder={s.addStep}
              placeholderTextColor={Colors.textMuted}
              maxLength={120}
              style={styles.addInput}
              onSubmitEditing={() => newStep.trim() && addStep.mutate()}
              returnKeyType="done"
            />
            <RoundButton
              label={s.addStep}
              glyph="+"
              onPress={() => newStep.trim() && addStep.mutate()}
              active={!!newStep.trim()}
            />
          </View>
        )}
        <ErrorText error={addStep.error} />
      </Card>

      <Card>
        <MonthPicker value={g.targetMonth} onChange={(m) => edit.mutate({ targetMonth: m ?? '' })} />
        <View style={styles.switchRow}>
          <T variant="body" style={styles.flex}>
            {s.goalRemind}
          </T>
          <Switch
            value={g.weeklyReminder}
            onValueChange={(on) => edit.mutate({ weeklyReminder: on })}
            trackColor={{ true: Colors.accent, false: Colors.surfaceStrong }}
            thumbColor={Colors.text}
            accessibilityLabel={s.goalRemind}
          />
        </View>
        <ErrorText error={edit.error} />
      </Card>

      {g.state === 'proposed' && (
        <Button title={s.withdrawGoal} variant="ghost" onPress={() => remove.mutate()} loading={remove.isPending} />
      )}
      {shared && !g.removalRequestedByMe && !g.removalRequestedByPartner && (
        <Button title={s.removeGoal} variant="ghost" onPress={askRemoval} loading={remove.isPending} />
      )}
    </Screen>
  );
}

function StepRow({
  goalId,
  step,
  canMark,
  onChange,
}: {
  goalId: string;
  step: GoalStep;
  canMark: boolean;
  onChange: (g: Goal) => void;
}) {
  const s = useStrings();
  const queryClient = useQueryClient();
  const mark = useMutation({
    mutationFn: (on: boolean) => api.markStep(goalId, step.id, on),
    onSuccess: (g) => {
      onChange(g);
      // A confirmed step earns нұр; let the home screen pick that up.
      void queryClient.invalidateQueries({ queryKey: ['today'] });
    },
  });
  const drop = useMutation({ mutationFn: () => api.deleteStep(goalId, step.id), onSuccess: onChange });
  const done = step.state === 'done';

  return (
    <View style={styles.step}>
      <View style={styles.stepRow}>
        <View style={[styles.check, done && styles.checkDone]}>{done && <T style={styles.checkMark}>✓</T>}</View>
        <T variant="body" style={[styles.flex, done && styles.stepDone]}>
          {step.title}
        </T>
        {!done && (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={s.stepRemove}
            onPress={() => drop.mutate()}
            hitSlop={8}>
            <T variant="small">✕</T>
          </Pressable>
        )}
      </View>
      {canMark && !done && (
        <View style={styles.stepActions}>
          {step.state === 'open' && <Chip label={s.stepMark} onPress={() => mark.mutate(true)} />}
          {step.state === 'waiting_for_partner' && (
            <>
              <T variant="small">{s.stepWaiting}</T>
              <Chip label={s.stepUndo} onPress={() => mark.mutate(false)} />
            </>
          )}
          {step.state === 'confirm_needed' && <Chip label={`${s.stepConfirm} · +100 ${s.nur}`} selected onPress={() => mark.mutate(true)} />}
        </View>
      )}
      <ErrorText error={mark.error ?? drop.error} />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: Spacing.three },
  step: { gap: Spacing.two, paddingVertical: Spacing.one },
  stepRow: { flexDirection: 'row', alignItems: 'center', gap: Spacing.three },
  stepActions: { flexDirection: 'row', alignItems: 'center', gap: Spacing.three, paddingLeft: 36 },
  stepDone: { color: Colors.textSecondary, textDecorationLine: 'line-through' },
  check: {
    width: 24,
    height: 24,
    borderRadius: Radius.pill,
    borderWidth: 1.5,
    borderColor: Colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  checkDone: { backgroundColor: Colors.accent, borderColor: Colors.accent },
  checkMark: { color: Colors.onAccent, fontSize: 14, lineHeight: 18, fontWeight: '700' },
  addRow: { flexDirection: 'row', alignItems: 'center', gap: Spacing.two },
  addInput: {
    flex: 1,
    minWidth: 0,
    minHeight: 44,
    borderRadius: Radius.md,
    borderColor: Colors.border,
    borderWidth: 1,
    backgroundColor: Colors.surfaceStrong,
    color: Colors.text,
    fontSize: 16,
    paddingHorizontal: Spacing.three,
  },
});
