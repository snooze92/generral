import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import React, { ReactElement } from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { mockApi, MockRoute, uiSettings } from './mockApi';

/** Renders a settings page with a mocked API; returns the fetch mock. */
export function renderSettingsPage(
  element: ReactElement,
  routes: Record<string, MockRoute>
) {
  const fetchMock = mockApi({ 'GET /api/v1/tag': [], ...routes });

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  // The page shell only renders once the UI settings are loaded.
  queryClient.setQueryData(['/settings/ui'], uiSettings);

  // The settings page blocks navigation with pending changes, which needs a data router.
  const router = createMemoryRouter([{ path: '*', element }]);

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );

  return fetchMock;
}

export function requests(
  fetchMock: ReturnType<typeof mockApi>,
  method: string,
  path: string
) {
  return fetchMock.mock.calls
    .filter(([input, init]) => {
      const url = new URL(String(input), 'http://localhost');
      return (init?.method ?? 'GET') === method && url.pathname === path;
    })
    .map(([, init]) =>
      init?.body ? JSON.parse(String(init.body)) : undefined
    );
}
