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

setTranslations(translations);

afterEach(() => {
  cleanup();
});
