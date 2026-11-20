// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/bootstrap.tsx
import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './App/App';

export async function bootstrap() {
  document.title = window.Generral.instanceName;

  const container = document.getElementById('root');

  const root = createRoot(container!);
  root.render(<App />);
}
