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

### Push notifications

`DailyPushScheduler` runs `DailyPushJob` every minute inside the API. Pushes go through Expo's push service
when `Push:UseExpo` is `true`; otherwise (the default, and in Development) they are written to the log.
Set `Push:SchedulerEnabled` to `false` to turn the job off.

### Photos

The app shrinks photos to 2 560 px, JPEG 82, before upload. The server decodes every upload again
(SkiaSharp), turns it upright, drops EXIF (and with it the location) and re-encodes it, so a client that skipped
the first step can't eat the quota. The 320 px and 1 080 px sizes are made on first request and kept.
Photo URLs are signed and stay valid one to two hours, so image views can load them without the token.

Files go to a folder on disk (`Photos:Root`, default `App_Data/photos`). The privacy doc requires storage inside
Kazakhstan, so an S3-compatible store replaces the folder once hosting is chosen; `IPhotoStore` is the seam.
`MemoryPurgeScheduler` removes deleted memories' files after 30 days; `Photos:PurgeEnabled` turns it off.

### API (Stage 1)

| Method | Path | |
| --- | --- | --- |
| POST | `/v1/auth/phone/request` | send a 6-digit code by SMS (one per minute) |
| POST | `/v1/auth/phone/verify` | code → access + refresh token; creates the user on first sign-in |
| POST | `/v1/auth/refresh` | rotate the refresh token |
| GET/PATCH | `/v1/me` | name, locale (`kk`/`ru`/`en`), time zone |
| POST | `/v1/couple/invitations` | invite a partner by phone; they get a code by SMS, valid 24 h |
| POST | `/v1/couple/invitations/{code}/accept` | only from the invited number; creates the couple |
| GET/DELETE | `/v1/couple` | couple, partner, days together / end the couple |
| GET | `/v1/couple/today` | task of the day, question of the day, streak, phase |
| POST | `/v1/couple/tasks/{id}/accept` | both must accept |
| POST | `/v1/couple/tasks/{id}/complete` | one marks it done, the other confirms: +50 нұр |
| POST | `/v1/couple/questions/{id}/answer` | blind answer; both answered → revealed, locked, +30 нұр |
| POST | `/v1/couple/streak/restore` | spend нұр to bring back a broken streak (offer is in `today.streak.restore`) |
| GET | `/v1/couple/phase` | phase, earned and balance, progress to the next phase |
| PUT | `/v1/me/devices` | register this device's Expo push token (moves it to the caller if someone else had it) |
| POST | `/v1/me/devices/unregister` | stop pushes to this device, on sign-out |
| GET/POST | `/v1/couple/memories` | memories, newest by the day they happened, 20 per page with `?cursor=` / add one |
| GET/PATCH/DELETE | `/v1/couple/memories/{id}` | one memory / edit or delete it (author only) |
| POST | `/v1/couple/memories/{id}/photos` | add a photo (multipart `file`, author only, up to 10 per memory) |
| PUT/DELETE | `/v1/couple/memories/{id}/reaction` | heart; the partner's first one earns +20 нұр (3 a day) |
| PUT/DELETE | `/v1/couple/memories/{id}/favorite` | shared star |
| GET | `/v1/couple/storage` | plan, photos stored and the limit (200 on Free), bytes used and quota |
| GET | `/v1/photos/{id}/{320\|1080\|2560}` | the photo, by the signed URL the API hands out |

The product and design documents (in Russian) live in the project's shared files, not in this repo.
