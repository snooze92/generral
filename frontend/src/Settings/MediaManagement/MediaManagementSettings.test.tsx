import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MockRequest } from 'test/mockApi';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import MediaManagementSettings from './MediaManagementSettings';

const naming = {
  renameEpisodes: true,
  replaceIllegalCharacters: true,
  colonReplacementFormat: 4,
  customColonReplacementFormat: '',
  multiEpisodeStyle: 5,
  standardEpisodeFormat: '{Series Title} - S{season:00}E{episode:00}',
  dailyEpisodeFormat: '{Series Title} - {Air-Date}',
  seriesFolderFormat: '{Series TitleYear}',
  seasonFolderFormat: 'Season {season}',
  specialsFolderFormat: 'Specials',
};

const mediaManagement = {
  copyUsingHardlinks: true,
  recycleBin: '',
  recycleBinCleanupDays: 7,
  skipFreeSpaceCheckWhenImporting: false,
  minimumFreeSpaceWhenImporting: 100,
  importExtraFiles: true,
  extraFileExtensions: 'srt',
  deleteEmptyFolders: false,
  setPermissionsLinux: false,
  chmodFolder: '755',
  chownGroup: '',
};

describe('Media management settings', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows live naming examples, saves both sections and manages root folders and mappings', async () => {
    const fetchMock = renderSettingsPage(<MediaManagementSettings />, {
      'GET /api/v1/config/naming': naming,
      'PUT /api/v1/config/naming': ({ body }: MockRequest) => body,
      'GET /api/v1/config/naming/examples': ({ url }: MockRequest) => ({
        singleEpisodeExample: `${url.searchParams.get('standardEpisodeFormat')}.mkv`,
        seriesFolderExample: 'UFC',
        seasonFolderExample: 'Season 2025',
      }),
      'GET /api/v1/config/mediamanagement': mediaManagement,
      'PUT /api/v1/config/mediamanagement': ({ body }: MockRequest) => body,
      'GET /api/v1/rootfolder': [
        { id: 1, path: '/sport', accessible: true, freeSpace: 2 * 1024 ** 3, unmappedFolders: [] },
      ],
      'POST /api/v1/rootfolder': ({ body }: MockRequest) => ({
        id: 2,
        path: (body as { path: string }).path,
        accessible: true,
        unmappedFolders: [],
      }),
      'GET /api/v1/remotepathmapping': [],
      'POST /api/v1/remotepathmapping': ({ body }: MockRequest) => ({ ...(body as object), id: 3 }),
    });

    expect(
      await screen.findByText(
        'Example: {Series Title} - S{season:00}E{episode:00}.mkv'
      )
    ).toBeTruthy();
    expect(screen.getByText('/sport')).toBeTruthy();
    expect(screen.getByText('2 GiB')).toBeTruthy();

    fireEvent.change(
      document.querySelector('input[name="standardEpisodeFormat"]')!,
      { target: { value: 'S{season:00}E{episode:00}' } }
    );
    expect(
      await screen.findByText('Example: S{season:00}E{episode:00}.mkv', {}, { timeout: 2000 })
    ).toBeTruthy();

    fireEvent.click(document.querySelector('input[name="deleteEmptyFolders"]')!);
    fireEvent.click(screen.getByText('Save Changes'));

    await waitFor(() =>
      expect(requests(fetchMock, 'PUT', '/api/v1/config/mediamanagement')).toEqual([
        { ...mediaManagement, deleteEmptyFolders: true },
      ])
    );
    expect(requests(fetchMock, 'PUT', '/api/v1/config/naming')).toEqual([
      { ...naming, standardEpisodeFormat: 'S{season:00}E{episode:00}' },
    ]);

    fireEvent.change(screen.getByPlaceholderText('/data/media/generral'), {
      target: { value: '/tv' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Add Root Folder' }));
    expect(await screen.findByText('/tv')).toBeTruthy();

    fireEvent.click(screen.getByLabelText('Add Remote Path Mapping'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(dialog.querySelector('input[name="host"]')!, {
      target: { value: 'qbit' },
    });
    fireEvent.change(dialog.querySelector('input[name="remotePath"]')!, {
      target: { value: '/downloads' },
    });
    fireEvent.change(dialog.querySelector('input[name="localPath"]')!, {
      target: { value: '/data/downloads' },
    });
    fireEvent.click(within(dialog).getByText('Save'));

    expect(await screen.findByText('/data/downloads')).toBeTruthy();
    expect(requests(fetchMock, 'POST', '/api/v1/remotepathmapping')[0]).toEqual({
      id: 0,
      host: 'qbit',
      remotePath: '/downloads',
      localPath: '/data/downloads',
    });
  });
});
