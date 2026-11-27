// A warm night sky: a deep plum ground, frosted cards, and a few bright voices on top of it.
// Pink is the couple's colour, gold is the moon and its нұр, and lavender, mint, coral and sky give
// each part of the app its own tint. Every screen takes its colors and spacing from here, so the
// palette can change in one place.
export const Colors = {
  background: '#170F1D',
  surface: 'rgba(255, 255, 255, 0.07)',
  surfaceStrong: 'rgba(255, 255, 255, 0.13)',
  border: 'rgba(255, 255, 255, 0.16)',
  text: '#FBF3EC',
  textSecondary: '#D6C8D2',
  textMuted: '#9A8C98',
  accent: '#FF8FB8',
  accentPressed: '#EE74A2',
  onAccent: '#2B0E1D',
  gold: '#FFD27A',
  lavender: '#B9A2FF',
  mint: '#7EE0B0',
  coral: '#FF9B7B',
  sky: '#7FC8FF',
  moonLit: '#FFE7B0',
  moonDark: 'rgba(255, 255, 255, 0.10)',
  success: '#7EE0B0',
  danger: '#FF8A80',
} as const;

// The screen's ground: plum at the top fading into the night, so cards float on something alive.
export const BackgroundGradient = ['#2C1636', '#1C1124', '#140D18'] as const;

// A tone tints a card, its kicker, its progress and its buttons. Each part of the app keeps one, so
// the same colour means the same thing everywhere: the task is pink, the question lavender, нұр gold.
export type Tone = 'pink' | 'gold' | 'lavender' | 'mint' | 'coral' | 'sky';

type ToneSpec = {
  color: string;
  // Two stops for fills such as buttons, progress and badges.
  gradient: readonly [string, string];
  // A faint wash for the card behind it.
  wash: readonly [string, string];
  border: string;
  onColor: string;
};

export const Tones: Record<Tone, ToneSpec> = {
  pink: {
    color: Colors.accent,
    gradient: ['#FFA3C6', '#FF7FA8'],
    wash: ['rgba(255, 143, 184, 0.20)', 'rgba(255, 143, 184, 0.05)'],
    border: 'rgba(255, 143, 184, 0.35)',
    onColor: '#2B0E1D',
  },
  gold: {
    color: Colors.gold,
    gradient: ['#FFE08F', '#FFB25E'],
    wash: ['rgba(255, 210, 122, 0.18)', 'rgba(255, 178, 94, 0.04)'],
    border: 'rgba(255, 210, 122, 0.35)',
    onColor: '#2E1A04',
  },
  lavender: {
    color: Colors.lavender,
    gradient: ['#C9B6FF', '#9C84FF'],
    wash: ['rgba(185, 162, 255, 0.20)', 'rgba(185, 162, 255, 0.05)'],
    border: 'rgba(185, 162, 255, 0.35)',
    onColor: '#1C1238',
  },
  mint: {
    color: Colors.mint,
    gradient: ['#9DEFC6', '#5DCF9A'],
    wash: ['rgba(126, 224, 176, 0.17)', 'rgba(126, 224, 176, 0.04)'],
    border: 'rgba(126, 224, 176, 0.32)',
    onColor: '#0B2A1C',
  },
  coral: {
    color: Colors.coral,
    gradient: ['#FFB59A', '#FF7F6E'],
    wash: ['rgba(255, 155, 123, 0.18)', 'rgba(255, 155, 123, 0.04)'],
    border: 'rgba(255, 155, 123, 0.35)',
    onColor: '#2E0F07',
  },
  sky: {
    color: Colors.sky,
    gradient: ['#A3D9FF', '#6FA8FF'],
    wash: ['rgba(127, 200, 255, 0.17)', 'rgba(127, 200, 255, 0.04)'],
    border: 'rgba(127, 200, 255, 0.32)',
    onColor: '#0A1F33',
  },
};

// The main action: pink melting into coral.
export const PrimaryGradient = ['#FFA3C6', '#FF8A8A'] as const;

export const Spacing = {
  one: 4,
  two: 8,
  three: 16,
  four: 24,
  five: 32,
  six: 64,
} as const;

export const Radius = {
  sm: 12,
  md: 20,
  lg: 32,
  pill: 999,
} as const;

export const MaxContentWidth = 560;
