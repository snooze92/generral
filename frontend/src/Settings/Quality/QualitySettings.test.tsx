import { fireEvent, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MockRequest } from 'test/mockApi';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import QualitySettings, { QualityDefinition } from './QualitySettings';

const definitions: QualityDefinition[] = [
  {
    id: 1,
    quality: { id: 1, name: 'SDTV' },
    title: 'SDTV',
    weight: 2,
    minSize: 2,
    preferredSize: 95,
    maxSize: 100,
  },
  {
    id: 2,
    quality: { id: 3, name: 'WEBDL-1080p' },
    title: 'WEBDL-1080p',
    weight: 3,
    minSize: 15,
    preferredSize: null,
    maxSize: null,
  },
];

describe('Quality settings', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('edits the definitions and saves them in one request', async () => {
    const fetchMock = renderSettingsPage(<QualitySettings />, {
      'GET /api/v1/qualitydefinition': definitions,
      'PUT /api/v1/qualitydefinition/update': ({ body }: MockRequest) => body,
    });

    expect(await screen.findByText('SDTV')).toBeTruthy();

    const max = document.querySelector(
      'input[name="2.maxSize"]'
    ) as HTMLInputElement;
    fireEvent.change(max, { target: { value: '400.5' } });
    fireEvent.change(document.querySelector('input[name="1.title"]')!, {
      target: { value: 'Standard' },
    });
    fireEvent.click(screen.getByText('Save Changes'));

    await waitFor(() =>
      expect(
        requests(fetchMock, 'PUT', '/api/v1/qualitydefinition/update')
      ).toHaveLength(1)
    );
    const [saved] = requests(
      fetchMock,
      'PUT',
      '/api/v1/qualitydefinition/update'
    );
    expect(saved).toEqual([
      { ...definitions[0], title: 'Standard' },
      { ...definitions[1], maxSize: 400.5 },
    ]);
  });
});
