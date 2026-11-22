import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MockRequest } from 'test/mockApi';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import NotificationSettings from './NotificationSettings';

const plexSchema = {
  id: 0,
  name: '',
  implementation: 'PlexServer',
  implementationName: 'Plex Media Server',
  configContract: 'PlexServerSettings',
  tags: [],
  fields: [
    { order: 0, name: 'host', label: 'Host', type: 'textbox', value: '' },
  ],
  onGrab: false,
  onDownload: false,
  onUpgrade: false,
  onRename: false,
  onSeriesDelete: false,
  supportsOnGrab: false,
  supportsOnDownload: true,
  supportsOnUpgrade: true,
  supportsOnRename: true,
  supportsOnSeriesDelete: true,
};

describe('Connect settings', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('adds a Plex connection with its supported triggers and tests it', async () => {
    const fetchMock = renderSettingsPage(<NotificationSettings />, {
      'GET /api/v1/notification': [],
      'GET /api/v1/notification/schema': [plexSchema],
      'POST /api/v1/notification/test': {},
      'POST /api/v1/notification': ({ body }: MockRequest) => ({
        ...(body as object),
        id: 1,
        fields: plexSchema.fields,
      }),
    });

    expect(await screen.findByText(/X-Plex-Token/)).toBeTruthy();
    fireEvent.click(await screen.findByLabelText('Add Connection'));
    fireEvent.click(await screen.findByText('Plex Media Server'));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByText('On Grab')).toBeNull();
    fireEvent.change(dialog.querySelector('input[name="name"]')!, {
      target: { value: 'Plex' },
    });
    fireEvent.click(dialog.querySelector('input[name="onDownload"]')!);
    fireEvent.change(dialog.querySelector('input[name="host"]')!, {
      target: { value: 'plex' },
    });

    fireEvent.click(within(dialog).getByText('Test'));
    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/notification/test')).toHaveLength(1)
    );

    fireEvent.click(within(dialog).getByText('Save'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    expect(requests(fetchMock, 'POST', '/api/v1/notification')[0]).toMatchObject({
      name: 'Plex',
      implementation: 'PlexServer',
      onDownload: true,
      onUpgrade: false,
      fields: [{ name: 'host', value: 'plex' }],
    });
    expect(screen.getByText('On File Import')).toBeTruthy();
  });
});
