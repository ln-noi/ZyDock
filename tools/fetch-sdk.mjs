// Fetches the .NET SDK zip using Node's own TLS stack (SChannel is blocked in this sandbox).
// Usage: node tools/fetch-sdk.mjs <url> <destFile>
import { createWriteStream, statSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import https from 'node:https';

const url = process.argv[2];
const dest = process.argv[3];
if (!url || !dest) {
  console.error('usage: node fetch-sdk.mjs <url> <destFile>');
  process.exit(2);
}
mkdirSync(dirname(dest), { recursive: true });

function download(target, redirects = 0) {
  return new Promise((resolve, reject) => {
    if (redirects > 8) return reject(new Error('too many redirects'));
    const req = https.get(target, { headers: { 'User-Agent': 'ZyDock-Build' } }, (res) => {
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        res.resume();
        const next = new URL(res.headers.location, target).toString();
        return resolve(download(next, redirects + 1));
      }
      if (res.statusCode !== 200) {
        res.resume();
        return reject(new Error(`HTTP ${res.statusCode} for ${target}`));
      }
      const total = Number(res.headers['content-length'] || 0);
      let got = 0;
      let lastPct = -10;
      const out = createWriteStream(dest);
      res.on('data', (chunk) => {
        got += chunk.length;
        if (total) {
          const pct = Math.floor((got / total) * 100);
          if (pct - lastPct >= 10) {
            lastPct = pct;
            console.log(`  ${pct}%  ${(got / 1048576).toFixed(1)} / ${(total / 1048576).toFixed(1)} MB`);
          }
        }
      });
      res.pipe(out);
      out.on('finish', () => out.close(() => resolve({ bytes: got, total })));
      out.on('error', reject);
      res.on('error', reject);
    });
    req.on('error', reject);
    req.setTimeout(120000, () => req.destroy(new Error('timeout')));
  });
}

const started = Date.now();
console.log(`downloading ${url}`);
download(url)
  .then((r) => {
    const size = statSync(dest).size;
    console.log(`done: ${dest}  ${(size / 1048576).toFixed(1)} MB in ${((Date.now() - started) / 1000).toFixed(1)}s`);
    if (r.total && size !== r.total) {
      console.error(`WARN size mismatch: expected ${r.total}, got ${size}`);
      process.exit(1);
    }
  })
  .catch((e) => {
    console.error('FAIL', e.message);
    process.exit(1);
  });
