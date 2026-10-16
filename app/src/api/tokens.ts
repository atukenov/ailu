import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';

// Tokens live in the Keychain / Keystore on devices. The web build (used only for development)
// has no secure store, so it falls back to localStorage.
const ACCESS = 'ailu.access';
const REFRESH = 'ailu.refresh';

async function read(key: string): Promise<string | null> {
  if (Platform.OS === 'web') return globalThis.localStorage?.getItem(key) ?? null;
  return SecureStore.getItemAsync(key);
}

async function write(key: string, value: string | null): Promise<void> {
  if (Platform.OS === 'web') {
    if (value === null) globalThis.localStorage?.removeItem(key);
    else globalThis.localStorage?.setItem(key, value);
    return;
  }
  if (value === null) await SecureStore.deleteItemAsync(key);
  else await SecureStore.setItemAsync(key, value);
}

let cache: { access: string | null; refresh: string | null } | null = null;

export const tokens = {
  async get() {
    cache ??= { access: await read(ACCESS), refresh: await read(REFRESH) };
    return cache;
  },
  async set(access: string, refresh: string) {
    cache = { access, refresh };
    await Promise.all([write(ACCESS, access), write(REFRESH, refresh)]);
  },
  async clear() {
    cache = { access: null, refresh: null };
    await Promise.all([write(ACCESS, null), write(REFRESH, null)]);
  },
};
