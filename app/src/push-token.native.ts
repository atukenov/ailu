import { getApps } from '@react-native-firebase/app';
import { getMessaging, getToken, onMessage, onTokenRefresh } from '@react-native-firebase/messaging';

// Firebase Cloud Messaging gives one kind of token on both platforms (on iOS it wraps the APNs token),
// so the server sends every push through FCM. Firebase is configured only when the build carried
// google-services.json / GoogleService-Info.plist; without them there is no default app and no push.
export function pushAvailable(): boolean {
  try {
    return getApps().length > 0;
  } catch {
    return false;
  }
}

export async function getPushToken(): Promise<string | null> {
  if (!pushAvailable()) return null;
  return getToken(getMessaging());
}

export function onPushTokenRefresh(listener: (token: string) => void): () => void {
  if (!pushAvailable()) return () => {};
  return onTokenRefresh(getMessaging(), listener);
}

export type ForegroundPush = { title?: string; body?: string };

// Messages that arrive while the app is open. In the background the system shows them by itself.
export function onForegroundPush(listener: (push: ForegroundPush) => void): () => void {
  if (!pushAvailable()) return () => {};
  return onMessage(getMessaging(), (m) =>
    listener({ title: m.notification?.title, body: m.notification?.body }),
  );
}
