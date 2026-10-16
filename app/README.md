# Ailu app

React Native app built with Expo (SDK 58) and Expo Router. Server state lives in TanStack Query;
nothing the server decides (нұр, phase, streak, the partner's answer) is computed on the device.

```sh
npm install
EXPO_PUBLIC_API_URL=http://<your-machine-ip>:5210 npx expo start   # a phone can't reach "localhost"
npm run typecheck && npm run lint
```

The API must be running (see `../backend`). In Development it writes SMS codes to its log.
`npx expo start --web` also works for quick checks; the API allows `http://localhost:8081` for that.

## Layout

| Path | What |
| --- | --- |
| `src/app/` | Screens. `_layout.tsx` decides which are reachable: signed out → `sign-in`; no name yet → `name`; no couple → `pair`; in a couple → `index` (Today) and `profile`. |
| `src/api/` | Typed client for the API, token storage (Keychain/Keystore), one shared refresh on 401. |
| `src/i18n/` | Strings. Russian is complete; Kazakh and English fall back to Russian per string. |
| `src/components/` | `ui.tsx` (screen, card, button, field) and the phase moon. |
| `src/constants/theme.ts` | Colors and spacing, taken from the mockups. |

The partner's activity shows up by polling `/v1/couple/today` every 30 s until the realtime hub exists.
