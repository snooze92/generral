import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';
import { setTranslations } from 'Utilities/String/translate';
import translations from '../Strings/en.json';

window.Generral = {
  apiKey: 'test-api-key',
  apiRoot: '/api/v1',
  instanceName: 'Generral',
  theme: 'light',
  urlBase: '',
  version: '0.1.0',
  isProduction: true,
};

// jsdom doesn't implement matchMedia, which the app store and theme hooks rely on.
if (!window.matchMedia) {
  window.matchMedia = (query: string) =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList;
}

// Nor ResizeObserver, which the series tags and path use to detect overflow.
if (!window.ResizeObserver) {
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

// Nor scrollIntoView, which select dropdowns call on the selected option when they open.
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {};
}

// Modals, menus and tooltips render into #portal-root (index.html), looked up when they're imported.
const portalRoot = document.createElement('div');
portalRoot.id = 'portal-root';
document.body.appendChild(portalRoot);

setTranslations(translations);

afterEach(() => {
  cleanup();
});
