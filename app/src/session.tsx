import { useQueryClient } from '@tanstack/react-query';
import { createContext, use, useEffect, useState, type ReactNode } from 'react';

import { api, ApiError, setSignedOutHandler } from '@/api/client';
import { tokens } from '@/api/tokens';
import type { TokenPair, User } from '@/api/types';

type Session = {
  status: 'loading' | 'signedOut' | 'signedIn';
  user: User | null;
  signIn: (pair: TokenPair) => Promise<void>;
  signOut: () => Promise<void>;
  setUser: (user: User) => void;
};

const SessionContext = createContext<Session | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<Session['status']>('loading');
  const [user, setUser] = useState<User | null>(null);

  useEffect(() => {
    const signedOut = () => {
      queryClient.clear();
      setUser(null);
      setStatus('signedOut');
    };
    setSignedOutHandler(signedOut);

    (async () => {
      const { access } = await tokens.get();
      if (!access) return signedOut();
      try {
        setUser(await api.me());
        setStatus('signedIn');
      } catch (e) {
        // Offline at launch: keep the stored session rather than signing the person out.
        if (e instanceof ApiError && e.code === 'offline') setStatus('signedIn');
        else signedOut();
      }
    })();
  }, [queryClient]);

  const value: Session = {
    status,
    user,
    setUser,
    async signIn(pair) {
      await tokens.set(pair.accessToken, pair.refreshToken);
      setUser(pair.user);
      setStatus('signedIn');
    },
    async signOut() {
      await tokens.clear();
      queryClient.clear();
      setUser(null);
      setStatus('signedOut');
    },
  };

  return <SessionContext value={value}>{children}</SessionContext>;
}

export function useSession(): Session {
  const session = use(SessionContext);
  if (!session) throw new Error('useSession must be used inside SessionProvider');
  return session;
}
