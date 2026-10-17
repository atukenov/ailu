import { useMutation, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, Switch, TextInput, View } from 'react-native';

import { api } from '@/api/client';
import type { GoalCategory } from '@/api/types';
import { MAX_STEPS, MonthPicker } from '@/components/goals';
import { Button, Chip, ErrorText, Field, Screen, T, TopBar } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';
import { useStrings } from '@/i18n';

const CATEGORIES: GoalCategory[] = ['travel', 'home', 'health', 'money', 'together'];

export default function NewGoal() {
  const s = useStrings();
  const queryClient = useQueryClient();
  const [title, setTitle] = useState('');
  const [category, setCategory] = useState<GoalCategory>('travel');
  const [month, setMonth] = useState<string | null>(null);
  const [remind, setRemind] = useState(true);
  const [steps, setSteps] = useState<string[]>(['']);

  const save = useMutation({
    mutationFn: () =>
      api.createGoal({
        title: title.trim(),
        category,
        targetMonth: month ?? '',
        weeklyReminder: remind,
        steps: steps.map((x) => x.trim()).filter(Boolean),
      }),
    onSuccess: (goal) => {
      void queryClient.invalidateQueries({ queryKey: ['goals'] });
      router.replace(`/goals/${goal.id}`);
    },
  });

  return (
    <Screen>
      <TopBar title={s.newGoal} onBack={() => router.back()} />
      <Field label={s.goalTitle} placeholder={s.goalTitlePlaceholder} value={title} onChangeText={setTitle} maxLength={80} />

      <View style={styles.group}>
        <T variant="label">{s.goalCategory}</T>
        <View style={styles.chips}>
          {CATEGORIES.map((c) => (
            <Chip key={c} label={s.goalCategories[c]} selected={c === category} onPress={() => setCategory(c)} />
          ))}
        </View>
      </View>

      <MonthPicker value={month} onChange={setMonth} />

      <View style={styles.switchRow}>
        <T variant="body" style={styles.flex}>
          {s.goalRemind}
        </T>
        <Switch
          value={remind}
          onValueChange={setRemind}
          trackColor={{ true: Colors.accent, false: Colors.surfaceStrong }}
          thumbColor={Colors.text}
          accessibilityLabel={s.goalRemind}
        />
      </View>

      <View style={styles.group}>
        <T variant="label">{s.goalSteps}</T>
        {steps.map((step, i) => (
          <TextInput
            key={i}
            value={step}
            onChangeText={(text) => setSteps((all) => all.map((x, j) => (j === i ? text : x)))}
            placeholder={s.stepPlaceholder(i + 1)}
            placeholderTextColor={Colors.textMuted}
            maxLength={120}
            style={styles.stepInput}
          />
        ))}
        {steps.length < MAX_STEPS && (
          <Button title={`+ ${s.addStep}`} variant="ghost" onPress={() => setSteps((all) => [...all, ''])} />
        )}
      </View>

      <ErrorText error={save.error} />
      <Button title={s.createGoal} onPress={() => save.mutate()} loading={save.isPending} disabled={!title.trim()} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  group: { gap: Spacing.two },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.two },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: Spacing.three },
  stepInput: {
    minHeight: 48,
    borderRadius: Radius.md,
    borderColor: Colors.border,
    borderWidth: 1,
    backgroundColor: Colors.surfaceStrong,
    color: Colors.text,
    fontSize: 16,
    paddingHorizontal: Spacing.three,
  },
});
