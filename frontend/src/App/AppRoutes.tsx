// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/App/AppRoutes.tsx
import React from 'react';
import { Navigate, Route } from 'react-router-dom';
import Blocklist from 'Activity/Blocklist/Blocklist';
import History from 'Activity/History/History';
import Queue from 'Activity/Queue/Queue';
import AddSeries from 'AddSeries/AddSeries';
import CalendarPage from 'Calendar/CalendarPage';
import NotFound from 'Components/NotFound';
import ManualImportPage from 'ManualImport/ManualImportPage';
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
import Backups from 'System/Backup/Backups';
import Events from 'System/Events/Events';
import LogFiles from 'System/Logs/LogFiles';
import Status from 'System/Status/Status';
import Tasks from 'System/Tasks/Tasks';
import Updates from 'System/Updates/Updates';
import WantedPage from 'Wanted/WantedPage';

export function appRouteElements() {
  return (
    <>
      <Route path="/" element={<Navigate to="/series" replace={true} />} />

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

      <Route path="/wanted/manualimport" element={<ManualImportPage />} />

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

      <Route path="/system/tasks" element={<Tasks />} />

      <Route path="/system/backups" element={<Backups />} />

      <Route path="/system/updates" element={<Updates />} />

      <Route path="/system/events" element={<Events />} />

      <Route path="/system/logs/files" element={<LogFiles />} />

      <Route path="*" element={<NotFound />} />
    </>
  );
}
