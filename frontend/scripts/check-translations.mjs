#!/usr/bin/env node
// Fails when a key passed to translate() in frontend/src is missing from src/Strings/en.json.
//
//   node scripts/check-translations.mjs [--unused]
//
// --unused also lists keys in en.json that nothing references (informational).
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const srcRoot = path.resolve(here, '../src');
const stringsFile = path.join(srcRoot, 'Strings', 'en.json');
const strings = JSON.parse(readFileSync(stringsFile, 'utf8'));

function* sourceFiles(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);

    if (entry.isDirectory()) {
      yield* sourceFiles(full);
    } else if (/\.(ts|tsx|js|jsx)$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name)) {
      yield full;
    }
  }
}

// The text between translate( and its matching parenthesis.
function* translateCalls(text) {
  const pattern = /\btranslate\(/g;
  while (pattern.exec(text)) {
    let depth = 1;
    let i = pattern.lastIndex;

    while (i < text.length && depth > 0) {
      if (text[i] === '(') depth++;
      if (text[i] === ')') depth--;
      i++;
    }

    yield text.slice(pattern.lastIndex, i - 1);
  }
}

const used = new Map();

for (const file of sourceFiles(srcRoot)) {
  const text = readFileSync(file, 'utf8');

  for (const call of translateCalls(text)) {
    // The key argument: string literals before the tokens object (covers `cond ? 'A' : 'B'`),
    // minus the ones a condition compares against (`state === 'error' ? 'A' : 'B'`).
    const keyPart = call.split('{')[0].replace(/[!=]==?\s*'[^']*'/g, '');

    for (const [, key] of keyPart.matchAll(/'([A-Za-z][A-Za-z0-9]*)'/g)) {
      if (!used.has(key)) used.set(key, path.relative(srcRoot, file));
    }
  }
}

const missing = [...used].filter(([key]) => !(key in strings));

if (process.argv.includes('--unused')) {
  const unused = Object.keys(strings).filter((key) => !used.has(key));
  console.log(`Unused keys (${unused.length}): ${unused.join(', ')}`);
}

if (missing.length) {
  for (const [key, file] of missing) {
    console.error(`Missing translation: ${key} (used in ${file})`);
  }

  process.exit(1);
}

console.log(`All ${used.size} translation keys are defined in src/Strings/en.json`);
