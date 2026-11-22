import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MockRequest } from 'test/mockApi';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import Indexer from './Indexer';
import IndexerSettings from './IndexerSettings';

const torznab: Indexer = {
  id: 0,
  name: '',
  implementation: 'Torznab',
  implementationName: 'Torznab',
  configContract: 'TorznabSettings',
  tags: [],
  enableRss: true,
  enableAutomaticSearch: true,
  enableInteractiveSearch: true,
  supportsRss: true,
  supportsSearch: true,
  protocol: 'torrent',
  priority: 25,
  downloadClientId: 0,
  fields: [
    { order: 0, name: 'baseUrl', label: 'URL', type: 'textbox', value: '' },
    {
      order: 1,
      name: 'apiKey',
      label: 'API Key',
      type: 'textbox',
      privacy: 'apiKey',
    },
  ] as Indexer['fields'],
};

const indexers: Indexer[] = [
  {
    ...torznab,
    id: 1,
    name: 'Jackett',
    enableRss: false,
    priority: 10,
  },
];

describe('Indexer settings', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lists indexers, adds a Torznab indexer and saves the RSS sync interval', async () => {
    const fetchMock = renderSettingsPage(<IndexerSettings />, {
      'GET /api/v1/indexer': indexers,
      '/api/v1/indexer/schema': [torznab],
      'POST /api/v1/indexer/test': {},
      'POST /api/v1/indexer': ({ body }: MockRequest) => ({
        ...(body as Indexer),
        id: 2,
      }),
      'GET /api/v1/settings/indexer': { rssSyncInterval: 15 },
      'PUT /api/v1/settings/indexer': ({ body }: MockRequest) => body,
    });

    expect(await screen.findByText('Jackett')).toBeTruthy();
    expect(screen.getByText('Priority 10')).toBeTruthy();
    expect(screen.getByText(/Settings → Apps/)).toBeTruthy();

    fireEvent.click(screen.getByLabelText('Add Indexer'));
    const picker = await screen.findByRole('dialog');
    fireEvent.click(await within(picker).findByText('Torznab'));

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Add Indexer - Torznab')).toBeTruthy();
    fireEvent.change(dialog.querySelector('input[name="name"]')!, {
      target: { value: 'Prowlarr' },
    });
    fireEvent.change(dialog.querySelector('input[name="baseUrl"]')!, {
      target: { value: 'http://prowlarr:9696/1/' },
    });

    fireEvent.click(within(dialog).getByText('Test'));
    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/indexer/test')).toHaveLength(
        1
      )
    );

    fireEvent.click(within(dialog).getByText('Save'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('Prowlarr')).toBeTruthy();

    const [saved] = requests(fetchMock, 'POST', '/api/v1/indexer');
    expect(saved).toMatchObject({
      name: 'Prowlarr',
      implementation: 'Torznab',
      enableRss: true,
      fields: [
        { name: 'baseUrl', value: 'http://prowlarr:9696/1/' },
        { name: 'apiKey' },
      ],
    });

    const interval = (await screen.findByDisplayValue(
      '15'
    )) as HTMLInputElement;
    fireEvent.change(interval, { target: { value: '30' } });
    fireEvent.blur(interval);
    fireEvent.click(screen.getByText('Save Changes'));

    await waitFor(() =>
      expect(requests(fetchMock, 'PUT', '/api/v1/settings/indexer')).toEqual([
        { rssSyncInterval: 30 },
      ])
    );
  });
});
