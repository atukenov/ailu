import type { CoupleWidgetProps } from './props';

// Web has no home screen: the widget is a no-op there.
export const CoupleWidget = {
  updateSnapshot(_props: CoupleWidgetProps) {},
  updateTimeline(_entries: { date: Date; props: CoupleWidgetProps }[]) {},
  reload() {},
};
