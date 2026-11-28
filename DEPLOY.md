# Выкладка Ailu в продакшн

Пошагово: где что создать, что куда вписать и в каком порядке. Что ещё не построено и какие решения открыты, смотрите в [`store/CHECKLIST.md`](store/CHECKLIST.md).

Значки в шагах:
- **[сделать]** — ещё не сделано или не знаем, сделано ли.
- **[проверить]** — скорее всего уже заполнено (Лев заполнил большую часть App Store Connect 27.11.2026): откройте и сверьте со значениями здесь.
- **→ В коде после этого** — что поменять в репозитории, когда шаг сделан. Если поменять нечего, так и написано.

## Как всё устроено

```
Телефон (приложение из App Store / Google Play, собирается в Expo EAS)
   │ HTTPS
   ▼
ailu-production.up.railway.app ── Railway: сервер .NET в Docker (backend/Dockerfile)
   │            том Railway /data: фото пар
   ├── Railway: PostgreSQL (тот же проект, внутренняя сеть)
   ├── Firebase Cloud Messaging: push
   ├── App Store Server API и Google Play API: проверка покупок
   └── Anthropic API: AI-идеи для Premium (когда будет ключ)

ailu-weld.vercel.app ── Vercel: сайт из папки web/ (лендинг, юридические документы, поддержка, удаление аккаунта)
```

Адреса сейчас:

| Что | Адрес |
| --- | --- |
| Сервер (API) | https://ailu-production.up.railway.app (проверка: `/health`) |
| Сайт | https://ailu-weld.vercel.app (адрес от Vercel, подтверждён Левом 28.11.2026; если появится свой домен, см. раздел 9) |
| Политика конфиденциальности | `<сайт>/legal/privacy-ru.html`, `<сайт>/legal/privacy-kk.html` |
| Пользовательское соглашение | `<сайт>/legal/terms-ru.html`, `-kk.html` |
| Условия подписки | `<сайт>/legal/subscription-ru.html`, `-kk.html` |
| Удаление аккаунта | `<сайт>/legal/delete-account-ru.html`, `-kk.html` |
| Поддержка | `<сайт>/support-ru.html`, `<сайт>/support-kk.html` |
| Короткие ссылки | `<сайт>/privacy`, `/terms`, `/subscription`, `/delete-account`, `/support` (ведут на русские версии) |

## Маршрут: что делать по порядку

Идите сверху вниз и отмечайте. В каждом шаге написано, **где** он делается, а ссылка ведёт к подробностям клик за кликом. Шаги iOS (Б) и Android (В) не зависят друг от друга: после блока А можно идти по любому из них или по обоим сразу.

**Уже сделано:** сервер и база на Railway (разделы 1–2), сайт на Vercel (раздел 9), Apple Developer оплачен, приложение создано в App Store Connect и почти заполнено (4.2–4.7).

### А. Общая подготовка (один раз, для обеих платформ)

| # | Что | Где | Подробно |
| --- | --- | --- | --- |
| А1 | Node 22 на компьютере, затем `git pull` и `cd app && npm ci` | терминал | 7.1 |
| А2 | `npx eas-cli@latest login`, потом `npx eas-cli@latest init`, закоммитить `app/app.json` | терминал | 7.1 |
| А3 | Проект Firebase, в нём приложения iOS и Android (`kz.ailu.app`), скачать `GoogleService-Info.plist` и `google-services.json` в `app/` | console.firebase.google.com | 6, шаги 1–2 |
| А4 | Ключ APNs (.p8) создать у Apple и загрузить в Firebase | developer.apple.com → Keys, потом Firebase | 6, шаг 3 |
| А5 | Сервисный ключ Firebase вставить в Railway `Push__Firebase__ServiceAccountJson` | Firebase → Service accounts, потом Railway | 6, шаг 5 |
| А6 | Оба файла Firebase отправить в EAS (`env:create` ×2) | терминал | 7.2 |
| А7 | На Railway проверить `Auth__ReviewPhone` и `Auth__ReviewCode` (не пустые) | Railway → Variables | CHECKLIST, раздел 1 |

### Б. iOS: от идентификаторов до ревью

| # | Что | Где | Подробно |
| --- | --- | --- | --- |
| Б1 | Идентификаторы, сертификат, профили: `npx eas-cli@latest credentials -p ios` | терминал | 7.3 |
| Б2 | Проверить два App ID и группу `group.kz.ailu.app` | developer.apple.com → Identifiers | 4.1 |
| Б3 | **Сборка:** `npx eas-cli@latest build -p ios --profile production` (15–40 мин, ждать на expo.dev) | терминал | 7.5 |
| Б4 | **Куда сборку:** `npx eas-cli@latest submit -p ios --profile production --latest`. Сборка сама попадает в App Store Connect → TestFlight (через 15–30 мин) | терминал | 7.5 |
| Б5 | Ключ для проверки покупок и уведомления о покупках | App Store Connect → Users and Access → Integrations; App Information | 4.8, 4.9 |
| Б6 | TestFlight: поставить на iPhone, войти, купить Plus в песочнице, проверить профиль | TestFlight на телефоне | 4.11 |
| Б7 | Страница версии 1.0: тексты, скриншоты 6.9" (`store/screenshots/ru/ios/`), **Build → +** выбрать сборку из Б4, вход для ревьюера | App Store Connect → приложение → iOS App 1.0 | 4.10 |
| Б8 | Подписки: прикрепить к версии (Plus и Premium, месяц и год), у каждой скриншот для ревью | там же, блок In-App Purchases and Subscriptions | 4.7 |
| Б9 | **Add for Review → Submit** | App Store Connect | 4.10 |

### В. Android: от ключа до продакшна

| # | Что | Где | Подробно |
| --- | --- | --- | --- |
| В1 | Создать приложение и платёжный профиль | play.google.com/console | 5.1, 5.2 |
| В2 | Ключ подписи: `npx eas-cli@latest credentials -p android` | терминал | 7.4 |
| В3 | **Сборка:** `npx eas-cli@latest build -p android --profile production`, скачать `.aab` по ссылке в конце | терминал | 7.5 |
| В4 | **Куда сборку (первый раз вручную):** Play Console → Test and release → Testing → **Internal testing** → Create new release → загрузить `.aab` | Play Console | 5.3 |
| В5 | Сервисный аккаунт (для проверки покупок на сервере и для `eas submit` дальше) | Google Cloud + Play Console | 5.6 |
| В6 | Подписки (появляются только после загрузки сборки в В4) и уведомления о покупках | Play Console → Monetize with Play | 5.5, 5.7 |
| В7 | App content (анкеты) и карточка магазина | Play Console → Policy; Grow users → Store presence | 5.4, 5.8 |
| В8 | Поставить с внутреннего теста на телефон, проверить вход и покупку | Android-телефон | 5.3, шаг 5 |
| В9 | Закрытый тест (12 тестировщиков, 14 дней) → Apply for production → релиз в продакшн. Следующие сборки: `npx eas-cli@latest submit -p android --profile production --latest` | Play Console | 5.9 |

### Г. После одобрения

Проверка по разделу 8. Новая версия потом: поднять `version` в `app/app.json`, снова Б3–Б4 / В3 + `submit`, номер сборки EAS повышает сам.

Разделы ниже — справочник по каждому сервису; читать их подряд не нужно.

Секреты (ключи, пароли, JSON-файлы) не кладите в git. Они живут только в переменных Railway и в EAS.

---

## 1. Railway: проект и база данных

**Статус 27.11.2026:** сделано. Проект Railway с сервисами `Postgres` и `ailu`, том подключён. **[проверить]** только бэкапы (шаг 4).

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

**Статус 27.11.2026:** сервер работает на `ailu-production.up.railway.app`, все переменные из шага 4 созданы. **[проверить]**, что у секретов не пустые значения: `Auth__ReviewPhone` и `Auth__ReviewCode` (без них ревьюер не войдёт), `Billing__Apple__*` (раздел 4.8), `Billing__Google__*` (5.6, 5.7), `Push__Firebase__ServiceAccountJson` (раздел 6), `Ai__ApiKey` (пусто = заглушка AI).

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
5. **Networking** → **Generate Domain** (порт 8080). **Сделано 27.11.2026:** адрес `ailu-production.up.railway.app`. Свой домен можно добавить позже (раздел 3).
6. Deploy. В логах должно быть `Now listening on: http://[::]:...`, а `https://ailu-production.up.railway.app/health` должен ответить `{"status":"ok"}`.
7. Дальше каждый пуш в `main` выкладывается сам; новые миграции базы применяются при старте.

## 3. Свой домен (по желанию)

Сейчас всё работает на бесплатных адресах `ailu-production.up.railway.app` и `*.vercel.app`, магазинам этого достаточно. Если купите домен (например ailu.kz):

| Запись у регистратора | Имя | Значение |
| --- | --- | --- |
| CNAME | `api` | адрес, который покажет Railway: сервис `ailu` → Settings → Networking → **+ Custom Domain** → `api.ailu.kz` |
| A и CNAME | `@`, `www` | значения, которые покажет Vercel: проект → Settings → Domains → **Add** → `ailu.kz` |

**→ В коде после этого:**
- `app/eas.json` → `build.base.env.EXPO_PUBLIC_API_URL` = `https://api.ailu.kz` (нужна новая сборка; старые сборки продолжат ходить на railway.app, поэтому не удаляйте старый адрес в Railway).
- `app/app.json` → `expo.extra.legalUrl` = `https://ailu.kz/legal`.
- `web/config.json` → `siteUrl`, затем `python3 web/build.py`.
- Ссылки в `store/listing-ru.md`, `store/listing-kk.md`, `store/data-safety.md`, `store/age-rating.md` (поиск: `grep -rn "vercel.app" store`).
- Адреса вебхуков в App Store Connect (раздел 4.9) и Pub/Sub (раздел 5.7), ссылки на документы в обоих магазинах.

## 4. App Store Connect

Нужен оплаченный Apple Developer Program (99 $ в год). Всё ниже делается на https://appstoreconnect.apple.com, кроме 4.1 (https://developer.apple.com/account). Apple иногда переименовывает пункты меню: если пункта нет на месте, ищите по названию раздела в левой колонке страницы приложения.

### 4.1 Идентификаторы в Apple Developer [проверить]

EAS при первой сборке сам создаёт идентификаторы и профили, но проверить стоит, потому что у приложения есть виджет и общая группа данных.

1. https://developer.apple.com/account → **Certificates, IDs & Profiles** → **Identifiers**.
2. Должны быть два App ID:
   - `kz.ailu.app` (основное приложение). Откройте его → вкладка **Capabilities**: включены **App Groups** (с группой `group.kz.ailu.app`), **Push Notifications**, **In-App Purchase** (включён всегда).
   - `kz.ailu.app.widget` (виджет). Capabilities: **App Groups** с той же группой `group.kz.ailu.app`.
3. Справа вверху фильтр → **App Groups**: есть `group.kz.ailu.app`. Если нет: **+** → App Groups → Description `Ailu`, Identifier `group.kz.ailu.app` → Register, затем отметьте группу в обоих App ID (Edit рядом с App Groups).
4. Если App ID ещё нет, ничего не создавайте руками: выполните раздел 7.3 (`eas credentials -p ios`), EAS спросит вход в Apple ID и создаст всё сам. Потом вернитесь и проверьте пункты 2–3.

**→ В коде после этого:** ничего, если идентификаторы совпали. Если пришлось взять другой бандл (например, `kz.ailu.app` занят), поменяйте его везде: `app/app.json` → `ios.bundleIdentifier`, `android.package`, `expo-widgets.bundleIdentifier` и `groupIdentifier`; `backend/src/Ailu.Api/appsettings.json` → `Billing:Apple:BundleId` и `Billing:Google:PackageName`; `app/src/purchases.ts` → `packageNameAndroid`; Firebase (раздел 6, шаг 2).

### 4.2 Договоры, налоги и банк [проверить]

Без активного договора Paid Apps покупки не работают даже в TestFlight.

1. App Store Connect → **Business** (в старом меню **Agreements, Tax, and Banking**).
2. **Paid Apps** → статус должен быть **Active**. Если нет: **View and Agree to Terms**, затем заполните **Bank Account** (счёт, на который Apple будет платить; для KZT подойдёт счёт в тенге или в долларах) и **Tax Forms** (форма W-8BEN для физлица или W-8BEN-E для ТОО).
3. Статус меняется на Active через несколько часов, иногда до суток.

**→ В коде после этого:** ничего.

### 4.3 Карточка приложения: App Information [проверить]

Слева **General → App Information** (в старом меню просто **App Information**).

| Поле | Значение |
| --- | --- |
| Name | `Ailu — для двоих` |
| Subtitle | `Одно дело в день вместе` |
| Bundle ID | `kz.ailu.app` (меняется только до первой загрузки сборки) |
| SKU | `ailu` |
| Primary Language | Russian |
| Category | Primary: **Lifestyle**, Secondary: **Social Networking** |
| Content Rights | **No**, «does not contain, show, or access third-party content» (AI-идеи пишет модель по нашему заказу, это не чужой контент) |
| License Agreement | **Apple's Standard License Agreement** (наше соглашение и так указано в описании и на экране оплаты) |

Внизу **Apple ID** (число, например `6739...`): скопируйте.

**→ В коде после этого:** `app/eas.json` → `submit.production.ios.ascAppId` = это число (уже вписано: `6840164020`). Если название или подзаголовок отличаются от `store/listing-ru.md`, поправьте файл, чтобы он оставался правдой.

### 4.4 Возрастной рейтинг [проверить]

**App Information → Age Rating → Edit** (или **Set Up Age Ratings**). Ответы целиком в [`store/age-rating.md`](store/age-rating.md). Коротко:

1. **In-App Controls:** Parental Controls — No, Age Assurance — No.
2. **Capabilities:** Unrestricted Web Access — No, User-Generated Content — **Yes**, Messaging and Chat — No, Advertising — No.
3. **Mature Themes / Medical or Wellness / Sexuality or Nudity / Violence / Chance-Based Activities:** везде **None** или **No** (отметка настроения — не медицинская функция; если Apple спросит Health or Wellness Topics, отвечайте None).
4. На итоговом экране Apple покажет рейтинг (скорее всего 9+ или 13+). Включите **Age Rating Override** и выберите **16+**, чтобы рейтинг совпадал с соглашением. Save.

**→ В коде после этого:** ничего. Если итог отличается от 16+, впишите его в `store/age-rating.md`.

### 4.5 Цены и доступность [проверить]

Слева **Pricing and Availability**.

1. **Price:** Free (0). Деньги приходят только от подписок и покупок внутри приложения.
2. **Availability:** как минимум **Kazakhstan**. Остальные страны по желанию (интерфейс только на русском и казахском; цены подписок Apple пересчитает в местную валюту).
3. **App Distribution Methods:** Public.
4. **Tax Category:** оставьте по умолчанию (App Store software).

**→ В коде после этого:** ничего.

### 4.6 Конфиденциальность: App Privacy [проверить]

Слева **Trust & Safety → App Privacy** (или **App Privacy**).

1. **Privacy Policy URL:** `https://ailu-weld.vercel.app/legal/privacy-ru.html`. **User Privacy Choices URL** — оставьте пустым.
2. **Data Collection → Get Started / Edit** → «Yes, we collect data from this app».
3. Отметьте ровно эти типы (таблица с пояснениями в [`store/data-safety.md`](store/data-safety.md)):
   - Contact Info: **Name**, **Phone Number**, **Physical Address** (доставка фотокниги);
   - User Content: **Photos or Videos**, **Other User Content**;
   - Sensitive Info (настроение);
   - Identifiers: **User ID**, **Device ID** (push-токен);
   - Purchases: **Purchase History**.
4. Для **каждого** типа Apple задаст три вопроса. Ответы одинаковые:
   - Purpose: только **App Functionality**;
   - Linked to the user's identity: **Yes**;
   - Used for tracking: **No**.
5. **Publish**. Итоговая карточка должна показывать «Data Linked to You» и пустой блок «Data Used to Track You».

**→ В коде после этого:** ничего. Если появится аналитика или Sentry, добавить Diagnostics здесь и в `store/data-safety.md`.

### 4.7 Подписки и покупка [проверить]

Слева **Monetization → Subscriptions**. ID должны совпасть с сервером буква в букву (`backend/src/Ailu.Api/Infrastructure/Billing.cs`, `DefaultProducts`).

1. **Subscription Groups → +** → Reference Name `Ailu`. Внутри группы **App Store Localization → +** → Russian: Subscription Group Display Name `Ailu`, App Name Display Options: «Use App Name».
2. Четыре подписки (**+** рядом с Subscriptions в группе):

   | Reference Name | Product ID | Subscription Duration | Цена (Kazakhstan) | Уровень |
   | --- | --- | --- | --- | --- |
   | Premium Monthly | `kz.ailu.premium.monthly` | 1 Month | 2 490 ₸ | 1 |
   | Premium Yearly | `kz.ailu.premium.yearly` | 1 Year | 23 904 ₸ | 1 |
   | Plus Monthly | `kz.ailu.plus.monthly` | 1 Month | 1 490 ₸ | 2 |
   | Plus Yearly | `kz.ailu.plus.yearly` | 1 Year | 14 304 ₸ | 2 |

3. У каждой подписки заполните:
   - **Availability:** все страны из 4.5.
   - **Subscription Prices → Add Subscription Price** → Country `Kazakhstan`, выберите цену из таблицы (или ближайшую из сетки Apple) → Next → Apple сам предложит цены для других стран → Confirm.
   - **App Store Localization → +** → Russian: Display Name (`Ailu Plus`, `Ailu Plus · год`, `Ailu Premium`, `Ailu Premium · год`), Description до 45 символов (например «Три задания в день и 500 фото для двоих», «Всё из Plus, AI-идеи и 1 200 фото»).
   - **Review Information:** скриншот экрана оплаты (возьмите `app-screens/ui-refresh/08-paywall.png` из папки проекта, или снимите в TestFlight) и заметка: `Unlocks paid features for both partners of the couple. Test with the review account from App Review Information.`
   - **Introductory Offers / Promotional Offers:** **не создавайте**. 7 дней Premium даёт сам сервер, один раз на пару, без карты.
   - **Family Sharing:** выключено (подписка уже общая на пару).
4. Уровни (**Edit Subscription Levels** в группе): Premium на уровне 1, Plus на уровне 2. Тогда переход с Plus на Premium Apple считает повышением.
5. Слева **In-App Purchases → +** → Type **Non-Consumable**, Reference Name `Year Story`, Product ID `kz.ailu.story.year`, цена 1 990 ₸, локализация Russian: «Видео-история года» / «Фильм о вашем годе из ваших воспоминаний», Review Information со скриншотом экрана видео-истории (`app-screens/video-story/4-order.png`). Важно: пока нет сервера, который рендерит видео (`store/CHECKLIST.md`), не прикладывайте эту покупку к первой версии на ревью: ревьюер купит и не получит фильм, это отказ по правилу 2.1. В приложении кнопка заказа сама спрятана, пока на сервере нет рендера.
6. Новые подписки уходят на ревью вместе с версией приложения: на странице версии (4.10) в блоке **In-App Purchases and Subscriptions** нажмите **+** и отметьте все четыре подписки. Статус до этого «Ready to Submit».

**→ В коде после этого:**
- Если какой-то Product ID отличается от таблицы, поправьте `AppleProductId` в `backend/src/Ailu.Api/Infrastructure/Billing.cs` → `DefaultProducts()` и выложите сервер. Приложение берёт ID с сервера, сборку менять не нужно.
- Если цена в тенге получилась другой (сетка Apple), поправьте `PriceKzt` там же (`YearlyPrice` считает год как 12 месяцев минус 20%; если Apple дал другую годовую цену, впишите число вместо `YearlyPrice(...)`), цены на сайте (`web/content/*/index.html`, `web/content/*/subscription.md`, затем `python3 web/build.py`) и в `store/listing-*.md`. В приложении на экране оплаты показывается цена из магазина.

### 4.8 Ключ для проверки покупок [проверить]

Сервер проверяет покупки через App Store Server API.

1. **Users and Access → Integrations → In-App Purchase** → **Generate In-App Purchase Key** (если ключ уже есть и его `.p8` у вас сохранён, новый не нужен).
2. Name `ailu-server` → Generate → **Download** (скачать можно один раз).
3. На той же странице: **Issuer ID** (вверху) и **Key ID** (в строке ключа).
4. Railway → сервис `ailu` → Variables: `Billing__Apple__IssuerId`, `Billing__Apple__KeyId`, `Billing__Apple__PrivateKey` (всё содержимое `.p8`, со строками `-----BEGIN PRIVATE KEY-----` и `-----END PRIVATE KEY-----`). Переменные уже созданы 27.11; проверьте, что у них не пустые значения. Сервер перезапустится сам.

**→ В коде после этого:** ничего.

### 4.9 Уведомления о покупках [проверить]

**General → App Information** → блок **App Store Server Notifications**:

1. **Production Server URL** → Edit → `https://ailu-production.up.railway.app/v1/billing/webhooks/apple`, Version **Version 2**.
2. **Sandbox Server URL** → тот же адрес, Version 2.
3. Save.

**→ В коде после этого:** ничего. При смене домена (раздел 3) поменять адрес здесь.

### 4.10 Версия 1.0: тексты, скриншоты, сборка [проверить]

Слева под **iOS App** версия **1.0 Prepare for Submission**.

1. **Previews and Screenshots:**
   - Вкладка **iPhone 6.9" Display**: 3–10 скриншотов 1320 × 2868 (или 1290 × 2796), PNG или JPG без прозрачности. Готовые: `store/screenshots/ru/ios/` (6 штук, новый интерфейс). Меньшие размеры iPhone Apple возьмёт из 6.9" автоматически.
   - iPad не нужен: в `app/app.json` стоит `supportsTablet: false`.
2. **Promotional Text, Description, Keywords:** из [`store/listing-ru.md`](store/listing-ru.md) (скопируйте как есть, лимиты проверены скриптом `node store/check-listing.mjs`).
3. **Support URL:** `https://ailu-weld.vercel.app/support-ru.html`. **Marketing URL:** `https://ailu-weld.vercel.app/`.
4. **Version:** `1.0.0` (должна совпасть с `version` в `app/app.json`). **Copyright:** `2026 <ваше ТОО или ФИО>`.
5. **Build:** появится через 15–30 минут после `eas submit` (раздел 7). Нажмите **+** (Add Build) и выберите сборку. Вопрос про шифрование (**Export Compliance**) не появится: в `app/app.json` уже стоит `usesNonExemptEncryption: false` (приложение использует только HTTPS). Если всё же спросит: «None of the algorithms mentioned above».
6. **In-App Purchases and Subscriptions:** + → отметьте 4 подписки (4.7, шаг 6).
7. **App Review Information:**
   - **Sign-in required:** включено. **User name:** `+77000000000` (значение `Auth__ReviewPhone` на Railway). **Password:** шесть цифр из `Auth__ReviewCode`.
   - **Contact Information:** имя, телефон в формате `+7...` и e-mail, по которым Apple сможет с вами связаться.
   - **Notes** (по-английски, скопируйте и подставьте номер и код):

     ```
     Ailu is a couples app: one shared task and one question a day, shared memories and goals.
     Sign-in is by phone number with a one-time code. Review account:
     1) Enter phone +7 700 000 00 00 and tap «Получить код».
     2) Enter code <Auth__ReviewCode>. SMS is not sent for this number; the code is fixed.
     The account is already paired with a demo partner who answers and confirms tasks automatically.
     Subscriptions (Plus, Premium) are sold only through In-App Purchase: Profile → «Подписка» → «Выбрать тариф», or tap any locked feature.
     One subscription unlocks paid features for both partners of the couple. Restore Purchases: Profile → «Подписка» → «Восстановить покупки».
     The printed photo book is a physical good (guideline 3.1.3(e)): the order is placed in the app and paid separately.
     AI date ideas (Premium) are generated by Anthropic's API from anonymous couple preferences only.
     Account deletion: Profile → «Удалить аккаунт».
     The interface is in Russian and Kazakh.
     ```

   - **Attachment:** не нужен.
8. **App Store Version Release:** **Manually release this version** (выпустите сами после одобрения) — удобнее для первого запуска. **Phased Release:** можно включить, обновление раскатится на 7 дней.
9. **Save**, затем **Add for Review** → **Submit to App Review**. Ревью обычно 1–2 дня.

**→ В коде после этого:**
- Для каждой следующей версии: поднимите `version` в `app/app.json` (например `1.0.1`) и создайте в App Store Connect версию с тем же номером (**+** рядом с iOS App). Номер сборки (`buildNumber`) EAS повышает сам (`appVersionSource: remote`, `autoIncrement`), его не трогайте.
- Если ревьюеру не удалось войти, проверьте, что `Auth__ReviewPhone` и `Auth__ReviewCode` на Railway не пустые: без них вход ревьюера выключен.

### 4.11 Тест покупок до ревью [сделать]

1. Соберите и отправьте сборку (раздел 7), откройте её через **TestFlight** на своём iPhone.
2. Войдите своим номером (или номером ревьюера), откройте экран оплаты, купите Plus. В TestFlight деньги не списываются: покупка идёт в песочницу, подписка на месяц длится несколько минут и продлевается сама.
3. Проверьте: в профиле «Plus» у вас и у партнёра; в логах Railway есть запрос `POST /v1/billing/webhooks/apple`.

**→ В коде после этого:** ничего, если всё прошло. Если покупка не находит товар: Paid Apps не Active (4.2) или Product ID не совпал (4.7).

## 5. Google Play Console

Нужен аккаунт разработчика (25 $ один раз): https://play.google.com/console. Всё ниже — в Play Console, кроме 5.6 и 5.7 (Google Cloud). Play Console сам ведёт по шагам: на странице приложения **Dashboard → Set up your app** список задач; ниже они в том же порядке.

Важное ограничение: подписки и покупки (5.5) можно создать только **после** загрузки первой сборки с Google Play Billing (в нашей она есть через `expo-iap`). Поэтому порядок: 5.1 → 5.2 → 5.3 (первая сборка во внутреннее тестирование) → 5.4 → 5.5 → 5.6 → 5.7 → 5.8.

### 5.1 Создать приложение [сделать]

1. **Home → Create app**.
2. App name `Ailu — для двоих`, Default language **Russian – ru-RU**, App or game **App**, Free or paid **Free**.
3. Отметьте обе декларации (Developer Program Policies, US export laws) → **Create app**.
4. Пакет `kz.ailu.app` закрепится первой загруженной сборкой (5.3) и потом не меняется.

**→ В коде после этого:** ничего.

### 5.2 Платёжный профиль [сделать]

Без него нельзя создать платные товары.

1. **Setup → Payments profile** (или Settings → Payments profile) → Create payments profile.
2. Тип (физлицо или организация), страна Kazakhstan, имя, адрес, контакт. Банковский счёт для выплат добавляется там же (Payment methods → Add payment method).

**→ В коде после этого:** ничего.

### 5.3 Первая сборка во внутреннее тестирование [сделать]

1. Соберите AAB: `cd app && npx eas-cli@latest build -p android --profile production` (раздел 7). В конце EAS даст ссылку на файл `.aab`, скачайте его.
2. Play Console → **Test and release → Testing → Internal testing** → **Create new release**.
3. **Play App Signing:** нажмите **Use Google-generated key** (рекомендуется; EAS подписывает загрузочным ключом, Google переподписывает).
4. **App bundles → Upload** → выберите `.aab`. Release name оставьте как есть, Release notes: `Первая сборка`.
5. **Next → Save and publish**. Вкладка **Testers** → Create email list → добавьте свою Gmail-почту → скопируйте ссылку «Join on the web» и откройте её на Android-телефоне.
6. Дальше сборки можно отправлять командой `npx eas-cli@latest submit -p android --profile production` (нужен ключ сервисного аккаунта, 5.6).

**→ В коде после этого:** ничего. `versionCode` EAS повышает сам (`appVersionSource: remote`, `autoIncrement`). Если когда-нибудь подключите Firebase Phone Auth: **Test and release → Setup → App signing** → скопируйте SHA-1 и SHA-256 «App signing key certificate» в Firebase → Project settings → Android app → Add fingerprint.

### 5.4 Раздел App content (Policy → App content) [сделать]

Каждая карточка ниже — отдельная задача на Dashboard. Ответы:

1. **Privacy policy:** `https://ailu-weld.vercel.app/legal/privacy-ru.html` → Save.
2. **App access:** «All or some functionality in my app is restricted» → **Add instructions**: Name `Review account`, Username `+77000000000`, Password `<Auth__ReviewCode>`, Any other information:
   `Enter the phone, tap «Получить код», enter the code. No SMS is sent for this number. The account is already paired with a demo partner. Account deletion: Profile → «Удалить аккаунт».` → Save.
3. **Ads:** «No, my app does not contain ads».
4. **Content rating:** Start questionnaire → e-mail → категория **All Other App Types** → ответы из [`store/age-rating.md`](store/age-rating.md) (насилие, секс, язык, вещества, азартные игры: No; Users interact: **Yes**; Shares location: No; Digital purchases: **Yes**) → Save → Submit.
5. **Target audience and content:** возраст **16–17** и **18 and over** → «Could your store listing unintentionally appeal to children?» **No**.
6. **News apps:** No. **COVID-19 contact tracing:** No. **Government apps:** No. **Financial features:** «My app doesn't provide any financial features». **Health apps:** «My app doesn't have any health features».
7. **Advertising ID:** «Does your app use advertising ID?» **No**. В сборке разрешение `AD_ID` заблокировано (`app/app.json` → `android.blockedPermissions`), так что ответ совпадает с манифестом.
8. **Data safety:** таблица целиком в [`store/data-safety.md`](store/data-safety.md):
   - Does your app collect or share any of the required user data types? **Yes**.
   - Is all of the user data collected by your app encrypted in transit? **Yes**.
   - Which account creation methods: **Username and other authentication** (номер телефона и код). Delete account URL: `https://ailu-weld.vercel.app/legal/delete-account-ru.html`.
   - Do you provide a way for users to request that some or all of their data is deleted? **Yes**.
   - Типы данных: Personal info (Name, Phone number, Address, User IDs), Photos, Messages → Other in-app messages, App activity (Other user-generated content, Other actions), Device or other IDs, Financial info → Purchase history. Для каждого: Collected **Yes**, Shared **No**, Processed ephemerally **No**, Required/Optional как в таблице, Purposes: **App functionality** (для телефона ещё **Account management**).
   - **Preview** → **Submit**.
9. **Account deletion** (если спросят отдельно): тот же адрес `.../legal/delete-account-ru.html`.

**→ В коде после этого:** ничего.

### 5.5 Подписки и покупка (Monetize with Play) [сделать после 5.3]

ID должны совпасть с `backend/src/Ailu.Api/Infrastructure/Billing.cs` (`GoogleProductId`, `GoogleBasePlanId`).

1. **Monetize with Play → Products → Subscriptions → Create subscription**:
   - Product ID `plus`, Name `Ailu Plus` → Create.
   - Вверху **Edit subscription details**: Benefits (до 4 строк): «Три задания в день», «До 500 фото», «Цели без ограничений», «Вся история пары». Description: «Plus для вас обоих». Save.
   - **Add base plan** → Base plan ID `monthly`, Type **Auto-renewing**, Billing period **1 Month**, Grace period по умолчанию, Resubscribe **Allow** → **Set prices** → Kazakhstan `1 490 KZT` (остальные страны пересчитает Google) → Save → **Activate**.
   - Ещё **Add base plan** → `yearly`, 1 Year, Kazakhstan `14 304 KZT` → Save → **Activate**.
   - Offers (пробные периоды, скидки) **не создавайте**: 7 дней Premium даёт сервер.
2. То же для Product ID `premium`, Name `Ailu Premium`: base plans `monthly` 2 490 KZT и `yearly` 23 904 KZT, оба **Activate**.
3. **Products → One-time products → Create one-time product**: Product ID `year_story`, Name `Видео-история года`, Description «Фильм о вашем годе из ваших воспоминаний», цена 1 990 KZT → Save → **Activate**. Как и в App Store, не активируйте её, пока нет сервера рендера видео.
4. **Setup → License testing** (или Settings → License testing): добавьте свою Gmail-почту, License response **RESPOND_NORMALLY**. С этим аккаунтом покупки бесплатные и тестовые.

**→ В коде после этого:** если ID подписки или базового плана отличается, поправьте `GoogleProductId`/`GoogleBasePlanId` в `Billing.cs` и выложите сервер. Если цена другая, поправьте `PriceKzt` там же и цены на сайте (`web/content`, затем `python3 web/build.py`).

### 5.6 Сервисный аккаунт: проверка покупок и `eas submit` [проверить]

Переменная `Billing__Google__ServiceAccountJson` на Railway уже создана 27.11; проверьте, что в ней ключ именно этого аккаунта и что у аккаунта есть права в Play Console (шаг 3).

1. https://console.cloud.google.com → выберите проект (можно тот же, что у Firebase) → **APIs & Services → Library** → **Google Play Android Developer API** → Enable.
2. **IAM & Admin → Service Accounts → Create service account** → имя `ailu-play` → Done → откройте его → **Keys → Add key → Create new key → JSON** → скачается файл.
3. Play Console → **Users and permissions → Invite new users** → e-mail сервисного аккаунта (вида `ailu-play@<проект>.iam.gserviceaccount.com`) → вкладка **App permissions → Add app** → Ailu → отметьте **View app information and download bulk reports**, **View financial data, orders, and cancellation survey responses**, **Manage orders and subscriptions**, **Release to testing tracks**, **Release to production, exclude devices, and use Play App Signing** → Apply → **Invite user**.
4. Railway → `ailu` → Variables → `Billing__Google__ServiceAccountJson` = содержимое JSON целиком одной строкой.
5. Тот же файл положите как `app/google-service-account.json` (в git он не попадёт).

**→ В коде после этого:** ничего: `app/eas.json` → `submit.production.android.serviceAccountKeyPath` уже указывает на `./google-service-account.json` (шаг 5). Права в Play Console начинают действовать до суток; если сервер отвечает 401/403 при проверке покупки, подождите.

### 5.7 Уведомления о покупках (RTDN) [сделать]

1. Google Cloud (тот же проект) → **Pub/Sub → Topics → Create topic** → ID `play-billing` → Create.
2. Откройте тему → **Permissions** (справа) → **Add principal** → `google-play-developer-notifications@system.gserviceaccount.com`, роль **Pub/Sub Publisher** → Save.
3. В теме → **Create subscription** → ID `play-billing-push`, Delivery type **Push**, Endpoint URL:
   `https://ailu-production.up.railway.app/v1/billing/webhooks/google?secret=<значение Billing__Google__WebhookSecret с Railway>` → Create.
4. Play Console → **Monetize with Play → Monetization setup** → **Real-time developer notifications**: Topic name `projects/<ID проекта Google Cloud>/topics/play-billing`, Notification content **Subscriptions, voided purchases, and all one-time products** → Save → **Send test notification**. В логах Railway должен появиться `POST /v1/billing/webhooks/google` с ответом 200.

**→ В коде после этого:** ничего. При смене домена поменять Endpoint URL в подписке Pub/Sub.

### 5.8 Карточка магазина (Grow users → Store presence) [сделать]

1. **Main store listing** (русский по умолчанию):
   - App name `Ailu — для двоих` (30), Short description и Full description из [`store/listing-ru.md`](store/listing-ru.md).
   - **App icon:** 512 × 512 PNG, готовый файл `brand/png/play-store-icon-512.png`.
   - **Feature graphic:** 1024 × 500, готовый файл `store/graphics/play-feature-graphic.png` (исходник рядом, `.html`).
   - **Phone screenshots:** 2–8 штук, 1080 × 1920, из `store/screenshots/ru/android/`.
   - Tablet screenshots не нужны.
2. **Translations → Add translation → Kazakh – kk** → тексты из [`store/listing-kk.md`](store/listing-kk.md). Скриншоты можно оставить русские.
3. **Store settings:** App category **Lifestyle**, Tags до 5 (Relationships, Lifestyle…), Store listing contact details: e-mail поддержки (обязательно), сайт `https://ailu-weld.vercel.app/`, телефон по желанию.

**→ В коде после этого:** ничего.

### 5.9 Закрытое тестирование и выход в продакшн [сделать]

Для нового **личного** аккаунта Google требует закрытое тестирование: не меньше 12 тестировщиков, которые не удаляли приложение 14 дней подряд. Для аккаунта организации этого требования нет.

1. **Testing → Closed testing → Create track** (или используйте трек Alpha) → Countries: Kazakhstan → Testers: e-mail-список из 12+ Gmail-адресов (друзья, пары) → Create release → **Add from library** (та же сборка, что во внутреннем тесте) → Save → Send for review.
2. Разошлите тестировщикам ссылку «Join on the web». Через 14 дней на Dashboard станет доступна кнопка **Apply for production**, заполните короткую анкету о тестировании.
3. **Production → Countries/regions** → Add → Kazakhstan (и другие по желанию) → **Create new release** → Add from library → Release notes на русском и казахском → **Next → Save → Send for review** (проверка от нескольких часов до 7 дней).
4. После одобрения: если в **Publishing overview** включён Managed publishing, нажмите **Publish**; иначе выйдет само.

**→ В коде после этого:**
- Когда трек продакшна открыт: `app/eas.json` → `submit.production.android.track` = `"production"` (сейчас `"internal"`), `releaseStatus` = `"completed"` (или `"draft"`, если хотите нажимать Publish вручную).
- Для следующих версий поднимите `version` в `app/app.json`; `versionCode` EAS увеличит сам.

## 6. Firebase: push-уведомления

Push идут через Firebase Cloud Messaging (PR #29) на iOS и Android.

1. https://console.firebase.google.com → Add project (Google Analytics можно выключить).
2. **Add app → iOS**, бандл `kz.ailu.app` → скачать `GoogleService-Info.plist`. **Add app → Android**, пакет `kz.ailu.app` → скачать `google-services.json`. Положите оба файла в `app/` (они не попадают в git).
3. Apple Developer → Certificates, Identifiers & Profiles → Keys → + → **Apple Push Notifications service (APNs)** → скачать `.p8`. Firebase → Project settings → Cloud Messaging → Apple app configuration → загрузить этот ключ, указать Key ID и Team ID.
4. Передать оба файла в EAS: раздел 7.2 (без них продакшн-сборка специально падает).
5. Firebase → Project settings → Service accounts → **Generate new private key**. Весь JSON целиком вставьте в переменную Railway `Push__Firebase__ServiceAccountJson`. `Push__Firebase__ProjectId` задавать не нужно: он берётся из JSON.
6. Новая нативная сборка (раздел 7) и проверка на настоящих телефонах: разрешить уведомления, дождаться задания дня в 09:00 по времени пары.

## 7. Expo EAS: проект, идентификаторы Apple, ключи и первые сборки

Всё делается в терминале на вашем компьютере (нужны пароль Apple ID и код 2FA, поэтому без вас не запустить). Нужны Node 22 и папка репозитория. Mac не нужен: сборки идут в облаке EAS. Команды по порядку; под каждой написано, что она спросит и что ответить. Если что-то пошло не так, пришлите вывод команды в тред.

Что уже настроено в репозитории и менять не нужно:

| Что | Значение | Где |
| --- | --- | --- |
| iOS bundle id, Android package | `kz.ailu.app` | `app/app.json` |
| Виджет iOS (отдельный App ID) | `kz.ailu.app.widget`, цель `ExpoWidgetsTarget` | `app/app.json` → плагин `expo-widgets` (он сам сообщает EAS о расширении) |
| Общая группа данных | `group.kz.ailu.app` (у приложения и у виджета) | там же |
| Push | entitlement `aps-environment` от `expo-notifications`, сами push через Firebase (раздел 6) | `app/app.json`, `app/app.config.js` |
| Профили сборки | `development` (dev client), `preview` (APK и ad hoc iOS для своих телефонов), `production` (магазины) | `app/eas.json`, у каждого своё окружение EAS (`environment`) |
| Адрес сервера | `EXPO_PUBLIC_API_URL` = `https://ailu-production.up.railway.app` (в `development` адрес сервера в вашей сети) | `app/eas.json` |
| Номера сборок | хранит и повышает EAS (`appVersionSource: remote`, `autoIncrement`) | `app/eas.json` |
| Apple ID приложения для `eas submit` | `6840164020` | `app/eas.json` → `submit.production.ios.ascAppId` |

### 7.1 Вход и проект EAS [сделать]

```sh
cd app
npm ci
npx eas-cli@latest login
```

Спросит **Email or username** и **Password** аккаунта Expo (если аккаунта нет: https://expo.dev/signup, бесплатно). Проверка: `npx eas-cli@latest whoami` печатает ваш логин.

```sh
npx eas-cli@latest init
```

- **Would you like to create a project for @<логин>/ailu?** → `Y`.
- Если спросит про организацию (**Which account should own this project?**), выберите свой личный аккаунт или организацию, которой будет принадлежать приложение.

Команда допишет в `app/app.json` поля `owner` и `extra.eas.projectId`. Если вместо этого напишет «Cannot automatically write to dynamic config», впишите их вручную: в `expo` добавьте `"owner": "<логин>"`, а в `expo.extra` добавьте `"eas": { "projectId": "<uuid из вывода>" }`.

`eas build:configure` запускать не нужно: `app/eas.json` уже есть.

**→ В коде после этого:** закоммитьте `app/app.json` с `owner` и `projectId` (или пришлите `projectId` в тред, Claude закоммитит).

### 7.2 Файлы Firebase в EAS [сделать после раздела 6, шаги 1–3]

Без них продакшн-сборка специально падает (в `preview` и `development` соберётся, но без push). Из папки `app/`, файлы лежат рядом:

```sh
npx eas-cli@latest env:create --name GOOGLE_SERVICES_JSON --type file --value ./google-services.json --visibility secret --environment production --environment preview --environment development
npx eas-cli@latest env:create --name GOOGLE_SERVICE_INFO_PLIST --type file --value ./GoogleService-Info.plist --visibility secret --environment production --environment preview --environment development
npx eas-cli@latest env:list --environment production
```

Последняя команда должна показать обе переменные. `EXPO_PUBLIC_API_URL` в EAS создавать не нужно, он задан в `eas.json`.

### 7.3 Идентификаторы, сертификат и профили iOS [сделать]

Это и есть «EAS создаёт идентификаторы»: без сборки, одной командой.

```sh
npx eas-cli@latest credentials -p ios
```

1. **Which build profile do you want to configure?** → `production`.
2. **Do you want to log in to your Apple account?** → `Y`. **Apple ID** → почта аккаунта разработчика, затем пароль и **6-значный код 2FA** с вашего телефона. Если команд несколько, выберите ту, на которую оплачен Developer Program.
3. В меню выберите **Build Credentials: Manage everything needed to build your project** → **Set up all the required credentials to build your project**.
4. EAS пройдёт по двум целям: `Ailu` (`kz.ailu.app`) и `ExpoWidgetsTarget` (`kz.ailu.app.widget`). Для каждой:
   - создаст App ID, если его нет, и включит нужные возможности (App Groups с `group.kz.ailu.app`, Push Notifications);
   - **Generate a new Apple Distribution Certificate?** → `Y` (спросит один раз, сертификат общий; если предложит **Reuse** существующий, тоже `Y`);
   - **Generate a new Apple Provisioning Profile?** → `Y`.
5. Если спросит **Would you like to set up Push Notifications for your project?** (ключ push для сервиса Expo) → `N`. Push идут через Firebase, ключ APNs создаётся вручную и загружается в Firebase (раздел 6, шаг 3). У Apple не больше двух ключей APNs на команду, лишний не нужен.
6. Выйдите из меню (**Exit**).

Потом проверьте в Apple Developer по разделу 4.1 (два App ID, у обоих App Groups с `group.kz.ailu.app`, у основного Push Notifications). Если какой-то возможности нет, ничего страшного: первая сборка (7.5) сверяет возможности с приложением и включает недостающие сама. Если не включила, отметьте вручную по 4.1, шаг 3, и запустите `npx eas-cli@latest credentials -p ios` ещё раз, чтобы профили пересоздались (**Build Credentials** → **Provisioning Profile** → **Delete**, потом снова **Set up all**).

Для `preview` на iOS (установка на свои телефоны без TestFlight) телефоны нужно зарегистрировать: `npx eas-cli@latest device:create` → **Website** → откройте ссылку или QR на каждом телефоне и установите профиль. Можно пропустить и тестировать через TestFlight.

### 7.4 Ключ подписи Android [сделать]

```sh
npx eas-cli@latest credentials -p android
```

1. **Which build profile do you want to configure?** → `production`.
2. **Keystore: Manage everything needed to build your project** → **Set up a new keystore** → **Generate a new Android Keystore?** → `Y`.
3. Выход (**Exit**).

Ключ хранится в EAS, это ключ загрузки. Google переподписывает приложение своим ключом (Play App Signing, раздел 5.3). Резервную копию можно скачать там же: **Download existing keystore**; храните её вне git.

### 7.5 Первые сборки [сделать]

iOS:

```sh
npx eas-cli@latest build -p ios --profile production
```

- Если спросит про вход в Apple → `Y` и тот же Apple ID (сессия обычно уже сохранена после 7.3).
- **Reuse / Generate** для сертификата и профилей → `Y` (всё уже создано в 7.3, EAS просто возьмёт).
- Про push-ключ Expo → `N` (как в 7.3).
- Сборка идёт 15–40 минут, ссылку на неё EAS печатает сразу (можно закрыть терминал и смотреть на https://expo.dev).

Отправка в App Store Connect (TestFlight):

```sh
npx eas-cli@latest submit -p ios --profile production --latest
```

Пароль не нужен: `app/eas.json` → `submit.production.ios` указывает на ключ App Store Connect API `HGAAFGK229`, файл которого лежит вне репозитория в `~/Desktop/Dev/ailu-keys/AuthKey_HGAAFGK229.p8` (путь `../../ailu-keys/...` считается от папки `app/`). На другом компьютере положите ключ туда же. Через 15–30 минут сборка появится в App Store Connect → TestFlight.

Android:

```sh
npx eas-cli@latest build -p android --profile production
```

Ключ возьмётся из 7.4. В конце будет ссылка на `.aab`: первую загрузите в Play Console вручную (раздел 5.3). Дальше `npx eas-cli@latest submit -p android --profile production --latest` (нужен ключ сервисного аккаунта, 5.6).

Если при первой сборке спросит про номер версии (**Initialize the remote version with ...?**) → `Y`.

### 7.6 Дальше

- Тест покупок: iOS через TestFlight (покупки идут в песочницу, сервер это понимает), Android через внутреннее тестирование и License testing в Play Console.
- Сборка для разработки на своём телефоне: `npx eas-cli@latest build -p ios --profile development` (или `-p android`). Для неё нужен сервер в вашей сети: адрес в `app/eas.json` → `build.development.env.EXPO_PUBLIC_API_URL`.
- Обновления «по воздуху» (`eas update`) пока не подключены: в приложении нет `expo-updates`. Сейчас каждое изменение идёт новой сборкой. Подключить можно позже (`npx eas-cli@latest update:configure`, затем новая сборка).

## 8. Проверка после выкладки

1. `https://ailu-production.up.railway.app/health` → `{"status":"ok"}`.
2. Вход по номеру ревьюера с кодом из `Auth__ReviewCode`: открывается демо-пара.
3. Покупка Plus в TestFlight → в профиле «Plus», у партнёра тоже.
4. Загрузка фото в воспоминание, затем перезапуск сервиса на Railway: фото на месте (значит, том подключён).
5. Промокод для подарка, проданного лично:

   ```sh
   curl -X POST https://ailu-production.up.railway.app/v1/admin/promo-codes \
     -H "X-Admin-Key: <Billing__AdminKey>" -H "Content-Type: application/json" \
     -d '{"code":"AIGERIM2027","tier":"premium","months":3,"days":0,"maxRedemptions":1,"note":"Подарок, оплата лично"}'
   ```

   Код: 4–32 буквы и цифры (дефисы и пробелы игнорируются). Срок: `months` до 24 или `days` до 366. Пара вводит его в «Профиль → Ввести код». Список кодов: `GET` на тот же адрес с тем же заголовком.

## 9. Vercel: сайт (лендинг и документы)

Сайт — папка `web/`: лендинг на русском (`/`) и казахском (`/kk/`), политика конфиденциальности, соглашение, условия подписки, удаление аккаунта и поддержка, всё на двух языках. Готовые страницы лежат в `web/public` и закоммичены, поэтому Vercel ничего не собирает.

### 9.1 Заполнить реквизиты [сделать]

1. Откройте `web/config.json` и заполните пустые поля: `operator` (ТОО или ИП, как в документах, например `ТОО «Ailu»`), `operatorId` (БИН или ИИН), `address`, `dataOfficer` (ФИО ответственного за данные), `supportEmail`, `privacyEmail` (можно тот же), `phone`, `smsProvider` (когда выберете). Пустые поля видны на страницах жёлтой меткой «[заполнить: ...]».
2. Пересоберите: `pip install markdown && python3 web/build.py`. Скрипт напишет, какие поля ещё пустые.
3. Когда юрист проверит тексты, поставьте `"showDraftNotice": false` и пересоберите: уберётся плашка «Черновик».
4. Закоммитьте `web/` и запушьте в `main`.

### 9.2 Проект на Vercel [сделать]

1. https://vercel.com → **Add New… → Project** → Import Git Repository → `atukenov/ailu` → Import.
2. **Project Name:** `ailu`. Vercel выдал адрес `ailu-weld.vercel.app` (имя `ailu` было занято); он уже прописан в приложении, на сайте и в `store/`.
3. **Framework Preset:** Other. **Root Directory:** Edit → `web` → Continue.
4. **Build and Output Settings:** Build Command — включите Override и оставьте пустым; Output Directory — `public` (то же написано в `web/vercel.json`). Install Command не нужен.
5. **Deploy**. Откройте адрес из Vercel и проверьте: `/`, `/kk/`, `/legal/privacy-ru.html`, `/legal/delete-account-kk.html`, `/support-ru.html`, `/privacy` (короткая ссылка).
6. Каждый пуш в `main`, который меняет `web/`, выкладывается сам. Чтобы Vercel не пересобирал сайт на изменения в `app/` и `backend/`: Project → Settings → Git → **Ignored Build Step** → `git diff --quiet HEAD^ HEAD -- .`.

**→ В коде после этого** (уже сделано для `https://ailu-weld.vercel.app`; повторить, только если адрес сменится, например на свой домен):
- `app/app.json` → `expo.extra.legalUrl` = `https://<ваш адрес>/legal` (приложение открывает отсюда политику и соглашение; нужна новая сборка или `eas update`).
- `web/config.json` → `siteUrl`, затем `python3 web/build.py`.
- Адреса в `store/listing-ru.md`, `store/listing-kk.md`, `store/data-safety.md`, `store/age-rating.md` и в этом файле: `grep -rln "ailu-weld.vercel.app" . | xargs sed -i 's#ailu-weld.vercel.app#<ваш адрес>#g'`.

CORS на сервере менять не нужно: сайт не обращается к API, а мобильному приложению CORS не нужен (`Cors:Origins` остаётся пустым).

## Что сколько стоит (ориентиры, проверьте на сайтах)

Apple Developer 99 $ в год, Google Play 25 $ один раз, Expo EAS: бесплатный тариф с ограниченным числом сборок в месяц. Railway (сервер, Postgres и тома в одном счёте, тариф Pro от 20 $ в месяц с включённым использованием), Vercel и Firebase берут помесячно по использованию; на старте это обычно 20–40 $ в месяц на всё вместе. Отдельной подписки на базу больше нет.
