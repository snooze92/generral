// Adapted from Sonarr's vite.config.ts (GPL-3.0-or-later).
import { readdirSync } from 'fs';
import path from 'path';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { patchCssModules } from 'vite-css-modules';

const src = path.resolve(import.meta.dirname, 'src');

// Sonarr-style bare imports: 'Components/Icon' resolves to src/Components/Icon.
const srcAliases = Object.fromEntries(
  readdirSync(src, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => [entry.name, path.join(src, entry.name)])
);

const backend = process.env.GENERRAL_BACKEND ?? 'http://localhost:8990';

export default defineConfig({
  plugins: [patchCssModules({ exportMode: 'default' }), react()],

  // Assets are referenced absolutely; the backend prefixes them with the URL base when serving HTML.
  base: '/',
  publicDir: 'public',

  define: {
    __DEV__: JSON.stringify(process.env.NODE_ENV !== 'production'),
  },

  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: true,
    license: true,
    rolldownOptions: {
      input: {
        index: path.resolve(import.meta.dirname, 'index.html'),
        login: path.resolve(import.meta.dirname, 'login.html'),
      },
    },
  },

  server: {
    port: 8991,
    strictPort: true,
    proxy: Object.fromEntries(
      ['/api', '/ping', '/initialize.json', '/logout', '/feed', '/signalr'].map((prefix) => [
        prefix,
        { target: backend, ws: prefix === '/signalr' },
      ])
    ),
  },

  optimizeDeps: {
    exclude: ['moment'],
  },

  resolve: {
    alias: srcAliases,
  },

  css: {
    postcss: import.meta.dirname,
    modules: {
      generateScopedName: '[name]_[local]_[hash:base64:5]',
    },
  },

  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['src/test/setup.ts'],
    css: { modules: { classNameStrategy: 'non-scoped' } },
    // react-router-dom 7 only re-exports react-router; under Node its CJS build would load a second
    // copy of react-router (and its router context) next to the ESM one that direct imports get.
    alias: { 'react-router-dom': 'react-router' },
  },
});
