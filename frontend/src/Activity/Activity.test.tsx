import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MockRequest } from 'test/mockApi';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import Blocklist from './Blocklist/Blocklist';
import History from './History/History';
import Queue from './Queue/Queue';

const quality = {
  quality: { id: 3, name: 'WEBDL-1080p', source: 'web', resolution: 1080 },
  revision: { version: 1, real: 0, isRepack: false },
};

const series = [{ id: 1, title: 'Formula 1', titleSlug: 'formula-1' }];

const episodes = [
  { id: 11, seriesId: 1, seasonNumber: 2026, episodeNumber: 3, title: 'Australian GP' },
];

const paged = (records: unknown[]) => ({
  page: 1,
  pageSize: 20,
  sortKey: 'date',
  sortDirection: 'descending',
  totalRecords: records.length,
  records,
});

const queueItem = {
  id: 7,
  seriesId: 1,
  episodeId: 11,
  episodeIds: [11],
  quality,
  size: 1000,
  sizeleft: 250,
  timeleft: '00:05:00',
  title: 'Formula1.2026.Round03.Australia.1080p.WEB',
  status: 'downloading',
  trackedDownloadStatus: 'ok',
  trackedDownloadState: 'downloading',
  statusMessages: [],
  downloadId: 'abc',
  protocol: 'torrent',
  downloadClient: 'qBittorrent',
};

const commonRoutes = {
  'GET /api/v1/series': series,
  'GET /api/v1/episode': episodes,
};

describe('Activity', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lists the queue and removes an item with the chosen options', async () => {
    const fetchMock = renderSettingsPage(<Queue />, {
      ...commonRoutes,
      'GET /api/v1/queue': paged([queueItem]),
      'DELETE /api/v1/queue/7': {},
    });

    const title = await screen.findByText(queueItem.title);
    const row = within(title.closest('tr') as HTMLElement);
    expect(await row.findByText('Formula 1')).toBeTruthy();
    expect(await row.findByText('2026x03')).toBeTruthy();
    expect(row.getByText('Australian GP')).toBeTruthy();
    expect(row.getByText('WEBDL-1080p')).toBeTruthy();
    expect(row.getByText('qBittorrent')).toBeTruthy();
    expect(row.getByText('00:05:00')).toBeTruthy();

    fireEvent.click(row.getByLabelText('Remove from queue'));
    const dialog = within(await screen.findByRole('dialog'));
    fireEvent.click(dialog.getByText('Blocklist Release'));
    fireEvent.click(dialog.getByRole('button', { name: 'Remove' }));

    await waitFor(() =>
      expect(requests(fetchMock, 'DELETE', '/api/v1/queue/7')).toHaveLength(1)
    );
    const call = fetchMock.mock.calls.find(
      ([, init]) => init?.method === 'DELETE'
    );
    expect(String(call?.[0])).toContain('removeFromClient=true&blocklist=true');
  });

  it('manually imports the files of an import-blocked download', async () => {
    const fetchMock = renderSettingsPage(<Queue />, {
      ...commonRoutes,
      'GET /api/v1/queue': paged([
        { ...queueItem, trackedDownloadState: 'importBlocked', status: 'completed' },
      ]),
      'GET /api/v1/manualimport': [
        {
          id: 1,
          path: '/downloads/abc/f1.mkv',
          relativePath: 'f1.mkv',
          size: 1000,
          series: series[0],
          episodes,
          quality,
          downloadId: 'abc',
          rejections: [],
        },
        {
          id: 2,
          path: '/downloads/abc/sample.mkv',
          relativePath: 'sample.mkv',
          size: 10,
          episodes: [],
          quality,
          rejections: [{ reason: 'Sample' }],
        },
      ],
      'POST /api/v1/command': { id: 2, name: 'ManualImport', status: 'completed' },
    });

    const title = await screen.findByText(queueItem.title);
    fireEvent.click(
      within(title.closest('tr') as HTMLElement).getByLabelText('Manual Import')
    );
    const dialog = within(await screen.findByRole('dialog'));
    expect(await dialog.findByText('sample.mkv')).toBeTruthy();
    expect(dialog.getByText('Sample')).toBeTruthy();
    fireEvent.click(dialog.getByRole('button', { name: 'Import' }));

    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/command')).toEqual([
        {
          name: 'ManualImport',
          importMode: 'auto',
          files: [
            {
              path: '/downloads/abc/f1.mkv',
              seriesId: 1,
              episodeIds: [11],
              quality,
              downloadId: 'abc',
            },
          ],
        },
      ])
    );
  });

  it('shows a magnet without metadata as downloading metadata', async () => {
    renderSettingsPage(<Queue />, {
      ...commonRoutes,
      'GET /api/v1/queue': paged([
        { ...queueItem, status: 'downloadingMetadata', size: 0, sizeleft: 0, timeleft: undefined },
      ]),
    });

    const row = within(
      (await screen.findByText(queueItem.title)).closest('tr') as HTMLElement
    );
    expect(row.getByLabelText('Downloading metadata')).toBeTruthy();
    expect(row.queryByText('0 B')).toBeNull();
  });

  it('refreshes the queue with RefreshMonitoredDownloads', async () => {
    const fetchMock = renderSettingsPage(<Queue />, {
      ...commonRoutes,
      'GET /api/v1/queue': paged([]),
      'POST /api/v1/command': { id: 1, name: 'RefreshMonitoredDownloads', status: 'completed' },
    });

    expect(await screen.findByText('Queue is empty')).toBeTruthy();
    fireEvent.click(screen.getByText('Refresh'));

    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/command')).toEqual([
        { name: 'RefreshMonitoredDownloads' },
      ])
    );
  });

  it('lists history and filters by event type', async () => {
    const fetchMock = renderSettingsPage(<History />, {
      ...commonRoutes,
      'GET /api/v1/history': ({ url }: MockRequest) =>
        paged(
          url.searchParams.get('eventType') === 'downloadFailed'
            ? []
            : [
                {
                  id: 1,
                  episodeId: 11,
                  seriesId: 1,
                  sourceTitle: 'Formula1.2026.Round03.Australia.1080p.WEB',
                  quality,
                  qualityCutoffNotMet: false,
                  date: '2026-03-15T08:00:00Z',
                  eventType: 'grabbed',
                  data: { indexer: 'Torznab', downloadClientName: 'qBittorrent' },
                },
              ]
        ),
    });

    const title = await screen.findByText(
      'Formula1.2026.Round03.Australia.1080p.WEB'
    );
    const row = within(title.closest('tr') as HTMLElement);
    expect(
      row.getAllByTitle('Episode grabbed from Torznab and sent to qBittorrent')
        .length
    ).toBeGreaterThan(0);
    expect(await row.findByText('2026x03')).toBeTruthy();

    fireEvent.click(screen.getByText('Filter'));
    fireEvent.click(await screen.findByText('Failed'));

    expect(await screen.findByText('No history found')).toBeTruthy();
    const urls = fetchMock.mock.calls.map(([input]) => String(input));
    expect(urls.some((url) => url.includes('eventType=downloadFailed'))).toBe(
      true
    );
  });

  it('lists the blocklist and removes an item', async () => {
    const fetchMock = renderSettingsPage(<Blocklist />, {
      ...commonRoutes,
      'GET /api/v1/blocklist': paged([
        {
          id: 4,
          seriesId: 1,
          episodeIds: [11],
          sourceTitle: 'Formula1.2026.Round03.Australia.720p.HDTV',
          quality,
          date: '2026-03-15T08:00:00Z',
          protocol: 'usenet',
          indexer: 'NZBgeek',
          message: 'Download failed',
        },
      ]),
      'DELETE /api/v1/blocklist/4': {},
    });

    const title = await screen.findByText(
      'Formula1.2026.Round03.Australia.720p.HDTV'
    );
    const row = within(title.closest('tr') as HTMLElement);
    expect(row.getByText('NZBgeek')).toBeTruthy();
    expect(row.getByText('nzb')).toBeTruthy();

    fireEvent.click(row.getByLabelText('Remove from Blocklist'));

    await waitFor(() =>
      expect(requests(fetchMock, 'DELETE', '/api/v1/blocklist/4')).toHaveLength(
        1
      )
    );
  });
});
