import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';

// Small flags this phone remembers (which upsells were shown, which achievements were celebrated).
// Losing them only means something shows once more.
const PREFIX = 'ailu.';

export const deviceStore = {
  async read(key: string): Promise<string | null> {
    try {
      if (Platform.OS === 'web') return globalThis.localStorage?.getItem(PREFIX + key) ?? null;
      return await SecureStore.getItemAsync(PREFIX + key);
    } catch {
      return null;
    }
  },
  async write(key: string, value: string): Promise<void> {
    try {
      if (Platform.OS === 'web') globalThis.localStorage?.setItem(PREFIX + key, value);
      else await SecureStore.setItemAsync(PREFIX + key, value);
    } catch {
      // Not remembering only means it may show once more.
    }
  },
};
