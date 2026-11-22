import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MockRequest } from 'test/mockApi';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import DownloadClient from './DownloadClient';
import DownloadClientSettings from './DownloadClientSettings';

const qbittorrent: DownloadClient = {
  id: 1,
  name: 'qBit',
  implementation: 'QBittorrent',
  implementationName: 'qBittorrent',
  configContract: 'QBittorrentSettings',
  tags: [],
  enable: true,
  protocol: 'torrent',
  priority: 1,
  removeCompletedDownloads: true,
  removeFailedDownloads: true,
  fields: [
    { order: 0, name: 'host', label: 'Host', type: 'textbox', value: 'qbit' },
    { order: 1, name: 'port', label: 'Port', type: 'number', value: 8080 },
  ] as DownloadClient['fields'],
};

describe('Download client settings', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('edits a download client and the failed download handling options', async () => {
    const fetchMock = renderSettingsPage(<DownloadClientSettings />, {
      'GET /api/v1/downloadclient': [qbittorrent],
      'PUT /api/v1/downloadclient/1': ({ body }: MockRequest) => body,
      'GET /api/v1/settings/downloadclient': {
        autoRedownloadFailed: true,
        autoRedownloadFailedFromInteractiveSearch: true,
        stalledDownloadTimeout: 0,
      },
      'PUT /api/v1/settings/downloadclient': ({ body }: MockRequest) => body,
    });

    fireEvent.click(
      await screen.findByLabelText('Edit Download Client - qBittorrent')
    );

    const dialog = await screen.findByRole('dialog');
    fireEvent.change(dialog.querySelector('input[name="host"]')!, {
      target: { value: 'localhost' },
    });
    fireEvent.click(within(dialog).getByText('Save'));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(
      requests(fetchMock, 'PUT', '/api/v1/downloadclient/1')[0]
    ).toMatchObject({
      name: 'qBit',
      enable: true,
      fields: [
        { name: 'host', value: 'localhost' },
        { name: 'port', value: 8080 },
      ],
    });

    expect(
      screen.getByText('Redownload Failed from Interactive Search')
    ).toBeTruthy();
    fireEvent.click(
      document.querySelector('input[name="autoRedownloadFailed"]')!
    );
    expect(
      screen.queryByText('Redownload Failed from Interactive Search')
    ).toBeNull();
    fireEvent.click(screen.getByText('Save Changes'));

    await waitFor(() =>
      expect(
        requests(fetchMock, 'PUT', '/api/v1/settings/downloadclient')
      ).toEqual([
        {
          autoRedownloadFailed: false,
          autoRedownloadFailedFromInteractiveSearch: true,
          stalledDownloadTimeout: 0,
        },
      ])
    );
  });
});
