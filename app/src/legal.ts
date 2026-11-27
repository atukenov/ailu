import Constants from 'expo-constants';
import * as WebBrowser from 'expo-web-browser';

// The legal pages are the website in web/ (deployed on Vercel), one page per language:
// <site>/legal/<doc>-<ru|kk>.html. The base comes from extra.legalUrl in app.json.
const BASE = (Constants.expoConfig?.extra?.legalUrl as string | undefined) ?? 'https://ailu.vercel.app/legal';

export type LegalDoc = 'privacy' | 'terms';

export function legalUrl(doc: LegalDoc, locale: string | undefined) {
  return `${BASE}/${doc}-${locale === 'kk' ? 'kk' : 'ru'}.html`;
}

export function openLegal(doc: LegalDoc, locale: string | undefined) {
  return WebBrowser.openBrowserAsync(legalUrl(doc, locale));
}
