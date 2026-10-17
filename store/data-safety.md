# Ответы о данных · App Store «App Privacy» и Google Play «Data safety»

Основа: что приложение и сервер реально хранят (`backend/src/Ailu.Api/Data/Entities.cs`) и раздел 2 политики (`docs/legal/privacy-ru.md`). Трекинга, рекламы и сторонней аналитики в приложении нет.

## App Store Connect · App Privacy

**Do you or your third-party partners collect data from this app?** Yes.

| Тип данных (Apple) | Собираем | Связано с личностью | Трекинг | Цель |
| --- | --- | --- | --- | --- |
| Contact Info → Name | да | да | нет | App Functionality |
| Contact Info → Phone Number | да | да | нет | App Functionality |
| User Content → Photos or Videos | да | да | нет | App Functionality |
| User Content → Other User Content (ответы, воспоминания, цели, места) | да | да | нет | App Functionality |
| Sensitive Info (настроение) | да | да | нет | App Functionality |
| Identifiers → User ID | да | да | нет | App Functionality |
| Identifiers → Device ID (push-токен) | да | да | нет | App Functionality |
| Purchases → Purchase History | да, с Фазы 2 | да | нет | App Functionality |
| Diagnostics → Crash Data, Performance Data | нет | | | Своих отчётов о сбоях пока нет; если подключим Sentry и т. п., добавить |
| Location, Contacts, Browsing/Search History, Health, Financial Info, Usage Data | нет | | | |

Настроение отнесено к Sensitive Info осторожно: Apple определяет её через этническую принадлежность, взгляды и т. д., а настроение ближе к ним, чем к Health. Юрист может решить иначе.

**Tracking:** No. `NSUserTrackingUsageDescription` и App Tracking Transparency не нужны.

## Google Play · Data safety

- **Does your app collect or share any of the required user data types?** Yes.
- **Is all of the user data collected by your app encrypted in transit?** Yes (HTTPS).
- **Do you provide a way for users to request that their data is deleted?** Yes — в приложении и по ссылке на странице удаления (нужна до отправки, см. чек-лист).

| Категория → тип | Collected | Shared | Обязательно? | Цель |
| --- | --- | --- | --- | --- |
| Personal info → Name | да | нет | обязательно | App functionality, Account management |
| Personal info → Phone number | да | нет* | обязательно | Account management |
| Personal info → User IDs | да | нет | обязательно | App functionality |
| Photos and videos → Photos | да | нет | по желанию | App functionality |
| Messages → Other in-app messages (ответы на вопрос дня) | да | нет | по желанию | App functionality |
| App activity → Other user-generated content (воспоминания, цели) | да | нет | по желанию | App functionality |
| App activity → Other actions (задания, серия) | да | нет | обязательно | App functionality |
| App info and performance | нет | | | |
| Device or other IDs (push-токен) | да | нет* | по желанию | App functionality |
| Financial info → Purchase history | да, с Фазы 2 | нет | по желанию | App functionality |
| Location, Contacts, Health and fitness, Web browsing, Audio, Files and docs, Calendar | нет | | | |

\* Передача SMS-провайдеру и сервису push-уведомлений считается «service provider» и по правилам Google не является sharing.

Настроение в Google Play отдельной категории не имеет; ближе всего «Health and fitness → Health info». Мы считаем его не медицинскими данными и не отмечаем. Если ревью Google спросит, отметить «Health info · collected · optional · App functionality».
