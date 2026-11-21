import ModelBase from 'App/ModelBase';

export type EpisodeSource = 'mapping' | 'manual';

export type EpisodeStatus = 'active' | 'orphaned';

export interface EpisodeLink {
  mappingId: number;
  eventId: number;
  partKey?: string;
}

/** The `/episode` resource (docs/specs/11-api.md, M1b). */
interface Episode extends ModelBase {
  seriesId: number;
  episodeFileId: number;
  seasonNumber: number;
  episodeNumber: number;
  absoluteEpisodeNumber?: number;
  title?: string;
  airDate?: string;
  airDateUtc?: string;
  lastSearchTime?: string;
  runtime: number;
  overview?: string;
  hasFile: boolean;
  monitored: boolean;
  numberLocked: boolean;
  source: EpisodeSource;
  status: EpisodeStatus;
  links: EpisodeLink[];
}

export default Episode;
