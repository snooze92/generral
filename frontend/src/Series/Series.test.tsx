import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import React from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import Episode from 'Episode/Episode';
import { mockApi, MockRequest, MockRoute, uiSettings } from 'test/mockApi';
import SeriesDetailsPage from './Details/SeriesDetailsPage';
import SeriesIndex from './Index/SeriesIndex';
import Series from './Series';

const statistics = {
  seasonCount: 1,
  episodeCount: 3,
  episodeFileCount: 1,
  totalEpisodeCount: 4,
  percentOfEpisodes: 33.3,
  sizeOnDisk: 0,
  releaseGroups: [],
};

const formula1: Series = {
  id: 1,
  title: 'Formula 1',
  sortTitle: 'formula 1',
  cleanTitle: 'formula1',
  titleSlug: 'formula-1',
  status: 'continuing',
  ended: false,
  overview: 'The pinnacle of motorsport.',
  year: 0,
  genres: [],
  seasons: [
    { seasonNumber: 2025, monitored: false, statistics },
    { seasonNumber: 2026, title: 'Season 77', monitored: true, statistics },
  ],
  path: '/tv/Formula 1',
  rootFolderPath: '/tv',
  qualityProfileId: 1,
  seasonFolder: true,
  monitored: true,
  monitorNewItems: 'all',
  seriesType: 'standard',
  tags: [1],
  added: '2026-11-01T00:00:00Z',
  statistics: { ...statistics, seasonCount: 2 },
};

const allSeries: Series[] = [
  formula1,
  {
    ...formula1,
    id: 2,
    title: 'Apple Event',
    sortTitle: 'apple event',
    titleSlug: 'apple-event',
    seriesType: 'daily',
    monitored: false,
    tags: [],
    seasons: [],
    statistics: { ...statistics, seasonCount: 0, episodeCount: 0 },
  },
];

function episode(id: number, episodeNumber: number, changes = {}): Episode {
  return {
    id,
    seriesId: 1,
    episodeFileId: 0,
    seasonNumber: 2026,
    episodeNumber,
    title: `Grand Prix ${episodeNumber}`,
    airDate: '2026-03-01',
    airDateUtc: '2026-03-01T14:00:00Z',
    runtime: 120,
    hasFile: false,
    monitored: true,
    numberLocked: false,
    source: 'mapping',
    status: 'active',
    links: [],
    ...changes,
  };
}

const episodes: Episode[] = [
  episode(11, 1, { hasFile: true, episodeFileId: 5 }),
  episode(12, 2, { numberLocked: true }),
  episode(13, 3, { status: 'orphaned', hasFile: true, episodeFileId: 6 }),
  episode(21, 1, {
    seasonNumber: 2025,
    title: 'Pre-season Testing',
    monitored: false,
  }),
];

function renderAt(path: string, routes: Record<string, MockRoute> = {}) {
  const fetchMock = mockApi({
    'GET /api/v1/series': allSeries,
    'GET /api/v1/tag': [{ id: 1, label: 'motorsport' }],
    'GET /api/v1/episode': episodes,
    ...routes,
  });

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  // The page shell only renders once the UI settings are loaded.
  queryClient.setQueryData(['/settings/ui'], uiSettings);

  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/" element={<div>Home</div>} />
          <Route path="/series" element={<SeriesIndex />} />
          <Route path="/series/:titleSlug" element={<SeriesDetailsPage />} />
        </Routes>
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

function row(text: string) {
  return screen.getByText(text).closest('tr') as HTMLElement;
}

describe('Series index', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lists the series sorted by title with type, seasons, progress and tags', async () => {
    renderAt('/series');

    expect(await screen.findByText('Formula 1')).toBeTruthy();

    const titles = screen
      .getAllByRole('row')
      .slice(1)
      .map((tr) => within(tr).getAllByRole('cell')[1].textContent);
    expect(titles).toEqual(['Apple Event', 'Formula 1']);

    const f1 = within(row('Formula 1'));
    expect(f1.getByText('Standard')).toBeTruthy();
    expect(f1.getByText('2')).toBeTruthy();
    expect(f1.getAllByText('1 / 3').length).toBeGreaterThan(0);
    expect(f1.getByText('motorsport')).toBeTruthy();
    expect(f1.getByText('Formula 1').closest('a')?.getAttribute('href')).toBe(
      '/series/formula-1'
    );

    expect(within(row('Apple Event')).getByText('Daily')).toBeTruthy();
  });

  it('toggles a series monitored with a PUT of the whole series', async () => {
    const fetchMock = renderAt('/series', {
      'PUT /api/v1/series/2': ({ body }: MockRequest) => body,
    });

    await screen.findByText('Apple Event');
    fireEvent.click(
      within(row('Apple Event')).getByLabelText(
        'Unmonitored, click to monitor'
      )
    );

    await waitFor(() =>
      expect(requests(fetchMock, 'PUT', '/api/v1/series/2')).toHaveLength(1)
    );
    expect(requests(fetchMock, 'PUT', '/api/v1/series/2')[0]).toMatchObject({
      id: 2,
      title: 'Apple Event',
      monitored: true,
    });
    expect(
      await within(row('Apple Event')).findByLabelText(
        'Monitored, click to unmonitor'
      )
    ).toBeTruthy();
  });

  it('shows the empty state with a link to add a series', async () => {
    renderAt('/series', { 'GET /api/v1/series': [] });

    expect(
      await screen.findByText(
        "No series found, to get started you'll want to add a new series."
      )
    ).toBeTruthy();
    expect(
      screen
        .getAllByText('Add New Series')
        .some((el) => el.closest('a')?.getAttribute('href') === '/add/new')
    ).toBe(true);
  });
});

describe('Series details', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the header and the latest season expanded with its episodes', async () => {
    const fetchMock = renderAt('/series/formula-1');

    expect(await screen.findByText('Grand Prix 2')).toBeTruthy();
    expect(screen.getAllByText('Formula 1').length).toBeGreaterThan(0);
    expect(screen.getByText('The pinnacle of motorsport.')).toBeTruthy();
    expect(screen.getByText('/tv/Formula 1')).toBeTruthy();
    expect(screen.getByText('motorsport')).toBeTruthy();
    expect(screen.getByText('Season 2026')).toBeTruthy();
    expect(screen.getByText('Season 77')).toBeTruthy();
    expect(screen.getByText('Season 2025')).toBeTruthy();

    const query = new URL(
      String(fetchMock.mock.calls.find(([input]) =>
        String(input).includes('/episode')
      )![0]),
      'http://localhost'
    ).searchParams;
    expect(query.get('seriesId')).toBe('1');
    expect(query.get('includeOrphaned')).toBe('true');

    // Newest episode first; season 2025 is collapsed.
    const numbers = screen
      .getAllByText(/^Grand Prix /)
      .map((el) => el.textContent);
    expect(numbers).toEqual(['Grand Prix 3', 'Grand Prix 2', 'Grand Prix 1']);

    expect(within(row('Grand Prix 3')).getByText('Orphaned')).toBeTruthy();
    expect(within(row('Grand Prix 2')).getByText('Missing')).toBeTruthy();
    expect(
      within(row('Grand Prix 2')).getByLabelText(
        'Number locked, click to unlock'
      )
    ).toBeTruthy();
    expect(
      within(row('Grand Prix 1')).getByLabelText(
        'Number not locked, click to lock it so renumbering keeps it'
      )
    ).toBeTruthy();

    expect(screen.queryByText('Pre-season Testing')).toBeNull();
    fireEvent.click(screen.getByLabelText('Show episodes'));
    expect(await screen.findByText('Pre-season Testing')).toBeTruthy();
  });

  it('toggles episode monitoring, locks and season monitoring', async () => {
    const fetchMock = renderAt('/series/formula-1', {
      'PUT /api/v1/episode/12': ({ body }: MockRequest) => ({
        ...episodes[1],
        ...(body as object),
      }),
      'PUT /api/v1/episode/11/lock': ({ body }: MockRequest) => ({
        ...episodes[0],
        ...(body as object),
      }),
      'PUT /api/v1/series/1': ({ body }: MockRequest) => body,
    });

    await screen.findByText('Grand Prix 2');

    fireEvent.click(
      within(row('Grand Prix 2')).getByLabelText(
        'Monitored, click to unmonitor'
      )
    );
    await waitFor(() =>
      expect(requests(fetchMock, 'PUT', '/api/v1/episode/12')).toEqual([
        { monitored: false },
      ])
    );
    expect(
      await within(row('Grand Prix 2')).findByLabelText(
        'Unmonitored, click to monitor'
      )
    ).toBeTruthy();

    fireEvent.click(
      within(row('Grand Prix 1')).getByLabelText(
        'Number not locked, click to lock it so renumbering keeps it'
      )
    );
    await waitFor(() =>
      expect(requests(fetchMock, 'PUT', '/api/v1/episode/11/lock')).toEqual([
        { numberLocked: true },
      ])
    );
    expect(
      await within(row('Grand Prix 1')).findByLabelText(
        'Number locked, click to unlock'
      )
    ).toBeTruthy();

    fireEvent.click(screen.getByLabelText('Monitor Season 2025'));
    await waitFor(() =>
      expect(requests(fetchMock, 'PUT', '/api/v1/series/1')).toHaveLength(1)
    );
    expect(
      requests(fetchMock, 'PUT', '/api/v1/series/1')[0].seasons
    ).toEqual([
      expect.objectContaining({ seasonNumber: 2025, monitored: true }),
      expect.objectContaining({ seasonNumber: 2026, monitored: true }),
    ]);
  });

  it('unmonitors all of a season\'s episodes at once', async () => {
    const fetchMock = renderAt('/series/formula-1', {
      'PUT /api/v1/episode/monitor': ({ body }: MockRequest) => {
        const { episodeIds, monitored } = body as {
          episodeIds: number[];
          monitored: boolean;
        };
        return episodes
          .filter((e) => episodeIds.includes(e.id))
          .map((e) => ({ ...e, monitored }));
      },
    });

    await screen.findByText('Grand Prix 2');

    fireEvent.click(screen.getAllByLabelText('Episode Monitoring')[0]);
    fireEvent.click(await screen.findByText('Unmonitor All Episodes'));

    await waitFor(() =>
      expect(requests(fetchMock, 'PUT', '/api/v1/episode/monitor')).toEqual([
        { episodeIds: [13, 12, 11], monitored: false },
      ])
    );
  });

  it('searches the series, a season and an episode, and shows queued episodes', async () => {
    const fetchMock = renderAt('/series/formula-1', {
      'GET /api/v1/queue': {
        page: 1,
        pageSize: 1000,
        totalRecords: 1,
        records: [
          {
            id: 7,
            seriesId: 1,
            episodeIds: [12],
            size: 100,
            sizeleft: 50,
            title: 'Formula1.2026.Round02',
            status: 'downloading',
            trackedDownloadStatus: 'ok',
            trackedDownloadState: 'downloading',
            statusMessages: [],
          },
        ],
      },
      'POST /api/v1/command': ({ body }: MockRequest) => ({
        id: 1,
        ...(body as object),
        status: 'completed',
      }),
    });

    await screen.findByText('Grand Prix 2');
    expect(
      await within(row('Grand Prix 2')).findByTitle('Downloading (50.0%)')
    ).toBeTruthy();
    expect(within(row('Grand Prix 1')).queryByTitle(/Downloading/)).toBeNull();

    fireEvent.click(screen.getByText('Search Monitored'));
    fireEvent.click(
      screen.getAllByLabelText(
        'Search for monitored episodes in this season'
      )[0]
    );
    fireEvent.click(within(row('Grand Prix 1')).getByLabelText('Automatic Search'));

    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/command')).toEqual([
        { name: 'SeriesSearch', seriesId: 1 },
        { name: 'SeasonSearch', seriesId: 1, seasonNumber: 2026 },
        { name: 'EpisodeSearch', episodeIds: [11] },
      ])
    );
  });

  it('edits the series images by URL', async () => {
    const fetchMock = renderAt('/series/formula-1', {
      'GET /api/v1/series': [
        {
          ...formula1,
          images: [
            { coverType: 'fanart', url: 'https://img.example.org/old.jpg', remoteUrl: 'https://img.example.org/old.jpg' },
            { coverType: 'screenshot', url: 'https://img.example.org/s.jpg' },
          ],
        },
      ],
      'PUT /api/v1/series/1': ({ body }: MockRequest) => body,
    });

    await screen.findByText('Grand Prix 2');
    fireEvent.click(screen.getByText('Edit Images'));

    const dialog = await screen.findByRole('dialog');
    const inputs = within(dialog).getAllByPlaceholderText('https://');
    expect((inputs[1] as HTMLInputElement).value).toBe('https://img.example.org/old.jpg');
    fireEvent.change(inputs[0], { target: { name: 'poster', value: ' https://img.example.org/p.jpg ' } });
    fireEvent.change(inputs[1], { target: { name: 'fanart', value: '' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(requests(fetchMock, 'PUT', '/api/v1/series/1')).toHaveLength(1)
    );
    expect(requests(fetchMock, 'PUT', '/api/v1/series/1')[0].images).toEqual([
      { coverType: 'screenshot', url: 'https://img.example.org/s.jpg' },
      { coverType: 'poster', url: 'https://img.example.org/p.jpg', remoteUrl: 'https://img.example.org/p.jpg' },
    ]);
  });

  it('edits the series options; the path is read-only once it has files', async () => {
    const withoutFiles = { ...formula1, statistics: { ...formula1.statistics!, episodeFileCount: 0 } };
    const fetchMock = renderAt('/series/formula-1', {
      'GET /api/v1/series': [withoutFiles],
      'GET /api/v1/qualityprofile': [
        { id: 1, name: 'HD-1080p', items: [] },
        { id: 4, name: 'Ultra-HD', items: [] },
      ],
      'PUT /api/v1/series/1': ({ body }: MockRequest) => body,
    });

    await screen.findByText('Grand Prix 2');
    fireEvent.click(screen.getByText('Edit'));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Edit - Formula 1')).toBeTruthy();
    const path = within(dialog).getByDisplayValue('/tv/Formula 1') as HTMLInputElement;
    expect(path.readOnly).toBe(false);
    fireEvent.change(path, { target: { name: 'path', value: '/tv/F1' } });
    fireEvent.click(await within(dialog).findByText('HD-1080p'));
    fireEvent.click(await screen.findByText('Ultra-HD'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(requests(fetchMock, 'PUT', '/api/v1/series/1')).toHaveLength(1)
    );
    expect(requests(fetchMock, 'PUT', '/api/v1/series/1')[0]).toMatchObject({
      id: 1,
      title: 'Formula 1',
      path: '/tv/F1',
      qualityProfileId: 4,
      tags: [1],
    });
  });

  it('keeps the path read-only when the series has files', async () => {
    renderAt('/series/formula-1');

    await screen.findByText('Grand Prix 2');
    fireEvent.click(screen.getByText('Edit'));

    const dialog = await screen.findByRole('dialog');
    expect((within(dialog).getByDisplayValue('/tv/Formula 1') as HTMLInputElement).readOnly).toBe(true);
    expect(within(dialog).getByText(/can't move existing files yet/)).toBeTruthy();
  });

  it('deletes the series after confirmation and goes back to the index', async () => {
    const fetchMock = renderAt('/series/formula-1', {
      'DELETE /api/v1/series/1': new Response(null, { status: 204 }),
    });

    await screen.findByText('Grand Prix 2');
    fireEvent.click(screen.getByText('Delete'));

    expect(await screen.findByText('Delete - Formula 1')).toBeTruthy();
    expect(requests(fetchMock, 'DELETE', '/api/v1/series/1')).toHaveLength(0);

    fireEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' })
    );

    expect(await screen.findByText('Home')).toBeTruthy();
    expect(requests(fetchMock, 'DELETE', '/api/v1/series/1')).toHaveLength(1);
  });

  it('shows the file quality and size, and deletes a file after confirmation', async () => {
    const fetchMock = renderAt('/series/formula-1', {
      'GET /api/v1/series': [
        { ...formula1, statistics: { ...formula1.statistics, sizeOnDisk: 3 * 1024 ** 3 } },
      ],
      'GET /api/v1/episodefile': [
        {
          id: 5,
          seriesId: 1,
          seasonNumber: 2026,
          relativePath: 'Season 2026/Formula 1 - S2026E01.mkv',
          path: '/tv/Formula 1/Season 2026/Formula 1 - S2026E01.mkv',
          size: 1.5 * 1024 ** 3,
          dateAdded: '2026-03-02T00:00:00Z',
          quality: {
            quality: { id: 3, name: 'WEBDL-1080p', source: 'web', resolution: 1080 },
            revision: { version: 1, real: 0, isRepack: false },
          },
          qualityCutoffNotMet: false,
        },
      ],
      'DELETE /api/v1/episodefile/5': {},
    });

    await screen.findByText('Grand Prix 1');
    expect(await within(row('Grand Prix 1')).findByText('WEBDL-1080p')).toBeTruthy();
    expect(within(row('Grand Prix 1')).getByText('1.5 GiB')).toBeTruthy();
    expect(screen.getByText('3 GiB')).toBeTruthy();
    expect(within(row('Grand Prix 2')).queryByLabelText('Delete Episode File')).toBeNull();

    fireEvent.click(within(row('Grand Prix 1')).getByLabelText('Delete Episode File'));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/Formula 1 - S2026E01\.mkv/)).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() =>
      expect(requests(fetchMock, 'DELETE', '/api/v1/episodefile/5')).toHaveLength(1)
    );
  });

  it('shows not found for an unknown slug', async () => {
    renderAt('/series/nope');

    expect(
      await screen.findByText('Sorry, that series cannot be found.')
    ).toBeTruthy();
  });
});
