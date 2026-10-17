# Store publishing

| File | What |
| --- | --- |
| `CHECKLIST.md` | What is left before submitting, and what needs Lev's accounts. Start here. |
| `listing-ru.md`, `listing-kk.md` | Store texts for App Store and Google Play. `node store/check-listing.mjs` checks the length limits. |
| `age-rating.md` | Answers for the App Store age rating, Google's IARC questionnaire and target audience. |
| `data-safety.md` | Answers for App Store «App Privacy» and Google Play «Data safety». |
| `screenshots/` | Framed screenshots (Russian) and the script that makes them. |

The privacy policy and terms are in `docs/legal/` (Russian and Kazakh), published with GitHub Pages from `/docs`.
The app links to them from the sign-in screen and the profile (`app/src/legal.ts`). Build settings are in `app/eas.json`.
