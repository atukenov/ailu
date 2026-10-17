import Constants from 'expo-constants';
import * as WebBrowser from 'expo-web-browser';

// The privacy policy and terms live in docs/legal and are published on GitHub Pages, one page per language.
const BASE = (Constants.expoConfig?.extra?.legalUrl as string | undefined) ?? 'https://atukenov.github.io/ailu/legal';

export type LegalDoc = 'privacy' | 'terms';

export function legalUrl(doc: LegalDoc, locale: string | undefined) {
  return `${BASE}/${doc}-${locale === 'kk' ? 'kk' : 'ru'}.html`;
}

export function openLegal(doc: LegalDoc, locale: string | undefined) {
  return WebBrowser.openBrowserAsync(legalUrl(doc, locale));
}
