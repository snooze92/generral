import ModelBase from 'App/ModelBase';

export type SeriesType = 'standard' | 'daily';

export type SeriesStatus = 'continuing' | 'ended';

export type MonitorNewItems = 'all' | 'none';

export type CoverType =
  | 'poster'
  | 'banner'
  | 'fanart'
  | 'screenshot'
  | 'clearlogo';

export interface Image {
  coverType: CoverType;
  url?: string;
  remoteUrl?: string;
}

export interface Statistics {
  seasonCount: number;
  episodeCount: number;
  episodeFileCount: number;
  totalEpisodeCount: number;
  percentOfEpisodes: number;
  sizeOnDisk: number;
  releaseGroups: string[];
}

export interface SeasonStatistics
  extends Omit<Statistics, 'seasonCount'> {
  nextAiring?: string;
  previousAiring?: string;
}

export interface Season {
  seasonNumber: number;
  title?: string;
  monitored: boolean;
  statistics?: SeasonStatistics;
  images?: Image[];
}

/** The `/series` resource (docs/specs/11-api.md, M1b). */
interface Series extends ModelBase {
  title: string;
  sortTitle: string;
  cleanTitle: string;
  titleSlug: string;
  status: SeriesStatus;
  ended: boolean;
  overview?: string;
  network?: string;
  year: number;
  genres: string[];
  images?: Image[];
  seasons: Season[];
  path: string;
  rootFolderPath: string;
  qualityProfileId: number;
  seasonFolder: boolean;
  monitored: boolean;
  monitorNewItems: MonitorNewItems;
  seriesType: SeriesType;
  tags: number[];
  added: string;
  nextAiring?: string;
  previousAiring?: string;
  statistics?: Statistics;
  guid?: string;
  searchSettings?: Record<string, unknown>;
}

export default Series;
