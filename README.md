# Ailu

An app for couples in Kazakhstan: one small shared thing to do each day, a shared memory of everything done,
and a moon that fills as the couple goes further together. Progress is measured in **нұр** ("light") and
levels are **moon phases**.

## Layout

| Path | What |
| --- | --- |
| `backend/` | .NET 10 Minimal API, PostgreSQL via EF Core. One folder per scenario under `src/Ailu.Api/Features`. |
| `app/` | React Native app (Expo, Expo Router, TanStack Query). See `app/README.md`. |

## Backend

Production setup (Railway with PostgreSQL, App Store, Google Play, Firebase) is in [`DEPLOY.md`](DEPLOY.md); `backend/Dockerfile` builds the image.

```sh
docker compose up -d postgres         # or any local PostgreSQL with postgres/postgres
cd backend
dotnet run --project src/Ailu.Api      # Development: migrates on start, OpenAPI at /openapi/v1.json
dotnet test                            # each test gets its own throwaway database
```

Tests use `AILU_TEST_PG` (default `Host=localhost;Port=5432;Username=postgres;Password=postgres`).
In Development, SMS codes are written to the log instead of being sent.

Add a migration after changing the model:

```sh
dotnet tool install --global dotnet-ef
dotnet ef migrations add <Name> -p src/Ailu.Api -o Data/Migrations
```

### Rules the code depends on

- **The couple is the primary entity.** Shared data is keyed by `CoupleId`, and the couple is always
  resolved from the caller's token, never from the URL.
- **A couple is ended, not deleted.** `DELETE /v1/couple` sets `Status = Ended`; history stays.
- **Нұр is server-side and idempotent.** The client never sends an amount. Every award is a row in the
  append-only `NurLedger` with key `(CoupleId, SourceType, SourceId, Date)`. `Couple.NurEarned` and
  `NurBalance` are caches updated in the same transaction.
- **Phases never drop.** They follow earned нұр, which only grows.
- **The partner's answer does not reach the device before the reveal.** Hiding it in the UI is not enough.
- **A streak day needs both partners.** One missed night per 7 days is forgiven.
- **A broken streak can be restored with нұр.** Each missed day the free night doesn't cover costs one restore
  (Free 1, Plus 3, Premium 5 a calendar month) and 100 нұр, written as a negative `NurLedger` row that lowers the
  balance, never earned нұр. The offer stands while nobody has started over, or for a week after they did,
  and then adds the new days on top.
- **At most two pushes a day per person**, in the couple's time zone: the task at 09:00 (window until 12:00)
  to both, and at 20:00 (until 23:00) a nudge only to someone whose partner already answered the question.
  `PushLog` has one row per sent push, which keeps it to once even across restarts or several instances.
- **Photos are counted when they are stored, never recounted.** `Couple.PhotoCount` and `StorageBytes` change in
  the same transaction as the photo row, through one conditional `UPDATE`, so two uploads at once can't pass the
  limit. A free couple keeps up to 200 photos; the paid plans' limits are not decided yet (only the roadmap's
  byte quotas apply: Plus 10 GB, Premium 30 GB; Free also has 1 GB as a backstop).
- **Only the author changes or deletes a memory**; both partners see it. Deleting frees the quota at once and the
  files go 30 days later.
- **A partner sees a mood, never its note, until the author shares it.** Mood needs the person's explicit consent
  (`PUT /v1/me/mood-consent`); withdrawing it deletes all their moods. Both marking on the same day earns +10 нұр once.
  After 12 months a day's mood becomes a count in `MoodMonth` and the day, note included, is deleted.
- **Deleting an account doesn't erase the partner's history** (privacy doc). `DELETE /v1/me` removes the person's
  phone, name, sessions, devices, moods, invitations, reactions, their memories with the photo files, the text of
  their answers and goals the partner never joined, and ends their couple. The нұр ledger, phase, streak, tasks
  done together, joined goals and the partner's own records stay; the user row stays as a bare id so those keep
  an author. The same number can sign up again as a new person.
- **The relationship history is built, not stored.** `GET /v1/couple/timeline` assembles it from the couple's own
  records on each request (start, days together and anniversaries, phases replayed from `NurLedger`, the first task
  and the first revealed answers, memories, finished goals, earned achievements), so it needs no table and can't drift from them. The
  server sends kinds and numbers and the app writes the words. Free couples see their last 90 days plus the day they
  began; Plus and Premium see everything. Nothing is locked: memories stay open in their own tab.

- **Achievements are for real-life moments, confirmed by both.** The library (`Achievement`, seeded in `Seed.cs`,
  Russian and Kazakh) is server content: the first three firsts are free, the rest open with Plus and show blurred to Free. One partner marks one
  with the day it happened, the other confirms, and the couple gets +80 нұр once. An earned achievement is never
  undone, not even when the plan lapses; only a new mark checks the plan.

- **AI ideas are Premium's, and the model never learns who the couple is.** A Premium couple asks for selections
  (`POST /v1/couple/ai/picks`: five ideas for a date, an evening at home, a film or music), 30 a calendar month
  (`Plans.PremiumAiPicksPerMonth`); the place is taken under the couple row lock before the model is called, and a
  failed call gives it back. Premium's two extra tasks a day are personal tasks the model writes; they are stored as
  `DailyTask` rows with the couple's `CoupleId`, which keeps them out of the shared library (`AiluDb.TaskLibrary`), and a
  library task stands in when the model can't be reached. The model gets only `CoupleProfile` (season, days together,
  love-language leanings, library tasks they did, goal categories) and the partner's typed wish: no names, phones,
  answers, memories or moods. Selections and saved ideas stay readable after the plan ends.

### AI

`IAiWriter` writes selections and personal tasks. With `Ai:ApiKey` set it is `ClaudeAiWriter` (Anthropic API,
`Ai:Model` default `claude-haiku-5-5`, `Ai:Effort` default `low`, structured JSON output; Sonnet/Opus/Fable models
also get server-side refusal fallbacks, Haiku has none and falls back to the library task or "try later");
without a key, `StubAiWriter` serves a small built-in set in Russian and Kazakh, so everything works
before a key exists. The prompts and the checks on what comes back are in `Services/AiPrompts.cs`.

AI is a paid feature: selections and personal tasks are Premium only (`Plans.AiPicksPerMonth`, 30 a month).
In production set these env vars on the API (Railway):

| Env var | Value |
|---|---|
| `Ai__ApiKey` | Anthropic API key from platform.claude.com (Settings, API keys). Set a monthly spend limit there too. |
| `Ai__Model` | optional, default `claude-haiku-5-5` (about $0.10 / $0.50 per million tokens in / out) |
| `Ai__Effort` | optional, default `low` |
| `Ai__TimeoutSeconds` | optional, default `40` |

### Push notifications

`DailyPushScheduler` runs `DailyPushJob` every minute inside the API. Pushes go through Firebase Cloud Messaging
(HTTP v1 API; iPhones are reached through FCM's APNs link) when `Push:Firebase:ServiceAccountJson` holds a
Firebase service account key (the whole JSON, env var `Push__Firebase__ServiceAccountJson`); the project id is
read from the key, or `Push:Firebase:ProjectId`. Without a key (the default, and in Development) pushes are
written to the log. Tokens FCM reports as gone are deleted from `Devices`. Set `Push:SchedulerEnabled` to
`false` to turn the job off.

### Store review sign-in

Reviewers can't get our SMS, so `Auth:ReviewPhone` + `Auth:ReviewCode` (six digits) give one number a fixed code
and no SMS. Signing in with it creates a demo couple with 12 days of history, two memories and a goal; the
partner (Айгерим, no phone, so nobody can sign in as her) accepts the task, confirms it, answers the question and
marks a mood whenever the reviewer opens Today. If the reviewer ends the couple or deletes the account, the next
sign-in makes a fresh one. The review account can't send invitations. Both settings empty (the default) turn it off.

### Photos

The app shrinks photos to 2 560 px, JPEG 82, before upload. The server decodes every upload again
(SkiaSharp), turns it upright, drops EXIF (and with it the location) and re-encodes it, so a client that skipped
the first step can't eat the quota. The 320 px and 1 080 px sizes are made on first request and kept.
Photo URLs are signed and stay valid one to two hours, so image views can load them without the token.

Files go to a folder on disk (`Photos:Root`, default `App_Data/photos`). The privacy doc requires storage inside
Kazakhstan, so an S3-compatible store replaces the folder once hosting is chosen; `IPhotoStore` is the seam.
`MemoryPurgeScheduler` removes deleted memories' files after 30 days, from every storage class, and once a day
sweeps photo folders no row points to (older than a day); `Photos:PurgeEnabled` turns it off.

Cold storage: a year after upload, `PhotoArchiveJob` (run by the same scheduler) moves a photo's 1080 and 2560 sizes
to the archive storage class, about half the price; thumbnails stay standard so lists scroll fast. Reads find a
file in either class. On disk the archive class is a second folder (`Photos:ArchiveRoot`, default
`<Photos:Root>-archive`). On S3 it must be an instant-retrieval class; one that needs a restore before reading
won't do.

### Gift subscriptions and promo codes

Both are a code a couple enters (`POST /v1/billing/redeem`); entering it adds a `Subscription` row with provider
`Gift` or `Promo`, so the plan reaches both partners and expires like any other. Each person and each couple
enters a code once (unique indexes on `CodeRedemption`). A second code of the same plan starts where the first
ends; a store subscription keeps running alongside. Codes never touch the 7-day trial.

All payments inside the app go through the App Store and Google Play (subscriptions and the video story).
Nothing else is sold in the app.

- **Gifts** are sold person to person, outside the app: the buyer pays the team directly, then the team makes a
  one-time gift code and hands it over. The code is 12 characters (`7KQ2-M9XD-4HPA`, no I/L/O/U), works for one
  couple, and `note` records who paid:

  ```sh
  curl -X POST $API/v1/admin/gift-codes -H "X-Admin-Key: $KEY" -H 'Content-Type: application/json' \
    -d '{"tier":"plus","months":3,"note":"Айгерим, перевод 27.11"}'
  curl $API/v1/admin/gift-codes -H "X-Admin-Key: $KEY"   # which were entered
  ```
- **Promo codes** are made by the team with `Billing:AdminKey` set:

  ```sh
  curl -X POST $API/v1/admin/promo-codes -H "X-Admin-Key: $KEY" -H 'Content-Type: application/json' \
    -d '{"code":"LOVE2026","tier":"plus","days":30,"maxRedemptions":500,"validUntil":"2026-12-31T19:00:00Z","note":"Instagram"}'
  curl $API/v1/admin/promo-codes -H "X-Admin-Key: $KEY"   # how many couples used each
  ```

### Photo book payment

A printed book is a physical good, so the app stores don't allow selling it through in-app purchase, and
it isn't. A couple orders in the app (delivery address and phone); the order waits as `awaiting_payment` and
the app says the team will write to them. The team agrees payment directly, then marks the order paid, and
the print job picks it up:

```sh
curl $API/v1/admin/photo-book-orders -H "X-Admin-Key: $KEY"            # waiting for payment (?status=all for every order)
curl -X POST $API/v1/admin/photo-book-orders/$ORDER/paid -H "X-Admin-Key: $KEY" \
  -H 'Content-Type: application/json' -d '{"note":"перевод 27.11"}'
```

### API (Stage 1)

| Method | Path | |
| --- | --- | --- |
| POST | `/v1/auth/phone/request` | send a 6-digit code by SMS (one per minute) |
| POST | `/v1/auth/phone/verify` | code → access + refresh token; creates the user on first sign-in |
| POST | `/v1/auth/refresh` | rotate the refresh token |
| GET/PATCH | `/v1/me` | name, locale (`kk`/`ru`/`en`), time zone |
| DELETE | `/v1/me` | delete the account (see above) |
| POST | `/v1/couple/invitations` | invite a partner by phone; they get a code by SMS, valid 24 h |
| POST | `/v1/couple/invitations/{code}/accept` | only from the invited number; creates the couple |
| GET/DELETE | `/v1/couple` | couple, partner, days together / end the couple |
| GET | `/v1/couple/today` | task of the day, question of the day, streak, phase |
| POST | `/v1/couple/tasks/{id}/accept` | both must accept |
| POST | `/v1/couple/tasks/{id}/complete` | one marks it done, the other confirms: +50 нұр |
| POST | `/v1/couple/questions/{id}/answer` | blind answer; both answered → revealed, locked, +30 нұр |
| POST | `/v1/couple/streak/restore` | spend нұр to bring back a broken streak (offer is in `today.streak.restore`) |
| GET | `/v1/couple/phase` | phase, earned and balance, progress to the next phase |
| PUT | `/v1/me/devices` | register this device's Firebase (FCM) push token (moves it to the caller if someone else had it) |
| POST | `/v1/me/devices/unregister` | stop pushes to this device, on sign-out |
| GET/POST | `/v1/couple/memories` | memories, newest by the day they happened, 20 per page with `?cursor=` / add one |
| GET/PATCH/DELETE | `/v1/couple/memories/{id}` | one memory / edit or delete it (author only) |
| POST | `/v1/couple/memories/{id}/photos` | add a photo (multipart `file`, author only, up to 10 per memory) |
| PUT/DELETE | `/v1/couple/memories/{id}/reaction` | heart; the partner's first one earns +20 нұр (3 a day) |
| PUT/DELETE | `/v1/couple/memories/{id}/favorite` | shared star |
| GET | `/v1/couple/storage` | plan, photos stored and the limit (200 on Free), bytes used and quota |
| GET | `/v1/photos/{id}/{320\|1080\|2560}` | the photo, by the signed URL the API hands out |
| GET | `/v1/archive` | couples the caller was in that ended, with their memory counts |
| GET | `/v1/archive/{coupleId}/memories` | an ended couple's memories, read-only; the partner's photos for 30 days after the end |
| GET/DELETE | `/v1/archive/{coupleId}/memories/{id}` | one archived memory / delete it (author only) |

The product and design documents (in Russian) live in the project's shared files, not in this repo.
