import { Column, LinearProgressIndicator, Row, Spacer, Text } from '@expo/ui/jetpack-compose';
import { background, cornerRadius, fillMaxSize, fillMaxWidth, height, paddingAll, width } from '@expo/ui/jetpack-compose/modifiers';
import { createWidget } from 'expo-widgets';

import { emptyWidget, type CoupleWidgetProps } from './props';

// The layout is serialized and run by the widget host (Glance), so it can only use what is in its
// own body plus the Expo UI components and modifiers. Glance has no gradients or spacedBy, so the
// ground is one warm plum and gaps are Spacers.
const CoupleWidgetLayout = (p: CoupleWidgetProps) => {
  'widget';
  const text = '#F7F0E8';
  const muted = '#C9BEB2';
  const accent = '#F2A7C7';
  const moon = '#F5E6C8';
  const success = '#B7D29A';
  const ground = [fillMaxSize(), background('#2E1B25'), cornerRadius(24), paddingAll(14)];

  if (!p.ready) {
    return (
      <Column modifiers={ground}>
        <Text color={accent} style={{ fontSize: 15, fontWeight: 'bold' }}>Ailu</Text>
        <Spacer modifiers={[height(8)]} />
        <Text color={text} maxLines={4} style={{ fontSize: 13 }}>{p.signedOut}</Text>
      </Column>
    );
  }

  const statusColor = p.done ? success : accent;
  return (
    <Column modifiers={ground}>
      <Row modifiers={[fillMaxWidth()]} verticalAlignment="center">
        <Text color={accent} style={{ fontSize: 13, fontWeight: 'bold' }}>Ailu</Text>
        <Spacer modifiers={[width(8)]} />
        <Text color={moon} maxLines={1} style={{ fontSize: 12 }}>{`${p.moon} · ${p.phase}`}</Text>
      </Row>
      <Spacer modifiers={[height(8)]} />
      <Row verticalAlignment="bottom">
        <Text color={text} style={{ fontSize: 30, fontWeight: 'bold' }}>{`${p.streak} 🔥`}</Text>
        <Spacer modifiers={[width(12)]} />
        <Column>
          <Text color={moon} style={{ fontSize: 16, fontWeight: 'bold' }}>{`${p.days}`}</Text>
          <Text color={muted} maxLines={1} style={{ fontSize: 11 }}>{p.daysLabel}</Text>
        </Column>
        <Spacer modifiers={[width(12)]} />
        <Column>
          <Text color={moon} style={{ fontSize: 16, fontWeight: 'bold' }}>{p.nur}</Text>
          <Text color={muted} maxLines={1} style={{ fontSize: 11 }}>{p.nurLabel}</Text>
        </Column>
      </Row>
      <Text color={muted} maxLines={1} style={{ fontSize: 11 }}>{p.streakLabel}</Text>
      <Spacer modifiers={[height(6)]} />
      <LinearProgressIndicator progress={p.phaseFill} color={accent} trackColor="#4A3340" modifiers={[fillMaxWidth()]} />
      <Spacer modifiers={[height(8)]} />
      <Text color={text} maxLines={2} style={{ fontSize: 14, fontWeight: 'bold' }}>{p.title}</Text>
      <Text color={statusColor} maxLines={2} style={{ fontSize: 12, fontWeight: '600' }}>{`● ${p.status}`}</Text>
      {p.note !== '' && <Text color={muted} maxLines={1} style={{ fontSize: 11 }}>{p.note}</Text>}
    </Column>
  );
};

export const CoupleWidget = createWidget<CoupleWidgetProps>('CoupleWidget', CoupleWidgetLayout, emptyWidget);
