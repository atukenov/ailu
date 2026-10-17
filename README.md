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
| GET | `/v1/couple/phase` | phase, earned and balance, progress to the next phase |

The product and design documents (in Russian) live in the project's shared files, not in this repo.
