import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import React from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { mockApi, MockRequest, MockRoute, uiSettings } from 'test/mockApi';
import TagSettings from './TagSettings';

const tags = [
  { id: 1, label: 'sports' },
  { id: 2, label: 'archive' },
];

const details = [
  { id: 1, label: 'sports', sourceIds: [10, 11], seriesIds: [5] },
  { id: 2, label: 'archive', sourceIds: [], seriesIds: [] },
];

const sources = [
  { id: 10, name: 'Formula 1', tags: [1] },
  { id: 11, name: 'MotoGP', tags: [1] },
];

function renderTags(routes: Record<string, MockRoute> = {}) {
  const fetchMock = mockApi({
    'GET /api/v1/tag': tags,
    'GET /api/v1/tag/detail': details,
    'GET /api/v1/source': sources,
    ...routes,
  });

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  // The page shell only renders once the UI settings are loaded.
  queryClient.setQueryData(['/settings/ui'], uiSettings);

  // The settings page blocks navigation with pending changes, which needs a data router.
  const router = createMemoryRouter([{ path: '*', element: <TagSettings /> }]);

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );

  return fetchMock;
}

function requests(
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

function card(label: string) {
  return screen.getByLabelText(`Tag Details - ${label}`)
    .parentElement as HTMLElement;
}

describe('Tag settings', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lists the tags sorted by label with their usage', async () => {
    renderTags();

    expect(await screen.findByText('sports')).toBeTruthy();

    const labels = screen
      .getAllByLabelText(/^Tag Details - /)
      .map((link) => link.getAttribute('aria-label'));
    expect(labels).toEqual(['Tag Details - archive', 'Tag Details - sports']);

    const used = within(card('sports'));
    expect(used.getByText('2 sources')).toBeTruthy();
    expect(used.getByText('1 series')).toBeTruthy();

    expect(within(card('archive')).getByText('No links')).toBeTruthy();
  });

  it('shows a hint when there are no tags yet', async () => {
    renderTags({ 'GET /api/v1/tag': [], 'GET /api/v1/tag/detail': [] });

    expect(await screen.findByText('No tags have been added yet')).toBeTruthy();
    expect(screen.getByLabelText('Add Tag')).toBeTruthy();
  });

  it('adds a tag, showing validation errors on the label', async () => {
    let saves = 0;
    const fetchMock = renderTags({
      'POST /api/v1/tag': ({ body }: MockRequest) =>
        ++saves === 1
          ? new Response(
              JSON.stringify([
                {
                  propertyName: 'label',
                  errorMessage: 'Allowed characters a-z, 0-9 and -',
                  isWarning: false,
                  severity: 'error',
                },
              ]),
              { status: 400 }
            )
          : { id: 3, ...(body as object) },
    });

    await screen.findByText('sports');
    fireEvent.click(screen.getByLabelText('Add Tag'));

    const dialog = await screen.findByRole('dialog');
    const input = dialog.querySelector(
      'input[name="label"]'
    ) as HTMLInputElement;

    fireEvent.change(input, { target: { value: 'bad label' } });
    fireEvent.click(within(dialog).getByText('Save'));
    expect(
      await within(dialog).findByText('Allowed characters a-z, 0-9 and -')
    ).toBeTruthy();

    fireEvent.change(input, { target: { value: 'motorsport' } });
    fireEvent.click(within(dialog).getByText('Save'));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('motorsport')).toBeTruthy();
    expect(requests(fetchMock, 'POST', '/api/v1/tag')).toEqual([
      { label: 'bad label' },
      { label: 'motorsport' },
    ]);
  });

  it('shows where a tag is used and renames it', async () => {
    const fetchMock = renderTags({
      'PUT /api/v1/tag/1': ({ body }: MockRequest) => body,
    });

    await screen.findByText('sports');
    fireEvent.click(screen.getByLabelText('Tag Details - sports'));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Tag Details - sports')).toBeTruthy();
    expect(await within(dialog).findByText('Formula 1')).toBeTruthy();
    expect(within(dialog).getByText('MotoGP')).toBeTruthy();
    expect(within(dialog).getByText('1 series')).toBeTruthy();

    fireEvent.change(within(dialog).getByDisplayValue('sports'), {
      target: { value: 'motorsport' },
    });
    fireEvent.click(within(dialog).getByText('Save'));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('motorsport')).toBeTruthy();
    expect(requests(fetchMock, 'PUT', '/api/v1/tag/1')).toEqual([
      { id: 1, label: 'motorsport' },
    ]);
  });

  it('deletes a tag in use after a confirmation that mentions its usage', async () => {
    const fetchMock = renderTags({
      'DELETE /api/v1/tag/1': () => new Response(null, { status: 204 }),
    });

    await screen.findByText('sports');
    fireEvent.click(screen.getByLabelText('Tag Details - sports'));

    const details = await screen.findByRole('dialog');
    fireEvent.click(within(details).getByText('Delete'));

    const confirm = await screen.findByRole('dialog');
    expect(
      within(confirm).getByText(
        /'sports' is used by 2 source\(s\) and 1 series\. Deleting it removes it from all of them/
      )
    ).toBeTruthy();
    const detailFetches = requests(fetchMock, 'GET', '/api/v1/tag/detail');
    const sourceFetches = requests(fetchMock, 'GET', '/api/v1/source');
    fireEvent.click(within(confirm).getByText('Delete'));

    await waitFor(() => expect(screen.queryByText('sports')).toBeNull());
    expect(requests(fetchMock, 'DELETE', '/api/v1/tag/1')).toHaveLength(1);
    // The tag was removed from its sources: their tags and the usage are refetched.
    await waitFor(() =>
      expect(
        requests(fetchMock, 'GET', '/api/v1/tag/detail').length
      ).toBeGreaterThan(detailFetches.length)
    );
    expect(requests(fetchMock, 'GET', '/api/v1/source').length).toBeGreaterThan(
      sourceFetches.length
    );
  });

  it('asks a plain confirmation for an unused tag', async () => {
    renderTags();

    await screen.findByText('archive');
    fireEvent.click(screen.getByLabelText('Tag Details - archive'));

    const details = await screen.findByRole('dialog');
    expect(
      within(details).getByText('Tag is not used and can be deleted')
    ).toBeTruthy();
    fireEvent.click(within(details).getByText('Delete'));

    expect(
      await screen.findByText(
        "Are you sure you want to delete the tag 'archive'?"
      )
    ).toBeTruthy();
  });
});
