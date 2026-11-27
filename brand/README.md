# Ailu logo and icons

The logo is the word **ailu**: the "a" is a gold crescent moon (ай), the dot over the "i" is a pink heart.
The app icon is the first two letters, **ai**, which is how "ай", the moon, sounds.

Everything is drawn in code in `build.mjs` (no fonts, no hand-exported files). Change the drawing there and run

```
node brand/build.mjs
```

from the repo root. It needs Playwright's Chromium and Python 3 with Pillow (it removes the alpha channel from icons that must be opaque, which App Store Connect requires).

| File | Use |
|---|---|
| `ailu-logo.svg` | Wordmark for dark backgrounds (master) |
| `ailu-logo-on-light.svg` | Wordmark with plum letters for light backgrounds |
| `ailu-logo-mono.svg` | One-colour wordmark |
| `ailu-icon.svg` | App icon master, 1024 × 1024 |
| `png/ailu-icon-1024.png` | App Store icon, opaque, square corners |
| `png/play-store-icon-512.png` | Google Play listing icon |
| `png/ailu-logo.png` | Wordmark PNG, transparent |

It also writes the app's icons (`app/assets/images/*`, `app/assets/expo.icon/Assets/ailu-ai.png`) and the website's `web/public/favicon.svg`, `logo.svg`, `apple-touch-icon.png` and `icon-512.png`.

Colours come from the app theme: night `#170F1D`, gold `#FFD27A`, pink `#FF8FB8`, cream `#FBF3EC`.
