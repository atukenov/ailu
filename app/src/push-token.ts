// The web build (development only) has no push. The native variant is push-token.native.ts.
export function pushAvailable(): boolean {
  return false;
}

export async function getPushToken(): Promise<string | null> {
  return null;
}

export function onPushTokenRefresh(_listener: (token: string) => void): () => void {
  return () => {};
}

export type ForegroundPush = { title?: string; body?: string };

export function onForegroundPush(_listener: (push: ForegroundPush) => void): () => void {
  return () => {};
}
