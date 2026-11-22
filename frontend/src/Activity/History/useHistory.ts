// Adapted from Sonarr (GPL-3.0-or-later): frontend/src/Activity/History/useHistory.ts
import ModelBase from 'App/ModelBase';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { QualityModel } from 'Quality/Quality';

export type HistoryEventType =
  | 'unknown'
  | 'grabbed'
  | 'seriesFolderImported'
  | 'downloadFolderImported'
  | 'downloadFailed'
  | 'episodeFileDeleted'
  | 'episodeFileRenamed'
  | 'downloadIgnored';

/** The `/history` resource (Sonarr's `HistoryResource`). */
export interface History extends ModelBase {
  episodeId: number;
  seriesId: number;
  sourceTitle: string;
  quality: QualityModel;
  qualityCutoffNotMet: boolean;
  date: string;
  downloadId?: string;
  eventType: HistoryEventType;
  data: Record<string, string | null>;
}

export const PAGE_SIZE = 20;

const useHistory = (page: number, eventType?: HistoryEventType) =>
  usePagedApiQuery<History>({
    path: '/history',
    page,
    pageSize: PAGE_SIZE,
    queryParams: { eventType },
  });

export default useHistory;
