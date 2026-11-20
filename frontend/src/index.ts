// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/index.ts
import 'Styles/globals.css';
import './index.css';
import { setTranslations } from 'Utilities/String/translate';
import translations from './Strings/en.json';

setTranslations(translations);

const { urlBase } = window.Generral;
const response = await fetch(`${urlBase}/initialize.json?t=${Date.now()}`, {
  credentials: 'same-origin',
});

if (response.status === 401) {
  const returnUrl = window.location.pathname + window.location.search;

  window.location.href = `${urlBase}/login?returnUrl=${encodeURIComponent(
    returnUrl
  )}`;
} else if (!response.ok) {
  throw new Error(`Failed to load ${urlBase}/initialize.json (${response.status})`);
} else {
  window.Generral = await response.json();

  const { bootstrap } = await import('./bootstrap');

  await bootstrap();
}
