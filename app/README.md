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

## Push notifications

Once a couple exists, the Today screen asks for notification permission and registers the device's Expo
push token with the API. This needs a real device and an EAS project: run `npx eas-cli@latest init` once
in this folder (it writes `extra.eas.projectId` to `app.json`), then use a development build, since
Expo Go can't receive remote pushes on Android. Without a project id the app works and simply skips
registration. Sign-out unregisters the device.

## Memories

Memories (`src/app/memories/`) hold a title, a day, a place, text and up to 10 photos. Photos are picked from
the library, shrunk on the phone to 2 560 px JPEG 82 (`src/photos.ts`), then uploaded one by one; the memory
is created first, so a failed upload can be retried from its page. A finished task offers "Сохранить в
воспоминания", which ties the memory to that task. Photo URLs come signed from the API and load without the token.

## Goals

Shared goals (`src/app/goals/`) have a title, a category, an optional month to aim for, a weekly reminder and
up to 20 steps. One partner proposes a goal and the other joins; then either marks a step done and the other
confirms, which earns the couple +100 нұр. Progress is the share of closed steps, counted by the server.
Removing a shared goal needs both. Free couples run two goals at a time; the list then shows a soft Plus card
instead of the "+" button.

## Layout

| Path | What |
| --- | --- |
| `src/app/` | Screens. `_layout.tsx` decides which are reachable: signed out → `sign-in`; no name yet → `name`; no couple → `pair`; in a couple → `index` (Today), `profile`, `memories/` and `goals/`. |
| `src/api/` | Typed client for the API, token storage (Keychain/Keystore), one shared refresh on 401. |
| `src/photos.ts` | Picking photos and shrinking them before upload. |
| `src/notifications.ts` | Permission, push token registration and sign-out unregistration. |
| `src/i18n/` | Strings. Russian is complete; Kazakh and English fall back to Russian per string. |
| `src/components/` | `ui.tsx` (screen, card, button, field, chip, progress bar), the goal month picker and the phase moon. |
| `src/constants/theme.ts` | Colors and spacing, taken from the mockups. |

The partner's activity shows up by polling `/v1/couple/today` every 30 s until the realtime hub exists.
