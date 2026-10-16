// Colors follow the mockups: a warm, dark photo-like ground with frosted cards and a soft pink accent.
// Every screen takes its colors and spacing from here, so the palette can change in one place.
export const Colors = {
  background: '#1C1714',
  surface: 'rgba(255, 255, 255, 0.08)',
  surfaceStrong: 'rgba(255, 255, 255, 0.14)',
  border: 'rgba(255, 255, 255, 0.22)',
  text: '#F7F0E8',
  textSecondary: '#C9BEB2',
  textMuted: '#8F857B',
  accent: '#F2A7C7',
  accentPressed: '#D987AB',
  onAccent: '#2A1620',
  moonLit: '#F5E6C8',
  moonDark: 'rgba(255, 255, 255, 0.10)',
  success: '#B7D29A',
  danger: '#F0A08A',
} as const;

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
