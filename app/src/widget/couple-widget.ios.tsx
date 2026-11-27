import { HStack, ProgressView, Spacer, Text, VStack } from '@expo/ui/swift-ui';
import {
  containerBackground,
  font,
  foregroundStyle,
  lineLimit,
  minimumScaleFactor,
  padding,
  progressViewStyle,
  tint,
  widgetURL,
} from '@expo/ui/swift-ui/modifiers';
import { createWidget, type WidgetEnvironment } from 'expo-widgets';

import { emptyWidget, type CoupleWidgetProps } from './props';

// The layout is serialized and run inside the widget extension, so it can only use what is in its
// own body plus the Expo UI components and modifiers. Colors repeat the app theme on purpose.
const CoupleWidgetLayout = (p: CoupleWidgetProps, env: WidgetEnvironment) => {
  'widget';
  const text = '#F7F0E8';
  const muted = '#C9BEB2';
  const accent = '#F2A7C7';
  const moon = '#F5E6C8';
  const success = '#B7D29A';
  const ground = {
    type: 'linearGradient' as const,
    colors: ['#4A2236', '#2A1A22', '#1C1714'],
    startPoint: { x: 0, y: 0 },
    endPoint: { x: 1, y: 1 },
  };
  const family = env.widgetFamily ?? 'systemSmall';
  const base = [containerBackground(ground, 'widget'), widgetURL('ailu://')];

  if (!p.ready) {
    if (family === 'accessoryInline') return <Text>Ailu</Text>;
    return (
      <VStack alignment="leading" spacing={6} modifiers={base}>
        <Text modifiers={[font({ size: 15, weight: 'bold', design: 'rounded' }), foregroundStyle(accent)]}>Ailu</Text>
        <Spacer />
        <Text modifiers={[font({ size: 13, weight: 'medium' }), foregroundStyle(text), lineLimit(4)]}>{p.signedOut}</Text>
      </VStack>
    );
  }

  const statusColor = p.done ? success : accent;

  // Lock Screen: no colors of our own, the system tints them.
  if (family === 'accessoryInline') return <Text>{`🔥 ${p.streak} · ${p.status}`}</Text>;
  if (family === 'accessoryCircular') {
    return (
      <VStack spacing={0} modifiers={base}>
        <Text modifiers={[font({ size: 20, weight: 'bold', design: 'rounded' }), minimumScaleFactor(0.6)]}>{`${p.streak}`}</Text>
        <Text modifiers={[font({ size: 10 })]}>🔥</Text>
      </VStack>
    );
  }
  if (family === 'accessoryRectangular') {
    return (
      <VStack alignment="leading" spacing={1} modifiers={base}>
        <Text modifiers={[font({ size: 15, weight: 'bold', design: 'rounded' })]}>{`🔥 ${p.streak} · ${p.days} ${p.daysLabel}`}</Text>
        <Text modifiers={[font({ size: 13 }), lineLimit(2)]}>{p.status}</Text>
      </VStack>
    );
  }

  const streakBlock = (
    <VStack alignment="leading" spacing={0}>
      <HStack spacing={4} alignment="firstTextBaseline">
        <Text modifiers={[font({ size: 34, weight: 'heavy', design: 'rounded' }), foregroundStyle(text), minimumScaleFactor(0.6)]}>
          {`${p.streak}`}
        </Text>
        <Text modifiers={[font({ size: 20 })]}>🔥</Text>
      </HStack>
      <Text modifiers={[font({ size: 12, weight: 'medium' }), foregroundStyle(muted), lineLimit(1)]}>{p.streakLabel}</Text>
    </VStack>
  );

  const status = (
    <HStack spacing={6} alignment="top">
      <Text modifiers={[font({ size: 9 }), foregroundStyle(statusColor), padding({ top: 3 })]}>●</Text>
      <Text modifiers={[font({ size: 12, weight: 'semibold' }), foregroundStyle(statusColor), lineLimit(2)]}>{p.status}</Text>
    </HStack>
  );

  if (family === 'systemSmall') {
    return (
      <VStack alignment="leading" spacing={4} modifiers={base}>
        <HStack>
          <Text modifiers={[font({ size: 13, weight: 'bold', design: 'rounded' }), foregroundStyle(accent)]}>Ailu</Text>
          <Spacer />
          <Text modifiers={[font({ size: 12, weight: 'semibold' }), foregroundStyle(moon)]}>{`${p.nur} ${p.nurLabel}`}</Text>
        </HStack>
        <Spacer />
        {streakBlock}
        <Spacer />
        {status}
      </VStack>
    );
  }

  // systemMedium and larger.
  return (
    <HStack spacing={14} alignment="top" modifiers={base}>
      <VStack alignment="leading" spacing={4}>
        <Text modifiers={[font({ size: 13, weight: 'bold', design: 'rounded' }), foregroundStyle(accent)]}>Ailu</Text>
        <Spacer />
        {streakBlock}
        <Spacer />
        <Text modifiers={[font({ size: 20, weight: 'bold', design: 'rounded' }), foregroundStyle(moon)]}>{`${p.days}`}</Text>
        <Text modifiers={[font({ size: 12, weight: 'medium' }), foregroundStyle(muted), lineLimit(1)]}>{p.daysLabel}</Text>
      </VStack>
      <VStack alignment="leading" spacing={6}>
        <HStack>
          <Text modifiers={[font({ size: 12, weight: 'semibold' }), foregroundStyle(moon), lineLimit(1)]}>{p.phase}</Text>
          <Spacer />
          <Text modifiers={[font({ size: 12, weight: 'semibold' }), foregroundStyle(moon)]}>{`${p.nur} ${p.nurLabel}`}</Text>
        </HStack>
        <ProgressView value={p.phaseFill} modifiers={[progressViewStyle('linear'), tint(accent)]} />
        <Spacer />
        <Text modifiers={[font({ size: 15, weight: 'bold' }), foregroundStyle(text), lineLimit(2)]}>{p.title}</Text>
        {status}
        {p.note !== '' && (
          <Text modifiers={[font({ size: 11 }), foregroundStyle(muted), lineLimit(1)]}>{p.note}</Text>
        )}
      </VStack>
    </HStack>
  );
};

export const CoupleWidget = createWidget<CoupleWidgetProps>('CoupleWidget', CoupleWidgetLayout, emptyWidget);
