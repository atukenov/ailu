import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { AppState, Platform } from 'react-native';

import { api } from '@/api/client';
import type { WidgetSummary } from '@/api/types';
import { strings, type Strings } from '@/i18n';
import { useSession } from '@/session';

import { CoupleWidget } from './couple-widget';
import { emptyWidget, type CoupleWidgetProps } from './props';

// Anything that changes what the couple sees on Today also changes the widget.
const WATCHED = new Set(['today', 'couple', 'storage']);

export function toWidgetProps(w: WidgetSummary, s: Strings): CoupleWidgetProps {
  const phase = s.phases[w.phaseIndex];
  const more = w.task.state === 'done' && w.task.done < w.task.perDay;
  const partner = w.partnerName ?? '';
  return {
    ready: true,
    days: w.daysTogether,
    daysLabel: s.widget.daysTogether(w.daysTogether),
    streak: w.streak,
    streakLabel: s.widget.streak(w.streak),
    nur: s.number(w.nurBalance),
    nurLabel: s.widget.nur,
    phase: phase?.name ?? '',
    moon: phase?.moon ?? '',
    phaseFill: Math.min(Math.max(w.phaseFill, 0), 1),
    title: w.task.title,
    status: more ? s.widget.moreTasks : (s.widget.task[w.task.state] ?? ''),
    done: w.task.state === 'done',
    note:
      w.question.revealed ? s.widget.answersOpen :
      w.question.partnerAnswered && !w.question.answered && partner ? s.widget.partnerAnswered(partner) :
      '',
    signedOut: s.widget.signedOut,
  };
}

// What the widget turns into at the couple's midnight if the app is not opened by then.
export function nextDayProps(w: WidgetSummary, today: CoupleWidgetProps, s: Strings): CoupleWidgetProps {
  const days = w.daysTogether + 1;
  return {
    ...today,
    days,
    daysLabel: s.widget.daysTogether(days),
    streak: w.streakTomorrow,
    streakLabel: s.widget.streak(w.streakTomorrow),
    title: '',
    status: s.widget.newDay,
    done: false,
    note: '',
  };
}

function push(w: WidgetSummary, s: Strings) {
  const today = toWidgetProps(w, s);
  try {
    if (Platform.OS === 'ios') {
      // iOS turns the widget over at midnight by itself; Android keeps the snapshot until the next sync.
      CoupleWidget.updateTimeline([
        { date: new Date(), props: today },
        { date: new Date(w.nextDayAt), props: nextDayProps(w, today, s) },
      ]);
    } else {
      CoupleWidget.updateSnapshot(today);
    }
  } catch {
    // A build without the widget extension (Expo Go, web) has nothing to update.
  }
}

// The widget must not keep showing a couple the person is no longer signed in to.
function clearWidget(locale?: string) {
  try {
    CoupleWidget.updateSnapshot({ ...emptyWidget, signedOut: strings(locale).widget.signedOut });
  } catch {
    // See push().
  }
}

// Keeps the home-screen widget in step with the app while a couple is signed in: on start, on every
// return to the foreground, and whenever Today or the couple changes. It runs only while the person
// is in a couple, so leaving (sign-out, an expired session, ending the couple, deleting the account)
// unmounts it and the widget goes back to "open Ailu".
export function useWidgetSync() {
  const locale = useSession().user?.locale;
  const queryClient = useQueryClient();
  const widget = useQuery({ queryKey: ['widget'], queryFn: api.widget, enabled: Platform.OS !== 'web' });

  useEffect(() => {
    if (widget.data) push(widget.data, strings(locale));
  }, [widget.data, locale]);

  useEffect(() => () => clearWidget(locale), [locale]);

  useEffect(() => {
    if (Platform.OS === 'web') return;
    const refetch = () => void queryClient.invalidateQueries({ queryKey: ['widget'] });
    const app = AppState.addEventListener('change', (state) => state === 'active' && refetch());
    const cache = queryClient.getQueryCache().subscribe((e) => {
      if (e.type === 'updated' && e.action.type === 'success' && WATCHED.has(String(e.query.queryKey[0]))) refetch();
    });
    return () => {
      app.remove();
      cache();
    };
  }, [queryClient]);
}
