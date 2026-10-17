# Возрастной рейтинг · ответы на анкеты

Ответы описывают приложение как оно есть в этой ветке. Если появится чат, реклама, ссылки наружу или задания про алкоголь, анкеты нужно пройти заново.

Минимальный возраст по нашим документам — **18 лет** (`docs/legal/terms-*.md`, раздел 2). Документ 6 допускал аудиторию с 16 лет при согласии родителей; такой сценарий в приложении не построен, поэтому выбран порог 18. Снизить его можно только вместе с экраном согласия законного представителя.

## App Store Connect · Age Rating

| Вопрос | Ответ | Почему |
| --- | --- | --- |
| Cartoon or Fantasy Violence | None | |
| Realistic Violence | None | |
| Prolonged Graphic or Sadistic Realistic Violence | None | |
| Profanity or Crude Humor | None | Тексты заданий и вопросов пишем мы, без грубостей |
| Mature or Suggestive Themes | None | Задания бытовые: подарок, прогулка, игра (`backend/src/Ailu.Api/Data/Seed.cs`) |
| Horror/Fear Themes | None | |
| Medical or Treatment Information | None | Настроение — это отметка, а не диагностика |
| Alcohol, Tobacco, or Drug Use or References | None | В заданиях нет ни баров, ни алкоголя |
| Simulated Gambling | None | Нұр не покупаются и не выигрываются |
| Sexual Content or Nudity | None | |
| Graphic Sexual Content and Nudity | None | |
| Contests | None | Между парами нет рейтингов и соревнований |
| Unrestricted Web Access | No | Ссылки наружу — только на наши документы |
| User-Generated Content | Yes | Воспоминания, фото и ответы, видимые только партнёру по паре |
| Messaging and Chat | No | Чата нет; ответ на вопрос дня — не переписка |
| Advertising | No | |
| Parental Controls | No | |
| Age Assurance | No | Дату рождения не спрашиваем |

Итог анкеты, скорее всего, 9+ или 13+ из-за пользовательского контента. **Выбрать вручную 18+** («Override to a higher rating»), чтобы рейтинг совпадал с соглашением.

## Google Play · Content rating (IARC)

- **Категория:** All Other App Types (не игра, не соцсеть с публичными профилями).
- Violence, Sexuality, Language, Controlled substances, Gambling: **No** на все вопросы.
- **Do users interact or exchange content?** Yes, только с одним человеком: своим партнёром по паре.
- **Does the app share the user's current physical location with other users?** No.
- **Does the app allow users to purchase digital goods?** Yes, после запуска подписки Plus/Premium (Фаза 2). Если первая сборка выходит без покупок, ответить No и пройти анкету заново перед релизом с подпиской.
- **Is it a web browser or search engine?** No.

## Google Play · Target audience and content

- **Target age groups:** только **18 и старше**.
- **Could the app unintentionally appeal to children?** No.
- **Ads:** приложение не содержит рекламы.
- **News app:** No. **COVID-19 contact tracing:** No. **Government app:** No.
- **Financial features:** No. **Health apps:** No (отметка настроения — не медицинская функция).
- **Account deletion:** https://atukenov.github.io/ailu/legal/delete-account-ru.html (удаление в приложении — PR #13).
