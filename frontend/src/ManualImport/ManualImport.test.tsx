import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import ManualImportPage from './ManualImportPage';

const webdl = { id: 3, name: 'WEBDL-1080p', source: 'web', resolution: 1080 };
const bluray = { id: 7, name: 'Bluray-1080p', source: 'bluray', resolution: 1080 };
const quality = { quality: webdl, revision: { version: 1, real: 0, isRepack: false } };

const series = [
  { id: 1, title: 'Formula 1', titleSlug: 'formula-1' },
  { id: 2, title: 'UFC', titleSlug: 'ufc' },
];

const ufcEpisodes = [
  { id: 21, seriesId: 2, seasonNumber: 2026, episodeNumber: 4, title: 'UFC 314 - Main Card', status: 'active' },
  { id: 22, seriesId: 2, seasonNumber: 2026, episodeNumber: 5, title: 'UFC 315 - Main Card', status: 'active' },
  { id: 23, seriesId: 2, seasonNumber: 2026, episodeNumber: 6, title: 'Gone', status: 'orphaned' },
];

describe('Manual Import', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('browses a folder, assigns series, episodes and quality, and moves the files', async () => {
    const fetchMock = renderSettingsPage(<ManualImportPage />, {
      'GET /api/v1/series': series,
      'GET /api/v1/episode': ufcEpisodes,
      'GET /api/v1/qualitydefinition': [
        { id: 1, quality: webdl },
        { id: 2, quality: bluray },
      ],
      'GET /api/v1/manualimport': [
        {
          id: 1,
          path: '/data/in/ufc.314.mkv',
          relativePath: 'ufc.314.mkv',
          folderName: 'in',
          size: 2000,
          episodes: [],
          quality,
          rejections: [{ reason: 'Unknown Series' }],
          assignable: true,
        },
      ],
      'POST /api/v1/command': { id: 3, name: 'ManualImport', status: 'completed' },
    });

    fireEvent.change(await screen.findByPlaceholderText('/data/torrents/generral/…'), {
      target: { name: 'folder', value: ' /data/in ' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Browse' }));

    const dialog = within(await screen.findByRole('dialog'));
    expect(await dialog.findByText('ufc.314.mkv')).toBeTruthy();
    expect(dialog.getByText('Unknown Series')).toBeTruthy();
    const importButton = dialog.getByRole('button', { name: 'Import' });
    expect(importButton.hasAttribute('disabled')).toBe(true);

    fireEvent.click(dialog.getByText('Select Series'));
    fireEvent.click(await screen.findByText('UFC'));
    await waitFor(() =>
      expect(fetchMock.mock.calls.map(([input]) => String(input)).join(' ')).toContain('/api/v1/episode?seriesId=2')
    );
    const row = within(dialog.getByText('ufc.314.mkv').closest('tr') as HTMLElement);
    const [, episodesSelect, qualitySelect] = row.getAllByRole('button');
    fireEvent.click(episodesSelect);
    fireEvent.click(await screen.findByText('2026x04 - UFC 314 - Main Card'));
    expect(screen.queryByText('2026x06 - Gone')).toBeNull();
    fireEvent.click(qualitySelect);
    fireEvent.click(await screen.findByText('Bluray-1080p'));

    expect(dialog.queryByText('Unknown Series')).toBeNull();
    await waitFor(() => expect(importButton.hasAttribute('disabled')).toBe(false));
    fireEvent.click(importButton);

    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/command')).toEqual([
        {
          name: 'ManualImport',
          importMode: 'move',
          files: [
            {
              path: '/data/in/ufc.314.mkv',
              folderName: 'in',
              seriesId: 2,
              episodeIds: [21],
              quality: { ...quality, quality: { id: 7 } },
            },
          ],
        },
      ])
    );
    expect(fetchMock.mock.calls.map(([input]) => String(input)).join(' ')).toContain(
      '/api/v1/manualimport?folder=%2Fdata%2Fin'
    );
  });

  it('shows why a folder could not be listed', async () => {
    renderSettingsPage(<ManualImportPage />, {
      'GET /api/v1/series': series,
      'GET /api/v1/qualitydefinition': [],
      'GET /api/v1/manualimport': new Response(
        JSON.stringify([{ propertyName: 'folder', errorMessage: "Folder '/nope' does not exist" }]),
        { status: 400, headers: { 'Content-Type': 'application/json' } }
      ),
    });

    const input = await screen.findByPlaceholderText('/data/torrents/generral/…');
    expect(screen.getByRole('button', { name: 'Browse' }).hasAttribute('disabled')).toBe(true);
    fireEvent.change(input, { target: { name: 'folder', value: '/nope' } });
    fireEvent.click(screen.getByRole('button', { name: 'Browse' }));

    expect(
      await within(await screen.findByRole('dialog')).findByText(/does not exist/)
    ).toBeTruthy();
  });
});
