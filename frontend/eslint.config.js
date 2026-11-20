// Lints Generral-owned frontend code. Files vendored from Sonarr (scripts/sonarr-files.txt) are
// ignored: they are checked against upstream by `node scripts/vendor-sonarr.mjs <sonarr> --check`.
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import js from '@eslint/js';
import prettier from 'eslint-config-prettier';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import simpleImportSort from 'eslint-plugin-simple-import-sort';
import globals from 'globals';
import tseslint from 'typescript-eslint';

const here = import.meta.dirname;

const vendoredFiles = readFileSync(path.join(here, 'scripts/sonarr-files.txt'), 'utf8')
  .split('\n')
  .map((line) => line.trim())
  .filter((line) => line && !line.startsWith('#'))
  .map((file) => `src/${file}`);

const srcDirs = readdirSync(path.join(here, 'src'), { withFileTypes: true })
  .filter((entry) => entry.isDirectory())
  .map((entry) => entry.name)
  .join('|');

export default tseslint.config(
  {
    ignores: ['dist/**', 'node_modules/**', 'src/**/*.module.css.d.ts', ...vendoredFiles],
  },

  {
    files: ['scripts/**/*.mjs', 'eslint.config.js', 'vite.config.ts', 'postcss.config.cjs'],
    languageOptions: { globals: globals.node },
  },

  js.configs.recommended,
  ...tseslint.configs.recommended,

  {
    files: ['**/*.cjs'],
    languageOptions: { sourceType: 'commonjs' },
    rules: { '@typescript-eslint/no-require-imports': 'off' },
  },

  {
    files: ['src/**/*.{ts,tsx}'],
    languageOptions: {
      globals: globals.browser,
    },
    plugins: {
      react,
      'react-hooks': reactHooks,
      'simple-import-sort': simpleImportSort,
    },
    settings: {
      // An explicit version: eslint-plugin-react's detection doesn't support ESLint 10's context API.
      react: { version: '18.3' },
    },
    rules: {
      ...react.configs.flat.recommended.rules,
      ...reactHooks.configs['recommended-latest'].rules,
      'react/prop-types': 'off',
      'react/jsx-boolean-value': ['error', 'always'],
      'react/self-closing-comp': 'error',
      'react-hooks/exhaustive-deps': 'error',
      '@typescript-eslint/no-unused-vars': [
        'error',
        {
          args: 'after-used',
          argsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
          destructuredArrayIgnorePattern: '^_',
          varsIgnorePattern: '^_',
          ignoreRestSiblings: true,
        },
      ],
      // Same ordering as Sonarr: packages, src aliases, relative paths, CSS, in one block.
      'simple-import-sort/imports': [
        'error',
        {
          groups: [['^\\u0000', '^@?\\w', `^(${srcDirs})(/.*|$)`, '^\\.', '^\\..*css$']],
        },
      ],
      'simple-import-sort/exports': 'error',
    },
  },

  prettier
);
