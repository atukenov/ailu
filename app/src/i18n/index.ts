import { ru, type Strings } from './ru';
import { kk } from './kk';
import { useSession } from '@/session';

// Russian is the default and Kazakh is complete; English falls back to Russian per string until
// it is translated, so the profile does not offer it yet.
const locales: Record<string, Partial<Strings>> = { ru, kk, en: {} };

export function strings(locale: string | undefined): Strings {
  return { ...ru, ...locales[locale ?? 'ru'] };
}

export function useStrings(): Strings {
  return strings(useSession().user?.locale);
}

export type { Strings };
