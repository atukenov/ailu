import { ru, type Strings } from './ru';
import { kk } from './kk';
import { useSession } from '@/session';

// Russian is complete for the closed test; Kazakh and English fall back to Russian per string
// until they are translated.
const locales: Record<string, Partial<Strings>> = { ru, kk, en: {} };

export function strings(locale: string | undefined): Strings {
  return { ...ru, ...locales[locale ?? 'ru'] };
}

export function useStrings(): Strings {
  return strings(useSession().user?.locale);
}

export type { Strings };
