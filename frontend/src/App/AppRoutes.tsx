// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/App/AppRoutes.tsx
import React from 'react';
import { Navigate, Route } from 'react-router-dom';
import Blocklist from 'Activity/Blocklist/Blocklist';
import History from 'Activity/History/History';
import Queue from 'Activity/Queue/Queue';
import AddSeries from 'AddSeries/AddSeries';
import CalendarPage from 'Calendar/CalendarPage';
import NotFound from 'Components/NotFound';
import PlaceholderPage, { Milestone } from 'Components/Page/PlaceholderPage';
import SeriesDetailsPage from 'Series/Details/SeriesDetailsPage';
import SeriesIndex from 'Series/Index/SeriesIndex';
import DownloadClientSettings from 'Settings/DownloadClients/DownloadClientSettings';
import GeneralSettings from 'Settings/General/GeneralSettings';
import IndexerSettings from 'Settings/Indexers/IndexerSettings';
import MediaManagementSettings from 'Settings/MediaManagement/MediaManagementSettings';
import MetadataSettings from 'Settings/Metadata/MetadataSettings';
import NotificationSettings from 'Settings/Notifications/NotificationSettings';
import Profiles from 'Settings/Profiles/Profiles';
import QualitySettings from 'Settings/Quality/QualitySettings';
import Settings from 'Settings/Settings';
import TagSettings from 'Settings/Tags/TagSettings';
import UISettings from 'Settings/UI/UISettings';
import Sources from 'Sources/Index/Sources';
import Status from 'System/Status/Status';
import translate from 'Utilities/String/translate';
import WantedPage from 'Wanted/WantedPage';

interface PlaceholderRoute {
  path: string;
  scope?: () => string;
  title: () => string;
  milestone: Milestone;
}

// Pages that later milestones will build (docs/specs/13-roadmap.md).
const PLACEHOLDERS: PlaceholderRoute[] = [
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

      <Route path="/add/new" element={<AddSeries />} />

      <Route path="/series" element={<SeriesIndex />} />

      <Route path="/series/:titleSlug" element={<SeriesDetailsPage />} />

      <Route path="/sources" element={<Sources />} />

      <Route path="/calendar" element={<CalendarPage />} />

      <Route path="/wanted/missing" element={<WantedPage kind="missing" />} />

      <Route
        path="/wanted/cutoffunmet"
        element={<WantedPage kind="cutoffUnmet" />}
      />

      <Route path="/activity/queue" element={<Queue />} />

      <Route path="/activity/history" element={<History />} />

      <Route path="/activity/blocklist" element={<Blocklist />} />

      <Route path="/settings" element={<Settings />} />

      <Route path="/settings/general" element={<GeneralSettings />} />

      <Route path="/settings/tags" element={<TagSettings />} />

      <Route path="/settings/ui" element={<UISettings />} />

      <Route path="/settings/profiles" element={<Profiles />} />

      <Route path="/settings/quality" element={<QualitySettings />} />

      <Route path="/settings/indexers" element={<IndexerSettings />} />

      <Route
        path="/settings/downloadclients"
        element={<DownloadClientSettings />}
      />

      <Route
        path="/settings/mediamanagement"
        element={<MediaManagementSettings />}
      />

      <Route path="/settings/connect" element={<NotificationSettings />} />

      <Route path="/settings/metadata" element={<MetadataSettings />} />

      <Route path="/system/status" element={<Status />} />

      <Route path="*" element={<NotFound />} />
    </>
  );
}
