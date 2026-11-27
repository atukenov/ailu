import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { AndroidImportance } from 'expo-notifications';
import { Platform } from 'react-native';

import { api } from '@/api/client';
import { getPushToken, onForegroundPush, onPushTokenRefresh, pushAvailable } from '@/push-token';

// Pushes come from the server through Firebase Cloud Messaging (FCM, which reaches iPhones via APNs).
// expo-notifications still asks for permission, owns the Android channel and shows pushes that
// arrive while the app is open. The server sends at most a few a day, so showing them then is fine.
if (Platform.OS !== 'web') {
  Notifications.setNotificationHandler({
    handleNotification: async () => ({
      shouldShowBanner: true,
      shouldShowList: true,
      shouldPlaySound: true,
      shouldSetBadge: false,
    }),
  });
}

// The channel id the server names in every Android push.
const ANDROID_CHANNEL = 'default';

let registered: string | null = null;
let stopListening: (() => void) | null = null;

async function send(token: string) {
  await api.registerDevice(token, Platform.OS as 'ios' | 'android');
  registered = token;
}

// Asks for permission once the couple exists (there is nothing to notify about before that) and
// tells the server where to send pushes. Never throws: the app works without notifications.
export async function registerForPush(): Promise<void> {
  if (Platform.OS === 'web' || !Device.isDevice) return;
  try {
    if (Platform.OS === 'android') {
      await Notifications.setNotificationChannelAsync(ANDROID_CHANNEL, {
        name: 'Ailu',
        importance: AndroidImportance.HIGH,
      });
    }

    let { status } = await Notifications.getPermissionsAsync();
    if (status !== 'granted') ({ status } = await Notifications.requestPermissionsAsync());
    if (status !== 'granted') return;

    if (!pushAvailable()) {
      console.warn('Push disabled: this build has no Firebase config (google-services.json / GoogleService-Info.plist).');
      return;
    }

    const token = await getPushToken();
    if (!token) return;
    await send(token);

    if (!stopListening) {
      const stopRefresh = onPushTokenRefresh((next) => {
        if (registered === null) return; // signed out meanwhile
        send(next).catch((e) => console.warn('Push token refresh failed', e));
      });
      // On iOS Firebase presents foreground pushes itself (firebase.json); on Android FCM hands
      // them to the app, so show them as a local notification.
      const stopForeground =
        Platform.OS === 'android'
          ? onForegroundPush(({ title, body }) => {
              if (!title && !body) return;
              Notifications.scheduleNotificationAsync({
                content: { title: title ?? null, body: body ?? null, sound: 'default' },
                trigger: { channelId: ANDROID_CHANNEL },
              }).catch(() => {});
            })
          : () => {};
      stopListening = () => {
        stopRefresh();
        stopForeground();
      };
    }
  } catch (e) {
    console.warn('Push registration failed', e);
  }
}

// On sign-out, so the next person to use this phone doesn't get our pushes.
export async function unregisterForPush(): Promise<void> {
  stopListening?.();
  stopListening = null;
  if (!registered) return;
  const token = registered;
  registered = null;
  try {
    await api.unregisterDevice(token, Platform.OS as 'ios' | 'android');
  } catch {
    // Signing out must not fail because of this; the next sign-in on this device takes the token over anyway.
  }
}
