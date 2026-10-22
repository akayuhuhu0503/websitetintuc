import { mkdir, copyFile } from 'node:fs/promises';
const destination = new URL('../wwwroot/vendor/jodit/', import.meta.url);
await mkdir(destination, { recursive: true });
for (const file of ['jodit.min.js', 'jodit.min.css']) {
  await copyFile(new URL(`../node_modules/jodit/es2021/${file}`, import.meta.url), new URL(file, destination));
}
await copyFile(new URL('../node_modules/jodit/LICENSE.txt', import.meta.url), new URL('LICENSE.txt', destination));
