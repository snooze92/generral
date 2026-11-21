import { QueryClient } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mockApi } from 'test/mockApi';
import App from './App';

function renderApp(path: string) {
  window.history.pushState({}, '', path);

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(<App queryClient={queryClient} />);
}

describe('App', () => {
  beforeEach(() => {
    mockApi();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the shell with the sidebar and header', async () => {
    renderApp('/activity/queue');

    expect(await screen.findByText('Generral')).toBeTruthy();
    expect(
      screen.getByRole('navigation', { name: 'Main Navigation' })
    ).toBeTruthy();
    expect(await screen.findByText('This page is coming in M2.')).toBeTruthy();
  });

  it('renders Settings → General with the host, security and logging sections', async () => {
    renderApp('/settings/general');

    expect(await screen.findByText('Port Number')).toBeTruthy();
    expect(screen.getByText('Security')).toBeTruthy();
    expect(screen.getByText('Logging')).toBeTruthy();
    expect(screen.getByDisplayValue('8990')).toHaveProperty('readOnly', true);
    expect(screen.getByText('Set by environment variable')).toBeTruthy();
    expect(screen.getByDisplayValue('admin')).toBeTruthy();
  });

  it('renders Settings → Tags', async () => {
    mockApi({
      '/api/v1/tag': [{ id: 1, label: 'sports' }],
      '/api/v1/tag/detail': [
        { id: 1, label: 'sports', sourceIds: [], seriesIds: [] },
      ],
      '/api/v1/source': [],
    });
    renderApp('/settings/tags');

    expect(await screen.findByText('sports')).toBeTruthy();
    expect(screen.getByLabelText('Add Tag')).toBeTruthy();
  });

  it('renders the not found page for unknown routes', async () => {
    renderApp('/nope');

    expect(
      await screen.findByText('You must be lost, nothing to see here.')
    ).toBeTruthy();
  });
});
