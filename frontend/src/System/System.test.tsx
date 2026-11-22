import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderSettingsPage, requests } from 'test/renderSettingsPage';
import Backups from './Backup/Backups';
import Events from './Events/Events';
import LogFiles from './Logs/LogFiles';
import Status from './Status/Status';
import Tasks from './Tasks/Tasks';

const backup = {
  id: 7,
  name: 'generral_backup_v0.1.0_2026.11.22_12.00.00.zip',
  path: '/backup/manual/generral_backup_v0.1.0_2026.11.22_12.00.00.zip',
  type: 'manual',
  size: 2048,
  time: '2026-11-22T12:00:00Z',
};

describe('System', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('Status shows health issues and disk space', async () => {
    renderSettingsPage(<Status />, {
      'GET /api/v1/health': [
        {
          source: 'IndexerRssCheck',
          type: 'error',
          message: 'No indexers available with RSS sync enabled',
          wikiUrl: '',
        },
      ],
      'GET /api/v1/diskspace': [
        { path: '/data', label: '', freeSpace: 50, totalSpace: 100 },
      ],
    });

    expect(
      await screen.findByText('No indexers available with RSS sync enabled')
    ).toBeTruthy();
    expect(await screen.findByText('/data')).toBeTruthy();
  });

  it('Tasks lists scheduled tasks and runs one now', async () => {
    const fetchMock = renderSettingsPage(<Tasks />, {
      'GET /api/v1/system/task': [
        {
          id: 1,
          name: 'Check Health',
          taskName: 'CheckHealth',
          interval: 360,
          lastExecution: '2026-11-22T11:00:00Z',
          nextExecution: '2026-11-22T17:00:00Z',
        },
      ],
      'GET /api/v1/command': [],
      'POST /api/v1/command': {
        id: 3,
        name: 'CheckHealth',
        status: 'completed',
      },
    });

    expect(await screen.findByText('6 hours')).toBeTruthy();
    fireEvent.click(screen.getByLabelText('Run Check Health'));
    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/command')).toEqual([
        { name: 'CheckHealth' },
      ])
    );
  });

  it('Backups lists, creates and deletes backups', async () => {
    const fetchMock = renderSettingsPage(<Backups />, {
      'GET /api/v1/system/backup': [backup],
      'POST /api/v1/command': { id: 4, name: 'Backup', status: 'completed' },
      'DELETE /api/v1/system/backup/7': {},
    });

    const link = await screen.findByText(backup.name);
    expect(link.closest('a')?.getAttribute('href')).toContain(
      '/api/v1/system/backup/7/download?apikey='
    );

    fireEvent.click(screen.getByText('Backup Now'));
    await waitFor(() =>
      expect(requests(fetchMock, 'POST', '/api/v1/command')).toEqual([
        { name: 'Backup' },
      ])
    );

    fireEvent.click(screen.getByLabelText('Delete Backup'));
    fireEvent.click(
      within(await screen.findByRole('dialog')).getByText('Delete')
    );
    await waitFor(() =>
      expect(
        requests(fetchMock, 'DELETE', '/api/v1/system/backup/7')
      ).toHaveLength(1)
    );
  });

  it('Log Files lists the files with download links', async () => {
    renderSettingsPage(<LogFiles />, {
      'GET /api/v1/log/file': [
        {
          id: 1,
          filename: 'generral.txt',
          lastWriteTime: '2026-11-22T12:00:00Z',
          contentsUrl: '/api/v1/log/file/generral.txt',
          downloadUrl: '/api/v1/log/file/generral.txt',
        },
      ],
    });

    expect(await screen.findByText('generral.txt')).toBeTruthy();
    expect(
      screen.getByText('Download').closest('a')?.getAttribute('href')
    ).toContain('/api/v1/log/file/generral.txt?apikey=');
  });

  it('Events lists recent log entries and filters by level', async () => {
    const fetchMock = renderSettingsPage(<Events />, {
      'GET /api/v1/log': {
        page: 1,
        pageSize: 50,
        totalRecords: 1,
        records: [
          {
            id: 1,
            time: '2026-11-22T12:00:00Z',
            level: 'warn',
            logger: 'ReleaseSearchService',
            message: 'Indexer Tracker failed: HTTP 500',
          },
        ],
      },
    });

    expect(
      await screen.findByText('Indexer Tracker failed: HTTP 500')
    ).toBeTruthy();
    fireEvent.click(screen.getByText('Warn'));
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) =>
          String(input).includes('level=warn')
        )
      ).toBe(true)
    );
  });
});
