import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useState } from 'react';

import { api } from '@/api/client';
import { Colors } from '@/constants/theme';
import { SessionProvider, useSession } from '@/session';

SplashScreen.preventAutoHideAsync();

export default function RootLayout() {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1 } } }));
  return (
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <StatusBar style="light" />
        <Routes />
      </SessionProvider>
    </QueryClientProvider>
  );
}

// Three states decide where a person can be: signed out, signed in without a couple, or in a couple.
function Routes() {
  const { status, user } = useSession();
  const signedIn = status === 'signedIn';
  const couple = useQuery({ queryKey: ['couple'], queryFn: api.couple, enabled: signedIn });

  const ready = status === 'signedOut' || (signedIn && !couple.isPending);
  useEffect(() => {
    if (ready) SplashScreen.hideAsync();
  }, [ready]);
  if (!ready) return null;

  const named = !!user?.name;
  const paired = !!couple.data;

  return (
    <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: Colors.background } }}>
      <Stack.Protected guard={!signedIn}>
        <Stack.Screen name="sign-in" />
      </Stack.Protected>
      <Stack.Protected guard={signedIn && !named}>
        <Stack.Screen name="name" />
      </Stack.Protected>
      <Stack.Protected guard={signedIn && named && !paired}>
        <Stack.Screen name="pair" />
      </Stack.Protected>
      <Stack.Protected guard={signedIn && named && paired}>
        <Stack.Screen name="index" />
        <Stack.Screen name="profile" options={{ presentation: 'modal' }} />
        <Stack.Screen name="memories/index" />
        <Stack.Screen name="memories/new" />
        <Stack.Screen name="memories/[id]" />
        <Stack.Screen name="goals/index" />
        <Stack.Screen name="goals/new" />
        <Stack.Screen name="goals/[id]" />
        <Stack.Screen name="paywall" options={{ presentation: 'modal' }} />
      </Stack.Protected>
    </Stack>
  );
}
