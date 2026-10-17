// Checks every "**Field (N):**" block in the listing files against its store limit N.
// The text is the next non-empty paragraph, up to the next "**" field or "##" heading.
import { readFileSync } from 'node:fs';

let failed = false;
for (const file of ['listing-ru.md', 'listing-kk.md']) {
  const lines = readFileSync(new URL(file, import.meta.url), 'utf8').split('\n');
  lines.forEach((line, i) => {
    const m = line.match(/^\*\*(.+?) \((\d+)(?:,[^)]*)?\):\*\*\s*(.*)$/);
    if (!m) return;
    const [, field, limit, inline] = m;
    const body = [];
    if (inline && !inline.startsWith('то же') && !inline.startsWith('App Store')) body.push(inline);
    for (let j = i + 1; j < lines.length && !/^(\*\*|## )/.test(lines[j]); j++) body.push(lines[j]);
    const text = body.join('\n').trim();
    if (!text) return; // "same as App Store" fields
    const length = [...text].length;
    const ok = length <= Number(limit);
    if (!ok) failed = true;
    console.log(`${ok ? 'ok  ' : 'LONG'} ${file} · ${field}: ${length}/${limit}`);
  });
}
process.exit(failed ? 1 : 0);
