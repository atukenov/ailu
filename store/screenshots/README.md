# Store screenshots

`ru/ios/` — App Store, 6.9" slot (1320 × 2868). `ru/android/` — Google Play phone (1080 × 1920; Play allows at most 2:1).

`source/` holds the raw captures of the current UI: the web build (`npx expo start --web`) at 430 × 932 pt and 3× scale, against
a local API seeded with a demo couple by `tools/screenshots/seed.py` in the project folder (Айгерим и Нурлан, 6 memories,
2 goals, both moods, quiz, 3 achievements). Then in the local database: `NurEarned` 2140 and `StreakDays` 47 so the moon shows
«Свой ритм», `StartedAt` moved back to 2025-02-14, and the two answers rewritten to fit the day's question. The photos are drawn
gradients, not real people.

To rebuild the framed images after changing a capture or a caption:

```sh
cd store/screenshots
npm i --no-save playwright   # or use a global install
CHROMIUM=/path/to/chromium node frame.mjs
```

Captions live in `frame.mjs`. Kazakh screenshots wait for the native-speaker review of the Kazakh texts.
