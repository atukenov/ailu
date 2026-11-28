import { type ReactNode, useEffect, useRef, useState } from 'react';
import { type LayoutChangeEvent, PanResponder, Platform, StyleSheet, View } from 'react-native';
import Animated, { LinearTransition, useAnimatedStyle, useSharedValue, withSpring } from 'react-native-reanimated';

import { T } from '@/components/ui';
import { Colors, Radius, Spacing } from '@/constants/theme';

import { tapFeedback } from './motion';

const GAP = Spacing.two;
const SETTLE = { damping: 22, stiffness: 260 };

type Labels = { drag: string; up: string; down: string };

// A list whose rows can be dragged up and down by a handle. The row being dragged follows the finger and
// trades places with a neighbour once it passes that neighbour's middle; the others slide out of its way.
// Screen readers get "move up" and "move down" actions on the handle instead. onReorder gets every id
// in the new order, and only when the order actually changed.
export function ReorderList<Item extends { id: string }>({
  items,
  renderItem,
  onReorder,
  labels,
}: {
  items: Item[];
  renderItem: (item: Item, handle: ReactNode) => ReactNode;
  onReorder: (ids: string[]) => void;
  labels: Labels;
}) {
  // The order shown while a finger is down; the items' own order otherwise.
  const [dragOrder, setDragOrder] = useState<string[] | null>(null);
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const dragY = useSharedValue(0);
  const heights = useRef(new Map<string, number>());
  const drag = useRef<{ id: string; start: string[]; order: string[]; shift: number } | null>(null);

  const ids = items.map((x) => x.id);
  const byId = new Map(items.map((x) => [x.id, x]));
  const shown = (dragOrder ?? ids).filter((id) => byId.has(id));
  const canDrag = items.length > 1;

  // Handlers are made once per row, so they reach the latest props through this ref.
  const latest = useRef({ ids, onReorder });
  useEffect(() => {
    latest.current = { ids, onReorder };
  });

  const begin = (id: string) => {
    const order = [...latest.current.ids];
    drag.current = { id, start: order, order, shift: 0 };
    dragY.value = 0;
    setDragOrder(order);
    setDraggingId(id);
    tapFeedback();
  };

  const move = (dy: number) => {
    const d = drag.current;
    if (!d) return;
    let order = d.order;
    let i = order.indexOf(d.id);
    let swapped = false;
    for (;;) {
      const offset = dy - d.shift;
      const next = order[i + 1];
      const prev = order[i - 1];
      const nextH = next ? (heights.current.get(next) ?? 0) + GAP : 0;
      const prevH = prev ? (heights.current.get(prev) ?? 0) + GAP : 0;
      if (next && offset > nextH / 2) {
        order = swap(order, i, i + 1);
        d.shift += nextH;
        i += 1;
      } else if (prev && offset < -prevH / 2) {
        order = swap(order, i, i - 1);
        d.shift -= prevH;
        i -= 1;
      } else break;
      swapped = true;
    }
    dragY.value = dy - d.shift;
    if (swapped) {
      d.order = order;
      setDragOrder(order);
      tapFeedback();
    }
  };

  const end = () => {
    const d = drag.current;
    drag.current = null;
    dragY.value = withSpring(0, SETTLE);
    setDraggingId(null);
    setDragOrder(null);
    if (d && d.order.join() !== d.start.join()) latest.current.onReorder(d.order);
  };

  const step = (id: string, by: -1 | 1) => {
    const order = [...latest.current.ids];
    const i = order.indexOf(id);
    if (i + by < 0 || i + by >= order.length) return;
    latest.current.onReorder(swap(order, i, i + by));
  };

  return (
    <View style={styles.list}>
      {shown.map((id, index) => (
        <Row
          key={id}
          id={id}
          dragging={draggingId === id}
          canDrag={canDrag}
          first={index === 0}
          last={index === shown.length - 1}
          labels={labels}
          dragY={dragY}
          onLayout={(e) => heights.current.set(id, e.nativeEvent.layout.height)}
          onBegin={begin}
          onMove={move}
          onEnd={end}
          onStep={step}
          render={(handle) => renderItem(byId.get(id)!, handle)}
        />
      ))}
    </View>
  );
}

function Row({
  id,
  dragging,
  canDrag,
  first,
  last,
  labels,
  dragY,
  onLayout,
  onBegin,
  onMove,
  onEnd,
  onStep,
  render,
}: {
  id: string;
  dragging: boolean;
  canDrag: boolean;
  first: boolean;
  last: boolean;
  labels: Labels;
  dragY: { value: number };
  onLayout: (e: LayoutChangeEvent) => void;
  onBegin: (id: string) => void;
  onMove: (dy: number) => void;
  onEnd: () => void;
  onStep: (id: string, by: -1 | 1) => void;
  render: (handle: ReactNode) => ReactNode;
}) {
  const handlers = useRef({ onBegin, onMove, onEnd });
  useEffect(() => {
    handlers.current = { onBegin, onMove, onEnd };
  });

  // One responder for the row's whole life: a new one mid-drag would lose the gesture. Its callbacks
  // only read handlers.current when touches arrive, never during render.
  // eslint-disable-next-line react-hooks/refs
  const [responder] = useState(() =>
    PanResponder.create({
      onStartShouldSetPanResponder: () => true,
      onStartShouldSetPanResponderCapture: () => true,
      onMoveShouldSetPanResponderCapture: () => true,
      // Keep the screen from scrolling away with the row.
      onPanResponderTerminationRequest: () => false,
      onShouldBlockNativeResponder: () => true,
      onPanResponderGrant: () => handlers.current.onBegin(id),
      onPanResponderMove: (_, g) => handlers.current.onMove(g.dy),
      onPanResponderRelease: () => handlers.current.onEnd(),
      onPanResponderTerminate: () => handlers.current.onEnd(),
    }),
  );

  const lifted = useAnimatedStyle(() => ({
    transform: [{ translateY: dragging ? dragY.value : 0 }],
  }));

  const handle = canDrag ? (
    <View
      {...responder.panHandlers}
      style={styles.handle}
      hitSlop={8}
      accessible
      accessibilityRole="adjustable"
      accessibilityLabel={labels.drag}
      accessibilityActions={[
        ...(first ? [] : [{ name: 'moveUp', label: labels.up }]),
        ...(last ? [] : [{ name: 'moveDown', label: labels.down }]),
      ]}
      onAccessibilityAction={(e) => {
        if (e.nativeEvent.actionName === 'moveUp') onStep(id, -1);
        if (e.nativeEvent.actionName === 'moveDown') onStep(id, 1);
      }}>
      <T variant="small" style={styles.grip} selectable={false}>
        ⋮⋮
      </T>
    </View>
  ) : (
    // A lone step keeps the same indent, so rows don't shift when a second one arrives.
    <View style={styles.handle} />
  );

  return (
    <Animated.View
      onLayout={onLayout}
      // The lifted row follows the finger itself; only the others glide.
      layout={dragging ? undefined : LinearTransition.duration(160)}
      style={[lifted, dragging && styles.lifted]}>
      {render(handle)}
    </Animated.View>
  );
}

function swap(order: string[], a: number, b: number) {
  const next = [...order];
  [next[a], next[b]] = [next[b], next[a]];
  return next;
}

const styles = StyleSheet.create({
  list: { gap: GAP },
  handle: {
    width: 28,
    minHeight: 32,
    alignItems: 'center',
    justifyContent: 'center',
    ...(Platform.OS === 'web' ? ({ cursor: 'grab', userSelect: 'none', touchAction: 'none' } as object) : {}),
  },
  grip: { color: Colors.textMuted, letterSpacing: -3, fontSize: 16 },
  lifted: {
    zIndex: 10,
    // Room around the content without moving it.
    marginHorizontal: -Spacing.two,
    paddingHorizontal: Spacing.two,
    borderRadius: Radius.md,
    backgroundColor: Colors.surfaceStrong,
    shadowColor: '#000',
    shadowOpacity: 0.3,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 6 },
    elevation: 8,
  },
});
