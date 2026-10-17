// Frames the raw app captures in source/ (430×932 pt at 3×, from the web build) into store screenshots
// with a caption. Run from this folder: node frame.mjs (needs Playwright; Chromium path via CHROMIUM).
import { chromium } from 'playwright';
import { mkdirSync, readFileSync } from 'node:fs';

const SHOTS = [
  ['today', 'Одно дело в день', 'вдвоём'],
  ['today-question', 'Ответ партнёра', 'откроется после вашего'],
  ['memories', 'Вся ваша история', 'в одном месте'],
  ['memory', 'Фото, место', 'и пара слов'],
  ['goal', 'Общие цели', 'шаг за шагом'],
];
// App Store 6.9" and Google Play phone (Play allows at most a 2:1 aspect ratio).
const SIZES = { ios: [1320, 2868], android: [1080, 1920] };

const browser = await chromium.launch(process.env.CHROMIUM ? { executablePath: process.env.CHROMIUM } : {});
for (const [platform, [w, h]] of Object.entries(SIZES)) {
  mkdirSync(`ru/${platform}`, { recursive: true });
  const page = await browser.newPage({ viewport: { width: w, height: h } });
  for (const [i, [name, line1, line2]] of SHOTS.entries()) {
    const src = `data:image/png;base64,${readFileSync(`source/${name}.png`).toString('base64')}`;
    const shotH = Math.round(h * 0.74);
    await page.setContent(`
      <body style="margin:0;width:${w}px;height:${h}px;overflow:hidden;background:radial-gradient(circle at 50% 0%,#3a2a2f,#1C1714 60%);
        font-family:-apple-system,'Inter','Segoe UI',sans-serif;color:#F5EDE3;display:flex;flex-direction:column;align-items:center">
        <div style="margin-top:${h * 0.06}px;text-align:center;font-size:${w * 0.075}px;font-weight:700;line-height:1.15">
          ${line1}<br><span style="color:#F2A7C7">${line2}</span>
        </div>
        <img src="${src}" style="margin-top:auto;height:${shotH}px;border-radius:${w * 0.06}px ${w * 0.06}px 0 0;
          border:${Math.round(w * 0.008)}px solid #3a3330;border-bottom:none;box-shadow:0 0 ${w * 0.05}px rgba(242,167,199,.18)">
      </body>`);
    await page.screenshot({ path: `ru/${platform}/${i + 1}-${name}.png` });
  }
  await page.close();
}
await browser.close();
