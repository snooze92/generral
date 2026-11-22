import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import WantedPage from './WantedPage';

const series = { id: 1, title: 'Formula 1', titleSlug: 'formula-1' };

const episode = (id: number, episodeNumber: number, title: string) => ({
  id,
  seriesId: 1,
  seasonNumber: 2026,
  episodeNumber,
  title,
  airDateUtc: '2026-03-15T05:00:00Z',
  monitored: true,
  series,
});

const paged = (records: unknown[]) => ({
  page: 1,
  pageSize: 20,
  sortKey: 'episodes.airDateUtc',
  sortDirection: 'ascending',
  totalRecords: records.length,
  records,
});

const command = { id: 1, name: 'EpisodeSearch', status: 'completed' };

describe('Wanted', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lists missing episodes, searches the selected ones and all of them', async () => {
    const fetchMock = renderSettingsPage(<WantedPage kind="missing" />, {
      'GET /api/v1/wanted/missing': paged([
        episode(11, 3, 'Australian GP'),
        episode(12, 4, 'Japanese GP'),
      ]),
      'POST /api/v1/command': command,
    });

    const row = within(
      (await screen.findByText('Australian GP')).closest('tr') as HTMLElement
    );
    expect(row.getByText('Formula 1')).toBeTruthy();
    expect(row.getByText('2026x03')).toBeTruthy();

    const url = String(fetchMock.mock.calls.find(([input]) =>
      String(input).includes('/wanted/missing')
    )?.[0]);
    expect(url).toContain('monitored=true');
    expect(url).toContain('includeSeries=true');

    fireEvent.click(row.getByRole('checkbox'));
    fireEvent.click(screen.getByText('Search Selected'));
    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/command')).toEqual([
        { name: 'EpisodeSearch', episodeIds: [11] },
      ])
    );

    fireEvent.click(await screen.findByText('Search All'));
    const confirm = within(await screen.findByRole('dialog'));
    expect(
      confirm.getByText(
        'Are you sure you want to search for all 2 missing episodes?'
      )
    ).toBeTruthy();
    fireEvent.click(confirm.getByText('Search'));
    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/command')[1]).toEqual({
        name: 'MissingEpisodeSearch',
      })
    );
  });

  it('shows the empty state for cutoff unmet', async () => {
    renderSettingsPage(<WantedPage kind="cutoffUnmet" />, {
      'GET /api/v1/wanted/cutoff': paged([]),
    });

    expect(await screen.findByText('No cutoff unmet items')).toBeTruthy();
  });
});
