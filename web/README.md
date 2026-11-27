# Ailu website

Landing page, privacy policy, terms, subscription terms, account deletion and support, in Russian and Kazakh.
Deployed on Vercel with Root Directory `web` and Output Directory `public` (no build step; see `vercel.json`).

| Path | What |
| --- | --- |
| `content/<ru|kk>/index.html` | Landing page body |
| `content/<ru|kk>/*.md` | Legal and support pages (Markdown with a small front matter) |
| `config.json` | Operator name, IDs, e-mails, site URL. `{{key}}` in the content is replaced with these values |
| `build.py` | Renders `content/` into `public/` |
| `public/` | The built site, committed. `style.css` and `favicon.svg` live here |

```sh
pip install markdown
python3 web/build.py
```

The app opens `<legalUrl>/<doc>-<ru|kk>.html` (`app/src/legal.ts`, `extra.legalUrl` in `app/app.json`), so keep those file names.
