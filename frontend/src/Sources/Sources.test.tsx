import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { mockApi, MockRequest, MockRoute, uiSettings } from 'test/mockApi';
import Sources from './Index/Sources';
import Source, { SourceTestResult } from './Source';

const staticSchema: Source = {
  id: 0,
  name: 'Static',
  type: 'static',
  typeName: 'Static',
  enabled: true,
  refreshInterval: 360,
  tags: [],
  eventCount: 0,
  fields: [
    {
      order: 0,
      name: 'format',
      label: 'Format',
      type: 'select',
      value: 'yaml',
      advanced: false,
      selectOptions: [
        { value: 'yaml', name: 'YAML' },
        { value: 'json', name: 'JSON' },
      ],
    },
    {
      order: 1,
      name: 'events',
      label: 'Events',
      type: 'textArea',
      value: '',
      advanced: false,
    },
  ] as Source['fields'],
};

const sources: Source[] = [
  {
    ...staticSchema,
    id: 1,
    name: 'Formula 1',
    type: 'thesportsdb',
    typeName: 'TheSportsDB',
    eventCount: 24,
    lastRefresh: '2026-11-20T10:00:00Z',
    lastSuccess: '2026-11-20T10:00:00Z',
    nextRefresh: '2026-11-20T16:00:00Z',
  },
  {
    ...staticSchema,
    id: 2,
    name: 'Broken feed',
    type: 'http-json',
    typeName: 'HTTP JSON',
    enabled: false,
    eventCount: 3,
    lastRefresh: '2026-11-20T10:00:00Z',
    lastError: 'example.com returned 503',
  },
];

const testResult: SourceTestResult = {
  success: true,
  events: [
    {
      id: 0,
      sourceId: 0,
      key: 'final',
      title: 'Grand Final',
      start: '2026-12-01T18:00:00Z',
      status: 'scheduled',
      fields: {},
      missedRefreshes: 0,
    },
  ],
  itemErrors: [
    { index: 2, fields: { title: null }, errors: ['title is required'] },
  ],
  responses: [
    {
      url: 'https://example.com/events.json',
      status: 200,
      contentType: 'application/json',
      body: '{"events":[]}',
      truncated: true,
      fromCache: false,
    },
  ],
  warnings: ['2 items were skipped'],
};

function renderSources(routes: Record<string, MockRoute>) {
  const fetchMock = mockApi({
    'GET /api/v1/source': sources,
    '/api/v1/source/schema': [staticSchema],
    ...routes,
  });

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  // The page shell only renders once the UI settings are loaded.
  queryClient.setQueryData(['/settings/ui'], uiSettings);

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <Sources />
      </MemoryRouter>
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

function row(name: string) {
  return screen.getByText(name).closest('tr') as HTMLElement;
}

describe('Sources', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it('lists the sources with their type, event count and last result', async () => {
    renderSources({});

    expect(await screen.findByText('Formula 1')).toBeTruthy();

    const ok = within(row('Formula 1'));
    expect(ok.getByText('TheSportsDB')).toBeTruthy();
    expect(ok.getByText('24')).toBeTruthy();
    expect(ok.getByText('OK')).toBeTruthy();
    expect(ok.getByTitle('Enabled')).toBeTruthy();

    const broken = within(row('Broken feed'));
    expect(broken.getByText('Error')).toBeTruthy();
    expect(broken.getByLabelText('example.com returned 503')).toBeTruthy();
    expect(broken.getByTitle('Disabled')).toBeTruthy();
  });

  it('refreshes a source and spins until its command has finished', async () => {
    let polls = 0;
    const fetchMock = renderSources({
      'POST /api/v1/source/1/refresh': {
        id: 7,
        name: 'RefreshSource',
        status: 'queued',
      },
      '/api/v1/command/7': () => ({
        id: 7,
        name: 'RefreshSource',
        status: ++polls > 1 ? 'completed' : 'started',
      }),
    });

    await screen.findByText('Formula 1');
    const button = within(row('Formula 1')).getByTitle('Refresh Source');
    fireEvent.click(button);

    await waitFor(() => expect(polls).toBe(2), { timeout: 4000 });
    await waitFor(() =>
      expect(
        requests(fetchMock, 'GET', '/api/v1/source').length
      ).toBeGreaterThan(1)
    );
  });

  it('shows why a source cannot be deleted', async () => {
    const fetchMock = renderSources({
      'DELETE /api/v1/source/1': () =>
        new Response(
          JSON.stringify([
            {
              propertyName: 'id',
              errorMessage: 'Source is used by mapping(s): F1',
              isWarning: false,
              severity: 'error',
            },
          ]),
          { status: 400 }
        ),
    });

    await screen.findByText('Formula 1');
    fireEvent.click(within(row('Formula 1')).getByTitle('Delete Source'));

    const dialog = await screen.findByRole('dialog');
    expect(
      within(dialog).getByText(/delete the source 'Formula 1'/)
    ).toBeTruthy();
    fireEvent.click(within(dialog).getByText('Delete'));

    expect(
      await within(dialog).findByText('Source is used by mapping(s): F1')
    ).toBeTruthy();
    expect(requests(fetchMock, 'DELETE', '/api/v1/source/1')).toHaveLength(1);
  });

  it('adds a source: picks a type, tests it with a preview and saves it', async () => {
    let saves = 0;
    const fetchMock = renderSources({
      'POST /api/v1/source/test': testResult,
      'POST /api/v1/source': ({ body }: MockRequest) =>
        ++saves === 1
          ? new Response(
              JSON.stringify([
                {
                  propertyName: 'name',
                  errorMessage: 'Should be unique',
                  isWarning: false,
                  severity: 'error',
                },
              ]),
              { status: 400 }
            )
          : { ...(body as Source), id: 3, eventCount: 0 },
    });

    await screen.findByText('Formula 1');
    fireEvent.click(screen.getByText('Add Source'));

    fireEvent.click(await screen.findByText('Static'));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Add Source - Static')).toBeTruthy();

    fireEvent.change(within(dialog).getByDisplayValue('Static'), {
      target: { value: 'Cup' },
    });
    const events = dialog.querySelector(
      'textarea[name="events"]'
    ) as HTMLTextAreaElement;
    fireEvent.change(events, { target: { value: '- title: Grand Final' } });

    fireEvent.click(within(dialog).getByText('Test'));

    expect(await within(dialog).findByText('Grand Final')).toBeTruthy();
    expect(within(dialog).getByText('final')).toBeTruthy();
    expect(within(dialog).getByText('Scheduled')).toBeTruthy();
    expect(within(dialog).getByText('2 items were skipped')).toBeTruthy();

    const [tested] = requests(fetchMock, 'POST', '/api/v1/source/test');
    expect(tested).toMatchObject({
      name: 'Cup',
      type: 'static',
      refreshInterval: 360,
      fields: [
        { name: 'format', value: 'yaml' },
        { name: 'events', value: '- title: Grand Final' },
      ],
    });

    fireEvent.click(
      within(dialog).getByRole('tab', { name: 'Item Errors (1)' })
    );
    expect(within(dialog).getByText('title is required')).toBeTruthy();

    fireEvent.click(
      within(dialog).getByRole('tab', { name: 'Raw Responses (1)' })
    );
    expect(
      within(dialog).getByText('https://example.com/events.json')
    ).toBeTruthy();
    expect(within(dialog).getByText(/"events":\[\]/)).toBeTruthy();
    expect(within(dialog).getByText(/truncated/)).toBeTruthy();

    fireEvent.click(within(dialog).getByText('Save'));
    expect(
      (await within(dialog).findAllByText('Should be unique')).length
    ).toBeGreaterThan(0);

    fireEvent.click(within(dialog).getByText('Save'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('Cup')).toBeTruthy();
  });

  it('lists the events of a source, optionally with the gone ones', async () => {
    const event = testResult.events[0];
    const fetchMock = renderSources({
      '/api/v1/source/1/events': ({ url }: MockRequest) =>
        url.searchParams.get('includeGone') === 'true'
          ? [
              { ...event, id: 1 },
              {
                ...event,
                id: 2,
                key: 'old',
                title: 'Old Race',
                status: 'gone',
              },
            ]
          : [{ ...event, id: 1 }],
    });

    await screen.findByText('Formula 1');
    fireEvent.click(within(row('Formula 1')).getByTitle('View Events'));

    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByText('Grand Final')).toBeTruthy();
    expect(within(dialog).queryByText('Old Race')).toBeNull();

    fireEvent.click(within(dialog).getByText('Show gone events'));
    expect(await within(dialog).findByText('Old Race')).toBeTruthy();
    expect(within(dialog).getByText('Gone')).toBeTruthy();
    expect(requests(fetchMock, 'GET', '/api/v1/source/1/events')).toHaveLength(
      2
    );
  });
});
