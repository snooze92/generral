import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import React from 'react';
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MappingPreview } from 'Mapping/Mapping';
import Source from 'Sources/Source';
import { mockApi, MockRequest, MockRoute, uiSettings } from 'test/mockApi';
import AddSeries from './AddSeries';
import defaultDefinition from './defaultDefinition';

const sources: Partial<Source>[] = [
  { id: 1, name: 'Formula 1', type: 'thesportsdb', typeName: 'TheSportsDB', eventCount: 24, tags: [], fields: [] },
  { id: 2, name: 'Cup', type: 'static', typeName: 'Static', eventCount: 3, tags: [], fields: [] },
];

const preview: MappingPreview = {
  rows: [
    {
      event: { id: 10, key: 'semi', title: 'Semi-final', start: '2027-11-10T15:00:00Z', status: 'scheduled' },
      kept: true,
      seasonNumber: 2027,
      episodeNumber: 1,
      title: 'Cup Semi-final',
      airDateUtc: '2027-11-10T15:00:00Z',
      numberLocked: false,
      renumbered: false,
      change: 'new',
      errors: [],
    },
    {
      event: { id: 11, key: 'friendly', title: 'Friendly', start: '2027-11-01T15:00:00Z', status: 'scheduled' },
      kept: false,
      rejectedBy: 'filter[0]',
      numberLocked: false,
      renumbered: false,
      change: 'filtered',
      errors: [],
    },
  ],
  summary: { events: 2, kept: 1, episodes: 1, new: 1, changed: 0, unchanged: 0, orphaned: 0, filtered: 1, errors: 0, renumbered: 0 },
  seriesType: 'standard',
  seasons: [{ seasonNumber: 2027 }],
  errors: [],
};

const presets = [
  {
    id: 'example-fight-league',
    name: 'Example Fight League – Numbered events',
    description: 'Every numbered event of the fictional Example Fight League',
    seriesTitle: 'EFL',
    tags: ['sport'],
    sourceType: 'thesportsdb',
    requiredSecrets: [],
    parameters: [
      { name: 'tsdbKey', label: 'API key', default: '123', secret: true },
      { name: 'seasons', label: 'Seasons', default: 'current', secret: false },
    ],
    shipped: true,
  },
];

const created = {
  id: 7,
  title: 'Cup',
  titleSlug: 'cup',
  seasons: [],
  tags: [],
};

const badRequest = (propertyName: string, errorMessage: string) =>
  new Response(
    JSON.stringify([{ propertyName, errorMessage, severity: 'error', isWarning: false }]),
    { status: 400, headers: { 'Content-Type': 'application/json' } }
  );

function SeriesPage() {
  const { titleSlug } = useParams();
  return <div>Series page {titleSlug}</div>;
}

function renderWizard(routes: Record<string, MockRoute> = {}) {
  const fetchMock = mockApi({
    'GET /api/v1/source': sources,
    'GET /api/v1/series': [],
    'GET /api/v1/tag': [{ id: 1, label: 'football' }],
    'POST /api/v1/mapping/preview': preview,
    'GET /api/v1/preset': presets,
    'GET /api/v1/qualityprofile': [
      { id: 1, name: 'Any', upgradeAllowed: false, cutoff: 1 },
      { id: 4, name: 'HD-1080p', upgradeAllowed: false, cutoff: 9 },
    ],
    ...routes,
  });

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  // The page shell only renders once the UI settings are loaded.
  queryClient.setQueryData(['/settings/ui'], uiSettings);

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/add/new']}>
        <Routes>
          <Route path="/add/new" element={<AddSeries />} />
          <Route path="/series/:titleSlug" element={<SeriesPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );

  return fetchMock;
}

function requests(fetchMock: ReturnType<typeof mockApi>, method: string, path: string) {
  return fetchMock.mock.calls
    .filter(([input, init]) => {
      const url = new URL(String(input), 'http://localhost');
      return (init?.method ?? 'GET') === method && url.pathname === path;
    })
    .map(([, init]) => (init?.body ? JSON.parse(String(init.body)) : undefined));
}

async function startFromScratch() {
  fireEvent.click(await screen.findByRole('option', { name: /From scratch/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Next' }));
}

async function goToMapping() {
  await startFromScratch();
  fireEvent.click(await screen.findByRole('option', { name: /Cup/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Next' }));
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
  await screen.findByRole('status');
}

describe('Add series', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('links to the sources page when there is no source', async () => {
    renderWizard({ 'GET /api/v1/source': [] });
    await startFromScratch();

    expect(await screen.findByText(/Add a source first/)).toBeTruthy();
    expect(
      screen.getAllByRole('link').some((el) => el.getAttribute('href') === '/sources')
    ).toBe(true);
  });

  it('previews the default mapping and shows definition errors on the field', async () => {
    const fetchMock = renderWizard({
      'POST /api/v1/mapping/preview': ({ body }: MockRequest) =>
        (body as { definition: string }).definition.includes('oops')
          ? badRequest('definition', "mapping: unknown property 'oops'")
          : preview,
    });

    await goToMapping();

    expect(requests(fetchMock, 'POST', '/api/v1/mapping/preview')[0]).toEqual({
      sourceId: 2,
      definition: defaultDefinition,
    });
    expect(screen.getByRole('status').textContent).toContain(
      '2 events · 1 kept · 1 episodes · 1 seasons'
    );
    expect(screen.getByText('Cup Semi-final')).toBeTruthy();
    expect(screen.getByText('2027x01')).toBeTruthy();
    expect(screen.getByText('filter[0]')).toBeTruthy();
    expect(screen.getByText('Filtered')).toBeTruthy();

    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement;
    expect(textarea.value).toBe(defaultDefinition);
    fireEvent.change(textarea, { target: { value: 'oops: 1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Preview' }));

    expect(await screen.findByText("mapping: unknown property 'oops'")).toBeTruthy();
    expect(screen.queryByText('Cup Semi-final')).toBeNull();
  });

  it('creates the series and its mapping, then opens the series', async () => {
    const fetchMock = renderWizard({
      'POST /api/v1/series': created,
      'POST /api/v1/mapping': { id: 3, name: 'Cup', seriesId: 7, sourceId: 2 },
      'GET /api/v1/command': [
        { id: 5, name: 'ApplyMappings', body: { mappingId: 3 }, status: 'completed' },
      ],
      'GET /api/v1/series/7': created,
    });

    await goToMapping();
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));

    const title = screen.getByDisplayValue('Cup');
    expect(title.getAttribute('name')).toBe('title');
    fireEvent.change(screen.getByPlaceholderText('/tv'), {
      target: { name: 'rootFolderPath', value: '/sport' },
    });
    expect(await screen.findByText('Any')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Add Cup' }));

    expect(await screen.findByText('Series page cup')).toBeTruthy();
    expect(requests(fetchMock, 'POST', '/api/v1/series')[0]).toEqual({
      title: 'Cup',
      rootFolderPath: '/sport',
      monitored: true,
      monitorNewItems: 'all',
      seasonFolder: true,
      seriesType: 'standard',
      qualityProfileId: 0,
      tags: [],
      seasons: [],
    });
    expect(requests(fetchMock, 'POST', '/api/v1/mapping')[0]).toEqual({
      name: 'Cup',
      sourceId: 2,
      definition: defaultDefinition,
      seriesId: 7,
      priority: 0,
      enabled: true,
    });
  });

  it('removes the series again when the mapping is rejected, so adding can be retried', async () => {
    let mappingPosts = 0;
    const fetchMock = renderWizard({
      'POST /api/v1/series': created,
      'POST /api/v1/mapping': () =>
        ++mappingPosts === 1
          ? badRequest('definition', 'title: invalid template')
          : { id: 3, name: 'Cup', seriesId: 7, sourceId: 2 },
      'DELETE /api/v1/series/7': () => new Response(null, { status: 204 }),
      'GET /api/v1/command': [],
      'GET /api/v1/series/7': created,
    });

    await goToMapping();
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    fireEvent.change(screen.getByPlaceholderText('/tv'), {
      target: { name: 'rootFolderPath', value: '/sport' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Add Cup' }));

    expect(await screen.findByText('title: invalid template')).toBeTruthy();
    expect(screen.getByText(/the series was not added/)).toBeTruthy();
    await waitFor(() =>
      expect(requests(fetchMock, 'DELETE', '/api/v1/series/7')).toHaveLength(1)
    );

    fireEvent.click(screen.getByRole('button', { name: 'Add Cup' }));
    expect(await screen.findByText('Series page cup')).toBeTruthy();
    expect(requests(fetchMock, 'POST', '/api/v1/series')).toHaveLength(2);
  });

  it('offers Import and From scratch with an empty gallery, and selects an imported preset', async () => {
    const fetchMock = renderWizard({
      'GET /api/v1/preset': [],
      'POST /api/v1/preset/import': presets[0],
    });

    expect(await screen.findByRole('option', { name: /From scratch/ })).toBeTruthy();
    fireEvent.click(screen.getByRole('option', { name: /Import preset/ }));
    fireEvent.change(screen.getByPlaceholderText('https://'), {
      target: { name: 'url', value: 'https://example.org/preset.yaml' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Import' }));

    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/preset/import')).toEqual([
        { url: 'https://example.org/preset.yaml' },
      ])
    );
  });

  it('instantiates a preset with its parameters, then opens the series', async () => {
    const fetchMock = renderWizard({
      'POST /api/v1/preset/example-fight-league/instantiate': { ...created, title: 'EFL', titleSlug: 'efl' },
    });

    fireEvent.click(await screen.findByRole('option', { name: /Example Fight League – Numbered events/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));

    expect(screen.getByDisplayValue('EFL').getAttribute('name')).toBe('title');
    fireEvent.change(screen.getByPlaceholderText('/tv'), {
      target: { name: 'rootFolderPath', value: '/sport' },
    });
    fireEvent.change(screen.getByPlaceholderText('123'), {
      target: { name: 'tsdbKey', value: 'premium' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Add EFL' }));

    expect(await screen.findByText('Series page efl')).toBeTruthy();
    expect(requests(fetchMock, 'POST', '/api/v1/preset/example-fight-league/instantiate')[0]).toEqual({
      title: 'EFL',
      rootFolderPath: '/sport',
      monitored: true,
      monitorNewItems: 'all',
      seasonFolder: true,
      qualityProfileId: 0,
      tags: [],
      parameters: {},
      secrets: { tsdbKey: 'premium' },
    });
  });
});
