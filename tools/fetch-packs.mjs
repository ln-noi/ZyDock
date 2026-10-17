// Downloads the NuGet packages required by a self-contained publish.
// Node's own TLS stack is used because this machine's SChannel path can be unavailable.
// Usage: node tools/fetch-packs.mjs <destDir> <id>:<version> [<id>:<version> ...]
import { createWriteStream, mkdirSync, existsSync, statSync, renameSync } from 'node:fs';
import { join } from 'node:path';
import https from 'node:https';

const [dest, ...specs] = process.argv.slice(2);
if (!dest || specs.length === 0) {
  console.error('usage: node fetch-packs.mjs <destDir> <id>:<version> ...');
  process.exit(2);
}
mkdirSync(dest, { recursive: true });

function download(url, file, redirects = 0) {
  return new Promise((resolve, reject) => {
    if (redirects > 8) return reject(new Error('too many redirects'));
    const req = https.get(url, { headers: { 'User-Agent': 'ZyDock-Build' } }, (res) => {
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        res.resume();
        return resolve(download(new URL(res.headers.location, url).toString(), file, redirects + 1));
      }
      if (res.statusCode !== 200) {
        res.resume();
        return reject(new Error(`HTTP ${res.statusCode} for ${url}`));
      }
      const total = Number(res.headers['content-length'] || 0);
      let got = 0;
      const out = createWriteStream(file + '.part');
      res.on('data', (c) => { got += c.length; });
      res.pipe(out);
      out.on('finish', () => out.close(() => {
        renameSync(file + '.part', file);
        resolve({ bytes: got, total });
      }));
      out.on('error', reject);
      res.on('error', reject);
    });
    req.on('error', reject);
    req.setTimeout(120000, () => req.destroy(new Error('timeout')));
  });
}

let failed = 0;
for (const spec of specs) {
  const [id, version] = spec.split(':');
  const lower = id.toLowerCase();
  const url = `https://api.nuget.org/v3-flatcontainer/${lower}/${version}/${lower}.${version}.nupkg`;
  const file = join(dest, `${lower}.${version}.nupkg`);
  if (existsSync(file) && statSync(file).size > 1024) {
    console.log(`skip  ${id} ${version} (already present)`);
    continue;
  }
  try {
    const r = await download(url, file);
    console.log(`ok    ${id} ${version} ${(r.bytes / 1048576).toFixed(1)} MB`);
  } catch (e) {
    failed++;
    console.error(`FAIL  ${id} ${version}: ${e.message}`);
  }
}
process.exit(failed === 0 ? 0 : 1);
