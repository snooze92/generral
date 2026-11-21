#!/usr/bin/env node
// Copies Sonarr's generic UI components into frontend/src and applies Generral's mechanical edits.
//
//   node scripts/vendor-sonarr.mjs <path to a Sonarr checkout> [--check]
//
// The upstream revision is pinned in sonarr-revision.mjs and recorded in COPYRIGHT.md and REUSE.toml. To update:
// check out a newer Sonarr, bump SONARR_REVISION, run this script and update-reuse.mjs, review
// the diff and fix the transforms that no longer match (the script fails loudly when one doesn't).
// --check verifies that the vendored files are exactly what the script would produce.
import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { SONARR_REVISION } from './sonarr-revision.mjs';


const here = path.dirname(fileURLToPath(import.meta.url));
const srcRoot = path.resolve(here, '../src');
const files = readFileSync(path.join(here, 'sonarr-files.txt'), 'utf8')
  .split('\n')
  .map((line) => line.trim())
  .filter((line) => line && !line.startsWith('#'));

// Applied to every vendored file.
const globalTransforms = [
  [/window\.Sonarr\b/g, 'window.Generral'],
  [/'X-Sonarr-Client': 'Sonarr'/g, "'X-Generral-Client': 'Generral'"],
];

// [file, search, replacement]; every search must match at least once.
const fileTransforms = [
  // The native API lives at /api/v1 (Sonarr's v5 UI API has no equivalent here).
  ['Utilities/Fetch/fetchJson.ts', "export const apiRoot = '/api/v5'; // window.Generral.apiRoot;", "export const apiRoot = '/api/v1';"],
  ['Utilities/String/translate.ts', "tokens.appName = 'Sonarr';", "tokens.appName = 'Generral';"],
  ['Helpers/createPersist.ts', "?? 'sonarr'", "?? 'generral'"],
  ['Components/Loading/LoadingMessage.tsx', "'Previously on Sonarr...',", "'Previously on Generral...',"],
  // No error reporting to a third party.
  ['Components/Error/ErrorBoundary.tsx', "import * as sentry from '@sentry/browser';\n", ''],
  ['Components/Error/ErrorBoundary.tsx', '\n    sentry.captureException(error);\n', '\n    console.error(error);\n'],
  // Generral's own illustrations instead of Sonarr's mascot.
  ['Components/NotFound.tsx', '/Content/Images/404.png', '/Content/Images/404.svg'],
  ['Components/Error/ErrorBoundaryError.tsx', '/Content/Images/error.png', '/Content/Images/error.svg'],
  // Translations are bundled (English only); Generral adds a spoiler-free default instead.
  ['Settings/UI/useUiSettings.ts', '  uiLanguage: number;\n', '  spoilerFreeMode: boolean;\n'],
  // Branding: teal accent (hue 182 ≈ #14b8a6) instead of Sonarr's blue (hue 215).
  ['Styles/Themes/themes.css', /(oklch\([^)]*?) 215\)/g, '$1 182)'],
  ['Styles/Themes/themes.css', /--sonarrBlue:/g, '--brandColor:'],
  // Upstream relies on SignalR to refresh tags; fix the cache updates so they work without it.
  ['Tags/useTags.ts', "setQueryData<Tag[]>(['tag']", "setQueryData<Tag[]>(['/tag']"],
  ['Tags/useTags.ts', 'oldData.filter((tag) => tag.id === id)', 'oldData.filter((tag) => tag.id !== id)'],
];

function sonarrFile(sonarrRoot, file) {
  return path.join(sonarrRoot, 'frontend', 'src', file);
}

function transform(file, text) {
  let result = text;

  for (const [search, replacement] of globalTransforms) {
    result = result.replace(search, replacement);
  }

  for (const [target, search, replacement] of fileTransforms) {
    if (target !== file) {
      continue;
    }

    const before = result;
    result =
      typeof search === 'string'
        ? result.split(search).join(replacement)
        : result.replace(search, replacement);

    if (before === result) {
      throw new Error(`Transform did not match in ${file}: ${search}`);
    }
  }

  return result;
}

function main() {
  const [sonarrRoot, flag] = process.argv.slice(2);
  const check = flag === '--check';

  if (!sonarrRoot || !existsSync(path.join(sonarrRoot, 'frontend', 'src'))) {
    console.error('Usage: node scripts/vendor-sonarr.mjs <path to a Sonarr checkout> [--check]');
    process.exit(2);
  }

  const revision = execFileSync('git', ['-C', sonarrRoot, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();

  if (revision !== SONARR_REVISION) {
    console.error(`Sonarr checkout is at ${revision}, expected ${SONARR_REVISION}`);
    process.exit(2);
  }

  let mismatches = 0;

  for (const file of files) {
    const expected = transform(file, readFileSync(sonarrFile(sonarrRoot, file), 'utf8'));
    const target = path.join(srcRoot, file);

    if (check) {
      if (!existsSync(target) || readFileSync(target, 'utf8') !== expected) {
        console.error(`Differs from upstream + transforms: ${file}`);
        mismatches++;
      }

      continue;
    }

    mkdirSync(path.dirname(target), { recursive: true });
    writeFileSync(target, expected);
  }

  if (check && mismatches > 0) {
    console.error(`${mismatches} vendored file(s) were edited by hand. Move the change into a transform, or stop vendoring the file.`);
    process.exit(1);
  }

  console.log(`${check ? 'Checked' : 'Vendored'} ${files.length} files from Sonarr@${revision.slice(0, 12)}`);
}

main();
