# Store screenshots

`ru/ios/` — App Store, 6.9" slot (1320 × 2868). `ru/android/` — Google Play phone (1080 × 1920; Play allows at most 2:1).

`source/` holds the raw captures: the web build (`npx expo start --web`) at 430 × 932 pt and 3× scale, against a local API
seeded with a demo couple (Алмаз и Нурай, 4 memories, 2 goals, both moods set; нұр and streak raised in the local database
so the moon shows «Свой ритм»). The photos are drawn gradients, not real people.

To rebuild the framed images after changing a capture or a caption:

```sh
cd store/screenshots
npm i --no-save playwright   # or use a global install
CHROMIUM=/path/to/chromium node frame.mjs
```

Captions live in `frame.mjs`. Kazakh screenshots wait until the server's tasks and questions are translated.
