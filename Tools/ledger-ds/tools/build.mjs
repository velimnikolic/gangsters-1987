// Bundle the package to dist/: ESM JS via esbuild, ledger.css assembled from
// fonts.css + tokens.css + components.css, fonts copied beside it.
import { build } from 'esbuild';
import { readFileSync, writeFileSync, mkdirSync, cpSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
mkdirSync(join(root, 'dist'), { recursive: true });

await build({
  entryPoints: [join(root, 'src/index.ts')],
  bundle: true,
  format: 'esm',
  jsx: 'automatic',
  external: ['react', 'react-dom', 'react/*', 'react-dom/*'],
  outfile: join(root, 'dist/index.js'),
  logLevel: 'info',
});

const css = ['fonts.css', 'tokens.css', 'components.css']
  .map((f) => readFileSync(join(root, 'src', f), 'utf8'))
  .join('\n');
writeFileSync(join(root, 'dist/ledger.css'), css);
cpSync(join(root, 'fonts'), join(root, 'dist/fonts'), { recursive: true });
console.log('dist ready');
