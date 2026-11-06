import { build } from 'esbuild';
import { writeFile } from 'node:fs/promises';
// These are code assets executed inside Nexus, separate from the local Vue application.
// Keep the existing C# boundary: snapshot/inspect return JSON; actions return functions.
for (const [entry, file, invoke] of [
  ['snapshot', 'nexus.js', true],
  ['search', 'nexus-search.js', false],
  ['download', 'nexus-download.js', false],
  ['inspect', 'inspect-cards.js', true],
]) {
  const result = await build({
    entryPoints: [`adapters/${entry}.ts`],
    bundle: true,
    format: 'iife',
    globalName: 'AshAdapter',
    target: 'es2022',
    minify: false,
    write: false,
  });
  const expression = `(() => {\n${result.outputFiles[0].text}\nreturn AshAdapter.default${invoke ? '()' : ''};\n})()`;
  await writeFile(`../desktop/Assets/${file}`, expression);
}
