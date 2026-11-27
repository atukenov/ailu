// What the home-screen widget draws. The widget runs in its own small JS runtime outside the app,
// without i18n or the API client, so the app hands it every text ready-made (see sync.ts).
export type CoupleWidgetProps = {
  // False until the app has synced once, and again after sign-out: the widget then asks to open Ailu.
  ready: boolean;
  days: number;
  daysLabel: string;
  streak: number;
  streakLabel: string;
  nur: string;
  nurLabel: string;
  phase: string;
  moon: string;
  // 0..1, how far the couple is into the current moon phase.
  phaseFill: number;
  title: string;
  status: string;
  done: boolean;
  // One extra line about the question of the day, or ''.
  note: string;
  signedOut: string;
};

export const emptyWidget: CoupleWidgetProps = {
  ready: false,
  days: 0,
  daysLabel: '',
  streak: 0,
  streakLabel: '',
  nur: '0',
  nurLabel: 'нұр',
  phase: '',
  moon: '',
  phaseFill: 0,
  title: '',
  status: '',
  done: false,
  note: '',
  signedOut: 'Откройте Ailu, чтобы увидеть вашу пару здесь',
};
