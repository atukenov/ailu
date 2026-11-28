import { useCallback, useState } from 'react';

// Pull-to-refresh state. A query's isRefetching is also true for background refetches (polling,
// focus, invalidation after a purchase), and handing that to RefreshControl makes iOS show the
// spinner and push the whole screen down and back up every time. Only a pull shows the spinner.
export function usePullRefresh() {
  const [refreshing, setRefreshing] = useState(false);
  const refresh = useCallback((work: Promise<unknown>) => {
    setRefreshing(true);
    work.catch(() => {}).finally(() => setRefreshing(false));
  }, []);
  return [refreshing, refresh] as const;
}
