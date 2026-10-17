import Constants from 'expo-constants';
import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { AndroidImportance } from 'expo-notifications';
import { Platform } from 'react-native';

import { api } from '@/api/client';

// The server sends at most two pushes a day (the 09:00 task and a 20:00 nudge), so showing them
// while the app is open is fine. The web build (development only) has no push support.
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

let registered: string | null = null;

// Asks for permission once the couple exists (there is nothing to notify about before that) and
// tells the server where to send pushes. Never throws: the app works without notifications.
export async function registerForPush(): Promise<void> {
  if (Platform.OS === 'web' || !Device.isDevice) return;
  try {
    if (Platform.OS === 'android') {
      await Notifications.setNotificationChannelAsync('default', {
        name: 'Ailu',
        importance: AndroidImportance.DEFAULT,
      });
    }

    let { status } = await Notifications.getPermissionsAsync();
    if (status !== 'granted') ({ status } = await Notifications.requestPermissionsAsync());
    if (status !== 'granted') return;

    // Expo push tokens are tied to the EAS project; it is set by `eas init` in app.json.
    const projectId = Constants.expoConfig?.extra?.eas?.projectId ?? Constants.easConfig?.projectId;
    if (!projectId) {
      console.warn('Push disabled: no EAS projectId. Run `npx eas-cli@latest init` in app/.');
      return;
    }

    const { data: token } = await Notifications.getExpoPushTokenAsync({ projectId });
    await api.registerDevice(token, Platform.OS as 'ios' | 'android');
    registered = token;
  } catch (e) {
    console.warn('Push registration failed', e);
  }
}

// On sign-out, so the next person to use this phone doesn't get our pushes.
export async function unregisterForPush(): Promise<void> {
  if (!registered) return;
  const token = registered;
  registered = null;
  try {
    await api.unregisterDevice(token, Platform.OS as 'ios' | 'android');
  } catch {
    // Signing out must not fail because of this; the next sign-in on this device takes the token over anyway.
  }
}
