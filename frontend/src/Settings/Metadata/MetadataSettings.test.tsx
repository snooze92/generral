import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MockRequest } from 'test/mockApi';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import MetadataSettings from './MetadataSettings';

const plexNfo = {
  id: 1,
  name: 'Plex NFO',
  implementation: 'PlexNfo',
  implementationName: 'Plex NFO',
  configContract: 'PlexNfoSettings',
  tags: [],
  enable: true,
  fields: [
    { order: 0, name: 'seriesMetadata', label: 'Series Metadata', type: 'checkbox', value: true },
    { order: 1, name: 'seriesImages', label: 'Series Images', type: 'checkbox', value: true },
  ],
};

describe('Metadata settings', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the Plex setup help and edits the Plex NFO options', async () => {
    const fetchMock = renderSettingsPage(<MetadataSettings />, {
      'GET /api/v1/metadata': [plexNfo],
      'PUT /api/v1/metadata/1': ({ body }: MockRequest) => body,
    });

    expect(await screen.findByText(/Plex NFO Series/)).toBeTruthy();
    expect(screen.getByText(/Personal Media Shows/)).toBeTruthy();
    expect(await screen.findByText('Series Images')).toBeTruthy();

    fireEvent.click(screen.getByLabelText('Edit Plex NFO Metadata'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(dialog.querySelector('input[name="seriesMetadata"]')!);
    fireEvent.click(within(dialog).getByText('Save'));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(requests(fetchMock, 'PUT', '/api/v1/metadata/1')[0]).toMatchObject({
      enable: true,
      fields: [
        { name: 'seriesMetadata', value: false },
        { name: 'seriesImages', value: true },
      ],
    });
  });
});
