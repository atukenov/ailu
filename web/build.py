#!/usr/bin/env python3
"""Builds the Ailu website (landing, legal pages, support) into web/public.

    pip install markdown
    python3 web/build.py

Sources: web/content/<lang>/*.md (legal and support pages) and web/content/<lang>/index.html (landing).
Values such as the operator name and support e-mail come from web/config.json as {{key}}.
An empty value is rendered as a visible yellow mark, and the build lists it, so nothing ships half-filled
by accident. The output in web/public is committed: Vercel serves it as is, with no build step.
"""
import html
import json
import re
import sys
from pathlib import Path

import markdown

ROOT = Path(__file__).resolve().parent
CONTENT = ROOT / "content"
PUBLIC = ROOT / "public"
CONFIG = json.loads((ROOT / "config.json").read_text(encoding="utf-8"))

LANGS = {
    "ru": {"name": "Русский", "short": "RU", "fill": "заполнить", "home": "Главная", "support": "Поддержка",
           "draft": "Черновик: текст ещё проверяет юрист."},
    "kk": {"name": "Қазақша", "short": "KK", "fill": "толтыру керек", "home": "Басты бет", "support": "Қолдау",
           "draft": "Жоба: мәтінді заңгер әлі тексеріп жатыр."},
}

# Page id -> output path per language. Paths match what the app opens (app/src/legal.ts: /legal/<doc>-<lang>.html).
PAGES = {
    "index": {"ru": "index.html", "kk": "kk/index.html"},
    "support": {"ru": "support-ru.html", "kk": "support-kk.html"},
    "privacy": {"ru": "legal/privacy-ru.html", "kk": "legal/privacy-kk.html"},
    "terms": {"ru": "legal/terms-ru.html", "kk": "legal/terms-kk.html"},
    "subscription": {"ru": "legal/subscription-ru.html", "kk": "legal/subscription-kk.html"},
    "delete-account": {"ru": "legal/delete-account-ru.html", "kk": "legal/delete-account-kk.html"},
}
FOOTER = ["privacy", "terms", "subscription", "delete-account", "support"]

missing: set[str] = set()


def fill(text: str, lang: str) -> str:
    def value(m: re.Match) -> str:
        key = m.group(1)
        if key not in CONFIG:
            sys.exit(f"Unknown placeholder {{{{{key}}}}}")
        v = str(CONFIG[key]).strip()
        if v:
            return html.escape(v)
        missing.add(key)
        return f'<mark class="todo">[{LANGS[lang]["fill"]}: {key}]</mark>'

    return re.sub(r"\{\{(\w+)\}\}", value, text)


def front_matter(text: str) -> tuple[dict, str]:
    meta = {}
    if text.startswith("---\n"):
        head, text = text[4:].split("\n---\n", 1)
        for line in head.splitlines():
            k, _, v = line.partition(":")
            meta[k.strip()] = v.strip()
    return meta, text


def url(page: str, lang: str) -> str:
    p = PAGES[page][lang]
    return "/" + (p[: -len("index.html")] if p.endswith("index.html") else p)


def titles() -> dict:
    out = {}
    for page in PAGES:
        for lang in LANGS:
            src = CONTENT / lang / (f"{page}.html" if page == "index" else f"{page}.md")
            meta, _ = front_matter(src.read_text(encoding="utf-8"))
            out[(page, lang)] = meta
    return out


def stores(lang: str) -> str:
    links = []
    if CONFIG["appStoreUrl"]:
        links.append(f'<a class="btn" href="{html.escape(CONFIG["appStoreUrl"])}">App Store</a>')
    if CONFIG["playUrl"]:
        links.append(f'<a class="btn" href="{html.escape(CONFIG["playUrl"])}">Google Play</a>')
    soon = {"ru": "Скоро в App Store и Google Play.", "kk": "Жақында App Store мен Google Play-де."}[lang]
    if not links:
        return f'<div class="stores"><span class="btn ghost">App Store</span><span class="btn ghost">Google Play</span></div><p class="soon">{soon}</p>'
    return f'<div class="stores">{"".join(links)}</div>'


def render(page: str, lang: str, meta: dict, body: str, all_meta: dict) -> str:
    other = "kk" if lang == "ru" else "ru"
    footer = " ".join(
        f'<a href="{url(p, lang)}">{html.escape(all_meta[(p, lang)].get("short", all_meta[(p, lang)]["title"]))}</a>'
        for p in FOOTER
    )
    kind = "landing" if page == "index" else "doc"
    draft = f'<p class="draft">{LANGS[lang]["draft"]}</p>' if meta.get("draft") == "yes" and CONFIG.get("showDraftNotice") else ""
    canonical = CONFIG["siteUrl"].rstrip("/") + url(page, lang)
    return f"""<!doctype html>
<html lang="{lang}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(meta["title"])}</title>
<meta name="description" content="{html.escape(meta.get("description", ""))}">
<link rel="canonical" href="{canonical}">
<link rel="alternate" hreflang="{other}" href="{CONFIG["siteUrl"].rstrip("/") + url(page, other)}">
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<meta name="theme-color" content="#170F1D">
<link rel="stylesheet" href="/style.css">
</head>
<body class="{kind}">
<header class="top">
  <a class="brand" href="{url("index", lang)}"><img src="/favicon.svg" alt="" width="28" height="28"> Ailu</a>
  <nav>
    <a href="{url("support", lang)}">{LANGS[lang]["support"]}</a>
    <a class="lang" href="{url(page, other)}" hreflang="{other}" lang="{other}">{LANGS[other]["short"]}</a>
  </nav>
</header>
<main>
{draft}
{body}
</main>
<footer>
  <nav>{footer}</nav>
  <p>© 2026 Ailu · {{{{operator}}}}</p>
</footer>
</body>
</html>
"""


def main() -> None:
    all_meta = titles()
    md = markdown.Markdown(extensions=["tables", "sane_lists", "attr_list"])
    count = 0
    for page in PAGES:
        for lang in LANGS:
            src = CONTENT / lang / (f"{page}.html" if page == "index" else f"{page}.md")
            meta, text = front_matter(src.read_text(encoding="utf-8"))
            body = text.replace("<!--stores-->", stores(lang)) if page == "index" else md.reset().convert(text)
            out = PUBLIC / PAGES[page][lang]
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(fill(render(page, lang, meta, body, all_meta), lang), encoding="utf-8")
            count += 1
    print(f"Built {count} pages into {PUBLIC.relative_to(ROOT.parent)}")
    if missing:
        print("Still empty in web/config.json (shown on the pages as [fill in]): " + ", ".join(sorted(missing)))


if __name__ == "__main__":
    main()
