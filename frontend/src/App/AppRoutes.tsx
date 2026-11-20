// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/App/AppRoutes.tsx
import React from 'react';
import { Navigate, Route } from 'react-router-dom';
import NotFound from 'Components/NotFound';
import PlaceholderPage, { Milestone } from 'Components/Page/PlaceholderPage';
import GeneralSettings from 'Settings/General/GeneralSettings';
import Settings from 'Settings/Settings';
import UISettings from 'Settings/UI/UISettings';
import Status from 'System/Status/Status';
import translate from 'Utilities/String/translate';

interface PlaceholderRoute {
  path: string;
  scope?: () => string;
  title: () => string;
  milestone: Milestone;
}

// Pages that later milestones will build (docs/specs/13-roadmap.md).
const PLACEHOLDERS: PlaceholderRoute[] = [
  { path: '/series', title: () => translate('Series'), milestone: 'M1' },
  { path: '/add/new', scope: () => translate('Series'), title: () => translate('AddNew'), milestone: 'M1' },
  { path: '/sources', title: () => translate('Sources'), milestone: 'M1' },
  { path: '/calendar', title: () => translate('Calendar'), milestone: 'M1' },
  {
    path: '/activity/queue',
    scope: () => translate('Activity'),
    title: () => translate('Queue'),
    milestone: 'M2',
  },
  {
    path: '/activity/history',
    scope: () => translate('Activity'),
    title: () => translate('History'),
    milestone: 'M2',
  },
  {
    path: '/activity/blocklist',
    scope: () => translate('Activity'),
    title: () => translate('Blocklist'),
    milestone: 'M2',
  },
  {
    path: '/wanted/missing',
    scope: () => translate('Wanted'),
    title: () => translate('Missing'),
    milestone: 'M2',
  },
  {
    path: '/wanted/cutoffunmet',
    scope: () => translate('Wanted'),
    title: () => translate('CutoffUnmet'),
    milestone: 'M2',
  },
  {
    path: '/settings/mediamanagement',
    scope: () => translate('Settings'),
    title: () => translate('MediaManagement'),
    milestone: 'M3',
  },
  {
    path: '/settings/profiles',
    scope: () => translate('Settings'),
    title: () => translate('Profiles'),
    milestone: 'M2',
  },
  {
    path: '/settings/quality',
    scope: () => translate('Settings'),
    title: () => translate('Quality'),
    milestone: 'M2',
  },
  {
    path: '/settings/indexers',
    scope: () => translate('Settings'),
    title: () => translate('Indexers'),
    milestone: 'M2',
  },
  {
    path: '/settings/downloadclients',
    scope: () => translate('Settings'),
    title: () => translate('DownloadClients'),
    milestone: 'M2',
  },
  {
    path: '/settings/connect',
    scope: () => translate('Settings'),
    title: () => translate('Connect'),
    milestone: 'M3',
  },
  {
    path: '/settings/metadata',
    scope: () => translate('Settings'),
    title: () => translate('Metadata'),
    milestone: 'M3',
  },
  { path: '/settings/tags', scope: () => translate('Settings'), title: () => translate('Tags'), milestone: 'M1' },
  { path: '/system/tasks', scope: () => translate('System'), title: () => translate('Tasks'), milestone: 'M3' },
  {
    path: '/system/backups',
    scope: () => translate('System'),
    title: () => translate('Backups'),
    milestone: 'M3',
  },
  {
    path: '/system/updates',
    scope: () => translate('System'),
    title: () => translate('Updates'),
    milestone: 'M3',
  },
  { path: '/system/events', scope: () => translate('System'), title: () => translate('Events'), milestone: 'M3' },
  {
    path: '/system/logs/files',
    scope: () => translate('System'),
    title: () => translate('LogFiles'),
    milestone: 'M3',
  },
];

export function appRouteElements() {
  return (
    <>
      <Route path="/" element={<Navigate to="/series" replace={true} />} />

      {PLACEHOLDERS.map(({ path, scope, title, milestone }) => (
        <Route
          key={path}
          path={path}
          element={
            <PlaceholderPage
              scope={scope?.()}
              title={title()}
              milestone={milestone}
            />
          }
        />
      ))}

      <Route path="/settings" element={<Settings />} />

      <Route path="/settings/general" element={<GeneralSettings />} />

      <Route path="/settings/ui" element={<UISettings />} />

      <Route path="/system/status" element={<Status />} />

      <Route path="*" element={<NotFound />} />
    </>
  );
}
