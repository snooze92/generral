import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import InteractiveSearchModal from './InteractiveSearchModal';
import { setReleaseSort } from './useReleases';

const quality = {
  quality: { id: 3, name: 'WEBDL-1080p', source: 'web', resolution: 1080 },
  revision: { version: 1, real: 0, isRepack: false },
};

function release(guid: string, changes = {}) {
  return {
    guid,
    quality,
    qualityWeight: 100,
    age: 2,
    ageHours: 48,
    ageMinutes: 2880,
    size: 4 * 1024 * 1024 * 1024,
    indexerId: 5,
    indexer: 'Torznab',
    title: `Formula1.2026.Round03.${guid}.1080p.WEB`,
    approved: true,
    rejected: false,
    rejections: [],
    publishDate: '2026-03-13T08:00:00Z',
    downloadAllowed: true,
    releaseWeight: 0,
    seeders: 60,
    leechers: 4,
    protocol: 'torrent',
    languages: [{ id: 1, name: 'English' }],
    ...changes,
  };
}

describe('Interactive search', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    setReleaseSort('releaseWeight', 'ascending');
  });

  function titles() {
    return screen
      .getAllByRole('row')
      .slice(1)
      .map((row) => row.querySelector('td:nth-child(3)')?.textContent);
  }

  it('keeps the server order by default, sorts by header and warns about timed-out indexers', async () => {
    renderSettingsPage(
      <InteractiveSearchModal
        isOpen={true}
        episodeId={11}
        title="2026x3"
        onModalClose={() => {}}
      />,
      {
        'GET /api/v1/release': () =>
          new Response(
            JSON.stringify([
              release('Small', { releaseWeight: 1, size: 1024 }),
              release('Best', { releaseWeight: 0, size: 2048 }),
              release('Rejected', {
                releaseWeight: 2,
                size: 4096,
                approved: false,
                rejections: ['Too big'],
              }),
            ]),
            {
              status: 200,
              headers: {
                'Content-Type': 'application/json',
                'X-Generral-Timed-Out-Indexers': 'LimeTorrents',
              },
            }
          ),
      }
    );

    await screen.findByText('Formula1.2026.Round03.Best.1080p.WEB');
    expect(
      screen.getByText(
        'Some indexers timed out, their results are missing: LimeTorrents'
      )
    ).toBeTruthy();
    const order = (...names: string[]) =>
      names.map((name) => `Formula1.2026.Round03.${name}.1080p.WEB`);
    expect(titles()).toEqual(order('Best', 'Small', 'Rejected'));

    fireEvent.click(screen.getByRole('columnheader', { name: 'Size' }));
    expect(titles()).toEqual(order('Small', 'Best', 'Rejected'));

    fireEvent.click(screen.getByRole('columnheader', { name: 'Size' }));
    expect(titles()).toEqual(order('Rejected', 'Best', 'Small'));
  });

  it('lists releases with rejections and grabs one', async () => {
    const fetchMock = renderSettingsPage(
      <InteractiveSearchModal
        isOpen={true}
        episodeId={11}
        title="2026x3 - Australian GP"
        onModalClose={() => {}}
      />,
      {
        'GET /api/v1/release': [
          release('Approved'),
          release('Rejected', {
            approved: false,
            rejected: true,
            rejections: ['Quality is not wanted in profile'],
          }),
        ],
        'POST /api/v1/release': ({ body }: { body: unknown }) => body,
      }
    );

    const approved = await screen.findByText(
      'Formula1.2026.Round03.Approved.1080p.WEB'
    );
    const approvedRow = within(approved.closest('tr') as HTMLElement);
    expect(approvedRow.getByText('Torznab')).toBeTruthy();
    expect(approvedRow.getByText('4 GiB')).toBeTruthy();
    expect(approvedRow.getByText('60 / 4')).toBeTruthy();
    expect(approvedRow.getByText('2 days')).toBeTruthy();
    expect(approvedRow.getByText('English')).toBeTruthy();
    expect(approvedRow.queryByLabelText('Release Rejected')).toBeNull();

    const rejectedRow = within(
      screen
        .getByText('Formula1.2026.Round03.Rejected.1080p.WEB')
        .closest('tr') as HTMLElement
    );
    expect(rejectedRow.getByLabelText('Release Rejected')).toBeTruthy();
    expect(
      rejectedRow.getByLabelText('Grab').getAttribute('title')
    ).toBe('Override and add to download queue');

    const urls = fetchMock.mock.calls.map(([input]) => String(input));
    expect(urls.some((url) => url.includes('/release?episodeId=11'))).toBe(
      true
    );

    fireEvent.click(approvedRow.getByLabelText('Grab'));

    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/release')).toEqual([
        { guid: 'Approved', indexerId: 5 },
      ])
    );
    await waitFor(() =>
      expect(approvedRow.getByLabelText('Grab').getAttribute('title')).toBe(
        'Added to download queue'
      )
    );
  });

  it('shows the grab error from the API', async () => {
    renderSettingsPage(
      <InteractiveSearchModal
        isOpen={true}
        episodeId={11}
        title="2026x3"
        onModalClose={() => {}}
      />,
      {
        'GET /api/v1/release': [release('Approved')],
        'POST /api/v1/release': () =>
          new Response(JSON.stringify({ message: 'Client is offline' }), {
            status: 409,
            headers: { 'Content-Type': 'application/json' },
          }),
      }
    );

    const grab = await screen.findByLabelText('Grab');
    fireEvent.click(grab);

    await waitFor(() =>
      expect(grab.getAttribute('title')).toBe('Client is offline')
    );
  });
});
