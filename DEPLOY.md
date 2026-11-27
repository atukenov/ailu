# Выкладка Ailu в продакшн

Пошагово: где что создать, что куда вписать и в каком порядке. Что ещё не построено и какие решения открыты, смотрите в [`store/CHECKLIST.md`](store/CHECKLIST.md).

## Как всё устроено

```
Телефон (приложение из App Store / Google Play, собирается в Expo EAS)
   │ HTTPS
   ▼
api.ailu.kz ── Railway: сервер .NET в Docker (backend/Dockerfile)
   │            том Railway /data: фото пар
   ├── Railway: PostgreSQL (тот же проект, внутренняя сеть)
   ├── Firebase Cloud Messaging: push
   ├── App Store Server API и Google Play API: проверка покупок
   └── Anthropic API: AI-идеи для Premium (когда будет ключ)

ailu.kz ── Vercel: сайт (лендинг, юридические документы, страница удаления аккаунта)
```

Порядок: **1** Railway: проект и база → **2** Railway: сервер → **3** домен → **4** App Store Connect → **5** Google Play → **6** Firebase → **7** сборки EAS → **8** проверка. Vercel (раздел 9) можно делать в любой момент.

Секреты (ключи, пароли, JSON-файлы) не кладите в git. Они живут только в переменных Railway и в EAS.

---

## 1. Railway: проект и база данных

База PostgreSQL живёт в том же проекте Railway, что и сервер, и сервер ходит к ней по внутренней сети Railway (без выхода в интернет).

1. https://railway.com → New Project → **Deploy PostgreSQL** (или в существующем проекте: **+ Create** → **Database** → **PostgreSQL**). Сервис появится под именем `Postgres`.
2. Тариф: для настоящих пользователей нужен платный (Pro): на нём есть бэкапы по расписанию и нет жёстких лимитов пробного тарифа.
3. Postgres → **Settings** → **Region**: EU West (Amsterdam), тот же регион, что у сервера (раздел 2).
4. **Бэкапы:** Postgres → вкладка **Backups** → **Edit schedule** → включите **Daily** и **Weekly** (Railway хранит ежедневные 6 дней, еженедельные 27 дней, ежемесячные 89 дней). Перед рискованными изменениями можно нажать **Create backup** вручную. То же расписание стоит включить и для тома сервера с фото (раздел 2, шаг 3).
5. Строку подключения отдельно собирать не нужно: в разделе 2 переменная `ConnectionStrings__Ailu` ссылается на переменные сервиса Postgres, и Railway подставит их сам. Готовый `DATABASE_URL` из Postgres не подходит: .NET (Npgsql) не понимает строку вида `postgresql://...`.
6. Таблицы создаст сам сервер при первом запуске (`Database__MigrateOnStartup=true`, раздел 2). Публичный доступ к базе (TCP Proxy) не включайте: серверу он не нужен.

**Как восстановить базу из бэкапа**

1. Postgres → **Backups** → нужный бэкап → **Restore**. Railway создаст новый том из бэкапа, а старый оставит в проекте неподключённым (на случай, если понадобится).
2. Нажмите **Deploy** на появившихся изменениях (staged changes), дождитесь, пока Postgres перезапустится.
3. Перезапустите сервер (сервис `ailu` → Restart) и проверьте `/health` и вход ревьюера (раздел 8). Данные после момента бэкапа пропадут, поэтому перед восстановлением предупредите пользователей или сделайте ручной бэкап текущего состояния.
4. Если нужно вернуть данные на минуту раньше, а не на момент бэкапа: в Railway есть Point-in-Time Recovery для Postgres (восстановление в новый сервис, окно около 4 недель). Его нужно включить заранее, см. https://docs.railway.com/volumes/point-in-time-recovery.

Своя копия вне Railway (по желанию, раз в месяц): на время включите TCP Proxy у Postgres, возьмите `DATABASE_PUBLIC_URL` и выполните `pg_dump "<DATABASE_PUBLIC_URL>" -Fc -f ailu-$(date +%F).dump`, затем выключите прокси.

> Фото пар по-прежнему хранятся на томе сервера `/data` (раздел 2), а не в базе. Бэкап Postgres их не включает: включите бэкапы и для этого тома.

## 2. Railway: сервер

1. В том же проекте Railway, где Postgres: **+ Create** → **GitHub Repo** → `atukenov/ailu`.
2. Сервис → **Settings**:
   - **Root Directory:** `backend`
   - **Config as code:** путь `/backend/railway.json` (сборка по Dockerfile, проверка `/health`, одна копия).
   - **Region:** EU West (Amsterdam), тот же, что у Postgres.
   - **Replicas: 1.** Фото лежат на томе, а планировщик push и очисток рассчитан на одну копию.
3. Сервис → правый клик → **Attach Volume**, путь монтирования **`/data`**, размер от 5 GB (растёт по мере фото). Сервер пишет фото в `/data/photos` и `/data/photos-archive`. Во вкладке **Backups** этого тома включите **Daily** и **Weekly**, как у Postgres.
4. Сервис → **Variables** (Raw Editor). Секреты сгенерируйте командой `openssl rand -base64 48`.

   ```env
   ASPNETCORE_ENVIRONMENT=Production
   ConnectionStrings__Ailu=Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}
   Database__MigrateOnStartup=true

   # Вход
   Auth__SigningKey=<openssl rand -base64 48>
   Auth__ReviewPhone=+77000000000
   Auth__ReviewCode=<шесть цифр>

   # Покупки: App Store (раздел 4)
   Billing__Apple__IssuerId=<Issuer ID>
   Billing__Apple__KeyId=<Key ID>
   Billing__Apple__PrivateKey=<содержимое .p8 целиком, с BEGIN/END>
   # Покупки: Google Play (раздел 5)
   Billing__Google__ServiceAccountJson=<содержимое JSON-ключа целиком, одной строкой>
   Billing__Google__WebhookSecret=<openssl rand -hex 24>

   # Промокоды и подарки
   Billing__AdminKey=<openssl rand -base64 32>

   # Фотокнига: печать вручную, отметки об отправке (можно оставить пустым)
   PhotoBook__PrintSecret=

   # AI-идеи для Premium; пусто = встроенная заглушка
   Ai__ApiKey=
   Ai__Model=

   # Push через Firebase (раздел 6): JSON сервисного аккаунта целиком
   Push__Firebase__ServiceAccountJson=
   ```

   `${{Postgres.…}}` это ссылки на переменные сервиса Postgres; Railway подставит внутренний адрес `postgres.railway.internal`. Если сервис базы назван иначе, замените `Postgres` на его имя. SSL внутри сети Railway не нужен.

   SMS-провайдер добавит свои переменные, когда его построят.
5. **Networking** → **Custom Domain** → `api.ailu.kz` (Railway покажет CNAME для DNS, раздел 3). Пока домена нет, можно нажать **Generate Domain** и получить адрес `*.up.railway.app`.
6. Deploy. В логах должно быть `Now listening on: http://[::]:...`, а `https://api.ailu.kz/health` должен ответить `{"status":"ok"}`.
7. Дальше каждый пуш в `main` выкладывается сам; новые миграции базы применяются при старте.

## 3. Домен

У регистратора домена (например ailu.kz):

| Запись | Имя | Значение |
| --- | --- | --- |
| CNAME | `api` | адрес, который показал Railway |
| A / CNAME | `@`, `www` | значения, которые покажет Vercel (раздел 9) |

После этого впишите адрес сервера в `app/eas.json` → `build.base.env.EXPO_PUBLIC_API_URL` (сейчас там заглушка `https://api.ailu.kz`).

## 4. App Store Connect

Нужен оплаченный Apple Developer Program.

1. **Apps → +** → New App: платформа iOS, бандл `kz.ailu.app`, основной язык русский, SKU `ailu`. Apple ID приложения (число) впишите в `app/eas.json` → `submit.production.ios.ascAppId`.
2. **Business → Agreements, Tax, and Banking:** подписать Paid Apps, заполнить банк и налоги. Без этого покупки не работают даже в тесте.
3. **Подписки** (Monetization → Subscriptions): группа «Ailu», в ней четыре подписки, цены в тенге:

   | Product ID | Срок | Цена |
   | --- | --- | --- |
   | `kz.ailu.plus.monthly` | 1 месяц | 1 490 ₸ |
   | `kz.ailu.plus.yearly` | 1 год | 14 304 ₸ |
   | `kz.ailu.premium.monthly` | 1 месяц | 2 490 ₸ |
   | `kz.ailu.premium.yearly` | 1 год | 23 904 ₸ |

   Premium выше Plus в группе (уровень 1), чтобы переход считался повышением. Бесплатный пробный период в магазине **не** включайте: 7 дней Premium даёт сервер.
4. **In-App Purchases:** Non-Consumable `kz.ailu.story.year`, 1 990 ₸ (видео-история года).
5. **Ключ для сервера:** Users and Access → Integrations → **In-App Purchase** → Generate. Скачайте `.p8` (один раз!). Issuer ID, Key ID и содержимое файла идут в `Billing__Apple__*` на Railway.
6. **Уведомления о покупках:** App Information → App Store Server Notifications → Version 2, адрес для Production и для Sandbox: `https://api.ailu.kz/v1/billing/webhooks/apple`.
7. **Push-ключ** для Firebase (APNs, `.p8`): создаётся в разделе 6, шаг 3.
8. Карточка приложения: текст из `store/listing-ru.md`, App Privacy из `store/data-safety.md`, Age Rating из `store/age-rating.md` (16+), скриншоты 6.9" из `store/screenshots/ru/ios/`, ссылки на политику конфиденциальности и поддержку.
9. **App Review Information:** вход для ревьюера (номер `Auth__ReviewPhone` и код `Auth__ReviewCode`, текст в `store/CHECKLIST.md`).

## 5. Google Play Console

1. **Create app:** название Ailu, язык по умолчанию русский, приложение, бесплатное. Пакет `kz.ailu.app` закрепится первой загруженной сборкой.
2. **Сервисный аккаунт** (для сервера и для `eas submit`):
   1. https://console.cloud.google.com → новый проект → APIs & Services → включить **Google Play Android Developer API**.
   2. IAM → Service Accounts → Create → Keys → Add key → JSON. Скачайте файл.
   3. Play Console → Users and permissions → Invite new users → e-mail сервисного аккаунта; права на приложение: **View financial data** и **Manage orders and subscriptions**, плюс **Release** для `eas submit`.
   4. Содержимое JSON идёт в `Billing__Google__ServiceAccountJson` на Railway. Тот же файл положите рядом с `app/` как `google-service-account.json` (он не попадает в git) и пропишите путь в `app/eas.json` → `submit.production.android.serviceAccountKeyPath`.
3. **Monetize → Subscriptions:** подписка `plus` с базовыми планами `monthly` (1 490 ₸, автопродление каждый месяц) и `yearly` (14 304 ₸, каждый год); подписка `premium` с `monthly` (2 490 ₸) и `yearly` (23 904 ₸). Бесплатные пробные предложения не создавайте.
4. **Monetize → In-app products:** `year_story`, 1 990 ₸.
5. **Уведомления о покупках (RTDN):**
   1. Google Cloud → Pub/Sub → Create topic `play-billing`.
   2. В правах темы добавьте `google-play-developer-notifications@system.gserviceaccount.com` с ролью **Pub/Sub Publisher**.
   3. Create subscription → Delivery type **Push**, endpoint: `https://api.ailu.kz/v1/billing/webhooks/google?secret=<Billing__Google__WebhookSecret>`.
   4. Play Console → Monetize → Monetization setup → Real-time developer notifications: тема `projects/<id проекта>/topics/play-billing`, нажмите Send test notification.
6. Карточка: тексты из `store/listing-ru.md` и `store/listing-kk.md`, Data safety из `store/data-safety.md`, Content rating и Target audience из `store/age-rating.md` (16–17 и 18+), App access (вход для ревьюера), Ads: нет, Delete account URL: https://atukenov.github.io/ailu/legal/delete-account-ru.html, скриншоты из `store/screenshots/ru/android/`.
7. Личный аккаунт разработчика: до продакшна обязательно закрытое тестирование с 12 тестировщиками 14 дней подряд.

## 6. Firebase: push-уведомления

Push идут через Firebase Cloud Messaging (PR #29) на iOS и Android.

1. https://console.firebase.google.com → Add project (Google Analytics можно выключить).
2. **Add app → iOS**, бандл `kz.ailu.app` → скачать `GoogleService-Info.plist`. **Add app → Android**, пакет `kz.ailu.app` → скачать `google-services.json`. Положите оба файла в `app/` (они не попадают в git).
3. Apple Developer → Certificates, Identifiers & Profiles → Keys → + → **Apple Push Notifications service (APNs)** → скачать `.p8`. Firebase → Project settings → Cloud Messaging → Apple app configuration → загрузить этот ключ, указать Key ID и Team ID.
4. Передать файлы в EAS (без них продакшн-сборка специально падает):

   ```sh
   cd app
   npx eas-cli@latest env:create --name GOOGLE_SERVICES_JSON --type file --value ./google-services.json --visibility secret --environment production
   npx eas-cli@latest env:create --name GOOGLE_SERVICE_INFO_PLIST --type file --value ./GoogleService-Info.plist --visibility secret --environment production
   ```

5. Firebase → Project settings → Service accounts → **Generate new private key**. Весь JSON целиком вставьте в переменную Railway `Push__Firebase__ServiceAccountJson`. `Push__Firebase__ProjectId` задавать не нужно: он берётся из JSON.
6. Новая нативная сборка (раздел 7) и проверка на настоящих телефонах: разрешить уведомления, дождаться задания дня в 09:00 по времени пары.

## 7. Сборки приложения (Expo EAS)

1. Один раз: `cd app && npx eas-cli@latest login && npx eas-cli@latest init` (запишет `extra.eas.projectId` в `app.json`, закоммитьте).
2. Проверьте `app/eas.json`: `EXPO_PUBLIC_API_URL` указывает на ваш сервер, `ascAppId` вписан.
3. iOS: `npx eas-cli@latest build -p ios --profile production`, затем `npx eas-cli@latest submit -p ios --profile production`. Сертификаты EAS создаст сам, когда спросит Apple ID.
4. Android: `npx eas-cli@latest build -p android --profile production`. Первую AAB загрузите в Play Console вручную (Testing → Internal testing), дальше работает `npx eas-cli@latest submit -p android --profile production`.
5. Тест покупок: iOS через TestFlight (покупки идут в песочницу, сервер это понимает), Android через внутреннее тестирование и License testing в Play Console.
6. Мелкие правки без новой сборки (только JS): `npx eas-cli@latest update --channel production`. Всё, что меняет нативную часть (новые библиотеки, иконки, Firebase, виджет), требует новой сборки и ревью.

## 8. Проверка после выкладки

1. `https://api.ailu.kz/health` → `{"status":"ok"}`.
2. Вход по номеру ревьюера с кодом из `Auth__ReviewCode`: открывается демо-пара.
3. Покупка Plus в TestFlight → в профиле «Plus», у партнёра тоже.
4. Загрузка фото в воспоминание, затем перезапуск сервиса на Railway: фото на месте (значит, том подключён).
5. Промокод для подарка, проданного лично:

   ```sh
   curl -X POST https://api.ailu.kz/v1/admin/promo-codes \
     -H "X-Admin-Key: <Billing__AdminKey>" -H "Content-Type: application/json" \
     -d '{"code":"AIGERIM2027","tier":"premium","months":3,"days":0,"maxRedemptions":1,"note":"Подарок, оплата лично"}'
   ```

   Код: 4–32 буквы и цифры (дефисы и пробелы игнорируются). Срок: `months` до 24 или `days` до 366. Пара вводит его в «Профиль → Ввести код». Список кодов: `GET` на тот же адрес с тем же заголовком.

## 9. Vercel: сайт

Сейчас юридические документы и страница удаления аккаунта открываются с GitHub Pages (`docs/`, адрес https://atukenov.github.io/ailu/legal/), и для магазинов этого достаточно. Vercel нужен, когда появится свой домен и лендинг:

1. https://vercel.com → Add New → Project → импорт `atukenov/ailu`.
2. Root Directory: папка сайта (лендинга ещё нет; это отдельная задача).
3. Settings → Domains → `ailu.kz` и `www.ailu.kz`, DNS-записи из раздела 3.
4. После переезда документов поменяйте `extra.legalUrl` в `app/app.json`, ссылки в `store/listing-*.md` и адрес удаления аккаунта в Play Console.

## Что сколько стоит (ориентиры, проверьте на сайтах)

Apple Developer 99 $ в год, Google Play 25 $ один раз, Expo EAS: бесплатный тариф с ограниченным числом сборок в месяц. Railway (сервер, Postgres и тома в одном счёте, тариф Pro от 20 $ в месяц с включённым использованием), Vercel и Firebase берут помесячно по использованию; на старте это обычно 20–40 $ в месяц на всё вместе. Отдельной подписки на базу больше нет.
