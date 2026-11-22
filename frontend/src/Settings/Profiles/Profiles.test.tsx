import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MockRequest } from 'test/mockApi';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import Profiles from './Profiles';
import { QualityProfile } from './Quality/useQualityProfiles';
import { ReleaseProfile } from './Release/useReleaseProfiles';

const profile: QualityProfile = {
  id: 1,
  name: 'HD',
  upgradeAllowed: true,
  cutoff: 3,
  language: { id: 1, name: 'English' },
  items: [
    { quality: { id: 1, name: 'SDTV' }, items: [], allowed: false },
    { quality: { id: 4, name: 'HDTV-720p' }, items: [], allowed: true },
    { quality: { id: 3, name: 'WEBDL-1080p' }, items: [], allowed: true },
  ],
};

const releaseProfile: ReleaseProfile = {
  id: 1,
  name: 'No cams',
  enabled: true,
  required: [],
  ignored: ['cam'],
  indexerId: 0,
  tags: [],
  excludedTags: [],
};

function renderProfiles() {
  const releaseProfiles = [releaseProfile];

  return renderSettingsPage(<Profiles />, {
    'GET /api/v1/qualityprofile': [profile],
    'PUT /api/v1/qualityprofile/1': ({ body }: MockRequest) => body,
    'DELETE /api/v1/qualityprofile/1': () =>
      new Response(
        JSON.stringify([
          {
            propertyName: '',
            errorMessage: 'Profile is used by series: Formula 1',
            isWarning: false,
            severity: 'error',
          },
        ]),
        { status: 400 }
      ),
    'GET /api/v1/releaseprofile': () => releaseProfiles,
    'POST /api/v1/releaseprofile': ({ body }: MockRequest) => {
      releaseProfiles.push({ ...(body as ReleaseProfile), id: 2 });
      return releaseProfiles[1];
    },
    'GET /api/v1/indexer': [],
    'GET /api/v1/language': [
      { id: -1, name: 'Any' },
      { id: -2, name: 'Original' },
      { id: 1, name: 'English' },
      { id: 2, name: 'French' },
    ],
  });
}

describe('Profiles', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('reorders and allows qualities of a quality profile', async () => {
    const fetchMock = renderProfiles();

    fireEvent.click(await screen.findByLabelText('Edit Quality Profile'));
    const dialog = await screen.findByRole('dialog');

    // Shown highest preference first.
    const names = () =>
      Array.from(dialog.querySelectorAll('li')).map((li) => li.textContent);
    expect(names()).toEqual(['WEBDL-1080p', 'HDTV-720p', 'SDTV']);

    fireEvent.click(within(dialog).getAllByTitle('Move Up')[2]);
    expect(names()).toEqual(['WEBDL-1080p', 'SDTV', 'HDTV-720p']);
    fireEvent.click(within(dialog).getByLabelText('SDTV'));
    fireEvent.click(within(dialog).getByText('Save'));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    const [saved] = requests(fetchMock, 'PUT', '/api/v1/qualityprofile/1');
    expect(saved.items).toEqual([
      { quality: { id: 4, name: 'HDTV-720p' }, items: [], allowed: true },
      { quality: { id: 1, name: 'SDTV' }, items: [], allowed: true },
      { quality: { id: 3, name: 'WEBDL-1080p' }, items: [], allowed: true },
    ]);
  });

  it('picks the language of a quality profile', async () => {
    const fetchMock = renderProfiles();

    fireEvent.click(await screen.findByLabelText('Edit Quality Profile'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(await within(dialog).findByText('English'));
    expect(await screen.findByText('Original')).toBeTruthy();
    expect(screen.getByText('French')).toBeTruthy();
    fireEvent.click(screen.getByText('Any'));

    fireEvent.click(within(dialog).getByText('Save'));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    const [saved] = requests(fetchMock, 'PUT', '/api/v1/qualityprofile/1');
    expect(saved.language).toEqual({ id: -1, name: 'Any' });
  });

  it('shows why a quality profile cannot be deleted', async () => {
    renderProfiles();

    fireEvent.click(await screen.findByLabelText('Edit Quality Profile'));
    fireEvent.click(
      within(await screen.findByRole('dialog')).getByText('Delete')
    );

    const confirm = await screen.findByRole('dialog');
    fireEvent.click(within(confirm).getByText('Delete'));
    expect(
      await within(confirm).findByText('Profile is used by series: Formula 1')
    ).toBeTruthy();
  });

  it('adds a release profile with ignored terms', async () => {
    const fetchMock = renderProfiles();

    expect(await screen.findByText('cam')).toBeTruthy();
    fireEvent.click(screen.getByLabelText('Add Release Profile'));

    const dialog = await screen.findByRole('dialog');
    fireEvent.change(dialog.querySelector('input[name="name"]')!, {
      target: { value: 'English' },
    });
    const required = within(dialog).getAllByPlaceholderText(
      'Add new restriction'
    )[0];
    fireEvent.change(required, { target: { value: 'english' } });
    fireEvent.keyDown(required, { key: 'Enter' });
    fireEvent.click(within(dialog).getByText('Save'));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(requests(fetchMock, 'POST', '/api/v1/releaseprofile')).toEqual([
      {
        id: 0,
        name: 'English',
        enabled: true,
        required: ['english'],
        ignored: [],
        indexerId: 0,
        tags: [],
        excludedTags: [],
      },
    ]);
    expect(await screen.findByText('English')).toBeTruthy();
  });
});
